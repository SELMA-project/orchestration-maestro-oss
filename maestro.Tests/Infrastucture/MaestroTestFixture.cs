using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Selma.GeneralUtils;
using Selma.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Selma.Orchestration.Maestro.Tests
{
  public class MaestroFixture : IAsyncLifetime
  {
    private static IConfiguration _config = ConfigurationHelper.GetIConfigurationRoot();

    private MessageSinkConverter _logger;
    private string _solutionRoot;
    private string _dockerComposeTestPath;

    private string _dockerComposeCmd;

    private string _dockerProject;
    public MaestroFixture(IMessageSink sink)
    {
      _logger = new MessageSinkConverter(sink);
    }


    public Task InitializeAsync()
    {
      // Do "global" initialization here; Only called once.
      _logger.WriteLine("Initializing...");

      var config = _config.GetSection("Tests:Docker");

      _solutionRoot = new DirectoryInfo(Directory.GetCurrentDirectory()).Parent.Parent.Parent.Parent.FullName;
      _dockerComposeTestPath = Path.Join(_solutionRoot, "docker-compose-test.yml");
      _dockerProject = config.GetValue<string>("ProjectName", "maestro-tests");
      _dockerComposeCmd = $"docker-compose -f {_dockerComposeTestPath} -p {_dockerProject}";

      var options = "";
      if (config.GetValue<bool>("BuildImages", true)) options = $"{options} --build";
      if (config.GetValue<bool>("ForceRecreate", true)) options = $"{options} --force-recreate";

      var script =
          $@"Set-Location {_solutionRoot}
             {_dockerComposeCmd} up -d {options}";

      _logger.WriteLine($"Running init script:\n{script}");

      Task.Run(async () =>
      {
        await foreach (var line in script.RunPowerShellPipe())
        {
          _logger.WriteLine(line.ToString());
        }
      }).Wait();

      var delay = _config.GetValue<TimeSpan>("RabbitMQ:ConnectionRetryIn", TimeSpan.FromSeconds(10));
      delay += TimeSpan.FromSeconds(2);

      _logger.WriteLine($"Waiting {delay.TotalSeconds}s for maestro to connect to RabbitMQ...");
      Task.Delay(delay).Wait();

      _logger.WriteLine("Initialized.");

      return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
      // Do "global" teardown here; Only called once.
      Task.Delay(1000).Wait();

      var script = $@"Set-Location {_solutionRoot}
                      {_dockerComposeCmd} stop";

      _logger.WriteLine($"Running teardown script:\n{script}");

      Task.Run(async () =>
      {
        await foreach (var line in script.RunPowerShellPipe())
        {
          _logger.WriteLine(line.ToString());
        }
      }).Wait();

      return Task.CompletedTask;
    }
  }
}