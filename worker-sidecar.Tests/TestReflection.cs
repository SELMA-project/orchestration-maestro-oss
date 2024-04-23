using Selma.Orchestration.SideCarModule;
using Selma.Orchestration.WorkerSC.Utils;

namespace worker_sidecar.Tests;

public class TestReflection
{
  [Theory]
  [InlineData("NLLB")]
  [InlineData("JavaScriptSC")]
  /// <summary>
  ///   Tests is CreateObjectFromTypeName builds 
  /// the objects given the type name and known types
  /// </summary>
  /// <param name="typeName">Name of the type to create an instance</param>
  public void CreateObjectFromTypeNameTest(string typeName)
  {
    var instance = Reflection.CreateObjectFromTypeName<ISideCarModule>(typeName);
    Assert.Equal(instance.GetType().Name, typeName);
  }

  [Fact]
  /// <summary>
  /// Tests if CreateObjectFromTypeName fails if a not known type is passed
  /// </summary>
  public void CreateObjectFromTypeName_TypeDoesntExist_Test()
  {
    var TypeName = "NonExistantType";
    var instance = Reflection.CreateObjectFromTypeName<ISideCarModule>(TypeName);
    Assert.Null(instance);
  }
}