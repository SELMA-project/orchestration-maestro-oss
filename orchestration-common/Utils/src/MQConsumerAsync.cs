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

namespace Selma.Orchestration.TaskMQUtils;

public class MQConsumerAsync : MQConsumer
{
  public MQConsumerAsync(IConfiguration configuration, ILogger logger, string consumptionQueueName, ushort prefetchCount = 1, string connectionTag = "")
      : base(configuration, logger, consumptionQueueName, prefetchCount, connectionTag)
  {
  }

  protected override ConnectionFactory CreateFactory()
  {
    var factory = base.CreateFactory();
    factory.DispatchConsumersAsync = true;
    _logger.LogInformation("using async dispatch consumers!");
    return factory;
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
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (ch, ea) =>
        {
          await Task.Yield();
          var body = ea.Body.ToArray();
          var message = Encoding.UTF8.GetString(body);
          var headers = ea.BasicProperties.Headers;
          var msg = JObject.Parse(message).ToObject<Message>();
          await consumerLogic(msg, cancelationToken);
          _channel.BasicAck(ea.DeliveryTag, multiple: false);
          // _logger.LogInformation($"acked message! {msg.Payload.ToObject<string>()}");
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
}
