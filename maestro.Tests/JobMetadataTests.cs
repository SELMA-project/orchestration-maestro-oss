using Selma.Orchestration.OrchestrationDB;
using Selma.Orchestration.Maestro.Tests.Data;
using Xunit;

namespace Selma.Orchestration.Maestro.Tests
{
  public class JobMetadataTests
  {
    [Theory]
    [ClassData(typeof(JobInfoTestData))]
    public void JobInfoQueueName_ValidInput_ReturnsExpected(JobInfo jobInfo, string format, string expected)
    {
      // act
      var result = jobInfo.QueueName(format);

      // assert
      Assert.Equal(expected, result);
    }

    [Theory]
    [ClassData(typeof(JobFilterTestData))]
    public void JobFilterValidate_ValidInput_ReturnsExpected(JobFilter jobFilter, JobInfo jobInfo, bool expected)
    {
      // act
      var result = jobFilter.Validate(jobInfo);

      // assert
      Assert.Equal(expected, result);
    }

    // todo: test invalid cases
  }
}
