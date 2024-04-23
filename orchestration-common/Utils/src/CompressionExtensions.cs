using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Selma.Orchestration.TaskMQUtils
{

  public static class CompressionExtensions
  {
    public static byte[] Compress(this byte[] data)
    {
      byte[] compressArray;
      using (MemoryStream memoryStream = new MemoryStream())
      {
        using (DeflateStream deflateStream = new DeflateStream(memoryStream, CompressionMode.Compress))
        {
          deflateStream.Write(data, 0, data.Length);
          deflateStream.Dispose(); // https://github.com/mono/mono/issues/15825
        }
        compressArray = memoryStream.ToArray();
      }
      return compressArray;
    }

    public static byte[] Decompress(this byte[] data)
    {
      byte[] decompressedArray;
      using (MemoryStream decompressedStream = new MemoryStream())
      {
        using (MemoryStream compressStream = new MemoryStream(data))
        {
          using (DeflateStream deflateStream = new DeflateStream(compressStream, CompressionMode.Decompress))
          {
            deflateStream.CopyTo(decompressedStream);
            deflateStream.Dispose(); // https://github.com/mono/mono/issues/15825
          }
        }
        decompressedArray = decompressedStream.ToArray();
      }
      return decompressedArray;
    }
    public static byte[] Pack(this byte[] data)
    {
      return BitConverter.GetBytes(Convert.ToInt32(data.Length)).Concat(data).ToArray();
    }
  }
}