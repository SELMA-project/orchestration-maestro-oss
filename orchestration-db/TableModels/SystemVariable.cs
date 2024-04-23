namespace Selma.Orchestration.OrchestrationDB
{
  public class SystemVariable
  {
    public SystemVariable(string key, string value)
    {
      Key = key;
      Value = value;
    }

    public string Key { get; set; }
    public string Value { get; set; }
  }
}
