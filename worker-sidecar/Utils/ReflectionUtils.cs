using System;
using System.Collections.Generic;
using System.Linq;

namespace Selma.Orchestration.WorkerSC.Utils
{
  public static class Reflection
  {
    public static T CreateObjectFromTypeName<T>(string typeName)
    {
      var type = GetTypesImplementingInterface<T>()?.Where(t => t.Name == typeName)?.FirstOrDefault();
      if (type != null)
      {
        return (T)Activator.CreateInstance(type)!;
      }
      return default(T)!;
    }

    public static List<Type> GetTypesImplementingInterface<T>()
    {
      return AppDomain.CurrentDomain.GetAssemblies()
                      .SelectMany(x => x.GetTypes())
                      .Where(x => typeof(T)
                                  .IsAssignableFrom(x) && !x.IsAbstract && !x.IsInterface)
                      .ToList();
    }
  }
}