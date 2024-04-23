using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using NLog.Web;

namespace Selma.Testing
{

  public class ConfigurationHelper
  {
    public static IConfigurationRoot GetIConfigurationRoot()
    {
      return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.test.json", true)
            .AddEnvironmentVariables()
            .Build();
    }
  }

  public class LoggerHelper
  {
    public static ILogger GetLogger(Type type)
    {
      NLogBuilder.ConfigureNLog("nlog.config");
      var logProvider = new NLogLoggerProvider();

      return logProvider.CreateLogger(type.FullName);
    }
  }
}