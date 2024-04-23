using System.IO;
using System.Text;
using Xunit.Abstractions;

namespace Selma.Testing
{
  public class TestOutputHelperConverter : TextWriter
  {
    ITestOutputHelper _output;
    public TestOutputHelperConverter(ITestOutputHelper output)
    {
      _output = output;
    }
    public override Encoding Encoding => Encoding.UTF8;
    public override void WriteLine(string message)
    {
      _output.WriteLine(message);
    }
    public override void WriteLine(string format, params object[] args)
    {
      _output.WriteLine(format, args);
    }
  }

  public class MessageSinkConverter : TextWriter
  {
    IMessageSink _output;
    public MessageSinkConverter(IMessageSink output)
    {
      _output = output;
    }
    public override Encoding Encoding => Encoding.UTF8;
    public override void WriteLine(string message)
    {
      _output.OnMessage(new Xunit.Sdk.DiagnosticMessage(message));
    }
    public override void WriteLine(string format, params object[] args)
    {
      _output.OnMessage(new Xunit.Sdk.DiagnosticMessage(string.Format(format, args)));
    }
  }
}