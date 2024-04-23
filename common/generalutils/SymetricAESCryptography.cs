using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Selma.GeneralUtils.Cryptography
{
  // SymetricAESCryptography Instructions
  // Rfc2898_SHA512(password, random salt + const pepper, const NumIterations) -> key; random IV -> AES -> secret. Payload which is distributed: IV+Salt+Secret. Needed to decrypt: Payload+Pepper+Password+NumIterations.
  // 1) Salt is random and unique per key. Generated during encryption. Distributed with secret. Protects against rainbow tables.
  // 2) The AES key is generated from the string password and salt, using NumIterations. Protects against finding the string password if the key is ever compromised, by slowing down the decryption process.
  // 3) The IV (initialization vector) is generated randomly. Distributed with secret. Protects against the fact that AES is deterministic, since it's random.
  // 4) The PepperSeed is a number used to generate deterministically the pepper. The Pepper here is an application-level computed-constant which is mixed with the salt. Protects the distributed secret since it obscures the salt +1 level.
  // 7) If the program's code is compromised, there's nothing left to protect.
  public interface ISymetricAESCryptographySeasoning
  {
    int PepperSeed { get; }// Should be unique to the application
    int NumIterations { get; }// number of Rfc2898 derivation iterations
  }

  public class SymetricAESCryptography<ISeasoning> where ISeasoning : ISymetricAESCryptographySeasoning, new()
  {
    private static int _keySize = 32; // 32 bytes - unique per secret, generated from the string key
    private static int _saltLength = 16;// 16 bytes - unique per secret, stored with secret. Concatenated with papper yields 32 bytes.
    private static int _ivlen = 16;// 16 bytes for the random initialization vector - cannot be changed, depends on AES
    private static ISeasoning Season = new ISeasoning();

    // Uses a Random salt - and stores it in the payload
    public static string Encrypt(Dictionary<string, string> keys, string secret)
    {
      var passwordVersion = keys.Keys.Max();
      var password = keys[passwordVersion];
      return Encrypt(password, passwordVersion, secret, GenerateRandomSalt());
    }
    private static string Encrypt(string password, string passwordVersion, string message, byte[] salt)
    {
      if (string.IsNullOrEmpty(message))
      {
        return ".";
      }

      // Generate the algorithm.
      var pepper = GetApplicationPepper();
      //var saltAndPepper = salt.Zip(pepper).Select((sp, i) => (byte)(sp.First ^ sp.Second)).ToArray();
      var saltAndPepper = salt.Concat(pepper).ToArray();
      Rfc2898DeriveBytes k = new(password, saltAndPepper, Season.NumIterations, HashAlgorithmName.SHA512);

      // Set variables to feed the algorithm
      Aes encAlg = Aes.Create();
      encAlg.Key = k.GetBytes(_keySize);
      encAlg.Padding = PaddingMode.ISO10126;

      // Encrypt the data.
      MemoryStream encryptionStream = new();
      CryptoStream encrypt = new(encryptionStream, encAlg.CreateEncryptor(), CryptoStreamMode.Write);
      byte[] utfPWD = new UTF8Encoding(false).GetBytes(message);

      encrypt.Write(utfPWD, 0, utfPWD.Length);
      encrypt.FlushFinalBlock();
      encrypt.Close();


      byte[] vector = encAlg.IV;
      byte[] generatedSecret = encryptionStream.ToArray();

      int vectorLenght = _ivlen;
      int generatedSecretLenght = generatedSecret.Length;

      // Save IV Vector to message so it can be accessed in decryption of the generated secret
      // Also save salt.
      byte[] secret = salt.Concat(vector.Concat(generatedSecret)).ToArray();

      if (secret.Length != generatedSecretLenght + vectorLenght + _saltLength)
        throw new("Encryption problems found.");

      // Save secret to base64 string prepended by a dot char. The dot char signals that it was encrypted using this method.
      // This is necessary for migrating unencrypted fields between DB versions.
      // Also burn in the passwordVersion. This is needed for future decryption
      var outputSecret = "." + $"#{passwordVersion}#" + Convert.ToBase64String(secret);
      k.Reset();
      return outputSecret;
    }

    public static string Decrypt(Dictionary<string, string> keys, string secret)
    {
      if (string.IsNullOrEmpty(secret) || secret == ".")
      {
        return "";
      }
      if (secret[0] != '.')
      {
        // The dot char signals that it was encrypted using this method. If this not present, we assume it was not encrypted.
        // This is necessary for migrating unencrypted fields between DB versions.
        return secret;
      }
      secret = secret.Substring(1); // remove dot

      // Extract key version if it exists
      //Match #<number># if first # exists
      string pattern = @"^#(\d+)#";
      Match match = Regex.Match(secret, pattern);
      string passwordVersion;
      string password;

      if (match.Success)
      {
        // Extract the number and the secret
        passwordVersion = match.Groups[1].Value;
        // If wanted password version is not present in the keys we cannot decrypt
        // Most likely will happen using different DB versions or reading very old rows
        if (!keys.ContainsKey(passwordVersion))
          return secret;
        password = keys[passwordVersion];
        secret = secret[(match.Index + match.Length)..];
      }
      else
      {
        //If the dot char was present it has to have a version number too
        //If this happens its most likely an error
        return secret;
      }

      // Get/Encode bytes sequence from generated Base64 string
      byte[] eSecret = Convert.FromBase64String(secret);

      // Get IV Vector
      byte[] salt = eSecret.Take(_saltLength).ToArray();
      byte[] IV = eSecret.Skip(_saltLength).Take(_ivlen).ToArray();
      byte[] bMessage = eSecret.Skip(_ivlen + _saltLength).ToArray();

      // Generate the algorithm
      var pepper = GetApplicationPepper();
      //var saltAndPepper = salt.Zip(pepper).Select((sp, i) => (byte)(sp.First ^ sp.Second)).ToArray();
      var saltAndPepper = salt.Concat(pepper).ToArray();
      Rfc2898DeriveBytes k = new(password, saltAndPepper, Season.NumIterations, HashAlgorithmName.SHA512);

      // Set variables to feed the algorithm.
      Aes decAlg = Aes.Create();
      decAlg.Key = k.GetBytes(_keySize);
      decAlg.IV = IV;
      decAlg.Padding = PaddingMode.ISO10126;

      // Decrypt message.
      MemoryStream decryptionStreamBacking = new();
      CryptoStream decrypt = new(decryptionStreamBacking, decAlg.CreateDecryptor(), CryptoStreamMode.Write);
      decrypt.Write(bMessage, 0, bMessage.Length);
      decrypt.Flush();
      decrypt.Close();

      // Save message to string
      string message = new UTF8Encoding(false).GetString(decryptionStreamBacking.ToArray());
      k.Reset();

      return message;
    }

    private static byte[] GenerateRandomSalt()
    {
      var salt = new byte[_saltLength];
      using (var random = RandomNumberGenerator.Create())
      {
        random.GetNonZeroBytes(salt);
      }
      return salt;
    }
    private static byte[] GetApplicationPepper()
    {
      var pepper = new byte[_saltLength];
      for (int i = 0; i < pepper.Length; i += 1)
      {
        pepper[i] = (byte)((i * Season.PepperSeed) % 251);
      }
      return pepper;
    }
  }
}