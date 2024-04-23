using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Selma.Orchestration.Models;
using Selma.Orchestration.TaskMQUtils;

namespace Selma.Orchestration
{
  public class RequestQueueConsumer : IHostedService, IRequestQueueConsumer
  {
    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _client;
    private readonly string _serviceName = "RequestQueueConsumer";
    public static Func<ILogger, IConfiguration, HttpClient, ImmutableList<MqMessage>, CancellationToken, MQWorker, Task<List<Result>>> WorkerFunction;

    public RequestQueueConsumer(IConfiguration configuration, ILogger<RequestQueueConsumer> logger, HttpClient client)
    {
      _logger = logger;
      _configuration = configuration;
      _client = client;
      _client.Timeout = TimeSpan.ParseExact(configuration["HttpClientTimeOut"], "c", CultureInfo.InvariantCulture);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
      _logger.LogInformation($"{_serviceName} is starting. {this.GetHashCode()}");

      cancellationToken.Register(() =>
      {
        // If needed, signal whatever wait you're stuck at to exit service
        _logger.LogWarning($"{_serviceName} background task is stopping.");
      });

      var mQWorker = new MQWorker(_configuration, _logger);
      Func<ImmutableList<MqMessage>, CancellationToken, MQWorker, Task<List<Result>>> PartiallyAppliedWorkerFunction = (messages, cancellationToken, worker) => { return WorkerFunction(_logger, _configuration, _client, messages, cancellationToken, worker); };
      await mQWorker.Run(PartiallyAppliedWorkerFunction, cancellationToken);

      _logger.LogWarning($"{_serviceName} excution task exiting...");
    }

#pragma warning disable 1998
    public async Task StopAsync(CancellationToken cancellationToken)
    {
      // Run your graceful clean-up actions
      _logger.LogWarning($"{_serviceName} stopping...");
    }
#pragma warning restore 1998
  }
}