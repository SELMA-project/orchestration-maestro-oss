using System;
using System.Text.Json.Serialization;

namespace Selma.Orchestration.OrchestrationDB
{
  [JsonConverter(typeof(JsonStringEnumConverter))]
  public enum OrchestrationStatus
  {
    Error = -1, // Processing error
    New = 0, // In DB, not in queue yet
    Waiting = 1, // In DB, waiting for dependencies
    Queued = 2, // In queue, waiting to be processed
    Done = 3, // Done
  }
}