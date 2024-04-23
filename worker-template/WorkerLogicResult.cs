using System;
using Newtonsoft.Json.Linq;
using Selma.Orchestration.Models;

namespace Selma.Orchestration.TaskMQUtils
{
  public interface Result
  {
    // Success,
    // FatalError,
    // Requeue,
    public Guid JobId { get; set; }
    public JToken Metadata { get; set; }

  }
  public struct Success : Result
  {
    public Success(object unserializedResult, BillingLog billingLog, Guid jobId, JToken metadata = null, string routingKey = null)
    {
      BillingLog = billingLog;
      UnserializedResult = unserializedResult;
      RoutingKey = routingKey;
      JobId = jobId;
      Metadata = metadata;
    }
    public BillingLog BillingLog { get; set; }
    public object UnserializedResult { get; set; }
    public string RoutingKey { get; set; }
    public Guid JobId { get; set; }
    public JToken Metadata { get; set; }
  }
  public struct FatalError : Result
  {
    public FatalError(string traceId, string errorMessage, string errorType, Guid jobId, JToken metadata = null)
    {
      TraceId = traceId;
      ErrorMessage = errorMessage;
      ErrorType = errorType;
      JobId = jobId;
      Metadata = metadata;
    }

    public FatalError(string errorMessage, string errorType, Guid jobId, JToken metadata = null) : this(Guid.NewGuid().ToString(), errorMessage, errorType, jobId, metadata)
    {
    }

    public string TraceId { get; set; }
    public string ErrorMessage { get; set; } // A detailed, possibly internal, message
    public string ErrorType { get; set; } // A short-code/type which represents the error. Can go for example through i18n to show the error to a user in the GUI.
    public Guid JobId { get; set; }
    public JToken Metadata { get; set; }

  }
  public struct Requeue : Result
  {
    public Requeue(Guid jobId, JToken metadata = null, string errorMessage = null)
    {
      ErrorMessage = errorMessage;
      JobId = jobId;
      Metadata = metadata;
    }

    public string ErrorMessage { get; set; }
    public Guid JobId { get; set; }
    public JToken Metadata { get; set; }

  }
}