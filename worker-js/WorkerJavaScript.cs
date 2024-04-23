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
using Selma.Orchestration.WorkerJSLib;

namespace Selma.Orchestration.WorkerJS
{
  public static class WorkerJavaScript
  {
    public static void Run() => Run(DefaultWorkerLogic);
    public static void Run(Func<ILogger, IConfiguration, HttpClient, ImmutableList<MqMessage>, CancellationToken, MQWorker, Task<List<TaskMQUtils.Result>>> workerLogic) => Worker.Run(workerLogic);
    public static Task<List<TaskMQUtils.Result>> DefaultWorkerLogic(ILogger logger, IConfiguration configuration, HttpClient cl, ImmutableList<MqMessage> msg, CancellationToken innerStoppingToken, MQWorker worker)
    {
      var serviceName = configuration?.GetValue<string>("InputQueue:Name") ?? "Service";

      // if (!Enum.TryParse(serviceName.Split('.')[0], out ServiceType serviceType))
      //   return new FatalError("Could not parse service type from queue name");
      var message = msg.First().Message.Payload;
      var jobId = msg.First().Message.JobId;
      var script = ScriptUtils.ExtractScript(message);
      var environment = message.Value<JObject>("Environment");
      var scriptResult = ScriptUtils.RunScript(logger, serviceName, message, jobId, script, environment, innerStoppingToken);
      return Task.FromResult(new List<TaskMQUtils.Result> { scriptResult });
    }
  }
}
