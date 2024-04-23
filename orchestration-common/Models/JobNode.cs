using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace Selma.Orchestration.OrchestrationDB
{
  public class JobNode
  {
    public JobNode()
    {
      Scripts = new ScriptData();
      JobInfo = new JobInfo();
    }

    // [JsonConstructorAttribute]
    public JobNode(Guid id,
                   HashSet<Guid> dependencies,
                   JToken jobData,
                   string jobType,
                   string jobProvider,
                   JToken jobMetadata = null,
                   string scriptInput = null,
                   string scriptOutput = null)
    {
      Id = id;
      Dependencies = dependencies;
      JobData = jobData;
      JobMetadata = jobMetadata;
      JobInfo = new JobInfo(type: jobType, provider: jobProvider);
      Scripts = new ScriptData(scriptInput, scriptOutput);
    }

    public JobNode(Guid id,
                   HashSet<Guid> dependencies,
                   JToken jobData,
                   JobInfo jobInfo,
                   ScriptData scripts)
    {
      Id = id;
      Dependencies = dependencies;
      JobData = jobData;
      JobInfo = jobInfo;
      Scripts = scripts;
    }

    public JToken JobResult { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    public OrchestrationStatus Status { get; set; }

    public Guid Id { get; set; }
    public HashSet<Guid> Dependencies { get; set; }
    public JToken JobData { get; set; }
    public JToken JobMetadata { get; set; }
    public JobInfo JobInfo { get; set; }
    public ScriptData Scripts { get; set; }

    [Obsolete("Prefer using JobInfo.Type", error: false)]
    public string JobType
    {
      get => JobInfo.Type;
      set => JobInfo.Type = value;
    }

    [Obsolete("Prefer using JobInfo.Provider", error: false)]
    public string JobProvider
    {
      get => JobInfo.Provider;
      set => JobInfo.Provider = value;
    }

  }

  [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
  public class ScriptData
  {
    public ScriptData(string input = null, string output = null)
    {
      Input = input;
      Output = output;
    }

    public string Input { get; set; }
    public string Output { get; set; }
  }

}
