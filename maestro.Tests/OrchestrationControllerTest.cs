using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Selma.Orchestration.OrchestrationDB;
using Xunit;
using Xunit.Abstractions;
using Selma.Testing;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System;
using System.Collections.Generic;
using System.Text;
using Selma.Orchestration.TaskMQUtils;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading;
using Selma.Orchestration.Models;
using System.Diagnostics;
using System.Linq;

namespace Selma.Orchestration.Maestro.Tests
{
  [CollectionDefinition("Maestro")]
  public class MaestroCollection : ICollectionFixture<MaestroFixture>
  {
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
  }

  [Collection("Maestro")]
  public class OrchestrationControllerTest
  {
    private const string _url = "http://localhost:10000/Orchestration/Graph";
    private readonly ITestOutputHelper _output;
    private IConfigurationRoot _config;

    private HttpClient _client;

    public OrchestrationControllerTest(ITestOutputHelper outputHelper)
    {
      _output = outputHelper;
      _config = ConfigurationHelper.GetIConfigurationRoot();
      _client = new HttpClient();
    }

    [Theory]
    [JsonFileTokens(@"Data\1provider2nodes.json|Data\1provider2nodes_output.json")]
    [JsonFileTokens(@"Data\1provider7nodes.json|Data\1provider7nodes_output.json")]
    public async Task Graph_EnqueueJobGraph_FromJson_Async(JToken input, JToken output)
    {
      // Arrange
      var inputJobGraph = input.ToObject<JobGraph>();
      var expectedJobGraph = output.ToObject<JobGraph>();

      // Act
      _output.WriteLine($"Enqueueing job graph {inputJobGraph.WorkflowId} ({inputJobGraph.JobNodes.Count} nodes)...");
      var enqueueResponse = await EnqueueGraphAsync(inputJobGraph);
      enqueueResponse.EnsureSuccessStatusCode();

      Task.Delay(1000).Wait();

      var resultResponse = await _client.GetAsync($"{_url}?guid={inputJobGraph.WorkflowId}");
      resultResponse.EnsureSuccessStatusCode();

      var responseString = await resultResponse.Content.ReadAsStringAsync();
      var resultJobGraph = JsonConvert.DeserializeObject<JobGraph>(responseString);

      // Assert
      Assert.True(resultJobGraph.EqualsIgnoreTimestamp(expectedJobGraph));
    }

    [Theory]
    [InlineData(300)]
    public async Task Graph_EnqueueJobGraph_ProcGen_Async(int JobGraphCount)
    {

      var _queueManager = new MQManager(_config, new NullLogger<MQManager>());
      _queueManager.BindQueueToTopicExchange(_config["Integration:Exchanges:Workers.Out"],
                                              "TestResults",
                                              "#");

      int jobNodeCount = GenerateGraphs(1).First().JobNodes.Count * JobGraphCount;
      _output.WriteLine($"\t[{0.0:F4}] Enqueueing {JobGraphCount} graphs ({jobNodeCount} nodes)...");

      await Task.Delay(5000);

      var stopwatch = Stopwatch.StartNew();
      foreach (var graph in GenerateGraphs(JobGraphCount))
      {
        var response = await EnqueueGraphAsync(graph);
        response.EnsureSuccessStatusCode();
      }
      _output.WriteLine($"\t[{stopwatch.Elapsed.TotalSeconds:F4}] Done.");


      var resultConsumer = new MQConsumer(_config, new NullLogger<MQConsumer>(), "TestResults", prefetchCount: 1);
      var tokenSource = new CancellationTokenSource();
      int currentJob = 0;
      Func<Message, CancellationToken, Task> f = async (message, stoppingToken) =>
      {
        currentJob++;
        if (currentJob >= jobNodeCount)
        {
          tokenSource.Cancel();
        }
        // var time = stopwatch.Elapsed.TotalSeconds;
        // _output.WriteLine($"\t[{time:F4}] Job {currentJob:D2}/{jobNodeCount}. avg: {time * 1000.0 / jobNodeCount} ms");
        await Task.CompletedTask;
      };
      try
      {
        await resultConsumer.Run(f, tokenSource.Token);
      }
      catch (System.OperationCanceledException) { };
      var time = (double)stopwatch.ElapsedMilliseconds;
      _output.WriteLine($"Finished. Avg. Job time: {time / jobNodeCount:F4} ms");

      await Task.Delay(5000);

      Assert.True(currentJob >= jobNodeCount);
    }

    [Theory]
    [JsonFileTokens(@"Data\1provider1node2scripts.json|Data\1provider1node2scripts_output.json")]
    public async Task Graph_EnqueueJobGraphWithScripts_FromJson_Async(JToken input, JToken output)
    {
      // Arrange
      var inputJobGraph = input.ToObject<JobGraph>();
      // var expectedJobGraph = output.ToObject<JobGraph>();

      // Act
      _output.WriteLine($"Enqueueing job graph {inputJobGraph.WorkflowId} ({inputJobGraph.JobNodes.Count} nodes)...");
      var enqueueResponse = await EnqueueGraphAsync(inputJobGraph);
      enqueueResponse.EnsureSuccessStatusCode();

      Task.Delay(1000).Wait();

      var resultResponse = await _client.GetAsync($"{_url}?guid={inputJobGraph.WorkflowId}");
      resultResponse.EnsureSuccessStatusCode();

      var responseString = await resultResponse.Content.ReadAsStringAsync();

      await Task.Delay(5000);

      // Assert
      Assert.True(JToken.DeepEquals(output, JToken.Parse(responseString)));
    }

    private async Task<HttpResponseMessage> EnqueueGraphAsync(JobGraph graph)
    {
      var graphJson = JsonConvert.SerializeObject(graph);
      var enqueueRequest = new StringContent(graphJson, Encoding.UTF8, "application/json");

      return await _client.PostAsync(_url, enqueueRequest);
    }

    private IEnumerable<JobGraph> GenerateGraphs(int count)
    {
      for (var i = 0; i < count; ++i)
      {
        var x = i.ToString("D4");

        yield return new JobGraph(
            Guid.Parse($"00000000-0000-0000-{x}-000000000000"),
            new List<JobNode>()
            {
              new JobNode(
                Guid.Parse($"00000000-0000-0000-{x}-000000000001"),
                new HashSet<Guid>(),
                new JObject{{"Text", $"Test 1. {i}"}},
                "StringInverter",
                "Selma"
              ),

              new JobNode(
                Guid.Parse($"00000000-0000-0000-{x}-000000000002"),
                new HashSet<Guid>(),
                new JObject{{"Text", $"Test 2. {i}"}},
                "StringInverter",
                "Selma"
              ),

              new JobNode(
                Guid.Parse($"00000000-0000-0000-{x}-000000000003"),
                new HashSet<Guid>(){
                  Guid.Parse($"00000000-0000-0000-{x}-000000000001"),
                  Guid.Parse($"00000000-0000-0000-{x}-000000000002"),
                },
                new JObject{{"Text", $"Test 3. {i}"}},
                "StringInverter",
                "Selma"
              ),

              new JobNode(
                Guid.Parse($"00000000-0000-0000-{x}-000000000004"),
                new HashSet<Guid>(),
                new JObject{{"Text", $"Test 4. {i}"}},
                "StringInverter",
                "Selma"
              ),

              new JobNode(
                Guid.Parse($"00000000-0000-0000-{x}-000000000005"),
                new HashSet<Guid>(){
                  Guid.Parse($"00000000-0000-0000-{x}-000000000003"),
                  Guid.Parse($"00000000-0000-0000-{x}-000000000004"),
                },
                new JObject{{"Text", $"Test 5. {i}"}},
                "StringInverter",
                "Selma"
              ),
            });
      }
    }
  }

  internal static class JobGraphTestExtensions
  {
    public static bool EqualsIgnoreTimestamp(this JobGraph result, JobGraph expected)
    {
      if (Object.ReferenceEquals(result, expected)) return true;
      if (Object.ReferenceEquals(result, null) || Object.ReferenceEquals(expected, null))
        return false;


      if (result.WorkflowId != expected.WorkflowId || result.Status != expected.Status) return false;

      foreach (var jobNode in result.JobNodes)
      {
        var other = expected.JobNodes.Find(j => j.Id == jobNode.Id);
        if (!other.JobResult["Text"].Equals(jobNode.JobResult["Text"])) return false;
      }
      return true;
    }
  }
}