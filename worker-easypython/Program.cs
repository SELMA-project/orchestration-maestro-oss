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

namespace Selma.Orchestration.WorkerEasyPy
{

  class Program
  {
    // const string EasyNmtUrl = @"http://localhost:8000/translate";
    const string TQListWorkers = @"http://localhost:9000/api/workers";
    const string TQAcquireMTWorker = @"http://localhost:9000/api/workers/acquire?type=mt";
    const string TQReleaseWorker = @"http://localhost:9000/api/workers/release";

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

            var strData = data.ToString(Newtonsoft.Json.Formatting.None);
            Console.WriteLine(strData);
            var content = new StringContent(strData, System.Text.Encoding.UTF8, "application/json");

            try
            {
              HttpResponseMessage acquireResponse = await client.GetAsync(TQAcquireMTWorker, innerStoppingToken);
              var WorkerUrl = await acquireResponse.Content.ReadAsStringAsync();
              Console.WriteLine($"Worker {WorkerUrl} acquired");

              HttpResponseMessage mainResponse = await client.PostAsync(WorkerUrl, content, innerStoppingToken);
              mainResponse.EnsureSuccessStatusCode();

              var releaseData = new StringContent(WorkerUrl, System.Text.Encoding.UTF8);
              HttpResponseMessage releaseResponse = await client.PostAsync(TQReleaseWorker, releaseData, innerStoppingToken);
              Console.WriteLine($"Worker {WorkerUrl} released");

              string responseBody = await mainResponse.Content.ReadAsStringAsync();
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
              return new List<TaskMQUtils.Result> { new FatalError($"{e.Message}", "python_worker_unknown_exception", message.JobId) };
            }

          }
          catch (HttpRequestException e)
          {
            Console.WriteLine("Error executing EasyPython", e);
            return new List<TaskMQUtils.Result> { new FatalError($"{e.Message}", "python_worker_http_exception", message.JobId) };
          }

        });
    }
  }
}
