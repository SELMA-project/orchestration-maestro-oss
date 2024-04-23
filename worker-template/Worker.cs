using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Selma.Orchestration.Models;
using Selma.Orchestration.TaskMQUtils;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Selma.Orchestration
{
  public class Worker
  {
    private static IHost Host = null;
    public static IConfiguration Configuration { get; private set; }
    public static void Setup()
    {
      var builder = new HostBuilder()
        .ConfigureAppConfiguration((context, config) =>
        {
          config.SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
            .AddJsonFile("credentials.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();
        })
        .ConfigureLogging((hostContext, loggingBuilder) =>
        {
          loggingBuilder.AddConfiguration(hostContext.Configuration.GetSection("Logging"));
          loggingBuilder.AddConsole();
        })
        .ConfigureServices((hostContext, services) =>
        {
          services.AddOptions();
          var client = new HttpClient();
          services.AddSingleton<HttpClient>(client);

          if (hostContext.Configuration.GetValue("InputQueue:IsStream", false))
          {
            services.AddSingleton<IHostedService, DynamicRequestStreamConsumer>();
            Console.WriteLine("Using multi queue consumer.");
          }
          else
          {
            services.AddSingleton<IHostedService, RequestQueueConsumer>();
            Console.WriteLine("Using single queue consumer");
          }

          /*services.AddLogging(loggingBuilder =>
          {
            loggingBuilder.ClearProviders();
            loggingBuilder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
            loggingBuilder.AddConfiguration(hostContext.Configuration);
          });*/
        });

      Host = builder.Build();
      Configuration = Host.Services.GetRequiredService<IConfiguration>();
    }

    public static void Run(Func<ILogger, IConfiguration, HttpClient, ImmutableList<MqMessage>, CancellationToken, MQWorker, Task<List<Result>>> workerLogic)
    {
      if (Host == null)
      {
        Setup();
      }
      if (Configuration.GetValue("InputQueue:IsStream", false))
      {
        DynamicRequestStreamConsumer.WorkerFunction = workerLogic;
      }
      else
      {
        RequestQueueConsumer.WorkerFunction = workerLogic;
      }
      Host.RunAsync().GetAwaiter().GetResult();
    }
  }
}