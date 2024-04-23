using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RabbitMQ.Client;
using Selma.Orchestration.Models;

namespace Selma.Orchestration.TaskMQUtils
{
  public partial class MQProducer
  {
    private string _rabbitMQUrl { get; set; }
    private IConnection _connection;
    private IModel _channel;
    private IConfiguration _configuration;
    private ILogger _logger;
    private string _writeToExchangeTopic;
    private bool _connected;

    public MQProducer(string writeToExchangeTopic,// writing is done to a topic exchange
                          IConfiguration configuration,
                          ILogger logger)
    {
      _configuration = configuration;
      _logger = logger;
      _writeToExchangeTopic = writeToExchangeTopic;

      _rabbitMQUrl = _configuration["RabbitMQ:Url"];
      _connected = false;
    }
    public async Task Produce(object message,
                              string writeToRoutingKey,
                              CancellationToken stoppingToken)
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
          if (stoppingToken.IsCancellationRequested)
            break;
          if (!_connected) { Connect(); }
          _logger.LogTrace($"Publishing to exchange:{_writeToExchangeTopic} key:{writeToRoutingKey}");
          IBasicProperties props = _channel.CreateBasicProperties();
          props.Persistent = true;
          props.Type = "Q";
          var messageDict = JObject.FromObject(message);
          var messageString = messageDict.ToString(Formatting.None);
          var messageBytes = Encoding.UTF8.GetBytes(messageString);
          _channel.BasicPublish(exchange: _writeToExchangeTopic,
                                routingKey: writeToRoutingKey,
                                basicProperties: props,
                                body: messageBytes);
          finished = true;
        }
        catch (RabbitMQ.Client.Exceptions.BrokerUnreachableException ex)
        {
          if (rmq_retries < maxRetries)
          {
            _logger.LogWarning($"Exception connecting to queue: {ex.Message}, retrying in {retryIn}");
            await Task.Delay(retryIn, stoppingToken);
            rmq_retries += 1;
          }
          else
          {
            throw new Exception($"Maximum number of retries reached", ex);
          }
        }
      } while (!finished);
    }
    private void Connect()
    {
      ConnectionFactory factory = new ConnectionFactory();
      _logger.LogInformation($"Connecting to {_rabbitMQUrl}");
      factory.Uri = new Uri(_rabbitMQUrl);
      factory.AutomaticRecoveryEnabled = true;
      factory.NetworkRecoveryInterval = TimeSpan.FromSeconds(10);

      _connection = factory.CreateConnection();
      _channel = _connection.CreateModel();
      _channel.ExchangeDeclare(exchange: _writeToExchangeTopic,
                               type: RabbitMQ.Client.ExchangeType.Topic,
                               durable: true);
      _connected = true;

    }

  }
}