using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Selma.Orchestration.TaskMQUtils;

namespace Selma.Orchestration.WorkerJSLib
{
  public class JsHttpClient
  {
    private const int MaxRetries = 3;
    private static readonly HttpClient Client = new HttpClient(new TimeoutHandler()) { Timeout = TimeSpan.FromSeconds(100) };

    private readonly ILogger _logger;
    private IDictionary<string, string> _headers;
    private Dictionary<string, string> _params;
    private Encoding _encoding;
    private string _mediaType;
    private TimeSpan _requestTimeout;


    public JsHttpClient(ILogger logger)
    {
      _logger = logger;
      _headers = new Dictionary<string, string>();
      _params = new Dictionary<string, string>();
      _encoding = Encoding.UTF8;
      _mediaType = MediaTypeNames.Text.Plain;
      _requestTimeout = TimeSpan.FromSeconds(100); // httpclient default
    }

    public void SetOptions(ExpandoObject obj)
    {
      var options = (IDictionary<string, object>)obj;

      if (options.TryGetValue("encoding", out var encoding))
      {
        ContentEncoding = encoding.ToString();
      }

      if (options.TryGetValue("timeout", out var timeout))
      {
        _requestTimeout = TimeSpan.FromSeconds(Convert.ToDouble(timeout));
      }

      if (options.TryGetValue("mediaType", out var mediaType))
      {
        _mediaType = mediaType.ToString();
      }
    }


    public string ContentEncoding
    {
      get => _encoding.WebName;
      set => _encoding = Encoding.GetEncoding(value);
    }


    public void SetHeader(string key, string value)
    {
      _headers[key] = value;
      if (key == "Content-Type")
      {
        _mediaType = value;
      }
    }

    public void SetHeaders(ExpandoObject headers)
    {
      if (headers is null)
      {
        ClearHeaders();
        return;
      }
      _headers = headers
                 .Where(x => x.Value != null)
                 .ToDictionary(kvp => kvp.Key,
                               kvp => kvp.Value!.ToString());
      if (_headers.ContainsKey("Content-Type"))
      {
        _mediaType = _headers["Content-Type"];
      }
    }

    public void ClearHeaders() => _headers.Clear();

    public void RemoveHeader(string key) => _headers.Remove(key);

    public void SetParams(ExpandoObject parameters)
    {
      if (parameters is null)
      {
        _params.Clear();
        return;
      }

      _params = parameters
                .Where(x => x.Value is not null)
                .ToDictionary(kvp => kvp.Key,
                              kvp => kvp.Value!.ToString());
    }

    public void ClearParams() => _params.Clear();


    public object Get(string url, object body = null)
      => Send(url, HttpMethod.Get, body);

    public object Post(string url, object body = null)
      => Send(url, HttpMethod.Post, body);

    public object Put(string url, object body = null)
      => Send(url, HttpMethod.Put, body);

    public object Patch(string url, object body = null)
      => Send(url, HttpMethod.Patch, body);

    public object Delete(string url, object body = null)
      => Send(url, HttpMethod.Delete, body);

    public object GetHttpStream(string url, bool readAsBase64 = false)
    {
      using var request = new HttpRequestMessage(HttpMethod.Get, url);
      var response = Client.Send(request);
      response.EnsureSuccessStatusCode();
      var stream = response.Content.ReadAsStream();
      if (readAsBase64)
      {
        var b64s = StreamToBase64(stream);
        return b64s;
      }
      return stream;
    }
    public object PostHttpStreamWithBody(string url, object body, bool readAsBase64 = false)
    {
      using var request = new HttpRequestMessage(HttpMethod.Post, url);
      if (body == null)
      {
        request.Content = new StringContent(string.Empty, _encoding, _mediaType);
      }
      else if (body is string s)
      {
        request.Content = new StringContent(s, _encoding, _mediaType);
      }
      else
      {
        request.Content = new StringContent(JsonConvert.SerializeObject(body), _encoding, _mediaType);
      }
      foreach (var (key, value) in _headers.ToList())
      {
        var contentHeaders = request.Content.Headers;
        var requestHeaders = request.Headers;
        contentHeaders.ToDictionary(kvp => kvp.Key, kvp => kvp.Value).Keys.Contains(key);
        try
        {
          if (contentHeaders.Contains(key)) contentHeaders.Remove(key);
          contentHeaders.Add(key, value);
        }
        catch
        {
          if (requestHeaders.Contains(key)) requestHeaders.Remove(key);
          requestHeaders.Add(key, value);
        }
      }
      var response = Client.Send(request);
      try
      {
        response.EnsureSuccessStatusCode();
      }
      catch (HttpRequestException ex)
      {
        return new TTS_Dto(null, (int)response.StatusCode); ;
      }
      var stream = response.Content.ReadAsStream();
      if (readAsBase64)
      {
        var b64s = StreamToBase64(stream);
        return new TTS_Dto(b64s, (int)response.StatusCode);
      }
      return new TTS_Dto(stream, (int)response.StatusCode);
    }
    public object PostFileForm(string url, string fileName, object fileStream)
    {
      System.Net.Http.HttpRequestMessage requestMessage = new();
      requestMessage.RequestUri = new(RequestUriUtil.GetUriWithQueryString(url, _params));
      requestMessage.Method = HttpMethod.Post;
      requestMessage.Headers.Add("accept", "application/json");

      using Stream fileStreamObject = (Stream)(fileStream);
      System.Net.Http.StreamContent streamContent = new(fileStreamObject);

      var content = new System.Net.Http.MultipartFormDataContent();
      content.Add(streamContent, name: "file", fileName: fileName);
      requestMessage.Content = content;
      var response = Client.Send(requestMessage);

      using var reader = new StreamReader(response.Content.ReadAsStream());
      var responseText = reader.ReadToEnd();

      return new
      {
        text = responseText,
        status = response.StatusCode,
        success = response.IsSuccessStatusCode
      };
    }
    private string StreamToBase64(Stream stream)
    {
      using var memoryStream = new MemoryStream();
      stream.CopyTo(memoryStream);
      var bytes = memoryStream.ToArray();
      var b64String = Convert.ToBase64String(bytes);
      return b64String;
    }

    private object Send(string url, HttpMethod method, object body = null)
    {
      var requestUri = RequestUriUtil.GetUriWithQueryString(url, _params);


      for (var retries = MaxRetries; retries > 0; retries--)
      {
        try
        {
          using var request = new HttpRequestMessage(method, requestUri);

          request.SetTimeout(_requestTimeout);

          if (body == null)
          {
            request.Content = new StringContent(string.Empty, _encoding, _mediaType);
          }
          else if (body is string s)
          {
            request.Content = new StringContent(s, _encoding, _mediaType);
          }
          else
          {
            request.Content = new StringContent(JsonConvert.SerializeObject(body), _encoding, _mediaType);
          }

          foreach (var (key, value) in _headers.ToList())
          {
            var contentHeaders = request.Content.Headers;
            var requestHeaders = request.Headers;
            contentHeaders.ToDictionary(kvp => kvp.Key, kvp => kvp.Value).Keys.Contains(key);
            try
            {
              if (contentHeaders.Contains(key)) contentHeaders.Remove(key);
              contentHeaders.Add(key, value);
            }
            catch
            {
              if (requestHeaders.Contains(key)) requestHeaders.Remove(key);
              requestHeaders.Add(key, value);
            }
          }

          using var response = Client.Send(request);

          using var reader = new StreamReader(response.Content.ReadAsStream());

          var responseText = reader.ReadToEnd();

          _logger.LogDebug(request.ToString());
          _logger.LogTrace(responseText);

          Reset();

          return new
          {
            text = responseText,
            status = response.StatusCode,
            success = response.IsSuccessStatusCode
          };

        }
        catch (HttpRequestException e) when (e.InnerException is IOException)
        {
          _logger.LogWarning($"Transient failure: {e.InnerException?.Message}. Retrying...");
        }
      }
      throw new HttpRequestException("Exceeded max retries for transient failure. Aborted.");
    }


    private void Reset()
    {
      ClearHeaders();
      ClearParams();
    }
  }
  public class TTS_Dto
  {
    public object audio;
    public int statusCode;

    public TTS_Dto(object wavStream, int code)
    {
      audio = wavStream;
      statusCode = code;
    }
  }
  public static class RequestUriUtil
  {
    /// Does not URL encode the keys/values since HttpClient will handle that
    /// automatically.
    public static string GetUriWithQueryString(string requestUri,
                                               Dictionary<string, string> queryStringParams)
    {
      var startingQuestionMarkAdded = false;
      var sb = new StringBuilder();
      sb.Append(requestUri);
      foreach (var (key, value) in queryStringParams.Where(x => x.Value != null))
      {
        sb.Append(startingQuestionMarkAdded ? '&' : '?');
        sb.Append(key);
        sb.Append('=');
        sb.Append(value);
        startingQuestionMarkAdded = true;
      }
      return sb.ToString();
    }
  }
}
