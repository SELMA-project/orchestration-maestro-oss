using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CS0618
namespace Selma.Orchestration.TaskMQUtils
{
  public static class HttpRequestExtensions
  {
    private static readonly HttpRequestOptionsKey<TimeSpan?> TimeoutProperty = new(TimeoutPropertyKey);
    private const string TimeoutPropertyKey = "RequestTimeout";

    public static void SetTimeout(this HttpRequestMessage request,
                                  TimeSpan? timeout)
    {
      if (request == null)
        throw new ArgumentNullException(nameof(request));

      request.Options.Set(TimeoutProperty, timeout);
    }

    public static TimeSpan? GetTimeout(this HttpRequestMessage request)
    {
      if (request == null)
        throw new ArgumentNullException(nameof(request));

      return request.Options.TryGetValue(TimeoutProperty, out var value)
          ? value
          : null;
    }
  }

  public class TimeoutHandler : DelegatingHandler
  {
    public TimeoutHandler() : this(new HttpClientHandler()) { }
    
    public TimeoutHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }

    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(60);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      using var cts = GetCancellationTokenSource(request, cancellationToken);
      try
      {
        return await base.SendAsync(request, cts?.Token ?? cancellationToken);
      }
      catch (OperationCanceledException)
          when (!cancellationToken.IsCancellationRequested)
      {
        throw new TimeoutException();
      }
    }

    private CancellationTokenSource GetCancellationTokenSource(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var timeout = request.GetTimeout() ?? DefaultTimeout;
      if (timeout == Timeout.InfiniteTimeSpan)
      {
        return null;
      }

      var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      cts.CancelAfter(timeout);
      return cts;
    }
  }
}

#pragma warning restore CS0618
