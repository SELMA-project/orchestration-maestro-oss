using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Selma.Orchestration.Models;
using Selma.Orchestration.TaskMQUtils;

namespace Selma.Orchestration.WorkerEasyNMT
{

  class Program
  {
    // static readonly HttpClient client = new HttpClient();
    // const string EasyNmtUrl = @"http://localhost:4545/translate";
    const string EasyNmtUrl = @"http://localhost:8000/translate";

    static void Main()
    {
      Worker.Run(
        async (ILogger l, IConfiguration c, HttpClient client, ImmutableList<MqMessage> msg, CancellationToken innerStoppingToken, MQWorker workerWrapper) =>
        { 
          var message = msg.First().Message;
          try
          {
            var data = message.Payload as JObject;
            Console.WriteLine("input received:", message);
            // var text = data["Text"].Value<string>() ?? "Lorem ipsum dolor sit amet";
            // var src_lang = data["src"].Value<string>() ?? "lv";
            // var dest_lang = data["dest"].Value<string>() ?? "en";

            // var content = new MultipartFormDataContent();
            // content.Add(new StringContent(src_lang), "source_lang");
            // content.Add(new StringContent(dest_lang), "target_lang");
            // content.Add(new StringContent(text), "text");

            var strData = data.ToString(Newtonsoft.Json.Formatting.None);
            Console.WriteLine(strData);
            var content = new StringContent(strData, System.Text.Encoding.UTF8, "application/json");

            try
            {
              HttpResponseMessage response = await client.PostAsync(EasyNmtUrl, content, innerStoppingToken);
              response.EnsureSuccessStatusCode();
              string responseBody = await response.Content.ReadAsStringAsync();
              Console.WriteLine(responseBody);

              var responseObj = JObject.Parse(responseBody);

              var result = new
              {
                Timestamp = DateTime.Now,
                Result = responseObj
              };
              return new List<TaskMQUtils.Result> { new Success(result, new BillingLog(providerDefinedCost: 0.0), message.JobId) };

            }
            catch (Exception e)
            {
              l.LogError($"{e.Message}");
              return new List<TaskMQUtils.Result> { new FatalError($"{e.Message}", "easynmt_worker_unknown_exception", message.JobId) };
            }

          }
          catch (HttpRequestException e)
          {
            Console.WriteLine("Error executing EasyNMT", e);
            return new List<TaskMQUtils.Result> { new FatalError($"{e.Message}", "easynmt_worker_http_exception", message.JobId) };
          }

        });
    }
  }
}
