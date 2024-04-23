using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Esprima;
using Jint;
using Jint.Constraints;
using Jint.Native;
using Jint.Runtime;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Selma.Orchestration.Models;
using Selma.Orchestration.TaskMQUtils;
using Formatting = Newtonsoft.Json.Formatting;
using System.Net.Http;

namespace Selma.Orchestration.WorkerJSLib
{
  public class ScriptUtils
  {
    public static Result RunScript(ILogger logger, string serviceName, JToken message, Guid jobId, string script, JObject environment, CancellationToken innerStoppingToken)
    {
      if (string.IsNullOrWhiteSpace(script))
        return new FatalError("Script not found", "javascript_worker_missing_script", jobId);

      var credentials = message.Value<string>("Credentials");
      if (string.IsNullOrWhiteSpace(credentials))
        logger.LogWarning("Empty credentials");

      var options = new Options();
      // options.Interop.Enabled = true;

      var cancellationConstraint = new CancellationConstraint(innerStoppingToken);
      options.Constraint(cancellationConstraint);

      var engine = new Engine(options);

      var billingLog = new BillingLog(providerDefinedCost: 0.0);
      var cleanupFunction = JsValue.Undefined;
      try
      {
        engine.SetValue("_request", new JsHttpClient(logger));
        engine.SetValue("_logger", new JsLogger(logger));
        engine.SetValue("_println", new Action<string>(Console.WriteLine));
        engine.SetValue("_print", new Action<string>(Console.Write));
        engine.SetValue("_delay", new Action<int>(msDelay => Task.Delay(msDelay).GetAwaiter().GetResult()));
        engine.SetValue("_guid", new Func<string>(() => Guid.NewGuid().ToString()));
        engine.SetValue("_remote", Environment.GetEnvironmentVariable("car_url"));
        engine.SetValue("_sha256", new Func<string, string>(s =>
        {
          var sha = SHA256.Create();
          var b = Encoding.UTF8.GetBytes(s);
          return Convert.ToHexString(sha.ComputeHash(b)).ToLowerInvariant();
        }));
        engine.SetValue("_xml_to_json", new Func<string, string>(xml =>
        {
          var xmlDoc = new XmlDocument();
          xmlDoc.LoadXml(xml);
          return JsonConvert.SerializeXmlNode(xmlDoc);
        }));
        engine.SetValue("_json_to_xml", new Func<string, string>(json =>
        {
          var node = JsonConvert.DeserializeXNode(json);
          return node.ToString();
        }));

        engine.SetValue("_set_billing", (Action<JsValue>)(b =>
        {
          if (!b.IsObject())
          {
            throw new JavaScriptException($"_set_billing() expects an object as argument: got '{b.Type}'");
          }
          var billing = JObject.FromObject(b.ToObject());
          billingLog = billing.ToObject<BillingLog>();
        }));

        engine.SetValue("atob", (Func<string, string>)(str => Encoding.UTF8.GetString(Convert.FromBase64String(str))));
        engine.SetValue("btoa", (Func<string, string>)(str => Convert.ToBase64String(Encoding.UTF8.GetBytes(str))));

        engine.SetValue("_jobId", jobId.ToString());
        engine.SetValue("_credentials", credentials);

        // engine.SetValue("_message", message);
        engine.SetValue("_messageJson", message.ToString(Formatting.None));
        engine.Execute(@"class BillingLog {
        constructor({numWords = null,
                    numBytes = null,
                    numCharacters = null,
                    audioVideoDuration = null,
                    providerDefinedCost = null} = {}) {
          this.numWords = numWords;
          this.numBytes = numBytes;
          this.numCharacters = numCharacters;
          this.audioVideoDuration = audioVideoDuration;
          this.providerDefinedCost = providerDefinedCost;
        }}");
        engine.Execute($"const _message = JSON.parse(_messageJson);");

        var messageEnvironment = message.Value<JObject>("Environment");
        if (messageEnvironment != null)
          environment.Merge(messageEnvironment);

        if (environment != null)
        {
          foreach (var venv in environment)
          {
            engine.SetValue(venv.Key, venv.Value.ToString());
          }
        }

        engine.Execute(script);

        var mainFunction = engine.GetValue("main");
        if (!mainFunction.IsCallable())
          return new FatalError("Missing main() function.", "javascript_worker_missing_main", jobId);

        cleanupFunction = engine.GetValue("cleanup");

        logger.LogInformation($"Running {serviceName} main() for job {jobId}...");
        var result = engine.Invoke(mainFunction);

        ScriptUtils.TryCleanup(logger, engine, cleanupFunction);

        var rawResponse = result.ToObject();

        return new Success(rawResponse, billingLog, jobId: jobId, routingKey: serviceName);
      }
      catch (Exception e) when (e is OperationCanceledException or TaskCanceledException)
      {
        // cancellationConstraint.Reset(CancellationToken.None);
        // TryCleanup(logger, engine, cleanupFunction);
        return new FatalError("Cancelled. ran cleanup function if available", "javascript_worker_canceled", jobId);
      }
      catch (ParserException e)
      {
        logger.LogError($"{nameof(ParserException)} at {e.LineNumber}:{e.Column}:{e.Description}");
        return new FatalError(e.Message, "javascript_worker_parser_exception", jobId);
      }
      catch (JavaScriptException e)
      {
        logger.LogError(e.ToString());
        ScriptUtils.TryCleanup(logger, engine, cleanupFunction);
        return new FatalError(e.Message, "javascript_worker_js_exception", jobId);
      }
      catch (Exception e)
      {
        if (e is HttpRequestException)
        {
          throw e;
        }
        logger.LogError(e, e.Message);
        ScriptUtils.TryCleanup(logger, engine, cleanupFunction);
        return new FatalError($"{e.GetType()} + {e.Message}", "javascript_worker_unknown_exception", jobId);
      }
    }
    public static string ExtractScript(JToken message)
    {
      var details = message.Value<JObject>("Details");
      var script = details?.Value<string>("Script");
      details?.Remove("Script");
      return script;
    }
    private static void TryCleanup(ILogger logger, Engine engine, JsValue cleanupFunction)
    {
      try
      {
        if (cleanupFunction.IsNull())
          return; //already ran

        if (cleanupFunction.IsUndefined() || !cleanupFunction.IsCallable())
        {
          logger.LogDebug("Skipped cleanup: no valid 'cleanup' function declared");
          return;
        }

        engine.Invoke(cleanupFunction);
        engine.SetValue("cleanup", (object)null);
        logger.LogDebug("Cleaned up");
      }
      catch (Exception e)
      {
        logger.LogError(e, "Failed to run cleanup function!");
      }
    }
  }
}
