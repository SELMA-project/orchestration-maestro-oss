using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Selma.Orchestration.Models;

namespace Selma.Orchestration.TaskMQUtils
{
  public class MQWorker : MQBatchConsumer
  {
    private string _outputExchangeName;
    public MQProducer _producer;
    // private SemaphoreSlim semaphoreSlim = new SemaphoreSlim(1, 1);
    //private Dictionary<Guid, DateTime> lastNotifications = new Dictionary<Guid, DateTime>();
    private readonly bool _ignoreRedelivery;
    private readonly bool _requeueOnNetworkError;
    private readonly TimeSpan _workerTimeout;
    public object _state; //For use in side cars/workers that need presistent state across runs
    public object _stateLock;
    private Dictionary<Guid, DateTime> lastNotifications = new();


    public MQWorker(IConfiguration configuration, ILogger logger) : base(configuration, logger, "")
    {
      _configuration = configuration;
      _logger = logger;

      _rabbitMQUrl = _configuration["RabbitMQ:Url"];

      _inputQueueName = _configuration["InputQueue:Name"];
      // Batch size
      _prefetchCount = Convert.ToUInt16(_configuration["InputQueue:PrefetchCount"]);
      BatchSize = _prefetchCount;

      _outputExchangeName = _configuration["OutputExchange:Name"];
      _producer = new MQProducer(_outputExchangeName, _configuration, _logger);

      _ignoreRedelivery = configuration.GetValue("InputQueue:IgnoreRedelivery", false);
      _requeueOnNetworkError = configuration.GetValue("InputQueue:RequeueOnNetworkError", false);

      _workerTimeout = configuration.GetValue("InputQueue:WorkerTimeout", Timeout.InfiniteTimeSpan);
      if (_workerTimeout < TimeSpan.Zero) _workerTimeout = Timeout.InfiniteTimeSpan;

      _logger.LogDebug("Ignore redelivery: {IgnoreRedelivery}; Worker function timeout: {WorkerFunctionTimeout}",
                       _ignoreRedelivery,
                       _workerTimeout == Timeout.InfiniteTimeSpan ? "infinite" : _workerTimeout);

      _rmq_retries = 0;
      _state = null;
      _stateLock = new Object();
    }

    public async Task Run(Func<ImmutableList<MqMessage>, CancellationToken, MQWorker, Task<List<Result>>> batchConsumerLogic,
                          CancellationToken cancellationToken)
    {
      bool finished = false;
      var retries = 0;
      do
      {
        if (cancellationToken.IsCancellationRequested)
          break;

        try
        {
          SetUpChannel();
          _consumer = SetUpConsumer(batchConsumerLogic, cancellationToken);
          // Assert input queue and output exchange existence
          _channel.QueueDeclare(queue: _inputQueueName,
                               autoDelete: false,
                               durable: true,
                               exclusive: false);

          _channel.ExchangeDeclare(exchange: _outputExchangeName,
                                  type: ExchangeType.Topic,
                                  durable: true);

          StartConsumer();
          await cancellationToken;
          finished = true;
        }
        catch (RabbitMQ.Client.Exceptions.BrokerUnreachableException ex)
        {
          if (retries < maxRetries)
          {
            _logger.LogWarning($"Exception connecting to queue: {ex.Message}, retrying in {retryInterval}");
            await Task.Delay(retryInterval, cancellationToken);
            retries += 1;
          }
          else
          {
            _logger.LogError($"Maximum number of retries reached. Exiting program.\n: {ex.ToString()}");
            return;
          }
        }
        catch (OperationCanceledException)
        {
          if (!cancellationToken.IsCancellationRequested) throw;
          _logger.LogDebug("Received cancellation signal!");
          break;
        }
        finally
        {
          await _batchTimer.DisposeAsync();
        }
      } while (!finished);
      _logger.LogInformation("Finished Queue Listener...");
    }

    protected AsyncEventingBasicConsumer SetUpConsumer(Func<ImmutableList<MqMessage>, CancellationToken, MQWorker, Task<List<Result>>> consumerLogic,
                                                     CancellationToken cancellationToken)
    {
      _batchTimer = new Timer(async _ =>
      {
        _logger.LogInformation("Batch timeout reached: flushing...");

        try
        {
          await _semaphore.WaitAsync(cancellationToken);

          await ProcessBatch(consumerLogic, cancellationToken);
        }
        finally
        {
          _semaphore.Release();
        }
      });

      var consumer = new AsyncEventingBasicConsumer(_channel);

      consumer.Received += async (_, ea) =>
      {
        await Task.Yield();

        var body = ea.Body.ToArray();
        var message = Encoding.UTF8.GetString(body);
        var msg = JObject.Parse(message).ToObject<Message>();

        _logger.LogTrace($"Adding message {ea.DeliveryTag} to buffer (thread: {Thread.CurrentThread.Name} {Environment.CurrentManagedThreadId})...");

        try
        {
          await _semaphore.WaitAsync(cancellationToken);

          _messageBuffer.Add(new(ea.DeliveryTag, msg), default);
          _logger.LogTrace($"Added message {ea.DeliveryTag} to buffer");

          // todo: if possible expedite batch flushing when all messages in channel are already in the buffer
          // (note: no straightforward way to do this with the RabbitMQ client)
          if (_messageBuffer.Count >= BatchSize)
          {
            await ProcessBatch(consumerLogic, cancellationToken);
            return;
          }
        }
        finally
        {
          _semaphore.Release();
        }


        // start/reset timer
        _batchTimer.Change(_batchTimeout, Timeout.InfiniteTimeSpan);
        _logger.LogTrace($"Reset batch timer to {DateTime.Now + _batchTimeout:u}");
      };

      return consumer;
    }

    protected async Task ProcessBatch(Func<ImmutableList<MqMessage>, CancellationToken, MQWorker, Task<List<Result>>> batchConsumerLogic, CancellationToken cancellationToken)
    {
      try
      {
        // disable timer
        // _batchTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        // _logger.LogTrace("Stopped batch timer");


        _logger.LogDebug($"Processing batch... ({_messageBuffer.Count} messages)");

        // StopConsumer();

        List<Result> results = await batchConsumerLogic(_messageBuffer.Keys.ToImmutableList(), cancellationToken, this);

        foreach (var result in results)
        {
          switch (result)
          {
            case Success success:
              _logger.LogInformation($"Successfully Finished job for item {result.JobId} on queue {_inputQueueName}");
              await NotifyFinalResult(success.UnserializedResult, result.JobId, cancellationToken, success.BillingLog, result.Metadata);
              break;
            case Requeue:
              _logger.LogInformation($"Soft Error on queue {_inputQueueName}: Requeuing job for item {result.JobId}");
              break;
            case FatalError error:
              _logger.LogInformation($"Hard Error on queue {_inputQueueName}: Not Requeuing job for item {result.JobId}");
              await NotifyError(result.JobId, error.ErrorMessage, error.ErrorType, error.TraceId, cancellationToken, result.Metadata);
              break;
          }
        }

        // StartConsumer();
      }
      catch (OperationCanceledException)
      {
        _logger.LogInformation("Cancelled batch");
        return;
      }
      catch (Exception e)
      {
        _logger.LogError(e, $"Error during batch process: {e.Message}");
      }

      try
      {
        AcknowledgeMessages();
        ReportAcknowledgements();

        _messageBuffer.Clear();
        _logger.LogTrace("Cleared buffer.");
      }
      catch (Exception e)
      {
        _logger.LogError(e, $"Error during batch process: {e.Message}");
      }
    }


    public async Task NotifyError(Guid jobId, string errorMsg, string errorType, string traceId, CancellationToken stoppingToken, JToken metadata, string routingKey = "Error")
    {
      var msg = new Message(jobId, MessageType.Error, JObject.FromObject(new ErrorPayload(errorMsg, errorType, traceId)), metadata);
      await _producer.Produce(msg, routingKey, stoppingToken);
      _logger.LogDebug($"Produced error for jobid {jobId}");
    }

    public async Task NotifyFinalResult(object unserializedData, Guid jobId, CancellationToken stoppingToken, BillingLog billingLog, JToken metadata, string routingKey = "FinalResult")
    {
      var serializedResult = JObject.FromObject(unserializedData);
      var payload_out = new FinalResultPayload(serializedResult, billingLog);
      var serializedPayload = JObject.FromObject(payload_out);
      var response = new Message(jobId, MessageType.FinalResult, serializedPayload, metadata);
      await _producer.Produce(response, writeToRoutingKey: routingKey, stoppingToken);
      _logger.LogDebug($"Produced FinalResult for jobid {jobId}");
    }
  }


}
