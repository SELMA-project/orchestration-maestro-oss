using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Selma.Orchestration.Models;
using Selma.Orchestration.OrchestrationDB;
using Selma.Orchestration.TaskMQUtils;

namespace Selma.Orchestration.Maestro
{
  public class JobEnqueuer
  {
    private ILogger _logger;
    private IConfiguration _configuration;
    private MQProducer _requestMQProducer;
    private MQManager _queueManager;
    private List<string> _seenJobTypes;
    private string _writeToExchangeTopic;
    private MessageProcessor _messageProcessor;
    private string _queueFormatString;

    public JobEnqueuer(ILogger<JobEnqueuer> logger, IConfiguration configuration, MessageProcessor messageProcessor)
    {
      _logger = logger;
      _configuration = configuration;
      _queueManager = new MQManager(_configuration, _logger);
      _seenJobTypes = new List<string>();
      _writeToExchangeTopic = _configuration["Integration:Exchanges:Workers.In"];
      _requestMQProducer = new MQProducer(_writeToExchangeTopic,
                                                                _configuration,
                                                                _logger);
      _queueManager.BindQueueToTopicExchange(_configuration["Integration:Exchanges:Workers.Out"],
                                              _configuration["Integration:Queues:ResultListener.In"],
                                              "#");
      _messageProcessor = messageProcessor;
      _queueFormatString = configuration.GetValue<string>("Integration:Queues:FormatString", null);
    }

    public async Task Enqueue(List<Job> jobs, CancellationToken stoppingToken)
    {
      foreach (var job in jobs)
      {
        await Enqueue(job, stoppingToken);
      }
    }

    public async Task Enqueue(Job job, CancellationToken stoppingToken)
    {
      if (stoppingToken.IsCancellationRequested)
      {
        var errorPayload = new ErrorPayload("Operation cancelled", "job_enqueue_error", null);
        job.SetError(errorPayload);
        _logger.LogError("{ErrorType}: {ErrorMessage}", errorPayload.Type, errorPayload.Message);
        return;
      }
      
      var jobInfo = new JobInfo { Type = job.Type, Provider = job.Provider, Language = job.Language, Scenario = job.Scenario };
      var queueName = jobInfo.QueueName(_queueFormatString);

      EnsureQueueExists(queueName);
      var writeToRoutingKey = queueName;

      var message = new Message(job.Id, MessageType.Request, job.Input, job.Metadata);
      try
      {
        message = _messageProcessor.Process(message, job);

        await _requestMQProducer.Produce(message, writeToRoutingKey, stoppingToken);
        _logger.LogDebug($"Published job {job.Id} at {queueName}");
      }
      catch (Exception e)
      {
        var errorPayload = new ErrorPayload(e.ToString(), "input_script_error", null);
        job.SetError(errorPayload);
        _logger.LogError(e, errorPayload.Type);
      }
    }

    public void EnsureQueueExists(string queueName)
    {
      if (_seenJobTypes.Contains(queueName)) return;

      _queueManager.BindQueueToTopicExchange(_configuration["Integration:Exchanges:Workers.In"], queueName, queueName);
      _seenJobTypes.Add(queueName);
    }

    [Obsolete("Prefer using EnsureQueueExists(string)")]
    public void EnsureQueueExists(string jobType, string jobProvider)
    {
      var queueName = $"{jobType}.{jobProvider}";
      EnsureQueueExists(queueName);
    }
  }
}