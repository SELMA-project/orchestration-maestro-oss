using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Selma.Orchestration.Maestro.JobArchiver;
using Selma.Orchestration.OrchestrationDB;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
try
{
  var builder = Host.CreateApplicationBuilder(args);

  builder.Configuration
         .AddJsonFile("appsettings.local.json", optional: true)
         .AddJsonFile("credentials.json", optional: false)
         .AddJsonFile("credentials.local.json", optional: true)
         .AddEnvironmentVariables();

  builder.Services
         .AddSingleton<OrchestrationDBContextFactory>()
         .AddHostedService<JobArchiver>();

  builder.Build()
         .Run();
}
catch (OperationCanceledException)
{
  Console.WriteLine("Operation cancelled");
}
catch (ApplicationException e)
{
  Console.WriteLine(e.Message);
}
