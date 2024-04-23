using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Selma.Orchestration.Maestro;
using Selma.Orchestration.OrchestrationDB;
using System.Threading;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace api.Controllers
{
  [ApiController]
  [Route("[controller]/[action]")]
  public class OrchestrationController : ControllerBase
  {
    private readonly JobEnqueuer _jobEnqueuer;
    private readonly IConfiguration _configuration;
    private readonly OrchestrationDBContext _context;
    private readonly JobMetrics _jobMetrics;

    public OrchestrationController(JobEnqueuer jobEnqueuer, IConfiguration configuration, OrchestrationDBContext context, JobMetrics jobMetrics)
    {
      _jobEnqueuer = jobEnqueuer;
      _configuration = configuration;
      _context = context;
      _jobMetrics = jobMetrics;
    }

    [HttpPost]
    public async Task<ActionResult<Guid>> Graph([FromBody] JobGraph jobGraph,
                                                CancellationToken stoppingToken)
    {
      if (await _context.Jobs.AnyAsync(j => j.WorkflowId == jobGraph.WorkflowId))
      {
        return BadRequest("Graph Id already exists");
      }

      var jobNodeIds = jobGraph.JobNodes.Select(j => j.Id);
      if (await _context.Jobs.AnyAsync(j => jobNodeIds.Contains(j.Id)))
      {
        return BadRequest("At least one Job Id already exists");
      }

      var newJobs = jobGraph.JobNodes
                            .Select(j => new Job(j, jobGraph.WorkflowId))
                            .ToList();
      
      _context.Jobs.AddRange(newJobs);

      await _context.SaveChangesAsync(stoppingToken);

        
      // Enqueue Jobs
      var executableJobs = newJobs.Where(j => j.Dependencies.Count == 0)
                                  .ToList();
      await _jobEnqueuer.Enqueue(executableJobs, stoppingToken);

      var errorJobs = executableJobs.Where(j => j.Status == OrchestrationStatus.Error)
                                    .ToList();
     
      _jobMetrics.Queued.Add(executableJobs.Count - errorJobs.Count);

      if (!errorJobs.Any()) return Ok();
      
      await _context.SaveChangesAsync(CancellationToken.None);
      return Ok(new JObject
      {
        {
          "errors", JArray.FromObject(errorJobs.Select(j => new
            {
              jobId = j.Id, error = j.Result
            })
          )
        }
      });

    }
    
    [HttpGet]
    public async Task<ActionResult<Guid>> Graph([FromQuery] Guid guid,
                                               CancellationToken stoppingToken)
    {
      var singleJob = await _context.Jobs.Where(j => j.Id == guid).FirstOrDefaultAsync();
      if (singleJob != null)
      {
        JobNode response = (JobNode)singleJob;
        string output_string = JsonConvert.SerializeObject(response);
        return Ok(output_string);
      }
      else
      {

        var jobList = _context.Jobs.Where(j => j.WorkflowId == guid);

        if (jobList.Count() == 0)
        {
          return BadRequest("Guid not recognized");
        }
        else
        {
          var response = new JobGraph();
          response.WorkflowId = guid;
          response.JobNodes = new System.Collections.Generic.List<JobNode>();
          response.Status = OrchestrationStatus.Done;

          foreach (var job in jobList)
          {
            JobNode node = (JobNode)job;
            response.JobNodes.Add(node);
            if ((int)job.Status < (int)response.Status)
            {
              response.Status = job.Status;
            }
          }
          DefaultContractResolver contractResolver = new DefaultContractResolver
          {
            NamingStrategy = new CamelCaseNamingStrategy()
          };

          string output_string = JsonConvert.SerializeObject(response, new JsonSerializerSettings
          {
            ContractResolver = contractResolver,
            Formatting = Formatting.Indented
          });
          return Ok(output_string);
        }
      }
    }

    [HttpGet]
    public Task<ActionResult> JobMetrics()
    {
      return Task.FromResult<ActionResult>(Ok(new
      {
        Done = new
        {
          Total = _jobMetrics.DoneTotal,
          TotalDuration = _jobMetrics.DoneTotalMs,
          AverageDurationMs = _jobMetrics.DoneAvgMs
        },
        Queued = new
        {
          Total = _jobMetrics.QueuedTotal
        }
      }));
    }

  }
}