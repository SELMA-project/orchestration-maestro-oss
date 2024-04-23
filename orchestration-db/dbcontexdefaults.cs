using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Selma.GeneralUtils.Cryptography;

namespace Selma.Orchestration.OrchestrationDB;

public interface IDbContextDefaults
{
  string SQLUUIDDefault { get; }
  string SQLDateDefault { get; }
  string SQLJsonDataType { get; }
  bool IsEncrypted { get; }
  DBContextSymetricCredentials DBContextSymetricCredentials { get; }
}

public class DBContextSymetricCredentials
{
  public DBContextSymetricCredentials(IConfigurationSection configuration)
  {
    Salt = Encoding.UTF8.GetBytes(configuration.GetValue("Salt", ""));
    Keys = new Dictionary<string, string>();

    foreach (var apiKeySection in configuration.GetSection("keys").GetChildren())
    {
      //check if number
      var keyVersion = apiKeySection.GetValue<string>("Version");
      if (int.TryParse(keyVersion, out _))
        Keys.Add(keyVersion, apiKeySection.GetValue<string>("Key"));
    }
  }
  public DBContextSymetricCredentials(string salt, string latestKey, Dictionary<string, string> keys)
  {
    Salt = Encoding.UTF8.GetBytes(salt);
    LatestKey = latestKey;
    Keys = keys;
  }
  public DBContextSymetricCredentials(byte[] salt, string latestKey, Dictionary<string, string> keys)
  {
    Salt = salt;
    LatestKey = latestKey;
    Keys = keys;
  }

  private DBContextSymetricCredentials()
  {
  }
  public static DBContextSymetricCredentials NoCredentials => new DBContextSymetricCredentials();
  public string LatestKey { get; set; }
  public byte[] Salt { get; set; }
  public Dictionary<string, string> Keys { get; set; }
}

public class MonitioSeasoning : ISymetricAESCryptographySeasoning
{
  public int PepperSeed => 32; // DO NOT REUSE ANYWHERE.
  public int NumIterations => 1200; // DO NOT REUSE ANYWHERE.
}
public class GenericDbDefaults : IDbContextDefaults
{
  public string SQLUUIDDefault => throw new NotImplementedException();

  public string SQLDateDefault => throw new NotImplementedException();

  public string SQLJsonDataType => throw new NotImplementedException();

  public DBContextSymetricCredentials DBContextSymetricCredentials => throw new NotImplementedException();

  public bool IsEncrypted => throw new NotImplementedException();
}
public class SqliteDbDefaults : IDbContextDefaults
{
  public SqliteDbDefaults(DBContextSymetricCredentials dBContextSymetricCredentials, IConfigurationRoot configuration)
  {
    DBContextSymetricCredentials = dBContextSymetricCredentials;
    IsEncrypted = configuration!.GetValue("Database:isEncrypted", false);

  }

  // Guid are case sensitive 
  // C# - Guids are upper case
  public string SQLUUIDDefault => "(upper(hex(randomblob(4))) || '-' || upper(hex(randomblob(2))) || '-4' || substr(upper(hex(randomblob(2))),2) || '-' || substr('89AB',abs(random()) % 4 + 1, 1) || substr(upper(hex(randomblob(2))),2) || '-' || upper(hex(randomblob(6))))";

  public string SQLDateDefault => "date('now')";

  public string SQLJsonDataType => "string";

  public DBContextSymetricCredentials DBContextSymetricCredentials { get; set; }

  public bool IsEncrypted { get; set; }
}
public class PostgresDbDefaults : IDbContextDefaults
{

  public PostgresDbDefaults(DBContextSymetricCredentials dBContextSymetricCredentials, IConfigurationRoot configuration)
  {
    DBContextSymetricCredentials = dBContextSymetricCredentials;
    IsEncrypted = configuration!.GetValue("Database:isEncrypted", false);

  }
  public string SQLUUIDDefault => "uuid_generate_v4()";

  public string SQLDateDefault => "timezone('UTC', now())";

  public string SQLJsonDataType => "jsonb";

  public DBContextSymetricCredentials DBContextSymetricCredentials { get; set; }

  public bool IsEncrypted { get; set; }

}
public class SqlServerDbDefaults : IDbContextDefaults
{
  public SqlServerDbDefaults(DBContextSymetricCredentials dBContextSymetricCredentials, IConfigurationRoot configuration)
  {
    DBContextSymetricCredentials = dBContextSymetricCredentials;
    IsEncrypted = configuration!.GetValue("Database:isEncrypted", false);
  }
  public string SQLUUIDDefault => "newid()";

  public string SQLDateDefault => "getdate()";

  public string SQLJsonDataType => "string";

  public DBContextSymetricCredentials DBContextSymetricCredentials { get; set; }

  public bool IsEncrypted { get; set; }
}

public static class SQLBuilderExtensions
{
  private static readonly JsonSerializerSettings JsonSerializerSettings = new()
  {
    Converters = new List<JsonConverter> { new StringEnumConverter() }
  };

  public static PropertyBuilder<T> IsJsonStr<T>(this PropertyBuilder<T> p, IDbContextDefaults defaults)
  {
    return p.HasColumnType(defaults.SQLJsonDataType)
            .HasConversion(o => JsonConvert.SerializeObject(o, JsonSerializerSettings),
                           o => JsonConvert.DeserializeObject<T>(o),
                           new ValueComparer<T>((c1, c2) => JToken.FromObject(c1).ToString() == JToken.FromObject(c2).ToString(),
                                                c => JToken.FromObject(c).ToString().GetHashCode(),
                                                c => JToken.FromObject(c).ToObject<T>()));
  }

  public static PropertyBuilder<JToken> IsJToken(this PropertyBuilder<JToken> p, IDbContextDefaults defaults)
  {
    return p.HasConversion(o => o.ToString(),
                           o => JToken.Parse(o),
                           new ValueComparer<JToken>((c1, c2) => c1.ToString() == c2.ToString(),
                                                     c => c.ToString().GetHashCode(),
                                                     c => JToken.Parse(c.ToString())));
  }

  public static PropertyBuilder<T> IsStringEnum<T>(this PropertyBuilder<T> p)
  {
    return p.HasConversion(o => o.ToString(),
                           o => (T)Enum.Parse(typeof(T), o));
  }

  public static PropertyBuilder<JToken> IsEncrypted(this PropertyBuilder<JToken> p, IDbContextDefaults defaults)
    => p.HasConversion(
      o => SymetricAESCryptography<MonitioSeasoning>.Encrypt(defaults.DBContextSymetricCredentials.Keys,
                                                             o.ToString()),
      o => JToken.Parse(SymetricAESCryptography<MonitioSeasoning>.Decrypt(defaults.DBContextSymetricCredentials.Keys,
                                                                          o)),
      new ValueComparer<JToken>((c1, c2) => c1.ToString() == c2.ToString(),
                                c => c.ToString().GetHashCode(),
                                c => JToken.Parse(c.ToString())));
}
