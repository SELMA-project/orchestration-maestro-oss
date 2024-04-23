using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Selma.Orchestration.TaskMQUtils
{
  /// <summary>
  /// A RabbitMQ producer with the ability to declare and bind a stream, optionally
  /// deleting it first.
  /// <seealso cref="MQProducer"/>
  /// <seealso cref="MQStreamConsumer{T}"/>
  /// </summary>
  public class MQStreamProducer : IDisposable
  {
    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;
    private IConnection _connection;
    private IModel _channel;

    public string StreamName { get; set; }
    public string RoutingKey { get; set; }
    public string ExchangeName { get; set; }

    private bool Connected => _connection?.IsOpen ?? false;


    public MQStreamProducer(ILogger logger,
                            IConfiguration configuration,
                            string exchangeName,
                            string streamName = "",
                            string routingKey = "")
    {
      _logger = logger;
      _configuration = configuration;

      StreamName = streamName;
      ExchangeName = exchangeName;
      RoutingKey = routingKey;
    }

    public async Task Produce(object message, CancellationToken stoppingToken)
    {
      await MqRetry(() =>
      {
        if (!Connected)
        {
          Connect();
          DeclareAndBindStream();
        }

        _logger.LogInformation("Publishing to exchange:{Exchange} key:{RoutingKey}", ExchangeName, RoutingKey);
        PublishMessage(message);
      }, stoppingToken);
    }

    /// <summary>
    /// Declare and bind the <c>StreamName</c> stream to the exchange.
    /// </summary>
    /// <remarks>Does nothing if <c cref="StreamName">StreamName</c> is null or empty.</remarks>
    /// <param name="forceDelete">If true, deletes the stream before declaring it.</param>
    /// <param name="stoppingToken" />
    public async Task DeclareStream(bool forceDelete = false, CancellationToken stoppingToken = default)
    {
      await MqRetry(() =>
      {
        if (string.IsNullOrWhiteSpace(StreamName)) return;
        
        if (!Connected)
          Connect();

        DeclareAndBindStream(forceDelete);
      }, stoppingToken);
    }

    private void PublishMessage(object mQMessage)
    {
      var message = JsonConvert.SerializeObject(mQMessage, Formatting.None);
      _logger.LogTrace("{MessagePayload}", message);

      var properties = _channel!.CreateBasicProperties();
      properties.Persistent = true;
      var body = Encoding.UTF8.GetBytes(message);

      _channel.BasicPublish(ExchangeName, RoutingKey, properties, body);
    }

    private void Connect()
    {
      var rabbitMQUrl = _configuration["RabbitMQ:Url"];
      var factory = new ConnectionFactory
      {
        Uri = new Uri(rabbitMQUrl),
        AutomaticRecoveryEnabled = true,
        NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
      };

      _logger.LogInformation("Connecting to {RabbitMQUrl}", rabbitMQUrl);
      _connection = factory.CreateConnection(GetType().Name);

      _channel = _connection.CreateModel();
      const ushort prefetchCount = 1; // Max 1 unacked msg on the channel
      _channel.BasicQos(prefetchSize: 0, prefetchCount, global: false);

      _channel.ExchangeDeclare(exchange: ExchangeName,
                               type: ExchangeType.Direct,
                               durable: true);
    }

    private void DeclareAndBindStream(bool forceDelete = false)
    {
      if (string.IsNullOrWhiteSpace(StreamName)) return;

      if (forceDelete)
      {
        _logger.LogDebug("Deleting {StreamName} if it exists", StreamName);
        _channel.QueueDelete(StreamName);
      }

      _channel.StreamDeclare(StreamName);
      _channel.QueueBind(StreamName, ExchangeName, RoutingKey);
      _logger.LogInformation("Declared {StreamName} and bound to {ExchangeName}; key {RoutingKey}", StreamName, ExchangeName, RoutingKey);
    }

    private async Task MqRetry(Action action, CancellationToken stoppingToken)
    {
      var rmqRetries = 0;
      var maxRetries = Convert.ToInt32(_configuration["RabbitMQ:ConnectionMaxRetries"]);
      var retryIn = TimeSpan.ParseExact(_configuration["RabbitMQ:ConnectionRetryIn"],
                                        "c",
                                        System.Globalization.CultureInfo.InvariantCulture);
      var finished = false;
      do
      {
        try
        {
          if (stoppingToken.IsCancellationRequested)
            break;

          action();

          finished = true;
        }
        catch (BrokerUnreachableException ex)
        {
          if (rmqRetries < maxRetries)
          {
            _logger.LogWarning(ex, "Exception connecting to stream: {ExceptionMessage}, retrying in {RetryTime}", ex.Message, retryIn);
            await Task.Delay(retryIn, stoppingToken);
            rmqRetries += 1;
          }
          else
          {
            throw new Exception("Maximum number of retries reached", ex);
          }
        }
      } while (!finished);
    }

    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
      if (!disposing) return;

      _channel?.Dispose();
      _connection?.Dispose();
    }
  }
}
