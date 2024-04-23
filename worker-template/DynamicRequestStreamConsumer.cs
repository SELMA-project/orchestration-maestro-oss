using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Selma.Orchestration.Models;
using Selma.Orchestration.OrchestrationDB;
using Selma.Orchestration.TaskMQUtils;

namespace Selma.Orchestration
{
  /// <summary>
  /// <para>Uses a stream to receive notifications about which queues to subscribe
  /// or unsubscribe.</para>
  /// <para>Spawns a new MQWorker when their service is added.<br/>
  /// Cancels existing MQWorkers when their service is removed.</para>
  /// </summary>
  public class DynamicRequestStreamConsumer : IHostedService, IRequestQueueConsumer
  {
    private readonly ILogger<DynamicRequestStreamConsumer> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _client;
    private const string ServiceName = nameof(DynamicRequestStreamConsumer);

    public static Func<ILogger, IConfiguration, HttpClient, ImmutableList<MqMessage>, CancellationToken, MQWorker, Task<List<Result>>> WorkerFunction;

    private JobFilter _jobFilter;
    private readonly Dictionary<string, (Task task, CancellationTokenSource token)> _mqWorkers = new();
    private readonly Dictionary<string, int> _buffer = new();
    private MQStreamConsumer<ServiceEvent> _consumer;


    public DynamicRequestStreamConsumer(IConfiguration configuration,
                                        ILogger<DynamicRequestStreamConsumer> logger,
                                        HttpClient client)
    {
      _logger = logger;
      _configuration = configuration;
      _client = client;
      _client.Timeout = TimeSpan.ParseExact(configuration["HttpClientTimeOut"], "c", CultureInfo.InvariantCulture);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
      ValidateConfiguration();

      _logger.LogInformation($"{ServiceName} is starting. {GetHashCode()}");

      var inputStreamName = _configuration["InputQueue:Name"];
      _logger.LogInformation($"Input stream: {inputStreamName}; Filter: {_jobFilter}");

      cancellationToken.Register(() =>
      {
        // If needed, signal whatever wait you're stuck at to exit service
        _logger.LogWarning($"{ServiceName} background task is stopping.");
      });

      // init service consumer
      _consumer = new(_configuration, _logger, inputStreamName, connectionTag: _jobFilter.Runtime);
      _consumer.NewMessage += HandleNewMessage;
      _consumer.ExistingMessage += HandleExistingMessage;
      _consumer.Ready += Flush;
      _consumer.Stopped += HandleConsumerStopped;

      await _consumer.Run(null, cancellationToken);

      _logger.LogWarning($"{ServiceName} execution task exiting...");
    }

    private void ValidateConfiguration()
    {
      _jobFilter = _configuration.GetSection("JobFilter").Get<JobFilter>();

      if (_jobFilter is null)
      {
        // backwards compatibility, attempt to use worker type
        var workerType = _configuration.GetValue<string>("WorkerType");
        if (!string.IsNullOrWhiteSpace(workerType))
        {
          _jobFilter = new JobFilter { Runtime = workerType };
          _logger.LogWarning("'WorkerType' config option is deprecated. Prefer using 'JobFilter.Runtime'");
        }
        else
        {
          const string msg = "'JobFilter' configuration missing: exiting...";
          _logger.LogCritical(msg);
          throw new ApplicationException(msg);
        }
      }

      // should at least filter by Runtime and/or (Type and Provider)
      if (_jobFilter.AcceptsAnyJob())
      {
        _logger.LogWarning("Empty JobFilter: Consumer will bind to any queue (this is probably not what you want, review JobFilter configuration)");
      }
    }


    /// <summary> Called when we receive a message that was already present on the stream when we first connected. </summary>
    private void HandleExistingMessage(object sender, ServiceEvent message)
    {
      if (!_jobFilter.Validate(message.Data))
      {
        _logger.LogTrace($"Ignored old message: {message}");
        return;
      }

      _logger.LogDebug($"Received old message: {message}");
      var queueName = message.Data.QueueName();

      if (!_buffer.ContainsKey(queueName))
        _buffer[queueName] = 0;

      _buffer[queueName] += message.Operation switch
      {
        ServiceEventOperation.Add => 1,
        ServiceEventOperation.Remove => -1,
        _ => 0
      };
    }

    /// <summary> Called when we receive a new message that was not present on the stream when we first connected. </summary>
    private void HandleNewMessage(object sender, ServiceEvent message)
    {
      var jobInfo = message.Data;

      if (!_jobFilter.Validate(jobInfo))
      {
        _logger.LogTrace($"Ignored new message: {message}");
        return;
      }

      _logger.LogDebug($"Received new msg: {message}");

      switch (message.Operation)
      {
        case ServiceEventOperation.Add:
          RunWorker(jobInfo.QueueName());
          break;
        case ServiceEventOperation.Remove:
          CancelWorker(jobInfo.QueueName());
          break;
        default:
          _logger.LogWarning($"Ignoring message with unknown operation: {message.Operation}");
          break;
      }
    }

    private void Flush(object sender, EventArgs _)
    {
      _logger.LogInformation("Received last existing message from stream.");

      ValidateBuffer();

      if (_buffer.Values.Any(v => v is < 0 or > 1))
      {
        _logger.LogWarning("Inconsistent stream state: one or more services were removed or added twice.");
      }


      var workers = _buffer.Where(kv => kv.Value >= 1) // for robustness, allow repeated worker launch requests from the notifications queue
                           .Select(kv => kv.Key)
                           .ToList();

      _logger.LogDebug($"Starting {workers.Count} new workers");
      workers.ForEach(RunWorker);

      _consumer.Resume();
    }

    /// <summary>
    /// Removes running workers from the list of workers to start.
    /// Cancels workers that are not on the 'workers to start' list.
    /// </summary>
    private void ValidateBuffer()
    {
      if (!_mqWorkers.Any()) return;

      foreach (var queueName in _mqWorkers.Keys)
      {
        if (_buffer.ContainsKey(queueName))
        {
          _buffer[queueName] = 0;
          _logger.LogTrace($"Ignored worker {queueName}: already running");
        }
        else
        {
          _logger.LogTrace($"Cancelling worker {queueName}: already running");
          CancelWorker(queueName);
        }
      }
    }

    private void HandleConsumerStopped(object sender, EventArgs _)
    {
      _buffer.Clear();
    }

    public void RunWorker(string queueName)
    {
      if (_mqWorkers.ContainsKey(queueName))
      {
        _logger.LogWarning($"Attempted to add worker that already exists at queue {queueName}");
        return;
      }

      _logger.LogInformation($"Starting worker at '{queueName}'");

      Func<ImmutableList<MqMessage>, CancellationToken, MQWorker, Task<List<Result>>> PartiallyAppliedWorkerFunction = (messages, cancellationToken, worker)
          => WorkerFunction(_logger, _configuration, _client, messages, cancellationToken, worker);

      var config = new ConfigurationBuilder().AddConfiguration(_configuration).Build();
      config["InputQueue:Name"] = queueName;
      config["InputQueue:IsStream"] = false.ToString();

      var mQWorker = new MQWorker(config, _logger)
      {
        ConnectionTag = _jobFilter.Runtime
      };
      var token = new CancellationTokenSource();
      var task = mQWorker.Run(PartiallyAppliedWorkerFunction, token.Token);
      _mqWorkers.Add(queueName, (task, token));
    }

    public void CancelWorker(string queueName)
    {
      if (!_mqWorkers.ContainsKey(queueName))
      {
        _logger.LogWarning($"Attempted to remove non existing worker at queue {queueName}.");
        return;
      }

      _logger.LogInformation($"Stopping worker at '{queueName}'");
      var (task, tokenSource) = _mqWorkers[queueName];

      tokenSource?.Cancel();
      tokenSource?.Dispose();
      _mqWorkers.Remove(queueName);
    }

#pragma warning disable 1998
    public async Task StopAsync(CancellationToken cancellationToken)
    {
      _logger.LogWarning($"{ServiceName} stopping...");
      try
      {
        // Run your graceful clean-up actions
        _consumer.ExistingMessage -= HandleExistingMessage;
        _consumer.NewMessage -= HandleNewMessage;
        _consumer.Ready -= Flush;
        _consumer.Stopped -= HandleConsumerStopped;

        if (!_mqWorkers.Any())
        {
          _logger.LogInformation("No workers running");
          return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation($"Cancelling {_mqWorkers.Count} workers...");
        _mqWorkers.Keys.ToList().ForEach(CancelWorker);

        const int millisecondsTimeout = 10000;
        if (!Task.WaitAll(_mqWorkers.Values.Select(x => x.task).ToArray(), millisecondsTimeout, cancellationToken))
        {
          throw new OperationCanceledException($"Not all worker task instances completed in {millisecondsTimeout}ms");
        }

        _logger.LogInformation($"{ServiceName} shutdown completed.");
        return;
      }
      catch (OperationCanceledException)
      {
      }
      _logger.LogWarning($"{ServiceName} shutdown aborted!");
      // Run your ungraceful shutdown actions
    }

#pragma warning restore 1998
  }
}
