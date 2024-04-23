using System;
using Newtonsoft.Json.Linq;

namespace Selma.Orchestration.Models
{
  public class Message
  {
    public Message(Guid jobId, MessageType type, JToken payload, JToken metadata)
    {
      JobId = jobId;
      Type = type;
      Payload = payload;
      Metadata = metadata;
    }

    public JToken Payload { get; set; } // The task-specific result dataload
    public Guid JobId { get; set; }
    public MessageType Type;
    public JToken Metadata { get; set; }
    public TimeSpan TimeElapsed { get; set; }

  }
  public enum MessageType
  {
    Error,
    Progress,
    PartialResult,
    FinalResult,
    Request
  }
  public interface Payload
  {

  }
  public class FinalResultPayload : Payload
  {
    public FinalResultPayload(JToken data, BillingLog billing)
    {
      Data = data;
      Billing = billing;
    }

    public JToken Data { get; set; }
    public BillingLog Billing { get; set; }

  }
  public class ProgressPayload : Payload
  {
    public ProgressPayload(double? percentage = null, double? currentValue = null, double? totalValue = null, double? estimated = null, string unit = null, DateTime? startingTime = null)
    {
      Percentage = percentage;
      CurrentValue = currentValue;
      TotalValue = totalValue;
      Estimated = estimated;
      Unit = unit;
      StartingTime = startingTime;
    }

    public double? Percentage { get; set; }
    public double? CurrentValue { get; set; }
    public double? TotalValue { get; set; }
    public double? Estimated { get; set; }
    public string Unit { get; set; }
    public DateTime? StartingTime { get; set; }
  }

  public class ErrorPayload : Payload
  {
    public ErrorPayload(string message, string type, string traceId)
    {
      Message = message;
      Type = type;
      TraceId = traceId;
    }

    public string Message { get; set; } // A detailed, possibly internal, message
    public string Type { get; set; } // A short-code/type which represents the error. Can go for example through i18n to show the error to a user in the GUI.
    public string TraceId { get; set; }
  }
}
