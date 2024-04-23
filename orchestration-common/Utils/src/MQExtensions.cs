using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Selma.Orchestration.TaskMQUtils
{
  public static class MQExtensions
  {

    /// <summary>
    /// Declare a stream using QueueDeclare.
    /// <para> Streams are always durable per their assumed use cases, they
    /// cannot be non-durable or exclusive like regular queues. </para>
    /// </summary>
    /// <param name="streamName"></param>
    /// <param name="arguments"></param>
    /// <returns></returns>
    public static QueueDeclareOk StreamDeclare(this IModel model, string streamName, IDictionary<string, object> arguments = null)
    {
      arguments ??= new Dictionary<string, object>();
      arguments["x-queue-type"] = "stream";

      return model.QueueDeclare(streamName, true, false, false, arguments);
    }

    public static QueueDeclareOk GetQueueInfo(this IConnection connection, string queueName)
    {
      // using a new channel because QueueDeclarePassive closes the channel
      // if the queue does not exist.
      var channel = connection.CreateModel();
      try
      {
        return channel.QueueDeclarePassive(queueName);
      }
      catch (OperationInterruptedException)
      {
        return null;
      }
      finally
      {
        channel.Close();
      }
    }

    public static bool QueueExists(this IConnection connection, string queueName)
      => connection.GetQueueInfo(queueName) is not null;

    public static void DisposeWithTimeout(this IModel model, TimeSpan timeout)
    {
      var task = Task.Run(model.Dispose);

      if (!task.Wait(timeout))
      {
        //timeout
        throw new TimeoutException("Timeout on IModel.Dispose()");
      }
    }

  }
}
