using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Selma.Orchestration.OrchestrationDB
{
  /// <summary>
  /// Allows specifying the criteria for a job so it can be matched to a worker.
  /// </summary>
  public class JobFilter
  {
    private static readonly JsonSerializerSettings JsonSerializerSettings = new()
    {
      Formatting = Formatting.None,
      NullValueHandling = NullValueHandling.Ignore
    };

    public string Runtime { get; set; }
    public string Type { get; set; }
    public string Provider { get; set; }
    public HashSet<string> Scenarios { get; set; } = new();
    public HashSet<string> Languages { get; set; } = new();

    public bool Validate(JobInfo jobInfo)
    {
      return IsValid(jobInfo.Runtime, Runtime) &&
             IsValid(jobInfo.Type, Type) &&
             IsValid(jobInfo.Provider, Provider) &&
             IsValid(jobInfo.Scenario, Scenarios) &&
             IsValid(jobInfo.Language, Languages);
    }

    public bool AcceptsAnyJob()
    {
      return IsWildcard(Runtime) && IsWildcard(Type) && IsWildcard(Provider)
             && IsWildcard(Scenarios) && IsWildcard(Languages);
    }

    public override string ToString()
    {
      return JsonConvert.SerializeObject(this, JsonSerializerSettings);
    }
    
    private static bool IsValid(string prop, HashSet<string> filters)
    {
      return IsWildcard(filters) || (filters?.Any(filter => ValidateProperty(prop, filter)) ?? false);
    }

    private static bool IsValid(string prop, string filter)
    {
      return IsWildcard(filter) || ValidateProperty(prop, filter);
    }


    private static bool ValidateProperty(string prop, string filter)
    {
      prop ??= string.Empty;
      return prop.Equals(filter, StringComparison.InvariantCultureIgnoreCase);
    }

    private static bool IsWildcard(HashSet<string> props)
    {
      return props is null || !props.Any() || props.All(IsWildcard);
    }

    private static bool IsWildcard(string token)
    {
      return string.IsNullOrWhiteSpace(token);
    }

  }
}
