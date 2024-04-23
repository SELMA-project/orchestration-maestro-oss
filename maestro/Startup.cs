using System;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Selma.Orchestration.OrchestrationDB;
using Microsoft.AspNetCore.Server.Kestrel.Core;


namespace Selma.Orchestration.Maestro
{
  public class Startup
  {
    public Startup(IConfiguration configuration, ILogger<Startup> logger)
    {
      Configuration = configuration;
      _logger = logger;
    }

    public IConfiguration Configuration { get; }

    private ILogger<Startup> _logger;

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {
      services.Configure<KestrelServerOptions>(options =>
      {
        options.Limits.MaxRequestBodySize = long.MaxValue;
      });

      services.AddSingleton<JobEnqueuer>();

      services.AddSingleton<IBootDatabaseJobPuller, BootDatabaseJobPuller>();
      services.AddHostedService<BootDatabaseJobPuller>(x => (BootDatabaseJobPuller)x.GetRequiredService<IBootDatabaseJobPuller>());

      // const string meterName = "job_metrics";
      // services.AddSingleton<IJobResultListener, JobResultListener>();
      // services.AddHostedService<JobResultListener>(x => (JobResultListener)x.GetRequiredService<IJobResultListener>());
      
      const string meterName = "job_metrics_batched";
      services.AddSingleton<IJobResultListener, BatchedJobResultListener>();
      services.AddHostedService(x => (BatchedJobResultListener)x.GetRequiredService<IJobResultListener>());

      services.AddSingleton(provider => new JobMetrics(meterName, provider.GetRequiredService<ILogger<JobMetrics>>()));
      
      services.AddTransient<MessageProcessor>();

      services.AddHostedService<JobArchiver>();

      services.AddControllers();
      services.AddControllers().AddNewtonsoftJson();
      services.AddTransient<OrchestrationDBContext>(context =>
      {
        var factory = new OrchestrationDBContextFactory();
        return factory.CreateDbContext(null);
      });

      if (String.IsNullOrEmpty(Configuration["ConnectionStrings:SelmaDBConnection"]))
      {
        throw new Exception("Missing configuration file appsettings.json or api/appsettings.json");
      }

      if (Convert.ToBoolean(Configuration["SkipPreMigrationStep"]) == false)
      {
        try
        {
          // 
          var connectionString = Configuration.GetConnectionString("SelmaDBConnection");
          var migrationsTableExists = Convert.ToBoolean(RunScalarQuery(connectionString,
                                              @"SELECT EXISTS (SELECT FROM pg_tables WHERE schemaname = 'public' AND tablename  = '__EFMigrationsHistory');"));
          var jobTableExists = Convert.ToBoolean(RunScalarQuery(connectionString,
                                               @"SELECT EXISTS (SELECT FROM pg_tables WHERE schemaname = 'public' AND tablename  = 'Jobs');"));
          _logger.LogInformation($"__EFMigrationsHistory table Exists: {migrationsTableExists}");
          _logger.LogInformation($"Jobs table Exists: {jobTableExists}");

          if (!migrationsTableExists && jobTableExists)
          {
            _logger.LogInformation($"Legacy case detected - running PreMigrationStep");
            if (!migrationsTableExists)
            {
              _logger.LogWarning($"Unexisting __EFMigrationsHistory. Running Pre-Migration Step.");
              RunScalarQuery(connectionString,
                             @"CREATE TABLE public.""__EFMigrationsHistory"" (
                                 ""MigrationId"" varchar(150) NOT NULL,
                                 ""ProductVersion"" varchar(32) NOT NULL,
                                 CONSTRAINT ""PK___EFMigrationsHistory"" PRIMARY KEY(""MigrationId"")
                               );");
            }
            var checkInitialMigrationQuery = Convert.ToInt64(RunScalarQuery(connectionString,
                                                             @"SELECT COUNT(*) FROM public.""__EFMigrationsHistory"" where ""MigrationId"" = '20220818162822_Initial'"));
            _logger.LogInformation($"checkInitialMigrationQuery: {checkInitialMigrationQuery}");

            if (checkInitialMigrationQuery <= 0)
            {
              RunScalarQuery(connectionString,
                             @"INSERT INTO ""__EFMigrationsHistory""(""MigrationId"", ""ProductVersion"") VALUES ('20220818162822_Initial', '6.0.6');");
              _logger.LogInformation($"__EFMigrationsHistory Added Initial Migration.");
            }
          }
          else
          {
            _logger.LogInformation($"Not running PreMigrationStep: Either Empty DB or Already Migrated");
          }
        }
        catch (Exception e2)
        {
          _logger.LogError($"Exception: {e2.Message}");
        }
      }

      var repeat = false;
      do
      {
        try
        {
          var factory = new OrchestrationDBContextFactory();
          var dbContext = factory.CreateDbContext(null);

          if (Convert.ToBoolean(Configuration["Development:RecreateDatabase"]))
          {
            if (!(dbContext.Database.GetConnectionString()?.Contains("Host=local") ?? false))
            {
              // Still try to see if DB wants this
              var databaseRecreateDatabaseVariable = dbContext.SystemVariables.FirstOrDefault(v => v.Key == "ForceDatabaseRecreate");
              if (databaseRecreateDatabaseVariable == null || !Convert.ToBoolean(databaseRecreateDatabaseVariable.Value))
              {
                throw new Exception("RecreateDatabase=true: Cannot do this on a database which is not on localhost [OR] local_* docker [OR] Database Table SystemVariables/ForceDatabaseRecreate=true (for safety). Rethink what you're trying to do.");
              }
            }
            _logger.LogWarning("RecreateDatabase=true, will destroy all schema and data.");
            dbContext.Database.EnsureDeleted();
            dbContext.Database.Migrate();
          }
          else if (Convert.ToBoolean(Configuration["Development:ForceMigrate"]))
          {
            _logger.LogWarning("ForceMigrate=true, will attempt migration");
            dbContext.Database.Migrate();
          }
          repeat = false;
        }
        catch (Exception e)
        {
          Thread.Sleep(5000);
          repeat = true;
          _logger.LogError($"Exception: {e.Message}");
        }
      } while (repeat);
      services.AddSwaggerGen(c =>
      {
        c.SwaggerDoc("v1", new OpenApiInfo { Title = "Orchestrator", Version = "v1" });
      });
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
      app.UseRouting();
      //app.UseAuthorization();
      app.UseDeveloperExceptionPage();
      app.UseCors(config =>
      {
        config.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin();
      });
      app.UseSwagger();
      app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "orchestrator v1"));

      app.UseEndpoints(endpoints =>
      {
        endpoints.MapControllers();
      });
    }

    private string RunScalarQuery(string connectionString, string query)
    {
      using (var sqlConnection = new Npgsql.NpgsqlConnection(connectionString))
      {
        sqlConnection.Open();
        using (var cmd = new Npgsql.NpgsqlCommand(query, sqlConnection))
        {
          return cmd.ExecuteScalar()?.ToString();
        }
      }
    }
  }
}
