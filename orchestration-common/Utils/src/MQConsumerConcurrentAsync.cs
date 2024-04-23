using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Selma.Orchestration.TaskMQUtils;

public class MQConsumerConcurrentAsync : MQConsumerAsync
{
  public MQConsumerConcurrentAsync(IConfiguration configuration, ILogger logger, string consumptionQueueName, ushort prefetchCount = 1, string connectionTag = "") :
      base(configuration, logger, consumptionQueueName, prefetchCount, connectionTag)
  {
  }


  protected override ConnectionFactory CreateFactory()
  {
    var factory = base.CreateFactory();
    factory.DispatchConsumersAsync = true;
    factory.ConsumerDispatchConcurrency = _prefetchCount;
    _logger.LogInformation($"using consumer dispatch concurrency: {_prefetchCount}");
    return factory;
  }
}
