using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Selma.Orchestration.Models;
using Selma.Orchestration.TaskMQUtils;
using Selma.Orchestration.SideCarModule;
using Selma.Orchestration.WorkerSC.Utils;
using Selma.Orchestration;
using System.Net.Http;
using System.Threading;
using System.Collections.Immutable;

Worker.Run(
  async (
    ILogger logger,
    IConfiguration
    configuration,
    HttpClient cl,
    ImmutableList<MqMessage> messages,
    CancellationToken innerStoppingToken,
    MQWorker worker
  ) =>
  {
    string reqWorkerType = configuration["SidecarWorker:WorkerType"];
    var action = Reflection.CreateObjectFromTypeName<ISideCarModule>(reqWorkerType);
    return await action.Run(logger, configuration, cl, messages, innerStoppingToken, worker);
  }
);
