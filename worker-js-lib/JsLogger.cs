using System.Linq;
using Microsoft.Extensions.Logging;

namespace Selma.Orchestration.WorkerJSLib
{
  public class JsLogger
  {
    private readonly ILogger _logger;
    private readonly string[] _argsTemplate;

    public JsLogger(ILogger logger)
    {
      _logger = logger;
      _argsTemplate = BuildArgTemplates(30);
    }

    public void Log(params object[] args) => Info(args);

    public void Info(params object[] args) => _logger.LogInformation(_argsTemplate[args.Length], args);
    public void Warn(params object[] args) => _logger.LogWarning(_argsTemplate[args.Length], args);
    public void Error(params object[] args) => _logger.LogError(_argsTemplate[args.Length], args);
    public void Debug(params object[] args) => _logger.LogDebug(_argsTemplate[args.Length], args);
    public void Critical(params object[] args) => _logger.LogCritical(_argsTemplate[args.Length], args);
    public void Trace(params object[] args) => _logger.LogTrace(_argsTemplate[args.Length], args);

    private static string[] BuildArgTemplates(int maxArgs)
    {
      return Enumerable.Range(0, maxArgs)
                       .Select(i => string.Join(", ", Enumerable.Range(0, i)
                                             .Select(idx => "{arg" + idx + "}")))
                       .ToArray();
    }
  }
}