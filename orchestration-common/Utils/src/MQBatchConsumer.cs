using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Selma.Orchestration.Models;

namespace Selma.Orchestration.TaskMQUtils;

public class MQBatchConsumer : BaseMQConsumer, IMQConsumer, IBatchMQConsumer
{
  protected const MessageStatus DefaultAck = MessageStatus.Requeue;
  protected readonly SemaphoreSlim _semaphore = new(1);
  protected readonly TimeSpan _batchTimeout;
  protected Timer _batchTimer;
  public ushort BatchSize { get; protected set; }
  public int Concurrency { get; }
  protected Dictionary<MqMessage, MessageStatus?> _messageBuffer;
  protected string _consumerTag;
  protected AsyncEventingBasicConsumer _consumer;

  public MQBatchConsumer(IConfiguration configuration,
                         ILogger logger,
                         string consumptionQueueName,
                         ushort batchSize = 1,
                         int concurrency = 2,
                         string connectionTag = "")
  {
    _logger = logger;
    BatchSize = batchSize;
    Concurrency = concurrency;

    _rabbitMQUrl = configuration["RabbitMQ:Url"];
    _inputQueueName = consumptionQueueName;

    _batchTimeout = TimeSpan.FromHours(1);

    retryInterval = TimeSpan.ParseExact(configuration["RabbitMQ:ConnectionRetryIn"],
                                         "c",
                                         System.Globalization.CultureInfo.InvariantCulture);
    maxRetries = Convert.ToInt32(configuration["RabbitMQ:ConnectionMaxRetries"]);

    _messageBuffer = new();

    ConnectionTag = connectionTag;

  }

  public MQBatchConsumer(IConfiguration configuration,
                         ILogger logger,
                         string consumptionQueueName,
                         TimeSpan batchTimeout,
                         ushort batchSize = 1,
                         int concurrency = 2,
                         string connectionTag = "") : this(configuration, logger, consumptionQueueName, batchSize, concurrency, connectionTag)
  {
    _batchTimeout = batchTimeout;
  }


  public void CreateConnection()
  {
    var factory = CreateFactory();
    _connection = factory.CreateConnection(GetConnectionName());
    _logger.LogInformation($"Connected ({_connection})");
  }

  public virtual ConnectionFactory CreateFactory()
  {
    return new ConnectionFactory
    {
      Uri = new Uri(_rabbitMQUrl),
      AutomaticRecoveryEnabled = true,
      NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
      DispatchConsumersAsync = true,
      ConsumerDispatchConcurrency = Concurrency
    };
  }

  public void CloseConnection()
  {
    _logger.LogInformation($"Closing connection ({_connection})");
    _connection.Close();
    _logger.LogInformation($"Closing Channel ({_channel})");
    _channel.Close();
  }

  public void CreateChannel()
  {
    _logger.LogDebug("SetUpChannel!");
    _channel = _connection.CreateModel();
    _channel.BasicQos(0, BatchSize, false);
    _logger.LogInformation($"Created channel ({_channel})");
  }

  public void SetUpChannel()
  {
    CreateConnection();
    CreateChannel();
  }

  public bool QueueExists(string queueName)
    => _connection.QueueExists(_inputQueueName);

  public int MessageCount(string queueName)
    => (int)_connection.GetQueueInfo(queueName).MessageCount;

  public async Task Run(Func<ImmutableList<MqMessage>, MQBatchConsumer, CancellationToken, Task> batchConsumerLogic,
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

  protected AsyncEventingBasicConsumer SetUpConsumer(Func<ImmutableList<MqMessage>, MQBatchConsumer, CancellationToken, Task> consumerLogic,
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

  protected async Task ProcessBatch(Func<ImmutableList<MqMessage>, MQBatchConsumer, CancellationToken, Task> batchConsumerLogic, CancellationToken cancellationToken)
  {
    try
    {
      // disable timer
      // _batchTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
      // _logger.LogTrace("Stopped batch timer");


      _logger.LogDebug($"Processing batch... ({_messageBuffer.Count} messages)");

      // StopConsumer();

      await batchConsumerLogic(_messageBuffer.Keys.ToImmutableList(), this, cancellationToken);


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

  protected void AcknowledgeMessages()
  {
    foreach (var (message, s) in _messageBuffer)
    {
      var status = s ?? DefaultAck;
      if (status == MessageStatus.Ack)
      {
        _channel.BasicAck(message.DeliveryTag, multiple: false);
      }
      else
      {
        _channel.BasicNack(message.DeliveryTag, multiple: false, requeue: status == MessageStatus.Requeue);
      }
    }
  }

  protected void ReportAcknowledgements()
  {
    var missingAcknowledgements = _messageBuffer.Count(m => m.Value is null);
    if (missingAcknowledgements > 0)
    {
      _logger.LogWarning($"Client did not acknowledge {missingAcknowledgements}/{_messageBuffer.Count} messages. Defaulting to {DefaultAck}...");
    }

    var ackStats = _messageBuffer.GroupBy(x => x.Value, x => x.Key)
                                 .ToDictionary(k => k.Key ?? DefaultAck, v => v.Count())
                                 .Select(kv => $"{kv.Key}: {kv.Value}");
    _logger.LogDebug($"Acknowledged messages: {string.Join("; ", ackStats)}");
  }

  protected void StartConsumer()
  {
    _consumerTag = _channel.BasicConsume(_inputQueueName, false, _consumer);
    _logger.LogDebug($"Started consumer (queue: {_inputQueueName} ; tag: {_consumerTag})");
  }

  protected void StopConsumer()
  {
    _channel.BasicCancel(_consumerTag);
    _logger.LogDebug($"Stopped consumer (tag: {_consumerTag})");
  }

  public string GetConnectionName()
  {
    var sb = new StringBuilder(GetType().Name);
    if (!string.IsNullOrWhiteSpace(_inputQueueName))
      sb.AppendFormat(" ({0})", _inputQueueName);

    if (!string.IsNullOrWhiteSpace(ConnectionTag))
      sb.AppendFormat(" - {0}", ConnectionTag);

    return sb.ToString();
  }

  public void SetMessageStatus(MqMessage mqMessage, MessageStatus status)
  {
    if (!_messageBuffer.ContainsKey(mqMessage))
    {
      throw new InvalidOperationException("Cannot set message status: not in message buffer");
    }
    _messageBuffer[mqMessage] = status;
  }

  public MessageStatus? GetMessageStatus(MqMessage mqMessage)
  {
    if (!_messageBuffer.ContainsKey(mqMessage))
    {
      throw new InvalidOperationException("Cannot set message status: not in message buffer");
    }
    return _messageBuffer[mqMessage];
  }
}


public record MqMessage(ulong DeliveryTag, Message Message);

public enum MessageStatus
{
  Requeue,
  Ack,
  Nack,
}
