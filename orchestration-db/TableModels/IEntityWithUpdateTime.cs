using System;

namespace Selma.Orchestration.OrchestrationDB
{
  public interface IEntityWithUpdateTime
  {
    DateTime Updated { get; set; }
  }
}