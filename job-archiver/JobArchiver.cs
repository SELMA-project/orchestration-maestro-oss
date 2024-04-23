using System.Diagnostics;
using System.Linq.Expressions;
using System.Text;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Npgsql;
using Selma.Orchestration.OrchestrationDB;
using Selma.Orchestration.TaskMQUtils;

namespace Selma.Orchestration.Maestro.JobArchiver;

public class JobArchiver : BackgroundService
{
  private const string ShortDayOfYearFormat = "yyyy-MM-dd";
  private readonly ILogger<JobArchiver> _logger;
  private readonly IConfiguration _configuration;
  private readonly OrchestrationDBContextFactory _factory;
  private readonly JobArchiverSettings _settings;
  private readonly IHostApplicationLifetime _hostLifetime;

  private DateTime _lastProgressNotify = DateTime.MinValue;
  private readonly TimeSpan _progressInterval = TimeSpan.FromMinutes(1);

  private DateTime LastArchiveDate { get; set; } = DateTime.MinValue;
  private int ArchivedJobs { get; set; } = 0;

  private bool HasJobLimitExpired => ArchivedJobs >= _settings.MaxJobs
                                     && DateTime.UtcNow >=  LastArchiveDate + _settings.MaxJobInterval;

  private bool CanArchive()
  {
    if (!_settings.HasJobLimit) 
      return true;
    
    if (HasJobLimitExpired) ArchivedJobs = 0;
    return ArchivedJobs < _settings.MaxJobs;
  }


  public JobArchiver(ILogger<JobArchiver> logger, IConfiguration configuration, OrchestrationDBContextFactory factory, IHostApplicationLifetime hostLifetime)
  {
    _logger = logger;
    _configuration = configuration;
    _factory = factory;
    _hostLifetime = hostLifetime;
    _settings = new JobArchiverSettings();
  }

  private DateTime NextCheck => DateTime.UtcNow + _settings.Interval;

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (stoppingToken.IsCancellationRequested) return;

    try
    {
      await using var context = _factory.CreateDbContext(null);
      if (!await context.Database.CanConnectAsync(stoppingToken))
      {
        var connectionString = _configuration.GetConnectionString("SelmaDBConnection")
                                             .Split("Pass")
                                             .First();
        throw new ApplicationException($"Failed to connect to the database: {connectionString}(...)");
      }

      _configuration.GetSection(nameof(JobArchiver)).Bind(_settings);
      _logger.LogDebug($"Loaded settings from config: {JsonConvert.SerializeObject(_settings, Formatting.None)}");

      if (!_settings.Enabled)
      {
        throw new ApplicationException("Shutting down: disabled in configuration");
      }

      if (!_settings.IsValid())
      {
        throw new ApplicationException("Invalid configuration: execution aborted");
      }

      if (!Directory.Exists(_settings.OutputDirectory))
      {
        Directory.CreateDirectory(_settings.OutputDirectory);
      }
    }
    catch (ApplicationException) { throw; }
    catch (Exception e) 
    {
      _logger.LogError(e, "Unexpected error when loading configuration: execution aborted");
      throw;
    }

    _logger.LogInformation($"Maximum job age is {_settings.MaxAge.Humanize()}");
    if(_settings.HasJobLimit)
      _logger.LogInformation($"Limit set to {_settings.MaxJobs} jobs every {_settings.MaxJobInterval.Humanize()}");

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        if (CanArchive())
        {
          await ArchiveAllJobsAsync(stoppingToken);
        }

        await Task.Delay(_settings.Interval, stoppingToken);
      }
      catch (TaskCanceledException)
      {
        _logger.LogDebug("Task cancelled: execution aborted");
        break;
      }
      catch (NpgsqlException e) when (e is {IsTransient: true})
      {
        _logger.LogWarning($"{e.GetType().FullName}: {e.Message}; retrying...");
        await Task.Delay(_settings.RetryDelayMs, stoppingToken);
      }
      catch (InvalidOperationException e) when ((e.InnerException?.InnerException ?? e.InnerException) is NpgsqlException {IsTransient: true} ie)
      {
        _logger.LogDebug($"{ie.GetType().FullName}: {e.Message} | {ie.Message}  {ie.InnerException?.Message}; retrying...");
        await Task.Delay(_settings.RetryDelayMs, stoppingToken);
      }
      catch (Exception e)
      {
        _logger.LogError(e, "Unexpected error");
        throw;
      }
    }
    _logger.LogInformation($"{nameof(JobArchiver)} execution finished");
    
    _hostLifetime.StopApplication();
  }

  public override async Task StopAsync(CancellationToken cancellationToken)
  {
    await base.StopAsync(cancellationToken);
    _logger.LogDebug($"{nameof(JobArchiver)} service stopped");
  }

  private async Task ArchiveAllJobsAsync(CancellationToken stoppingToken)
  {
    if (stoppingToken.IsCancellationRequested) return;

    var date = DateTime.UtcNow - _settings.MaxAge;
    int jobCount;
    
    await using (var readContext = _factory.CreateDbContext(null))
    {
      jobCount = readContext.Jobs
                            .AsNoTracking()
                            .Count(JobDateFilter(date));
    }

    if (jobCount == 0)
    {
      _logger.LogInformation($"No jobs to archive. Next check {NextCheck.Humanize()}");
      return;
    }

    _logger.LogTrace($"Found {jobCount} jobs created before {date}");
    _logger.LogInformation($"Archiving {jobCount} jobs...");

    var batchCount = 0;
    var remainingJobs = jobCount;
    var stopwatch = Stopwatch.StartNew();

    do
    {
      await using var context = _factory.CreateDbContext(null);

      var batchSize = _settings.BatchSize;
      if (_settings.HasJobLimit)
      {
        var remainingJobsToLimit = _settings.MaxJobs - ArchivedJobs;
        if (remainingJobsToLimit < batchSize)
        {
          batchSize = remainingJobsToLimit;
          _logger.LogDebug($"Limiting next batch to {batchSize} jobs because of job limit ({ArchivedJobs}/{_settings.MaxJobs})");
        }
      }

      var jobs = context.Jobs
                        .Where(JobDateFilter(date))
                        .OrderBy(job => job.Updated)
                        .Take(batchSize);
      
      foreach (var job in jobs)
      {
        await LogJobAsync(job);
        context.Remove(job);
      }

      batchCount++;

      await context.SaveChangesAsync(stoppingToken);

      ArchivedJobs += batchSize;
      LastArchiveDate = DateTime.UtcNow;

      NotifyProgress(remainingJobs, jobCount, batchCount);

      if(stoppingToken.IsCancellationRequested) break;
      remainingJobs = context.Jobs.AsNoTracking().Count(JobDateFilter(date));
    } while (remainingJobs > 0 && CanArchive());
    

    stopwatch.Stop();

    _logger.LogInformation($"Done. Archived {jobCount.ToString()} jobs ({batchCount} batches) in {stopwatch.Elapsed.Humanize()}");
    _logger.LogInformation($"Next check in {_settings.Interval.Humanize()} ({NextCheck})");
  }

  private Expression<Func<Job, bool>> JobDateFilter(DateTime oldJobDateTime)
  {
    return job => job.Updated < oldJobDateTime
                  && (!_settings.OnlyArchiveCompletedJobs
                      || job.Status == OrchestrationStatus.Done);
  }

  private void NotifyProgress(int remainingJobs, int jobCount, int batchCount)
  {
    var now = DateTime.UtcNow;
    if (now - _lastProgressNotify <= _progressInterval) return;

    var progressPercent = (double)(jobCount - remainingJobs) / jobCount * 100.0;
    _logger.LogInformation($"Archiving... (batch {batchCount} - {progressPercent:F1}%)");
    _lastProgressNotify = now;
  }

  private async Task LogJobAsync(Job job)
  {
    var timeLastUpdated = DateTime.UtcNow;
    var blob = JsonConvert.SerializeObject(job, Formatting.None);

    // This creates a unique file per "timeLastUpdated" granularity Day (Update day in the origin rethinkDB)
    var timeLastUpdatedStr = timeLastUpdated.ToString(ShortDayOfYearFormat);
    var fileName = Path.Combine(_settings.OutputDirectory, $"{timeLastUpdatedStr}.deflated");
    await using var outputFile = File.Open(fileName, FileMode.Append, FileAccess.Write, FileShare.Read);
    await outputFile.WriteAsync(Encoding.UTF8.GetBytes(blob).Compress().Pack());
    _logger.LogTrace($"Archived job {job.Id} to {fileName}.");
  }
}