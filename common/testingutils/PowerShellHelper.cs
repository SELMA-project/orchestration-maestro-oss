using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;
using System.Threading.Tasks;

namespace Selma.GeneralUtils
{
  public static class PowerShellHelperExtensions
  {
    public static IAsyncEnumerable<PSObject> RunPowerShellPipe(this string script)
    {
      return PowerShellHelper.RunPipe(script);
    }
    public static async Task RunPowerShell(this string script)
    {
      await foreach (var l in PowerShellHelper.RunPipe(script)) ;
    }
  }

  public static class PowerShellHelper
  {
    public static IAsyncEnumerable<PSObject> RunPipe(string script)
    {
      var rs = RunspaceFactory.CreateRunspace();
      rs.Open();
      var pipeline = rs.CreatePipeline();
      pipeline.Commands.AddScript(script);
      return new PsAsyncEnumerable(pipeline);
    }
  }

  internal class PsAsyncEnumerable : IAsyncEnumerable<PSObject>
  {
    private readonly Pipeline pipe;
    public PsAsyncEnumerable(Pipeline pipe) => this.pipe = pipe;

    public IAsyncEnumerator<PSObject> GetAsyncEnumerator(CancellationToken cancellationToken = new())
        => new PsAsyncEnumerator(this.pipe);
  }

  internal class PsAsyncEnumerator : IAsyncEnumerator<PSObject>
  {
    private readonly Pipeline pipe;
    private TaskCompletionSource dataReady = new();

    public PsAsyncEnumerator(Pipeline pipe)
    {
      this.pipe = pipe;
      this.pipe.Output.DataReady += NotificationHandler;
      this.pipe.Error.DataReady += NotificationHandler;
      this.pipe.InvokeAsync();
    }

    private void NotificationHandler(object sender, EventArgs e)
    {
      this.dataReady.SetResult();
    }

    public ValueTask DisposeAsync()
    {
      this.pipe.Dispose();
      return ValueTask.CompletedTask;
    }

    public async ValueTask<bool> MoveNextAsync()
    {
      while (!this.pipe.Output.EndOfPipeline)
      {
        var item = this.pipe.Output.NonBlockingRead(1).FirstOrDefault();
        if (item != null)
        {
          this.Current = item;
          return true;
        }

        await this.dataReady.Task;
        this.dataReady = new TaskCompletionSource();
      }

      return false;
    }

    public PSObject Current { get; private set; }
  }
}