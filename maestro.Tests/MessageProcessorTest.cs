using System;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Selma.Testing;
using Xunit;
using Selma.Orchestration.Models;
using Selma.Orchestration.OrchestrationDB;


namespace Selma.Orchestration.Maestro.Tests
{
  public class MessageProcessorTest
  {
    private readonly MessageProcessor _processor;

    private static readonly ScriptData EmptyScriptData = new ScriptData();
    private static readonly ScriptData InvalidScriptData = new ScriptData(input: @"I am invalid JS.", @"I am also invalid JS.");

    public MessageProcessorTest()
    {
      _processor = new MessageProcessor(new NullLogger<MessageProcessor>(), ConfigurationHelper.GetIConfigurationRoot());
    }

    [Theory]
    [InlineData(MessageType.Request)]
    [InlineData(MessageType.FinalResult)]
    public void ProcessedMessage_WithoutScripts_ReturnsOriginalMessage(MessageType messageType)
    {
      // Arrange
      var payload = JToken.FromObject(new { Text = "test" });
      if (messageType == MessageType.FinalResult) payload = JToken.FromObject(new FinalResultPayload(payload, new BillingLog()));
      var message = new Message(Guid.Empty, messageType, payload, null);
      var job = new Job { Scripts = JToken.FromObject(EmptyScriptData) };

      // Act
      var processedMessage = _processor.Process(message, job);

      // Assert
      Assert.Equal(message, processedMessage);
    }

    [Theory]
    [InlineData(MessageType.Request)]
    [InlineData(MessageType.FinalResult)]
    public void ProcessedMessage_WithInvalidScript_ThrowsMessageProcessingException(MessageType messageType)
    {
      // Arrange
      var data = JToken.FromObject(new { Text = "test" });
      var payload = messageType switch
      {
        MessageType.FinalResult => JToken.FromObject(new FinalResultPayload(data, new BillingLog())),
        _ => data
      };
      var message = new Message(Guid.Empty, messageType, payload, null);

      var job = new Job { Scripts = JToken.FromObject(InvalidScriptData) };

      // Act
      void Act() => _processor.Process(message, job);

      // Assert
      Assert.Throws<MessageProcessingException>(Act);
    }

    [Theory]
    [InlineData("Test 1234")]
    public void ProcessedRequestMessage_WithValidInputScript_ReturnsProcessedRequestMessage(string testString)
    {
      // Arrange
      var payload = JToken.FromObject(new { sourceItemType = testString });

      var message = new Message(Guid.Empty, MessageType.Request, payload, null);
      var scriptData = new ScriptData(input: "return { 'Text': _input.sourceItemType };");
      var job = new Job { Scripts = JToken.FromObject(scriptData) };

      // Act
      var processedMessage = _processor.Process(message, job);
      var processedMessageTestString = processedMessage.GetPayloadData()["Text"].Value<string>();

      // Assert
      Assert.Equal(testString, processedMessageTestString);
    }

    [Theory]
    [InlineData("Test 2345")]
    public void ProcessedFinalResultMessage_WithValidOutputScript_ReturnsProcessedFinalResultMessage(string testString)
    {
      // Arrange
      var data = JToken.FromObject(new { Text = testString });

      var payload = JToken.FromObject(new FinalResultPayload(data, new BillingLog()));

      var message = new Message(Guid.Empty, MessageType.FinalResult, payload, null);
      var scriptData = new ScriptData(output: "return { 'sourceItemType': _input.Text };");
      var job = new Job { Scripts = JToken.FromObject(scriptData) };

      // Act
      var processedMessage = _processor.Process(message, job);
      var processedMessageTestString = processedMessage.GetPayloadData()["sourceItemType"].Value<string>();

      // Assert
      Assert.Equal(testString, processedMessageTestString);
    }

    [Theory]
    [InlineData("Test 12345")]
    public void JobMetadata_IsAccessibleFromScript_InRequestMessage(string testString)
    {
      // Arrange
      var payload = JToken.FromObject(new { sourceItemType = testString });

      var message = new Message(Guid.Empty, MessageType.Request, payload, null);
      var scriptData = new ScriptData(input: "return { 'Text': `${_input.sourceItemType} ${_meta.DocumentId}` };");
      var documentId = Guid.NewGuid().ToString();
      var job = new Job
      {
        Scripts = JToken.FromObject(scriptData),
        Metadata = JToken.FromObject(new { DocumentId = documentId })
      };

      // Act
      var processedMessage = _processor.Process(message, job);
      var processedMessageTestString = processedMessage.GetPayloadData()["Text"].Value<string>();

      // Assert
      Assert.Equal($"{testString} {documentId}", processedMessageTestString);
    }

    [Theory]
    [InlineData("Test 23456")]
    public void JobMetadata_IsAccessibleFromScript_InFinalResultMessage(string testString)
    {
      // Arrange
      var data = JToken.FromObject(new { Text = testString });

      var payload = JToken.FromObject(new FinalResultPayload(data, new BillingLog()));

      var message = new Message(Guid.Empty, MessageType.FinalResult, payload, null);
      var scriptData = new ScriptData(output: "return { 'sourceItemType': `${_input.Text} ${_meta.DocumentId}` };");
      var documentId = Guid.NewGuid().ToString();
      var job = new Job
      {
        Scripts = JToken.FromObject(scriptData),
        Metadata = JToken.FromObject(new
        {
          DocumentId = documentId
        })
      };

      // Act
      var processedMessage = _processor.Process(message, job);
      var processedMessageTestString = processedMessage.GetPayloadData()["sourceItemType"].Value<string>();

      // Assert
      Assert.Equal($"{testString} {documentId}", processedMessageTestString);
    }

  }
}