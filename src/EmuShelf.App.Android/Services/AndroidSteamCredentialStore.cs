using System.Security.Cryptography;
using System.Text;
using Android.Security.Keystore;
using EmuShelf.Core.Achievements;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;

namespace EmuShelf.App.Android.Services;

/// <summary>The key-encryption key never leaves Android Keystore; the blob is app-private.</summary>
public sealed class AndroidSteamCredentialStore(string directory) : ISteamCredentialStore
{
    private const string Alias = "emushelf-steam-achievements";
    private readonly string _path = Path.Combine(directory, "steam-achievements.key");
    public bool IsPersistent => true;

    private static IKey GetKey()
    {
        using var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load(null);
        if (store.GetKey(Alias, null) is { } existing) return existing;
        using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes!, "AndroidKeyStore")!;
        using var spec = new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm!)!
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone!)!
            .Build()!;
        generator.Init(spec);
        return generator.GenerateKey()!;
    }

    public string? Read()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var bytes = File.ReadAllBytes(_path);
            if (bytes.Length < 29 || bytes.Length > 1024) return null;
            using var key = GetKey();
            using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            using var spec = new GCMParameterSpec(128, bytes[..12]);
            cipher.Init(Javax.Crypto.CipherMode.DecryptMode, key, spec);
            return Encoding.UTF8.GetString(cipher.DoFinal(bytes[12..])!);
        }
        catch (Exception) { return null; } // invalidated Keystore: reconnect, never log a credential
    }

    public void Write(string keyText)
    {
        try
        {
            using var key = GetKey();
            using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(Javax.Crypto.CipherMode.EncryptMode, key);
            var encrypted = cipher.DoFinal(Encoding.UTF8.GetBytes(keyText))!;
            var iv = cipher.GetIV()!;
            if (iv.Length != 12) throw new CryptographicException("Unexpected encryption parameters.");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(_path + ".tmp", [..iv, ..encrypted]);
            File.Move(_path + ".tmp", _path, true);
        }
        catch (Exception) { throw new CryptographicException("Could not protect the Steam API key on this device."); }
    }

    public void Clear() => File.Delete(_path);
}
