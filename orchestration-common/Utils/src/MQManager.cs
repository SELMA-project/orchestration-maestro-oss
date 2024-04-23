using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Selma.Orchestration.TaskMQUtils
{
  public class MQManager
  {
    private string _rabbitMQUrl { get; set; }
    private IConnection _connection;
    private IModel _channel;
    private IConfiguration _configuration;
    private ILogger _logger;
    bool _connected;

    public MQManager(IConfiguration configuration, ILogger logger)
    {
      _configuration = configuration;
      _logger = logger;
      _connected = false;
      _rabbitMQUrl = _configuration["RabbitMQ:Url"];
    }

    private void Connect()
    {
      ConnectionFactory factory = new ConnectionFactory();
      factory.Uri = new Uri(_rabbitMQUrl);
      factory.AutomaticRecoveryEnabled = true;
      factory.NetworkRecoveryInterval = TimeSpan.FromSeconds(10);
      _connection = factory.CreateConnection();
      _channel = _connection.CreateModel();
      _connected = true;
    }


    public void BindQueueToTopicExchange(string exchangeName,
                                         string queueName,
                                         string routingKeyName,
                                         bool durableQueue = true,
                                         bool autoDeleteQueue = false,
                                         bool exclusiveQueue = false,
                                         IDictionary<string, object> queueArguments = null)

    {
      var rmq_retries = 0;
      var maxRetries = Convert.ToInt32(_configuration["RabbitMQ:ConnectionMaxRetries"]);
      TimeSpan retryIn = TimeSpan.ParseExact(_configuration["RabbitMQ:ConnectionRetryIn"],
                                             "c",
                                             System.Globalization.CultureInfo.InvariantCulture);
      bool finished = false;
      do
      {
        try
        {
          if (!_connected) { Connect(); }

          _channel.ExchangeDeclare(exchange: exchangeName,
                                   type: RabbitMQ.Client.ExchangeType.Topic,
                                   durable: true);
          _channel.QueueDeclare(queue: queueName,
                                autoDelete: autoDeleteQueue,
                                durable: durableQueue,
                                exclusive: exclusiveQueue,
                                arguments: queueArguments);
          // Binding between and exchange and each target queues is made via a routing key, per queue.
          _channel.QueueBind(queueName,
                             exchangeName,
                             routingKeyName);
          finished = true;
        }
        catch (BrokerUnreachableException ex)
        {
          if (rmq_retries < maxRetries)
          {
            _logger.LogError($"Exception connecting: {ex.Message}, retrying in {retryIn}. URL: {_rabbitMQUrl}");
            //await Task.Delay(retryIn, stoppingToken);
            Thread.Sleep(retryIn);
            rmq_retries += 1;
          }
          else
          {
            throw new Exception($"Maximum number of retries reached", ex);
          }
        }
      } while (!finished);
    }
    public bool QueueExists(string queueName)
    {
      return MQExtensions.QueueExists(_connection, queueName);
    }


    public async Task DeclareQueue(string queueName, CancellationToken stoppingToken = default)
    {
      var rmq_retries = 0;
      var maxRetries = Convert.ToInt32(_configuration["RabbitMQ:ConnectionMaxRetries"]);
      var retryIn = TimeSpan.ParseExact(_configuration["RabbitMQ:ConnectionRetryIn"],
                                        "c",
                                        System.Globalization.CultureInfo.InvariantCulture);
      var finished = false;
      do
      {
        try
        {
          if (!_connected) { Connect(); }

          _channel.QueueDeclare(queue: queueName,
                                autoDelete: false,
                                durable: true,
                                exclusive: false);
          finished = true;
        }
        catch (BrokerUnreachableException ex)
        {
          if (rmq_retries < maxRetries)
          {
            _logger.LogError($"Exception connecting: {ex.Message}, retrying in {retryIn}. URL: {_rabbitMQUrl}");
            try
            {
              await Task.Delay(retryIn, stoppingToken);
              rmq_retries += 1;
            }
            catch (OperationCanceledException)
            {
              finished = true;
            }
          }
          else
          {
            throw new Exception("Maximum number of retries reached", ex);
          }
        }
      } while (!finished);
    }
  }
}
