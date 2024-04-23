using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Selma.Orchestration.Models;
using Selma.Orchestration.TaskMQUtils;

namespace Selma.Orchestration.WorkerRest
{
  class Program
  {
    static void Main(string[] args)
    {
      Worker.Setup();
      //Console.WriteLine(Worker.Configuration["HttpClientTimeOut"]);
      Worker.Run(async (ILogger logger,
                        IConfiguration configuration,
                        HttpClient client,
                        ImmutableList<MqMessage> msg,
                        CancellationToken innerStoppingToken,
                        MQWorker workerWrapper) =>
      {
        HttpDetails httpDetails;
        var message = msg.First().Message;
        try
        {
          httpDetails = ((JObject)message.Payload).Value<HttpDetails>();
        }
        catch (Exception e)
        {
          throw new Exception("Failed to parse HTTP details.", e);
        }

        var response = await client.SendAsync((HttpRequestMessage)httpDetails, innerStoppingToken);
        var responseJson = await response.Content.ReadAsStringAsync(innerStoppingToken);

        JObject unserializedResult;
        try
        {
          unserializedResult = JObject.Parse(responseJson);
        }
        catch (Exception e)
        {
          throw new Exception("Failed to parse HTTP response to JSON.", e);
        }

        return new List<Result> { new Success(unserializedResult, new BillingLog(providerDefinedCost: 0.0), message.JobId) };
      });
    }
  }

  internal class HttpDetails
  {
    public string Body { get; set; }
    public string ContentType { get; set; }
    public IDictionary<string, string> Headers { get; set; }
    public string Url { get; set; }
    public string Method { get; set; }

    public static explicit operator HttpRequestMessage(HttpDetails details)
    {
      var message = new HttpRequestMessage
      {
        RequestUri = new System.Uri(details.Url),
        Method = new HttpMethod(details.Method),
        Content = new StringContent(details.Body, Encoding.UTF8, details.ContentType),
      };
      details.Headers.ToList().ForEach(kv => message.Headers.Add(kv.Key, kv.Value));
      return message;
    }
  }
}