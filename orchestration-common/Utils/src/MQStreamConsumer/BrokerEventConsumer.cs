using System;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Selma.Orchestration.TaskMQUtils
{
  /// <summary>
  /// A consumer that listens for `queue.created` and `queue.deleted` events on
  /// the `amq.rabbitmq.event` queue. Requires the `rabbitmq-event-exchange`
  /// plugin running on the broker.
  /// </summary>
  public class BrokerEventConsumer : EventingBasicConsumer
  {
    private const string EventsExchange = "amq.rabbitmq.event";
    private const string EventsRoutingKey = "queue.*";
    private const string EventFanoutExchange = "fanout.events.queue";
    private readonly ILogger _logger;
    private readonly Action _queueDeletedCallback;
    private readonly Action _queueCreatedCallback;
    private string InputQueue { get; init; }


    public BrokerEventConsumer(ILogger logger, IConnection connection, string inputQueue, string brokerEventExchange = EventsExchange, Action queueCreatedCallback = null, Action queueDeletedCallback = null) : base(null)
    {
      _logger = logger;
      _queueDeletedCallback = queueDeletedCallback;
      _queueCreatedCallback = queueCreatedCallback;
      InputQueue = inputQueue;

      Model = connection.CreateModel();
      Model.BasicQos(0, 1, false);

      Received += OnReceived;

      var queueInfo = Model.QueueDeclare(exclusive: false);
      Model.ExchangeDeclarePassive(brokerEventExchange);

      // use intermediate exchange so more than one worker can receive the events
      Model.ExchangeDeclare(EventFanoutExchange, ExchangeType.Fanout);
      Model.ExchangeBind(EventFanoutExchange, brokerEventExchange, EventsRoutingKey);

      Model.QueueBind(queueInfo.QueueName, EventFanoutExchange, EventsRoutingKey);
      Model.BasicConsume(queueInfo.QueueName, false, this);
    }


    private void OnReceived(object sender, BasicDeliverEventArgs args)
    {
      try
      {
        var routingKey = args.RoutingKey;
        var eventType = routingKey.Split('.')[1];

        var headers = args.BasicProperties.Headers;
        var name = (byte[])headers["name"];
        var queueName = Encoding.UTF8.GetString(name);

        Action callback = null;
        if (queueName == InputQueue)
        {
          callback = eventType switch
          {
            "deleted" => _queueDeletedCallback,
            "created" => _queueCreatedCallback,
            _ => null
          };
        }
        Model.BasicAck(args.DeliveryTag, false);
        callback?.Invoke();
      }
      catch (Exception e)
      {
        _logger.LogError(e, "Error receiving broker event message");
        if (Model.IsOpen)
        {
          Model.BasicNack(args.DeliveryTag, multiple: false, requeue: false);
        }
        throw;
      }
    }

  }
}
