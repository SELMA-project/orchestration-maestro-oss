using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit.Sdk;

namespace Selma.Testing
{
  public class TextFileDataAttribute : DataAttribute
  {
    private readonly string _filePath;
    private readonly string _propertyName;
    private static IConfiguration _config = ConfigurationHelper.GetIConfigurationRoot();
    public TextFileDataAttribute(string propertyName)
    {
      _propertyName = propertyName;
      _filePath = _config[_propertyName];
    }
    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
      if (testMethod == null) { throw new ArgumentNullException(nameof(testMethod)); }

      // Get the absolute path to the file
      var path = Path.IsPathRooted(_filePath)
          ? _filePath
          : Path.GetRelativePath(Directory.GetCurrentDirectory(), _filePath);

      if (!File.Exists(path))
      {
        throw new Exception($"Could not find file at path: {path}");
      }

      // Load the file
      var fileData = File.ReadLines(_filePath);
      if (!fileData.Any())
      {
        throw new Exception($"File in {path} is empty.");
      }

      return fileData.ToList().Select(x => new object[] { x });
    }
  }


  // https://andrewlock.net/creating-a-custom-xunit-theory-test-dataattribute-to-load-data-from-json-files/
  public class JsonFileDataAttribute : DataAttribute
  {
    private readonly string _filePath;
    private readonly string _propertyName;
    public JsonFileDataAttribute(string filePath)
        : this(filePath, null) { }
    public JsonFileDataAttribute(string filePath, string propertyName)
    {
      _filePath = filePath;
      _propertyName = propertyName;
    }
    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
      if (testMethod == null) { throw new ArgumentNullException(nameof(testMethod)); }

      // Get the absolute path to the JSON file
      var path = Path.IsPathRooted(_filePath)
          ? _filePath
          : Path.GetRelativePath(Directory.GetCurrentDirectory(), _filePath);

      if (!File.Exists(path))
      {
        throw new Exception($"Could not find file at path: {path}");
      }

      // Load the file
      var fileData = File.ReadAllText(_filePath);

      if (string.IsNullOrEmpty(_propertyName))
      {
        //whole file is the data
        return JsonConvert.DeserializeObject<List<object[]>>(fileData);
      }

      // Only use the specified property as the data
      var allData = JObject.Parse(fileData);
      var data = allData[_propertyName];
      return data.ToObject<List<object[]>>();
    }
  }

  public class JsonFileTokensAttribute : DataAttribute
  {
    private readonly string[][] _filePaths;
    public JsonFileTokensAttribute(params string[] filePaths)
    {
      _filePaths = filePaths.Select(p => p.Split('|')).ToArray();
    }
    public override IEnumerable<JToken[]> GetData(MethodInfo testMethod)
    {
      if (testMethod == null) { throw new ArgumentNullException(nameof(testMethod)); }

      var allTestCasesResult = new List<JToken[]>();
      foreach (var filePaths in _filePaths)
      {
        var result = new List<JToken>();
        foreach (var filePath in filePaths)
        {
          var path = Path.Join(Directory.GetCurrentDirectory(), filePath);
          if (!File.Exists(path))
          {
            throw new Exception($"Could not find file at path: {path}");
          }
          result.Add(JToken.Parse(File.ReadAllText(path)));
        }
        allTestCasesResult.Add(result.ToArray());
      }
      return allTestCasesResult;
    }
  }
}