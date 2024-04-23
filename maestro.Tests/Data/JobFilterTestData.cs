using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Selma.Orchestration.OrchestrationDB;

namespace Selma.Orchestration.Maestro.Tests.Data;

public class JobFilterTestData : IEnumerable<object[]>
{
  public IEnumerator<object[]> GetEnumerator()
  {
    var data = new Dictionary<JobFilter, (JobInfo jobInfo, bool expected)[]>
    {
      {
        new JobFilter
        {
          Provider = "Google",
          Type = "Transcription"
        },
        new[]
        {
          (new JobInfo {Type = "Transcription", Provider = "Google"}, true),
          (new JobInfo {Type = "Translation", Provider = "Google"}, false),
          (new JobInfo {Type = "Transcription", Provider = "Azure"}, false)
        }
      },
      {
        new JobFilter
        {
          Runtime = "JavaScript",
        },
        new[]
        {
          (new JobInfo {Runtime = "JavaScript", Provider = "Google"}, true),
          (new JobInfo {Runtime = "JavaScript", Type = "Transcription"}, true),
          (new JobInfo {Type = "Translation", Provider = "Google"}, false),
          (new JobInfo {Type = "Transcription", Provider = "Azure"}, false)
        }
      },
      {
        new JobFilter
        {
          Languages = null, // all
          Scenarios = {"WORLD"}
        },
        new[]
        {
          (new JobInfo {Scenario = "WORLD", Provider = "Google"}, true),
          (new JobInfo {Scenario = "WORLD", Type = "Translation", Language = "en"}, true),
          (new JobInfo {Scenario = "WORLD", Type = "Translation", Language = "gb"}, true),
          (new JobInfo {Scenario = "PT", Type = "Translation", Provider = "Google", Language = "es"}, false),
          (new JobInfo {Type = "Transcription", Provider = "Azure", Language = "en"}, false)
        }
      },
      {
        new JobFilter
        {
          Languages = {"en", "es"}, Scenarios = null // all
        },
        new[]
        {
          (new JobInfo {Scenario = "WORLD", Provider = "Google"}, false),
          (new JobInfo {Scenario = "WORLD", Type = "Translation", Language = "en"}, true),
          (new JobInfo {Scenario = "WORLD", Type = "Translation", Language = "gb"}, false),
          (new JobInfo {Scenario = "PT", Type = "Translation", Provider = "Google", Language = "es"}, true),
          (new JobInfo {Type = "Transcription", Provider = "Azure", Language = "en"}, true)
        }
      },
      {
        new JobFilter
        {
          Languages = {"en", "es"}, Scenarios = {"WORLD"}
        },
        new[]
        {
          (new JobInfo {Scenario = "WORLD", Provider = "Google"}, false),
          (new JobInfo {Scenario = "WORLD", Type = "Translation", Language = "en"}, true),
          (new JobInfo {Scenario = "WORLD", Type = "Translation", Language = "gb"}, false),
          (new JobInfo {Scenario = "PT", Type = "Translation", Provider = "Google", Language = "es"}, false),
          (new JobInfo {Type = "Transcription", Provider = "Azure", Language = "en"}, false)
        }
      }
    };

    return data.SelectMany(x => x.Value,
                           (x,
                            results) =>
                           {
                             return new object[]
                             {
                               x.Key,
                               results.jobInfo,
                               results.expected
                             };
                           })
               .GetEnumerator();
  }

  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
