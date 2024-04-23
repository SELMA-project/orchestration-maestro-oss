using System;
using System.IO;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLog.Web;

namespace Selma.Orchestration.Maestro
{
  class Program
  {

    public static void Main(string[] args)
    {
      try
      {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        NLogBuilder.ConfigureNLog("nlog.config").GetCurrentClassLogger();
        WebHost.CreateDefaultBuilder(args)
               .UseShutdownTimeout(TimeSpan.FromSeconds(10))
               .UseStartup<Startup>()
               .ConfigureAppConfiguration((IConfigurationBuilder builder) =>
               {
                 builder
                     .SetBasePath(Directory.GetCurrentDirectory())
                     .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                     .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
                     .AddJsonFile("credentials.json", optional: false)
                     .AddJsonFile("credentials.local.json", optional: true)
                     .AddEnvironmentVariables()
                     .Build();
               })
               .ConfigureLogging(logging =>
               {
                 // logging.ClearProviders();
                 // logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
               })
               .UseNLog()
               .Build()
               .Run();
      }
      catch (OperationCanceledException)
      {
        
      }
      finally
      {
        NLog.LogManager.Shutdown();
      }
    }
  }
}
