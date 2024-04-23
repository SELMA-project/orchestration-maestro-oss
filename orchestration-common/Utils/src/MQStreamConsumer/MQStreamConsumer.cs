using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Selma.Orchestration.TaskMQUtils
{

  [Flags]
  public enum LogicMode
  {
    None = 0,
    NewMessage = 1,
    ExistingMessage = 2
  }

  public class MQStreamConsumer<T> : MQConsumer
  {
    private readonly LogicMode _logicMode;

    private EventingBasicConsumer _consumer;
    private Func<T, CancellationToken, Task> _consumerLogic;
    private CancellationToken _cancellationToken;

    private int _existingMessageCount;
    private int _messageIndex;
    private string _tag;
    private bool _lastExistingMessage;
    private readonly int _eventConsumerStartDelayMs;

    public MQStreamConsumer(IConfiguration configuration,
                            ILogger logger,
                            string consumptionQueueName,
                            ushort prefetchCount = 1, // I think this must be 1 (ama)
                            string connectionTag = "")
        : base(configuration, logger, consumptionQueueName, 1, connectionTag)
    {
      _existingMessageCount = 0;
      _messageIndex = 0;
      _tag = string.Empty;
      _logicMode = configuration["LogicMode"] != null ? Enum.Parse<LogicMode>(configuration["LogicMode"]) : LogicMode.None;
      _lastExistingMessage = false;
      _eventConsumerStartDelayMs = configuration.GetValue("StartDelayMs", 2000);
    }

    private Dictionary<string, object> ChannelConsumeArguments => new() { { "x-stream-offset", _messageIndex } };

    private string StreamName
    {
      get => _inputQueueName;
      set => _inputQueueName = value;
    }

    private bool IsReceivingNewMessages => _messageIndex >= _existingMessageCount;

    public event EventHandler Ready;
    public event EventHandler<T> NewMessage;
    public event EventHandler<T> ExistingMessage;
    public event EventHandler Stopped;


    /// <summary>
    ///   Stops consuming messages from the stream.
    /// </summary>
    public void Stop()
    {
      if (!_consumer.IsRunning)
      {
        _logger.LogTrace("Ignored Stop() (not running)");
        return;
      }
      _channel.BasicCancel(_tag);
      _logger.LogTrace("Stopping...");
    }

    /// <summary>
    ///   Starts, or resumes, consuming messages from the stream. 
    /// </summary>
    public void Start()
    {
      if (_consumer is null || _channel is null)
      {
        InitializeConsumer();
      }

      if (_consumer.IsRunning)
      {
        _logger.LogTrace("Ignored Start() (already running)");
        return;
      }

      if (!string.IsNullOrEmpty(_tag))
      {
        Resume();
      }
      else
      {
        StartImpl();
      }
    }

    private void StartImpl()
    {

      _logger.LogTrace("Starting...");

      Thread.Sleep(2000);

      _existingMessageCount = (int)_connection.GetQueueInfo(StreamName).MessageCount;
      _logger.LogTrace($"existing msg count: {_existingMessageCount}");

      // empty stream, notify last existing message
      if (IsReceivingNewMessages) _lastExistingMessage = true;

      _tag = _channel.BasicConsume(StreamName, false, _tag ?? "", ChannelConsumeArguments, _consumer);
    }


    public void Resume()
    {
      if (_consumer.IsRunning)
      {
        _logger.LogTrace("Ignored Resume() (already running)");
        return;
      }
      _logger.LogTrace("Resuming...");

      _tag = _channel.BasicConsume(StreamName, false, _tag ?? "", ChannelConsumeArguments, _consumer);
    }

    /// <summary>
    /// Stops consuming messages from the stream and resets the message index.
    /// </summary>
    public void Reset()
    {
      _logger.LogTrace("Resetting...");
      if (_consumer.IsRunning)
      {
        Stop();
      }
      _messageIndex = 0;
      _tag = "";
    }

    private void Shutdown()
    {
      _channel.Abort(Constants.NotFound, "The stream was deleted");
      try
      {
        _channel.DisposeWithTimeout(TimeSpan.FromSeconds(1));
      }
      catch (Exception e)
      {
        Console.WriteLine(e);
      }
      _channel = null;
      _consumer = null;
    }


    public async Task Run(Func<T, CancellationToken, Task> consumerLogic, CancellationToken cancellationToken)
    {
      _cancellationToken = cancellationToken;
      _consumerLogic = consumerLogic;

      var retryIn = TimeSpan.ParseExact(_configuration["RabbitMQ:ConnectionRetryIn"],
                                        "c",
                                        CultureInfo.InvariantCulture);
      maxRetries = Convert.ToInt32(_configuration["RabbitMQ:ConnectionMaxRetries"]);

      var finished = false;
      _rmq_retries = 0;

      do
      {
        if (cancellationToken.IsCancellationRequested)
        {
          _logger.LogWarning("Cancellation requested");
          break;
        }

        try
        {
          try
          {
            CreateConnection();
            SetUpBrokerEvents();

            InitializeConsumer();

            if (_connection.QueueExists(StreamName))
              Start();
            else
              _logger.LogWarning($"Stream '{StreamName}' does not exist: idling...");


            await cancellationToken;
            finished = true;
          }
          catch (BrokerUnreachableException ex)
          {
            if (_rmq_retries < maxRetries)
            {
              _logger.LogWarning($"Exception connecting to stream: {ex.Message}, retrying in {retryIn}");
              await Task.Delay(retryIn, cancellationToken);
              _rmq_retries += 1;
            }
            else
            {
              _logger.LogError($"Maximum number of retries reached. Exiting program.\n: {ex}");
              return;
            }
          }
        }
        catch (OperationCanceledException)
        {
          if (cancellationToken.IsCancellationRequested)
          {
            _logger.LogWarning("Cancellation requested");
            break;
          }
        }
      } while (!finished);
      _logger.LogInformation("Finished Stream Listener...");
    }




    /// <summary>
    /// Fires when _channel.BasicConsume() succeeds
    /// </summary>
    private void OnConsumerRegistered(object _, ConsumerEventArgs args)
    {
      _logger.LogInformation($"Started consuming from stream: {StreamName} (tags: {string.Join(',', args.ConsumerTags)})");

      _existingMessageCount = (int)_channel.MessageCount(StreamName);
      _logger.LogTrace($"existing msg count: {_existingMessageCount}");

      if (_lastExistingMessage)
      {
        _lastExistingMessage = false;
        Ready?.Invoke(this, EventArgs.Empty);
      }
    }

    /// <summary>
    /// Fires when _channel.BasicCancel() succeeds
    /// </summary>
    private void OnConsumerUnregistered(object _, ConsumerEventArgs args)
    {
      _logger.LogInformation($"Stopped consuming from stream: {StreamName} (tags: {string.Join(',', args.ConsumerTags)})");

      if (_lastExistingMessage)
      {
        _lastExistingMessage = false;
        Ready?.Invoke(this, EventArgs.Empty);
      }
    }

    /// <summary>
    /// Fires when the channel is closed (_channel.Abort())
    /// </summary>
    private void OnConsumerShutdown(object o, ShutdownEventArgs shutdownEventArgs)
    {
      _messageIndex = 0;
      _tag = "";

      Stopped?.Invoke(this, EventArgs.Empty);
    }

    private async void OnConsumerReceived(object ch, BasicDeliverEventArgs ea)
    {
      var body = ea.Body.ToArray();
      var message = Encoding.UTF8.GetString(body);
      var msg = JObject.Parse(message).ToObject<T>();

      if (IsReceivingNewMessages)
      {
        _logger.LogDebug($"OnReceivedNewMessage({ea.BasicProperties.Headers["x-stream-offset"]})");
        NewMessage?.Invoke(this, msg);

        if (_logicMode.HasFlag(LogicMode.NewMessage))
          await _consumerLogic(msg, _cancellationToken);
      }
      else
      {
        _logger.LogDebug($"OnReceivedExistingMessage({ea.BasicProperties.Headers["x-stream-offset"]})");
        ExistingMessage?.Invoke(this, msg);


        if (_messageIndex + 1 == _existingMessageCount)
          _lastExistingMessage = true;

        if (_logicMode.HasFlag(LogicMode.ExistingMessage))
          await _consumerLogic(msg, _cancellationToken);
      }

      ++_messageIndex;

      if (_lastExistingMessage)
      {
        Stop();
      }

      _channel.BasicAck(ea.DeliveryTag, false);
    }


    private void InitializeConsumer()
    {
      if (_channel is not null)
      {
        Shutdown();
      }

      CreateChannel();

      _consumer = new EventingBasicConsumer(_channel);

      _consumer.Received += OnConsumerReceived;
      _consumer.Registered += OnConsumerRegistered;
      _consumer.Unregistered += OnConsumerUnregistered;
      _consumer.Shutdown += OnConsumerShutdown;

    }

    private void SetUpBrokerEvents()
    {
      try
      {
        new BrokerEventConsumer(_logger, _connection, StreamName, queueCreatedCallback: () =>
        {
          // allow the stream to be initialized with 'existing' messages
          if(_eventConsumerStartDelayMs > 0)
          {
            Thread.Sleep(TimeSpan.FromMilliseconds(_eventConsumerStartDelayMs));
          }

          _logger.LogWarning("StreamConsumer: Received created event");
          Start();
        }, queueDeletedCallback: () =>
        {
          _logger.LogWarning("StreamConsumer: Received deleted event");
          Shutdown();
        });
        _logger.LogTrace("Created broker event consumer");
      }
      catch (OperationInterruptedException e)
      {
        _logger.LogWarning($"Could not find broker event exchange. Will not be able to detect when {StreamName} is deleted. Is the event exchange plugin enabled?");
        _logger.LogTrace(e.Message);
      }
    }


  }

}
