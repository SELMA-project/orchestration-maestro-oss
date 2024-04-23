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

namespace Selma.Orchestration.WorkerExample
{

  class Program
  {
    static void Main()
    {
      // var f = Worker.;
      Worker.Run(
        async (ILogger l, IConfiguration c, HttpClient cl, ImmutableList<MqMessage> msg, CancellationToken innerStoppingToken, MQWorker worker) =>
        {
          var message = msg.First().Message; //Document to processs
          var requestData = message.Payload;
          var text = requestData["Text"].Value<string>();
          var resultData = new
          {
            Timestamp = DateTime.Now,
            Text = new string(text.ToCharArray().Reverse().ToArray())
          };
          await Task.Delay(0);
          return new List<TaskMQUtils.Result> { new Success(resultData, new BillingLog(providerDefinedCost: 0.0), message.JobId) };
        });
    }
  }
}