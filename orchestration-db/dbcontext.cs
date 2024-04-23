using System;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Selma.Orchestration.OrchestrationDB
{
  public class OrchestrationDBContextFactory : IDesignTimeDbContextFactory<OrchestrationDBContext>
  {
    public OrchestrationDBContext CreateDbContext(string[] args)
    {
      AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
      var configuration = new ConfigurationBuilder()
                          .SetBasePath(Directory.GetCurrentDirectory())
                          .AddJsonFile("appsettings.json", optional: true)
                          .AddJsonFile("credentials.json", false)
                          .AddJsonFile("credentials.local.json", true)
                          .AddEnvironmentVariables()
                          .Build();

      var connectionString = configuration.GetConnectionString("SelmaDBConnection");
      // Console.WriteLine($"Connection: {connectionString.Split("Pass")[0]} (...)");
      var optionsBuilder = new DbContextOptionsBuilder<OrchestrationDBContext>();
      var credentials = new DBContextSymetricCredentials(configuration.GetSection("DataRestingCredentials"));

      if (connectionString.Contains("Data Source="))
      {
        optionsBuilder.UseSqlite(connectionString);
        return new OrchestrationDBContext(new SqliteDbDefaults(credentials, configuration), optionsBuilder.Options);
      }
      else
      {
        optionsBuilder.UseNpgsql(connectionString, builder =>
        {
          builder.EnableRetryOnFailure(maxRetryCount: 6,
                                       maxRetryDelay: TimeSpan.FromSeconds(30),
                                       errorCodesToAdd: null);
        });
        return new OrchestrationDBContext(new PostgresDbDefaults(credentials, configuration), optionsBuilder.Options);
      }

    }
  }
  public interface IOrchestrationDBContext { };
  public class OrchestrationDBContext : DbContext, IOrchestrationDBContext
  {
    public IDbContextDefaults _defaults;

    public OrchestrationDBContext(DbContextOptions<OrchestrationDBContext> options)
        : base(options)
    {
    }

    public OrchestrationDBContext(IDbContextDefaults defaults, DbContextOptions<OrchestrationDBContext> options) : base(options)
    {
      _defaults = defaults;
    }

    // Platform Core 
    public DbSet<Job> Jobs { get; set; }

    // System Management
    public DbSet<SystemVariable> SystemVariables { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
      modelBuilder.Entity<Job>(entity =>
      {
        entity.HasKey(e => e.Id);
        entity.HasIndex(e => e.Status);
        entity.HasIndex(e => e.WorkflowId);
        entity.HasIndex(e => e.Updated);

        entity.Property(e => e.Created)
              .HasDefaultValueSql(_defaults.SQLDateDefault);
        entity.Property(e => e.Updated)
              .HasDefaultValueSql(_defaults.SQLDateDefault);
        entity.Property(e => e.Dependencies)
              .IsJsonStr<HashSet<Guid>>(_defaults);
        entity.Property(e => e.OG_Dependencies)
              .IsJsonStr<HashSet<Guid>>(_defaults);
        entity.Property(e => e.Status)
              .IsStringEnum();
        entity.Property(e => e.Scripts)
              .IsJToken(_defaults);
        entity.Property(e => e.Metadata)
              .IsJToken(_defaults);

        if (_defaults.IsEncrypted)
        {
          entity.Property(e => e.Input)
                .IsEncrypted(_defaults);
          entity.Property(e => e.Request)
                .IsEncrypted(_defaults);
          entity.Property(e => e.Result)
                .IsEncrypted(_defaults);
        }
        else
        {
          entity.Property(e => e.Input)
                .IsJToken(_defaults);
          entity.Property(e => e.Request)
                .IsJToken(_defaults);
          entity.Property(e => e.Result)
                .IsJToken(_defaults);
        }

      });


      modelBuilder.Entity<SystemVariable>(entity =>
      {
        entity.HasKey(e => e.Key);
      });
    }

    private void RefreshUpdateTimestamp()
    {
      var changedEntities = ChangeTracker.Entries<IEntityWithUpdateTime>()
                                         .Where(x => x.State == EntityState.Modified);

      foreach (var entity in changedEntities)
      {
        entity.Entity.Updated = DateTime.UtcNow;
      }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) 
      => SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
      RefreshUpdateTimestamp();
      return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
    
    public override int SaveChanges() 
      => SaveChanges(acceptAllChangesOnSuccess: true);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
      RefreshUpdateTimestamp();
      return base.SaveChanges(acceptAllChangesOnSuccess);
    }
  }
}
