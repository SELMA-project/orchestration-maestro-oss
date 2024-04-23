using System;
using System.Collections.Generic;
using Selma.GeneralUtils.Cryptography;

namespace Selma.Benchmark
{

  class Program
  {
    public class MonitioSeasoning : ISymetricAESCryptographySeasoning
    {
      public int PepperSeed => 32; // DO NOT REUSE ANYWHERE.
      public int NumIterations => 1200; // DO NOT REUSE ANYWHERE.
    }

    static void BenchmakSymetricAESCryptography()
    {
      Dictionary<string, string> keys = new();
      keys.Add("1", "passwordTestKey");

      string text = "This is example text";

      var start = DateTime.Now;
      for (int i = 0; i < 1000; i++)
      {
        var encryped = SymetricAESCryptography<MonitioSeasoning>.Encrypt(keys, text);
      }
      var finish = DateTime.Now;
      Console.WriteLine($"Ding! {finish - start}");
    }
    static void Main(string[] args)
    {
      BenchmakSymetricAESCryptography();
    }
  }
}



