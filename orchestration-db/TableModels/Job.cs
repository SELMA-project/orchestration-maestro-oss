using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Selma.Orchestration.Models;

namespace Selma.Orchestration.OrchestrationDB
{
  public class Job : IEntityWithUpdateTime
  {
    public Job()
    {
    }

    public Job(JobNode node, Guid workflowId)
    {
      Id = node.Id;
      Status = (node.Dependencies.Count == 0 ? OrchestrationStatus.Queued : OrchestrationStatus.Waiting);
      WorkflowId = workflowId;
      Dependencies = node.Dependencies;
      OG_Dependencies = node.Dependencies;
      Type = node.JobInfo.Type;
      Provider = node.JobInfo.Provider;
      Language = node.JobInfo.Language;
      Scenario = node.JobInfo.Scenario;
      Request = node.JobData;
      Input = node.JobData;
      Scripts = JToken.FromObject(node.Scripts);
      if (!node.JobMetadata.IsNullOrEmpty())
      {
        Metadata = node.JobMetadata;
      }
    }

    public Guid Id { get; set; }
    public DateTime Updated { get; set; }
    public DateTime Created { get; set; }
    public OrchestrationStatus Status { get; set; }
    public Guid WorkflowId { get; set; }
    public HashSet<Guid> Dependencies { get; set; }
    public HashSet<Guid> OG_Dependencies { get; set; }
    public string Type { get; set; }
    public string Provider { get; set; }
    public string Scenario { get; set; }
    public string Language { get; set; }
    public JToken Request { get; set; }
    public JToken Input { get; set; }
    public JToken Result { get; set; }
    public JToken Scripts { get; set; }
    public JToken Metadata { get; set; }

    public static explicit operator JobNode(Job job)
    {
      var node = new JobNode();
      node.Id = job.Id;
      node.Status = job.Status;
      node.Dependencies = job.OG_Dependencies;
      node.JobInfo = new JobInfo
      {
        Type = job.Type,
        Provider = job.Provider,
        Language = job.Language,
        Scenario = job.Scenario
      };
      node.JobData = job.Request;
      if (job.Status == OrchestrationStatus.Done)
      {
        node.JobResult = job.Result;
      }
      var scripts = (JObject)job.Scripts;
      node.Scripts.Input = scripts.Value<string>("Input");
      node.Scripts.Output = scripts.Value<string>("Output");
      node.JobMetadata = job.Metadata;
      return node;
    }

    public override string ToString() 
      => $"[{Id}] {Type ?? "<Type>"}.{Provider ?? "<Provider>"} (wf: {WorkflowId})";

    public void SetError(ErrorPayload errorPayload)
    {
      Status = OrchestrationStatus.Error;
      Result = JObject.FromObject(errorPayload);
    }
  }
}