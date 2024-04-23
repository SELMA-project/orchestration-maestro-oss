using System;
using System.Linq;

namespace Selma.Orchestration.OrchestrationDB
{
  /// <summary>
  /// <para>Job metadata relevant for workers and orchestration.</para>
  /// </summary>
  /// <seealso cref="JobFilter"/>
  public class JobInfo
  {
    public const string WildcardString = "Any";
    private const string DefaultFormatString = $"{nameof(Type)}.{nameof(Provider)}";

    private static readonly string[] SupportedProps =
    {
      nameof(Type), nameof(Provider), nameof(Scenario), nameof(Language), nameof(Runtime)
    };

    public JobInfo(string type = null, string provider = null, string formatString = null)
    {
      Runtime = null;
      Type = type;
      Provider = provider;
      Scenario = null;
      Language = null;
      FormatString = formatString;
    }

    /// <example> JavaScript <br/> Standalone </example>
    public string Runtime { get; set; }
    
    /// <example> Translation <br/> Voiceover <br/> Transcription </example>
    public string Type { get; set; }

    /// <example> Google <br/> Azure <br/> Selma </example>
    public string Provider { get; set; }
    
    /// <example> WORLD <br/> PT </example>
    public string Scenario { get; set; }
    
    /// <example> en_gb <br/> pt-pt <br/> jp </example>
    public string Language { get; set; }

    /// <summary> A dot delimited format string defining the properties and their order. </summary>
    /// <example> Type.Provider <br/> Provider.Type.Scenario.Language </example>
    public string FormatString { get; init; }

    public string QueueName() => QueueName(FormatString);

    /// <summary>
    /// Returns a queue name for this job info based on the format string.
    /// </summary>
    /// <remarks>Null or empty properties present in the format string are
    /// replaced with the <see cref="WildcardString"/>. </remarks>
    /// <exception cref="FormatException"></exception>
    public string QueueName(string format, bool trimWildcards = true, string separator = ".")
    {
      // provide default formatting 
      if (string.IsNullOrWhiteSpace(format)) format = DefaultFormatString;

      var tokens = format.ToLowerInvariant().Split(separator);
      
      // provide default formatting for unsupported format strings
      if (tokens.Except(SupportedProps.Select(x => x.ToLowerInvariant())).Any())
      {
        throw new FormatException($"The format '{format}' is invalid. Use one of the following: {string.Join(", ", SupportedProps)}");
      }

      var properties = typeof(JobInfo).GetProperties();
      var values = tokens.Select(x => properties.Single(p => p.Name.ToLowerInvariant() == x)
                                                .GetValue(this)?.ToString())
                         .ToList();

      // remove redundant wildcards from the end, but leave at least one
      if (trimWildcards)
      {
        for (var i = values.Count - 1; i >= 1; i--)
        {
          if(!string.IsNullOrWhiteSpace(values[i])) break;
          values.RemoveAt(i);
        }
      }
      
      return string.Join(separator, values.Select(x => string.IsNullOrWhiteSpace(x) ? WildcardString : x));
    }
    
  }
}
