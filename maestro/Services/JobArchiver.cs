using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Npgsql;
using Selma.Orchestration.OrchestrationDB;
using Selma.Orchestration.TaskMQUtils;

namespace Selma.Orchestration.Maestro
{
  public class JobArchiver : BackgroundService
  {
    private const string ShortDayOfYearFormat = "yyyy-MM-dd";
    private readonly ILogger<JobArchiver> _logger;
    private readonly IConfiguration _configuration;
    private readonly OrchestrationDBContext _context;
    private readonly JobArchiverSettings _settings;

    private DateTime _lastProgressNotify = DateTime.MinValue;
    private readonly TimeSpan _progressInterval = TimeSpan.FromMinutes(1);

    public JobArchiver(ILogger<JobArchiver> logger, IConfiguration configuration, OrchestrationDBContext context)
    {
      _logger = logger;
      _configuration = configuration;
      _context = context;
      _settings = new JobArchiverSettings();
    }

    private DateTime NextCheck => DateTime.UtcNow + _settings.Interval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      if (stoppingToken.IsCancellationRequested) return;
      
      try
      {
        _configuration.GetSection(nameof(JobArchiver)).Bind(_settings);
        _logger.LogDebug($"Loaded settings from config: {JsonConvert.SerializeObject(_settings, Formatting.None)}");

        if (!_settings.Enabled)
        {
          _logger.LogWarning("Skipping JobArchiver: disabled in configuration");
          return;
        }

        if (!_settings.IsValid())
        {
          _logger.LogWarning("Invalid configuration: execution aborted");
          return;
        }

        if (!Directory.Exists(_settings.OutputDirectory))
        {
          Directory.CreateDirectory(_settings.OutputDirectory);
        }
      }
      catch (Exception e)
      {
        _logger.LogError(e, "Unexpected error when loading configuration: execution aborted");
        return;
      }

      _logger.LogInformation($"Maximum job age is {_settings.MaxAge.TotalDays} days");

      while (!stoppingToken.IsCancellationRequested)
      {
        try
        {
          await ArchiveAllJobsAsync(stoppingToken);

          await Task.Delay(_settings.Interval, stoppingToken);
        }
        catch (TaskCanceledException)
        {
          _logger.LogDebug("Task cancelled: execution aborted");
          break;
        }
        catch (PostgresException e)
        {
          _logger.LogWarning($"{e.GetType().FullName}: {e.MessageText}; retrying every {_settings.RetryDelayMs}ms");
          await Task.Delay(_settings.RetryDelayMs, stoppingToken);
        }
        catch (InvalidOperationException e) when (e.InnerException is NpgsqlException ie)
        {
          _logger.LogDebug($"{ie.GetType().FullName}: {ie.Message}; retrying...");
          await Task.Delay(_settings.RetryDelayMs, stoppingToken);
        }
        catch (Exception e)
        {
          _logger.LogError(e, "Unexpected error");
          _logger.LogDebug($"{nameof(JobArchiver)} execution aborted");
          return;
        }
      }
      _logger.LogInformation($"{nameof(JobArchiver)} execution finished");
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
      var jobCount = _context.Jobs.AsNoTracking().Count(JobDateFilter(date));

      if (jobCount == 0)
      {
        _logger.LogInformation($"No jobs to archive. Next check at {NextCheck}");
        return;
      }

      _logger.LogTrace($"Found {jobCount} jobs created before {date}");
      _logger.LogInformation($"Archiving {jobCount} jobs...");

      var batchCount = 0;
      var remainingJobs = jobCount;
      var stopwatch = Stopwatch.StartNew();
      var jobs = _context.Jobs
                         .Where(JobDateFilter(date))
                         .OrderBy(job => job.Updated)
                         .Take(_settings.BatchSize);

      do
      {
        foreach (var job in jobs)
        {
          await LogJobAsync(job);
          _context.Remove(job);
        }

        batchCount++;

        await _context.SaveChangesAsync(stoppingToken);

        NotifyProgress(remainingJobs, jobCount, batchCount);

        remainingJobs = _context.Jobs.AsNoTracking().Count(JobDateFilter(date));
      } while (remainingJobs > 0);

      stopwatch.Stop();

      _logger.LogInformation($"Done. Archived {jobCount.ToString()} jobs ({batchCount} batches) in {stopwatch.Elapsed.TotalSeconds:F1}s.");
      _logger.LogInformation($"Next check in {_settings.Interval.ToString()} ({NextCheck})");
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

  internal class JobArchiverSettings
  {
    public bool Enabled { get; init; } = false;
    public TimeSpan Interval { get; init; } = Timeout.InfiniteTimeSpan;
    public TimeSpan MaxAge { get; init; } = TimeSpan.MaxValue;
    public string OutputDirectory { get; init; } = Directory.GetCurrentDirectory();
    public bool OnlyArchiveCompletedJobs { get; init; } = false;
    public int BatchSize { get; init; } = 1000;
    public int RetryDelayMs { get; init; } = 30000;

    public bool IsValid()
      => Interval != Timeout.InfiniteTimeSpan
         && MaxAge != TimeSpan.MaxValue
         && MaxAge > TimeSpan.Zero
         && BatchSize > 0
         && RetryDelayMs > 500;
  }
}
