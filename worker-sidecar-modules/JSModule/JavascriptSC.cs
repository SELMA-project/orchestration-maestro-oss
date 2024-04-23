using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Selma.Orchestration.TaskMQUtils;
using Selma.Orchestration.WorkerJSLib;
using System.Collections.Immutable;

namespace Selma.Orchestration.SideCarModule;

public class JavaScriptSC : ISideCarModule
{
  public async Task<List<Result>> Run(ILogger logger, IConfiguration configuration, HttpClient cl, ImmutableList<MqMessage> msg, CancellationToken innerStoppingToken, MQWorker worker)
  {
    var serviceName = configuration?.GetValue<string>("InputQueue:Name") ?? "Service";

    var message = msg.First().Message; //Document to processs
    var jobId = message.JobId;
    var docobj = message.Payload.ToObject<JObject>()!;

    var scriptPath = configuration!.GetValue<string>("JSWorker:Script");
    var requestUrl = configuration!.GetValue<string>("JSWorker:RequestUrl");

    // Add custom environment variables to be accessed by javascript here
    //TODO change to dynamic loading the "Environment" section in the future
    JObject environment = new()
    {
      { "_remote", requestUrl }
    };
    var script = File.ReadAllText(scriptPath);
    return new List<Result> { await Task.FromResult(ScriptUtils.RunScript(logger, serviceName, docobj, jobId, script, environment, innerStoppingToken)) };
  }
}