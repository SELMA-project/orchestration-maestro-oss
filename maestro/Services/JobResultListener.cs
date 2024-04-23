using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Npgsql;
using Selma.Orchestration.Models;
using Selma.Orchestration.OrchestrationDB;
using Selma.Orchestration.TaskMQUtils;
using System.Net.Http;
using System.Net.Sockets;

namespace Selma.Orchestration.Maestro
{
  public interface IJobResultListener
  {

  }

  public class JobResultListener : BackgroundService, IJobResultListener
  {
    private IConfiguration _configuration;
    private JobEnqueuer _jobEnqueuer;
    private ILogger _logger;
    private MessageProcessor _messageProcessor;
    private readonly JobMetrics _jobMetrics;
    private readonly OrchestrationDBContextFactory _factory;
    private readonly MQProducer _resultProducer;
    private readonly JsonMergeSettings _mergeSettings;
    private int _maxRetryCount;
    private int _updateWorkflowMaxRetryCount;

    public JobResultListener(JobEnqueuer jobEnqueuer, ILogger<JobResultListener> logger, IConfiguration configuration, MessageProcessor messageProcessor, JobMetrics jobMetrics)
    {
      _jobEnqueuer = jobEnqueuer;
      _logger = logger;
      _configuration = configuration;
      _messageProcessor = messageProcessor;
      _jobMetrics = jobMetrics;
      _factory = new OrchestrationDBContextFactory();
      _mergeSettings = new JsonMergeSettings
      { MergeArrayHandling = MergeArrayHandling.Concat };

      _maxRetryCount = Convert.ToInt32(_configuration["JobResultListener:MaxRetryCount"]);
      _updateWorkflowMaxRetryCount = Convert.ToInt32(_configuration["JobResultListener:UpdateWorkflow:MaxRetryCount"]);
      var producerExchange = _configuration.GetValue<string>("Integration:Exchanges:Maestro.Out");
      if (!string.IsNullOrWhiteSpace(producerExchange))
      {
        _resultProducer = new MQProducer(producerExchange, _configuration, _logger);
      }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      var resultConsumer = new MQConsumer(_configuration,
                                          _logger,
                                          _configuration["Integration:Queues:ResultListener.In"],
                                          _configuration.GetValue<ushort>("Integration:Queues:PrefetchCount", 50),
                                          nameof(JobResultListener));


      await resultConsumer.Run(ConsumerLogic, stoppingToken);
    }

    private async Task ConsumerLogic(Message message, CancellationToken innerStoppingToken)
    {
      var tries = 0;
      var done = false;
      do
      {
        try
        {
          var stopwatch = Stopwatch.StartNew();
          // write result in O.DB
          await using var dbContext = _factory.CreateDbContext(null);
          var job = dbContext.Jobs
                             .TagWith("JobResult_FetchJob")
                             .FirstOrDefault(j => j.Id == message.JobId);

          if (job is null)
          {
            // todo: handle this case?
            _logger.LogWarning("Job {JobId} not found in database: ignoring (old job forgotten in the message queue?)", message.JobId);
            return;
          }

          switch (message.Type)
          {
            case MessageType.Error:
              AbortJob(job, dbContext);
              break;
            case MessageType.FinalResult:
              try
              {
                message = _messageProcessor.Process(message, job);
                // Add timeElapsed to message
                message.TimeElapsed = DateTime.UtcNow - job.Updated;

              }
              catch (Exception e)
              {
                AbortJob(job, dbContext);
                _logger.LogError(e, "Error processing message with output script");
                if (_resultProducer != null)
                {
                  message.Payload = JToken.FromObject(new ErrorPayload(e.Message, "message_processing_exception", Guid.NewGuid().ToString()));
                  message.Type = MessageType.Error;
                }
                break;
              }
              job.Status = OrchestrationStatus.Done;
              var data = (message.Payload as JObject).ToObject<FinalResultPayload>().Data;
              job.Result = data;
              dbContext.SaveChanges();
              // handle workflow
              var workflowId = job.WorkflowId;
              var followUpJobs = await UpdateWorkflow(job, workflowId, innerStoppingToken);
              await _jobEnqueuer.Enqueue(followUpJobs, innerStoppingToken);

              var errorJobs = followUpJobs.Where(j => j.Status == OrchestrationStatus.Error)
                                          .ToList();

              if (errorJobs.Any())
              {
                dbContext.SaveChanges();
              }
              
              _jobMetrics.Queued.Add(followUpJobs.Count - errorJobs.Count);
              _jobMetrics.DoneDuration.Record(stopwatch.ElapsedMilliseconds,
                                              KeyValuePair.Create<string, object>("count", 1));
              break;
          }

          if (_resultProducer != null)
          {
            await _resultProducer.Produce(message, "", innerStoppingToken);
          }
          done = true;
        }
        catch (NpgsqlException e) when (e.IsTransient)
        {
          tries++;
          _logger.LogWarning(e, $"Retrying after transient error ({tries}/{_maxRetryCount})");
          await Task.Delay(TimeSpan.FromMilliseconds(250), innerStoppingToken);
        }
        catch (Exception e)
        {
          _logger.LogError(e, "Unexpected error receiving worker result");

          // retry in case of worker network error
          if (!(e is SocketException or HttpRequestException or TaskCanceledException) && !innerStoppingToken.IsCancellationRequested)
          {
            tries = _maxRetryCount;
            break;
          }
          tries++;
          await Task.Delay(TimeSpan.FromMilliseconds(250), innerStoppingToken);
        }
      } while (!done && !innerStoppingToken.IsCancellationRequested && tries < _maxRetryCount);
    }

    private static void AbortJob(Job job, DbContext dbContext)
    {
      job.Status = OrchestrationStatus.Error;
      dbContext.Update(job);
      dbContext.SaveChanges();
    }

    public async Task<List<Job>> UpdateWorkflow(Job completedJob, Guid workflowId, CancellationToken stoppingToken)
    {
      _logger.LogTrace($"Job {completedJob} completed: Updating workflow...");
      var orchestrateNextJobs = new List<Job>();
      var rand = new Random();
      var workflowJobIds = GetWaitingJobIds(workflowId);
      foreach (var jobId in workflowJobIds)
      {
        var tries = 0;
        var done = false;
        try
        {
          do
          {
            if (tries > 0) _logger.LogWarning($"Retrying job {jobId} ({tries}/{_updateWorkflowMaxRetryCount})...");

            try
            {
              await using var dbContext = _factory.CreateDbContext(null);
              var job = dbContext.Jobs
                                 .TagWith("JobResult_UpdateWorkflow_FetchWaitingJob")
                                 .Single(o => o.Id == jobId);

              var hadDependency = job.Dependencies.Remove(completedJob.Id);
              if (hadDependency)
              {
                var inputContainer = (JContainer)job.Input;
                inputContainer.Merge(completedJob.Result, _mergeSettings);
                job.Input = inputContainer;

                var canEnqueue = job.Dependencies.Count == 0;
                if (canEnqueue) job.Status = OrchestrationStatus.Queued;

                dbContext.SaveChanges(); // can throw DbUpdateConcurrencyException
                _logger.LogTrace("Saved {Job}", job);

                if (canEnqueue) orchestrateNextJobs.Add(job);
              }
              done = true;
              if (tries > 0) _logger.LogDebug($"Saved job {job.Id} after {tries} retries");
            }
            catch (DbUpdateConcurrencyException e)
            {
              await LogDbUpdateConcurrencyException(e);
              tries += 1;
              await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken);
            }
            catch (Exception e) when (e is not MessageProcessingException)
            {
              var delay = rand.Next(_configuration.GetValue<int>("Integration:Database:MaxRetryDelayMs", 10));
              _logger.LogWarning(e, "{ExceptionName} commiting job {JobId}. Retrying in {RetryDelay} ms. {ExceptionMessage}",
                                 e.GetType().Name, jobId, delay, e.Message);
              tries += 1;
              await Task.Delay(TimeSpan.FromMilliseconds(delay), stoppingToken);
            }
          } while (!done && tries < _updateWorkflowMaxRetryCount);
        }
        catch (Exception e)
        {
          _logger.LogError(e, "Unexpected error updating job {JobId} (workflow: {WorkflowId})",
                           jobId, workflowId);
        }
      }
      return orchestrateNextJobs;
    }

    private List<Guid> GetWaitingJobIds(Guid workflowId)
    {
      using var dbContext = _factory.CreateDbContext(null);
      var workflowJobIds = dbContext.Jobs
                                    .TagWith("JobResult_GetWaitingJobIdsForWorkflow")
                                    .Where(o => o.WorkflowId == workflowId
                                                && o.Status == OrchestrationStatus.Waiting)
                                    .Select(j => j.Id)
                                    .ToList();
      return workflowJobIds;
    }

    private async Task LogDbUpdateConcurrencyException(DbUpdateConcurrencyException e)
    {
      _logger.LogWarning($"DbUpdateConcurrencyException on {e.Entries.Count} jobs:");
      foreach (var entry in e.Entries)
      {
        if (entry.Entity is Job j)
        {
          var originalValues = entry.OriginalValues;
          var databaseValues = await entry.GetDatabaseValuesAsync();

          var propertyNames = originalValues.Properties
                                            .Where(p => p.IsConcurrencyToken
                                                        && originalValues[p] != databaseValues?[p])
                                            .Select(p => p.Name);
          _logger.LogWarning($"\t{j.ToString()} cached values of {string.Join(", ", propertyNames)} differ from db");
        }
      }
    }
  }
}
