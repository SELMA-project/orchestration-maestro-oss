using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Selma.Orchestration.OrchestrationDB;

namespace Selma.Orchestration.Maestro
{
  internal interface IBootDatabaseJobPuller
  {
  }

  public class BootDatabaseJobPuller : BackgroundService, IBootDatabaseJobPuller
  {
    private const int DefaultJobAgeHours = 24;
    
    private JobEnqueuer _jobEnqueuer;
    private ILogger _logger;
    private OrchestrationDBContext _dbcontext;
    private readonly TimeSpan _progressReportInterval;
    private readonly TimeSpan _jobAge;

    public BootDatabaseJobPuller(JobEnqueuer jobEnqueuer, ILogger<BootDatabaseJobPuller> logger, OrchestrationDBContext dbcontext, IConfiguration configuration)
    {
      _jobEnqueuer = jobEnqueuer;
      _logger = logger;
      _dbcontext = dbcontext;
      _progressReportInterval = TimeSpan.FromMinutes(1);

      var jobAgeString = configuration.GetSection(nameof(BootDatabaseJobPuller))
                                      .GetValue<string>("JobAge");
      if (string.IsNullOrWhiteSpace(jobAgeString))
      {
        _jobAge = TimeSpan.FromHours(DefaultJobAgeHours);
        logger.LogWarning($"Missing JobAge, using default: {DefaultJobAgeHours} hours");
      }
      else
      {
        _jobAge = TimeSpan.Parse(jobAgeString);
      }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      if (_jobAge.TotalHours <= 0)
        throw new ArgumentOutOfRangeException("JobAge must be positive");
      _logger.LogInformation($"Using job age {_jobAge.ToString("g")}");
      // Note: this query is only ran once at boot time. It only fetches jobs that were already queued before crash.
      var createdDate = DateTime.UtcNow - _jobAge;
      var pendingJobs = _dbcontext.Jobs
                                  .Where(j => j.Status == OrchestrationStatus.Queued &&
                                              j.Created > createdDate);

      var count = 0;
      var total = pendingJobs.Count();
      var lastProgressReport = DateTime.UtcNow;

      var errors = 0;
      try
      {
        _logger.LogInformation($"Processing {total} boot-time pending jobs created after {createdDate.ToString("u")}...");
        foreach (var job in pendingJobs)
        {
          if (stoppingToken.IsCancellationRequested)
          {
            // note: For some reason breaking a loop of a streaming query takes
            //       a long time: it might be enumerating everything on the db
            //       driver side? Possible bug.
            //       If this isn't desired try a buffered query with pagination
            _logger.LogWarning($"Cancelling... (enqueued {count} boot-time jobs)");
            return;
          }

          await _jobEnqueuer.Enqueue(job, stoppingToken);
          if (job.Status == OrchestrationStatus.Error)
          {
            errors++;
          }
          count++;

          var now = DateTime.UtcNow;
          if (now - lastProgressReport < _progressReportInterval) continue;

          lastProgressReport = now;
          _logger.LogInformation($"Enqueued jobs: {count}/{total}");
        }
        _logger.LogInformation($"Finished {count} boot-time pending jobs");
      }
      catch (Exception e) when (e is not OperationCanceledException)
      {
        _logger.LogError(e, "Unexpected error");
      }
      finally
      {
        if (errors > 0)
        {
          await _dbcontext.SaveChangesAsync(CancellationToken.None);
        }
      }
    }
  }
}
