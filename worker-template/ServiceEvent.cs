using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Selma.Orchestration.OrchestrationDB;

namespace Selma.Orchestration.Models;

public record ServiceEvent
{
  public ServiceEventOperation Operation { get; set; }
  public JobInfo Data { get; set; }

  public override string ToString()
  {
    return $"{nameof(ServiceEvent)}: {Operation} ({Data.QueueName()}, {Data.Runtime})";
  }
}

[JsonConverter(typeof(StringEnumConverter))]
public enum ServiceEventOperation
{
  Add,
  Remove
}
