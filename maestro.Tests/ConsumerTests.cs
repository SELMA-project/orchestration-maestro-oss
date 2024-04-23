using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Meziantou.Extensions.Logging.Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using RabbitMQ.Client.Events;
using Selma.Orchestration.Models;
using Selma.Orchestration.TaskMQUtils;
using Xunit;
using Xunit.Abstractions;

namespace Selma.Orchestration.Maestro.Tests;

public class ConsumerTests
{
  private readonly ITestOutputHelper _output;
  private XUnitLoggerProvider _loggerProvider;

  public ConsumerTests(ITestOutputHelper output)
  {
    _output = output;

    _loggerProvider = new XUnitLoggerProvider(_output, new XUnitLoggerOptions
    {
      IncludeScopes = false,
      UseUtcTimestamp = true,
      IncludeLogLevel = true,
      TimestampFormat = "hh:mm:ss.ffff"
    });
  }

  [Fact]
  public async Task TestMqConsumerNormal()
  {
    var configBuilder = new ConfigurationBuilder();
    var inputQueue = "Test.Prefetch.In";


    var config = configBuilder.AddInMemoryCollection(new Dictionary<string, string>
                              {
                                {"RabbitMQ:Url", "amqp://localhost:8672"},
                                {"InputQueue:Name", inputQueue},
                                {"RabbitMQ:ConnectionRetryIn", "00:00:05"},
                                {"RabbitMQ:ConnectionMaxRetries", "60"},
                                {"OutputExchange:Name", "amq.topic"}
                              })
                              .Build();

    var mqManager = new MQManager(config, NullLogger.Instance);

    await SetupProducer(mqManager, inputQueue, config, 15);

    var logger = _loggerProvider.CreateLogger(nameof(MQConsumer));
    var consumer = new MQConsumer(config, logger, inputQueue, 5, "maestro.Tests");

    var tokenSource = new CancellationTokenSource();
    tokenSource.CancelAfter(TimeSpan.FromSeconds(20));
    consumer.SetUpChannel();
    await consumer.Run(ConsumerLogic, tokenSource.Token);
  }


  [Fact]
  public async Task TestConsumerAsync()
  {
    var configBuilder = new ConfigurationBuilder();
    var inputQueue = "Test.Prefetch.In";


    var config = configBuilder.AddInMemoryCollection(new Dictionary<string, string>
                              {
                                {"RabbitMQ:Url", "amqp://localhost:8672"},
                                {"InputQueue:Name", inputQueue},
                                {"RabbitMQ:ConnectionRetryIn", "00:00:05"},
                                {"RabbitMQ:ConnectionMaxRetries", "60"},
                                {"OutputExchange:Name", "amq.topic"}
                              })
                              .Build();

    var mqManager = new MQManager(config, NullLogger.Instance);

    await SetupProducer(mqManager, inputQueue, config, 15);

    var logger = _loggerProvider.CreateLogger(nameof(MQConsumerAsync));
    var consumer = new MQConsumerAsync(config, logger, inputQueue, 5, "maestro.Tests");

    var tokenSource = new CancellationTokenSource();
    tokenSource.CancelAfter(TimeSpan.FromSeconds(20));
    consumer.SetUpChannel();
    await consumer.Run(ConsumerLogic, tokenSource.Token);
  }

  [Fact]
  public async Task TestConcurrentConsumerAsync()
  {
    var configBuilder = new ConfigurationBuilder();
    var inputQueue = "Test.Prefetch.In";


    var config = configBuilder.AddInMemoryCollection(new Dictionary<string, string>
                              {
                                {"RabbitMQ:Url", "amqp://localhost:8672"},
                                {"InputQueue:Name", inputQueue},
                                {"RabbitMQ:ConnectionRetryIn", "00:00:05"},
                                {"RabbitMQ:ConnectionMaxRetries", "60"},
                                {"OutputExchange:Name", "amq.topic"}
                              })
                              .Build();

    var mqManager = new MQManager(config, NullLogger.Instance);

    await SetupProducer(mqManager, inputQueue, config, 15);


    var logger = _loggerProvider.CreateLogger(nameof(MQConsumerConcurrentAsync));
    var consumer = new MQConsumerConcurrentAsync(config, logger, inputQueue, 5, "maestro.Tests");

    var tokenSource = new CancellationTokenSource();
    tokenSource.CancelAfter(TimeSpan.FromSeconds(20));
    consumer.SetUpChannel();

    var consumerTask = consumer.Run(ConsumerLogic, tokenSource.Token);

    do
    {
      await Task.Delay(1000, tokenSource.Token);
    } while (consumer.MessageCount(inputQueue) > 0);

    tokenSource.Cancel();
    await consumerTask;
  }

  [Fact]
  public async Task TestBatchedConsumerAsync()
  {
    var configBuilder = new ConfigurationBuilder();
    var inputQueue = "Test.BatchedConsumer.In";


    var config = configBuilder.AddInMemoryCollection(new Dictionary<string, string>
                              {
                                {"Logging:LogLevel:Default", "Trace"},
                                {"RabbitMQ:Url", "amqp://localhost:8672"},
                                {"InputQueue:Name", inputQueue},
                                {"RabbitMQ:ConnectionRetryIn", "00:00:05"},
                                {"RabbitMQ:ConnectionMaxRetries", "60"},
                                {"OutputExchange:Name", "amq.topic"}
                              })
                              .Build();

    var mqManager = new MQManager(config, NullLogger.Instance);

    await SetupProducer(mqManager, inputQueue, config, 15);


    var logger = _loggerProvider.CreateLogger(nameof(MQBatchConsumer));


    var tokenSource = new CancellationTokenSource();
    var consumer = new MQBatchConsumer(config, logger, inputQueue,
                                       batchSize: 5,
                                       batchTimeout: TimeSpan.FromSeconds(2),
                                       concurrency: 4);

    tokenSource.CancelAfter(TimeSpan.FromSeconds(20));
    consumer.SetUpChannel();

    Task BatchConsumerLogic(ImmutableList<MqMessage> mqMessages,
                            MQBatchConsumer batchConsumer,
                            CancellationToken cancellationToken)
    {
      var total = 0;
      foreach (var mqMessage in mqMessages)
      {
        batchConsumer.SetMessageStatus(mqMessage, MessageStatus.Ack);
        total++;
      }
      var payloads = string.Join(", ", mqMessages.Select(x => x.Message.Payload.ToString()).ToArray());
      logger.LogInformation($"[BatchConsumerLogic] {total} messages were processed: {payloads}");
      return Task.CompletedTask;
    }

    await consumer.Run(BatchConsumerLogic,
                       tokenSource.Token);
  }
  
  
  [Fact]
  public async Task TestBatchedConsumerAsync_WhenMessagesNotAcked_DefaultToRequeue()
  {
    var configBuilder = new ConfigurationBuilder();
    var inputQueue = "Test.BatchedConsumer.In";


    var config = configBuilder.AddInMemoryCollection(new Dictionary<string, string>
                              {
                                {"Logging:LogLevel:Default", "Trace"},
                                {"RabbitMQ:Url", "amqp://localhost:8672"},
                                {"InputQueue:Name", inputQueue},
                                {"RabbitMQ:ConnectionRetryIn", "00:00:05"},
                                {"RabbitMQ:ConnectionMaxRetries", "60"},
                                {"OutputExchange:Name", "amq.topic"}
                              })
                              .Build();

    var mqManager = new MQManager(config, NullLogger.Instance);

    await SetupProducer(mqManager, inputQueue, config, 15);


    var logger = _loggerProvider.CreateLogger(nameof(MQBatchConsumer));


    var tokenSource = new CancellationTokenSource();
    var consumer = new MQBatchConsumer(config, logger, inputQueue,
                                       batchSize: 15,
                                       batchTimeout: TimeSpan.FromSeconds(2),
                                       concurrency: 4);

    tokenSource.CancelAfter(TimeSpan.FromSeconds(5));
    consumer.SetUpChannel();

    Task BatchConsumerLogic(ImmutableList<MqMessage> mqMessages,
                            MQBatchConsumer batchConsumer,
                            CancellationToken cancellationToken)
    {
      var total = mqMessages.Count;
      var payloads = string.Join(", ", mqMessages.Select(x => x.Message.Payload.ToString()).ToArray());
      logger.LogInformation($"[BatchConsumerLogic] {total} messages were ignored: {payloads}");
      return Task.Delay(1000, cancellationToken);
    }

    await consumer.Run(BatchConsumerLogic,
                       tokenSource.Token);
  }


  [Fact]
  public async Task TestBatchedConsumerAsync_WhenListIsNotFullAsyncOld()
  {
    var configBuilder = new ConfigurationBuilder();
    var inputQueue = "Test.BatchedConsumer.In";


    var config = configBuilder.AddInMemoryCollection(new Dictionary<string, string>
                              {
                                {"Logging:LogLevel:Default", "Trace"},
                                {"RabbitMQ:Url", "amqp://localhost:8672"},
                                {"InputQueue:Name", inputQueue},
                                {"RabbitMQ:ConnectionRetryIn", "00:00:05"},
                                {"RabbitMQ:ConnectionMaxRetries", "60"},
                                {"OutputExchange:Name", "amq.topic"}
                              })
                              .Build();

    var mqManager = new MQManager(config, NullLogger.Instance);

    await SetupProducer(mqManager, inputQueue, config, 15);


    var logger = _loggerProvider.CreateLogger(nameof(MQBatchConsumer));


    var tokenSource = new CancellationTokenSource();
    var consumer = new MQBatchConsumer(config, logger, inputQueue,
                                       batchSize: 16, // bigger than produced messages
                                       batchTimeout: TimeSpan.FromSeconds(5),
                                       concurrency: 4);

    tokenSource.CancelAfter(TimeSpan.FromSeconds(20));
    consumer.SetUpChannel();

    Task BatchConsumerLogic(ImmutableList<MqMessage> mqMessages,
                            MQBatchConsumer batchConsumer,
                            CancellationToken cancellationToken)
    {
      var total = 0;
      foreach (var mqMessage in mqMessages)
      {
        batchConsumer.SetMessageStatus(mqMessage, MessageStatus.Ack);
        total++;
      }
      var payloads = string.Join(", ", mqMessages.Select(x => x.Message.Payload.ToString()).ToArray());
      logger.LogInformation($"[BatchConsumerLogic] {total} messages were processed: {payloads}");
      return Task.CompletedTask;
    }

    await consumer.Run(BatchConsumerLogic,
                       tokenSource.Token);
  }
  
  
  [Fact]
  public async Task TestBatchedConsumerAsync_WhenEarlyExit()
  {
    var configBuilder = new ConfigurationBuilder();
    var inputQueue = "Test.BatchedConsumer.In";


    var config = configBuilder.AddInMemoryCollection(new Dictionary<string, string>
                              {
                                {"Logging:LogLevel:Default", "Trace"},
                                {"RabbitMQ:Url", "amqp://localhost:8672"},
                                {"InputQueue:Name", inputQueue},
                                {"RabbitMQ:ConnectionRetryIn", "00:00:05"},
                                {"RabbitMQ:ConnectionMaxRetries", "60"},
                                {"OutputExchange:Name", "amq.topic"}
                              })
                              .Build();

    var mqManager = new MQManager(config, NullLogger.Instance);

    await SetupProducer(mqManager, inputQueue, config, 15);


    var logger = _loggerProvider.CreateLogger(nameof(MQBatchConsumer));


    var tokenSource = new CancellationTokenSource();
    var consumer = new MQBatchConsumer(config, logger, inputQueue,
                                       batchSize: 16, // bigger than produced messages
                                       batchTimeout: TimeSpan.FromSeconds(20), // bigger than consumer lifetime
                                       concurrency: 4);

    tokenSource.CancelAfter(TimeSpan.FromSeconds(10));  // consumer max lifetime
    consumer.SetUpChannel();

    Task BatchConsumerLogic(ImmutableList<MqMessage> mqMessages,
                            MQBatchConsumer batchConsumer,
                            CancellationToken cancellationToken)
    {
      var total = 0;
      foreach (var mqMessage in mqMessages)
      {
        batchConsumer.SetMessageStatus(mqMessage, MessageStatus.Ack);
        total++;
      }
      var payloads = string.Join(", ", mqMessages.Select(x => x.Message.Payload.ToString()).ToArray());
      logger.LogInformation($"[BatchConsumerLogic] {total} messages were processed: {payloads}");
      return Task.CompletedTask;
    }

    await consumer.Run(BatchConsumerLogic,
                       tokenSource.Token);
  }

  private static async Task SetupProducer(MQManager mqManager, string inputQueue, IConfigurationRoot config, int messageCount)
  {
    await mqManager.DeclareQueue(inputQueue);
    mqManager.BindQueueToTopicExchange("amq.topic", inputQueue, inputQueue);

    var producer = new MQProducer("amq.topic", config, NullLogger.Instance);
    foreach (var i in Enumerable.Range(1, messageCount))
    {
      JToken payload = JToken.FromObject(i.ToString());
      await producer.Produce(new Message(Guid.Empty, MessageType.Request, payload, null), inputQueue, CancellationToken.None);
    }

    await Task.Delay(500);
  }

  private async Task ConsumerLogic(Message message, CancellationToken cancellationToken)
  {
    var payload = message.Payload.ToObject<string>();
    // _output.WriteLine($"message {payload}: started (thread: {Thread.CurrentThread.Name} {Environment.CurrentManagedThreadId})");
    // await Task.Delay(2000, cancellationToken);

    // _output.WriteLine($"{DateTime.Now:h:mm:ss.fff} ({payload}) [{delay}]");
    _output.WriteLine($"message {payload}: finished (thread: {Thread.CurrentThread.Name} {Environment.CurrentManagedThreadId})");
  }
}
