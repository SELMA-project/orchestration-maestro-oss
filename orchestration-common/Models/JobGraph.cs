using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Selma.Orchestration.OrchestrationDB
{
  public class JobGraph
  {
    public JobGraph()
    {
    }

    public JobGraph(Guid workflowId, List<JobNode> jobNodes)
    {
      WorkflowId = workflowId;
      JobNodes = jobNodes;
    }

    public Guid WorkflowId { get; set; }
    public List<JobNode> JobNodes { get; set; }
    [JsonConverter(typeof(StringEnumConverter))]
    public OrchestrationStatus Status { get; set; }
  }
}