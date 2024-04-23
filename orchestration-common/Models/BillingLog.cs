using System;
using Newtonsoft.Json;

namespace Selma.Orchestration.Models
{
  [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
  public class BillingLog
  {
    public BillingLog() { }

    public BillingLog(long? numWords = null,
                      long? numBytes = null,
                      long? numCharacters = null,
                      TimeSpan? audioVideoDuration = null,
                      double? providerDefinedCost = null)
    {
      NumWords = numWords;
      NumBytes = numBytes;
      NumCharacters = numCharacters;
      AudioVideoDuration = audioVideoDuration;
      ProviderDefinedCost = providerDefinedCost;
    }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public long? NumWords { get; set; }
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public long? NumBytes { get; set; }
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public long? NumCharacters { get; set; }
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public TimeSpan? AudioVideoDuration { get; set; }
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public double? ProviderDefinedCost { get; set; }
  }
}