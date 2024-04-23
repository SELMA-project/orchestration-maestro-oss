using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
  public class BatchedJobResultListener : BackgroundService, IJobResultListener
  {
    private IConfiguration _configuration;
    private JobEnqueuer _jobEnqueuer;
    private ILogger _logger;
    private MessageProcessor _messageProcessor;
    private readonly OrchestrationDBContextFactory _factory;
    private readonly MQProducer _resultProducer;
    private readonly JsonMergeSettings _mergeSettings;
    private int _maxRetryCount;
    private JobMetrics _jobMetrics;

    private JobBatchOptions _jobBatchOptions;
    
    public record struct JobBatchOptions(TimeSpan Timeout, ushort MaxSize, ushort Concurrency);
    public BatchedJobResultListener(JobEnqueuer jobEnqueuer, ILogger<BatchedJobResultListener> logger, IConfiguration configuration, MessageProcessor messageProcessor, JobMetrics jobMetrics)
    {
      _jobEnqueuer = jobEnqueuer;
      _logger = logger;
      _configuration = configuration;
      _messageProcessor = messageProcessor;
      _jobMetrics = jobMetrics;
      _factory = new OrchestrationDBContextFactory();
      _mergeSettings = new JsonMergeSettings
          {MergeArrayHandling = MergeArrayHandling.Concat};

      _maxRetryCount = Convert.ToInt32(_configuration["JobResultListener:MaxRetryCount"]);
      Convert.ToInt32(_configuration["JobResultListener:UpdateWorkflow:MaxRetryCount"]);

      _jobBatchOptions = configuration.GetSection("JobResultListener:Batch")
                                      .Get<JobBatchOptions>();
      
      var producerExchange = _configuration.GetValue<string>("Integration:Exchanges:Maestro.Out");
      if (!string.IsNullOrWhiteSpace(producerExchange))
      {
        _resultProducer = new MQProducer(producerExchange, _configuration, _logger);
      }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      var resultConsumer = new MQBatchConsumer(_configuration, _logger,
                                               _configuration["Integration:Queues:ResultListener.In"],
                                               batchTimeout: _jobBatchOptions.Timeout,
                                               batchSize: _jobBatchOptions.MaxSize,
                                               concurrency: _jobBatchOptions.Concurrency,
                                               nameof(BatchedJobResultListener));

      await resultConsumer.Run(BatchConsumerLogic, stoppingToken);
    }

    private async Task BatchConsumerLogic(ImmutableList<MqMessage> mqMessages,
                                          MQBatchConsumer consumer,
                                          CancellationToken cancellationToken)
    {
      var tries = 0;
      var done = false;
      do
      {
        try
        {
          var stopwatch = Stopwatch.StartNew();
          
          await using var dbContext = _factory.CreateDbContext(null!);

          var jobs = await dbContext.Jobs
                                    .Where(job => mqMessages.Select(m => m.Message.JobId)
                                                            .Contains(job.Id))
                                    //.Select(j => new {j.Id, j.Dependencies, j.Input, ... }) //todo: trim down query
                                    .ToDictionaryAsync(j => j.Id, j => j, cancellationToken);

          foreach (var mqMessage in mqMessages)
          {
            try
            {
              var message = mqMessage.Message;
              if (jobs.TryGetValue(message.JobId, out var job) == false)
              {
                _logger.LogWarning($"Skipped job {message.JobId}: missing from database (old job forgotten in the message queue?)");
                consumer.SetMessageStatus(mqMessage, MessageStatus.Nack);
                continue;
              }

              if (message.Type is not (MessageType.Error or MessageType.FinalResult))
              {
                _logger.LogWarning($"Skipped job {message.JobId}: invalid message type {message.Type}");
                consumer.SetMessageStatus(mqMessage, MessageStatus.Nack);
                continue;
              }
              
              consumer.SetMessageStatus(mqMessage, MessageStatus.Ack);

              // message is either an error
              if (message.Type is MessageType.Error)
              {
                job.SetError(message.Payload.ToObject<ErrorPayload>());
                continue;
              }

              // or a final result
              try
              {
                message = _messageProcessor.Process(message, job);
                message.TimeElapsed = DateTime.UtcNow - job.Updated;
              }
              catch (Exception e)
              {
                var errorPayload = new ErrorPayload(e.Message, "output_script_error", null);
                job.SetError(errorPayload);
                _logger.LogError(e, errorPayload.Type);
                continue;
              }
              
              job.Status = OrchestrationStatus.Done;
              var data = (message.Payload as JObject).ToObject<FinalResultPayload>().Data;
              job.Result = data;
            }
            catch (Exception e)
            {
              consumer.SetMessageStatus(mqMessage, MessageStatus.Nack); 
              _logger.LogError(e, "Unexpected error processing message in batch");
            }
          }

          var doneJobs = jobs.Where(j => j.Value.Status == OrchestrationStatus.Done)
                             .ToDictionary(j => j.Key, j => j.Value);

          var followUpJobs = FetchDependentJobs(doneJobs, dbContext);
          var orchestrateNextJobs = await UpdateDependentJobs(followUpJobs, doneJobs);

          try
          {
            await dbContext.SaveChangesAsync(cancellationToken);
          }
          catch (DbUpdateConcurrencyException e)
          { 
            // as long as there is only one batched job result listener
            Trace.Assert(false, "there should be no concurrency update issues", e.Message);
          }

          // end of batch transaction


          foreach (var job in orchestrateNextJobs)
          {
            try
            {
              await _jobEnqueuer.Enqueue(job, CancellationToken.None);
            }
            catch (Exception e)
            {
              _logger.LogError(e, $"Failed to enqueue job {job.Id}");
            }
          }
          
          // check for enqueue errors
          var enqueueErrorCount = orchestrateNextJobs.Count(j => j.Status == OrchestrationStatus.Error);
          if (enqueueErrorCount > 0)
          {
            await dbContext.SaveChangesAsync(CancellationToken.None);
          }
          
          await ForwardMessagesToDirector(jobs);
          
          _jobMetrics.Queued.Add(orchestrateNextJobs.Count - enqueueErrorCount);
          _jobMetrics.DoneDuration.Record(stopwatch.ElapsedMilliseconds, new KeyValuePair<string, object>("count", mqMessages.Count)); 
          done = true;
        }
        catch (NpgsqlException e) when (e.IsTransient)
        {
          tries++;
          _logger.LogWarning(e, $"Retrying after transient error ({tries}/{_maxRetryCount})");
          await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
        catch (Exception e)
        {
          _logger.LogError(e, "Unexpected error receiving worker result");

          // retry in case of worker network error
          if (!(e is SocketException or HttpRequestException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
          {
            tries = _maxRetryCount;
            break;
          }
          tries++;
          await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
      } while (!done && !cancellationToken.IsCancellationRequested && tries < _maxRetryCount);
    }

    private async Task ForwardMessagesToDirector(Dictionary<Guid, Job> allJobs)
    {
      if (_resultProducer == null) return;

      var jobs = allJobs.Values
                        .Where(job => job.Status is OrchestrationStatus.Error or OrchestrationStatus.Done);
      foreach (var job in jobs)
      {
        try
        {
          var message = JobToMessage(job);
          await _resultProducer.Produce(message, "", CancellationToken.None);
        }
        catch (Exception e)
        {
          _logger.LogError(e, $"Failed to produce message for job {job.Id}");
        }
      }
    }

    private static Message JobToMessage(Job job)
    {
      var messageType = job.Status == OrchestrationStatus.Error
          ? MessageType.Error
          : MessageType.FinalResult;
      
      var payload = job.Status switch
      {
        OrchestrationStatus.Done => JObject.FromObject(new FinalResultPayload(job.Result, new())),
        OrchestrationStatus.Error => JObject.FromObject(job.Result),
        _ => throw new InvalidOperationException($"Unexpected job status: '{Enum.GetName(job.Status)}'")
      };
      
      return new Message(job.Id,
                         messageType,
                         payload,
                         job.Metadata);
    }

    private static IEnumerable<Job> FetchDependentJobs(Dictionary<Guid, Job> doneJobs, OrchestrationDBContext dbContext)
    {
      var workflowIds = doneJobs.Values
                                .Select(j => j.WorkflowId)
                                .ToHashSet();

      var dependentJobs = dbContext.Jobs
                                   .Where(job => job.Status == OrchestrationStatus.Waiting
                                                 && workflowIds.Contains(job.WorkflowId))
                                   .AsEnumerable()
                                   .Where(waitingJob => waitingJob.Dependencies.Overlaps(doneJobs.Keys)); // waiting jobs that had a dependency complete
      return dependentJobs;
    }

    private Task<List<Job>> UpdateDependentJobs(IEnumerable<Job> dependentJobs, Dictionary<Guid, Job> doneJobs)
    {
      var orchestrateNextJobs = new List<Job>();
      foreach (var job in dependentJobs)
      {
        var completedJobs = job.Dependencies.Intersect(doneJobs.Keys).Select(key => doneJobs[key]);
        foreach (var completedJob in completedJobs)
        {
          Trace.Assert(job.Dependencies.Remove(completedJob.Id), "follow up job should have dependencies to remove");
          var inputContainer = (JContainer)job.Input;
          inputContainer.Merge(completedJob.Result, _mergeSettings);
          job.Input = inputContainer;

          var canEnqueue = job.Dependencies.Count == 0;
          if (canEnqueue)
          {
            job.Status = OrchestrationStatus.Queued;
            orchestrateNextJobs.Add(job);
          }
        }
      }

      return Task.FromResult(orchestrateNextJobs);
    }
  }
}
