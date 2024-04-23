namespace Selma.Orchestration.Maestro.JobArchiver;

internal class JobArchiverSettings
{
  public bool Enabled { get; init; } = false;
  public TimeSpan Interval { get; init; } = Timeout.InfiniteTimeSpan;
  public TimeSpan MaxAge { get; init; } = TimeSpan.MaxValue;
  public string OutputDirectory { get; init; } = Directory.GetCurrentDirectory();
  public bool OnlyArchiveCompletedJobs { get; init; } = false;
  public int BatchSize { get; init; } = 1000;
  public int RetryDelayMs { get; init; } = 30000;
  public int MaxJobs { get; init; } = 0;
  public TimeSpan MaxJobInterval { get; init; } = Timeout.InfiniteTimeSpan;

  public bool HasJobLimit => MaxJobs > 0 && MaxJobInterval != Timeout.InfiniteTimeSpan;
  
  public bool IsValid()
    => Interval != Timeout.InfiniteTimeSpan
       && MaxAge != TimeSpan.MaxValue
       && MaxAge > TimeSpan.Zero
       && BatchSize > 0
       && RetryDelayMs > 500;
}
