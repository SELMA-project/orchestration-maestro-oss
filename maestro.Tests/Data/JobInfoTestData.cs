using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Selma.Orchestration.OrchestrationDB;

namespace Selma.Orchestration.Maestro.Tests.Data;

public class JobInfoTestData : IEnumerable<object[]>
{
  public IEnumerator<object[]> GetEnumerator()
  {
    var data = new Dictionary<JobInfo, (string formatString, string expected)[]>
    {
      {
        new JobInfo
        {
          Provider = "Google",
          Type = "Transcription"
        },
        new[]
        {
          ("Type.Provider", "Transcription.Google"),
          ("Provider.Type", "Google.Transcription"),
          ("Provider.Type.Scenario.Language", "Google.Transcription"),
          ("Provider.Type.Language", "Google.Transcription")
        }
      },
      {
        new JobInfo
        {
          Type = "Translation",
          Provider = "Selma",
          Language = "en"
        },
        new[]
        {
          ("Type.Provider", "Translation.Selma"),
          ("Provider.Type", "Selma.Translation"),
          ("Provider.Type.Scenario.Language", $"Selma.Translation.{JobInfo.WildcardString}.en"),
          ("Provider.Type.Language", "Selma.Translation.en")
        }
      },
      {
        new JobInfo
        {
          Type = "Clustering",
          Provider = "Selma",
        },
        new[]
        {
          ("Type.Provider", "Clustering.Selma"),
          ("Provider.Type", "Selma.Clustering"),
          ("Provider.Type.Scenario.Language", "Selma.Clustering"),
          ("Provider.Type.Language", "Selma.Clustering")
        }
      },
      {
        new JobInfo
        {
          Type = "Clustering",
          Provider = "Selma",
          Scenario = "WORLD",
          Language = "PT"
        },
        new[]
        {
          ("Type.Provider", "Clustering.Selma"),
          ("Provider.Type", "Selma.Clustering"),
          ("Provider.Type.Scenario.Language", "Selma.Clustering.WORLD.PT"),
          ("Provider.Type.Language", "Selma.Clustering.PT")
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
                               results.formatString,
                               results.expected
                             };
                           }).GetEnumerator();
  }

  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
