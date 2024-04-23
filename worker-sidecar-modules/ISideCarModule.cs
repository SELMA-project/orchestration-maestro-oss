
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Selma.Orchestration.Models;
using Selma.Orchestration.TaskMQUtils;
using System.Collections.Immutable;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Selma.Orchestration.SideCarModule
{
  public interface ISideCarModule
  {
    Task<List<TaskMQUtils.Result>> Run(ILogger logger, IConfiguration configuration, HttpClient cl, ImmutableList<MqMessage> message, CancellationToken innerStoppingToken, MQWorker worker);

  }
}