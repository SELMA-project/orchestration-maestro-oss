using System;
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
  // Use Consumer<Message> for a result consumer (e.g., integration/orchestration)
  // Use Consumer<Message> for a request consumer (e.g., a worker)
  public class MQConsumer : BaseMQConsumer, IMQConsumer, ISequentialMQConsumer
  {
    public MQConsumer(IConfiguration configuration,
                      ILogger logger,
                      string consumptionQueueName,
                      ushort prefetchCount = 1,
                      string connectionTag = "")
    {
      _configuration = configuration;
      _logger = logger;
      _inputQueueName = consumptionQueueName;
      _rabbitMQUrl = _configuration["RabbitMQ:Url"];
      _rmq_retries = 0;
      _prefetchCount = prefetchCount;
      ConnectionTag = connectionTag;
    }


    public void CreateConnection()
    {
      var factory = CreateFactory();
      _connection = factory.CreateConnection(GetConnectionName());
      _logger.LogInformation($"Connected ({_connection})");
    }

    protected virtual ConnectionFactory CreateFactory()
    {
      return new ConnectionFactory
      {
        Uri = new Uri(_rabbitMQUrl),
        AutomaticRecoveryEnabled = true,
        NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
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
      _channel.BasicQos(0, _prefetchCount, false);
      _logger.LogInformation($"Created channel ({_channel})");
    }

    public void SetUpChannel()
    {
      CreateConnection();
      CreateChannel();
    }

    public bool QueueExists(string queueName)
    {
      return MQExtensions.QueueExists(_connection, queueName);
    }

    public int MessageCount(string queueName)
    {
      return (int)_channel.QueueDeclarePassive(queue: queueName)
                          .MessageCount;
    }

    public async Task Run(Func<Message, CancellationToken, Task> consumerLogic,
                          CancellationToken cancelationToken)
    {
      TimeSpan retryIn = TimeSpan.ParseExact(_configuration["RabbitMQ:ConnectionRetryIn"],
                                             "c",
                                             System.Globalization.CultureInfo.InvariantCulture);
      maxRetries = Convert.ToInt32(_configuration["RabbitMQ:ConnectionMaxRetries"]);

      bool finished = false;
      _rmq_retries = 0;
      do
      {
        if (cancelationToken.IsCancellationRequested)
          break;

        try
        {
          SetUpChannel();
          var consumer = new EventingBasicConsumer(_channel);
          consumer.Received += async (ch, ea) =>
          {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            var headers = ea.BasicProperties.Headers;
            var msg = JObject.Parse(message).ToObject<Message>();
            await consumerLogic(msg, cancelationToken);
            _channel.BasicAck(ea.DeliveryTag, multiple: false);
            // _logger.LogInformation($"acked message {msg.Payload.ToObject<string>()}");
          };
          var tag = _channel.BasicConsume(_inputQueueName, false, consumer);
          _logger.LogInformation($"Listening to queue: {_inputQueueName} (tag: {tag})");
          await cancelationToken;
          finished = true;
        }
        catch (RabbitMQ.Client.Exceptions.BrokerUnreachableException ex)
        {
          if (_rmq_retries < maxRetries)
          {
            _logger.LogWarning($"Exception connecting to queue: {ex.Message}, retrying in {retryIn}");
            await Task.Delay(retryIn, cancelationToken);
            _rmq_retries += 1;
          }
          else
          {
            _logger.LogError($"Maximum number of retries reached. Exiting program.\n: {ex.ToString()}");
            return;
          }
        }
        catch (OperationCanceledException)
        {
          if (cancelationToken.IsCancellationRequested)
            break;
        }
      } while (!finished);
      _logger.LogInformation("Finished Queue Listener...");
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
  }

}
