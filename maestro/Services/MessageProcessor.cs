using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using Selma.Orchestration.Models;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Selma.Orchestration.OrchestrationDB;

namespace Selma.Orchestration.Maestro
{
  public class MessageProcessor
  {
    private readonly ILogger<MessageProcessor> _logger;
    private readonly EngineFactory _engineFactory;

    public MessageProcessor(ILogger<MessageProcessor> logger, IConfiguration config)
    {
      _logger = logger;

      var jintConfig = config.GetSection("MessageProcessor:Jint");
      _engineFactory = new EngineFactory(jintConfig);
    }


    public Message Process(Message msg, Job job)
    {
      var scripts = job.Scripts.ToObject<ScriptData>();
      var source = scripts.GetByMessageType(msg.Type);
      
      if (string.IsNullOrEmpty(source))
      {
        _logger.LogTrace("{JobId} ({MessageType}) skipped: script is null or empty", msg.JobId, msg.Type);
        return msg;
      }

      _logger.LogTrace("{JobId} ({MessageType}) processing...", msg.JobId, msg.Type);

      try
      {
        var input = msg.GetPayloadData().ToObject<object>();
        var metadata = job.Metadata?.ToObject<object>();
        var jobInput = msg.Type == MessageType.FinalResult ? job.Input?.ToObject<object>() : null;

        var output = _engineFactory.Build()
                                   .SetValue("_input", input ?? JsValue.Undefined)
                                   .SetValue("_meta", metadata ?? JsValue.Undefined)
                                   .SetValue("_jobInput", jobInput ?? JsValue.Undefined)
                                   .Evaluate($"(function (){{{source}}})()");
        msg.SetPayloadData(JToken.FromObject(output.ToObject()));
      }
      catch (JavaScriptException e)
      {
        throw new MessageProcessingException(msg, source, new Exception(e.Message + $" (at {e.LineNumber}:{e.Column})")); 
      }
      catch (Exception e)
      {
        throw new MessageProcessingException(msg, source, e); 
      }

      var payloadString = msg.Payload.ToString(Formatting.None);
      _logger.LogTrace("{JobId} ({MessageType}) done, new msg: {ProcessedMessagePayload} (...)", msg.JobId, msg.Type, payloadString.Substring(0, Math.Min(payloadString.Length, 100)));

      return msg;
    }
  }

  public static class MessageProcessorExtensions
  {
    public static string GetByMessageType(this ScriptData scripts, MessageType messageType)
    {
      if (scripts is null) return string.Empty;

      return messageType switch
      {
        MessageType.Request => scripts.Input,
        MessageType.FinalResult => scripts.Output,
        _ => throw new NotImplementedException($"Message of type {messageType} cannot be processed.")
      };
    }

    public static void SetPayloadData(this Message message, JToken data)
    {
      if (message.Type is MessageType.FinalResult) message.Payload["Data"]?.Replace(data);
      else message.Payload = data;
    }
    
    public static JToken GetPayloadData(this Message message) => message.Type switch
    {
      MessageType.FinalResult => message.Payload["Data"],
      _ => message.Payload
    };

  }

  internal class EngineFactory
  {
    private readonly Action<Options> _options;

    public EngineFactory(IConfiguration config)
    {
      _options = options =>
      {
        options.LimitRecursion(config.GetValue<int>("LimitRecursion", 0))
               .MaxStatements(config.GetValue<int>("MaxStatements", 0))
               .TimeoutInterval(config.GetValue<TimeSpan>("TimeoutInterval", TimeSpan.Zero));
      };
    }

    public Engine Build()
    {
      var engine = new Engine(_options);
      engine.SetValue("_log", new Action<object>(Console.WriteLine));
      engine.SetValue("_println", new Action<string>(Console.WriteLine));
      engine.SetValue("_print", new Action<string>(Console.Write));
      engine.SetValue("_guid", new Func<string>(() => Guid.NewGuid().ToString()));
      engine.SetValue("_sha256", new Func<string, string>(s =>
      {
        var sha = SHA256.Create();
        var b = Encoding.UTF8.GetBytes(s);
        return Convert.ToHexString(sha.ComputeHash(b)).ToLowerInvariant();
      }));
      engine.SetValue("atob", (Func<string, string>)(str => Encoding.UTF8.GetString(Convert.FromBase64String(str))));
      engine.SetValue("btoa", (Func<string, string>)(str => Convert.ToBase64String(Encoding.UTF8.GetBytes(str))));
      return engine;
    }


  }
  [Serializable]
  public class MessageProcessingException : Exception
  {
    public Message JobMessage;
    public string ScriptSource;

    public MessageProcessingException(Message jobMessage, string scriptSource)
    : base($"{jobMessage.JobId} ({jobMessage.Type})")
    {
      JobMessage = jobMessage;
      ScriptSource = scriptSource;
    }
    
    
    public MessageProcessingException(Message jobMessage, string scriptSource, Exception innerException) 
        : base($"{jobMessage.JobId} ({jobMessage.Type}) {innerException.GetType().Name}: {innerException.Message}")
    {
      JobMessage = jobMessage;
      ScriptSource = scriptSource;
    }
  }
}