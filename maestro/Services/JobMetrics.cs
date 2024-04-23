using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Selma.Orchestration.Maestro;

using System.Diagnostics.Metrics;

public class JobMetrics : IDisposable
{
  private ILogger<JobMetrics> _logger;
  public string MeterName { get; }
  private readonly MeterListener _meterListener = new();
  private readonly Meter _meter;


  public long DoneTotalMs { get; private set; } = 0;
  public int DoneTotal { get; private set; } = 0;
  public double DoneAvgMs => (double)DoneTotalMs / DoneTotal;
  
  public int QueuedTotal { get; private set; }

  public Counter<int> Queued { get; private set; }
  public Histogram<long> DoneDuration { get; private set; }

  public JobMetrics(string meterName,  ILogger<JobMetrics> logger)
  {
    _logger = logger;
    MeterName = meterName;
    _meterListener.InstrumentPublished = (instrument, listener) =>
    {
      if (instrument.Meter.Name == MeterName)
      {
        listener.EnableMeasurementEvents(instrument);
      }
    };
    _meterListener.SetMeasurementEventCallback<int>(OnMeasurementRecordedQueuedCount);
    _meterListener.SetMeasurementEventCallback<long>(OnMeasurementRecordedDoneTime);
    _meterListener.Start();
    
    _meter = new Meter(MeterName, "1.0");
    
    Queued = _meter.CreateCounter<int>($"{MeterName}.new.count", "jobs", "jobs added to database");
    DoneDuration = _meter.CreateHistogram<long>($"{MeterName}.done.duration", "ms", "duration of successfully processed job results");
  }

  private void OnMeasurementRecordedDoneTime(Instrument instrument, long measurement, ReadOnlySpan<KeyValuePair<string, object>> tags, object state)
  {
    var count = (int?)tags.ToArray().FirstOrDefault(x => x.Key is "count").Value ?? 1;
    
    DoneTotalMs += measurement;
    DoneTotal += count;

    _logger.LogTrace($"{instrument.Name} recorded measurement {TimeSpan.FromMilliseconds(measurement):g} for {count} jobs (batch avg: {TimeSpan.FromMilliseconds(DoneAvgMs):g})");
  }

  private void OnMeasurementRecordedQueuedCount(Instrument instrument, int measurement, ReadOnlySpan<KeyValuePair<string, object>> tags, object state)
  {
    QueuedTotal += measurement;
    _logger.LogTrace($"{instrument.Name} recorded measurement {measurement}");
  }


  public void Dispose()
  {
    _logger.LogInformation($"Recorded {QueuedTotal} queued jobs, {DoneTotal} done jobs or batches; Avg. job duration: {TimeSpan.FromMilliseconds(DoneAvgMs):g}");
    
    _meter?.Dispose();
    _meterListener?.Dispose();
  }
}
