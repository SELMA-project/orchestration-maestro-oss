using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Selma.Orchestration.Models;
using Selma.Orchestration.TaskMQUtils;

public interface IMQConsumer
{

}

public abstract class BaseMQConsumer
{
  protected string _rabbitMQUrl { get; set; }
  public ushort _prefetchCount { get; protected set; }
  protected int _rmq_retries;
  protected int maxRetries;
  protected IConnection _connection;
  protected IModel _channel;
  protected IConfiguration _configuration;
  protected ILogger _logger;
  protected string _inputQueueName;
  public string ConnectionTag { get; set; }
  protected TimeSpan retryInterval;

}
internal interface IBatchMQConsumer
{
  public Task Run(Func<ImmutableList<MqMessage>, MQBatchConsumer, CancellationToken, Task> batchConsumerLogic,
                        CancellationToken cancellationToken);
}

internal interface ISequentialMQConsumer
{
  public Task Run(Func<Message, CancellationToken, Task> consumerLogic,
                        CancellationToken cancelationToken);
}