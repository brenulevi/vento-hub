using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vento.Sharing.Desktop.Security;

public sealed record SavedCredentials(string Identifier, string Password);

public static class SavedCredentialsStore
{
    private const int KeyLength = 32;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("vento-sharing-credentials-v1");
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VentoSharing");
    private static readonly string KeyPath = Path.Combine(DirectoryPath, "credentials.key");
    private static readonly string DataPath = Path.Combine(DirectoryPath, "credentials.bin");

    public static SavedCredentials? Load()
    {
        if (!File.Exists(DataPath))
            return null;

        var key = File.ReadAllBytes(KeyPath);
        var data = File.ReadAllBytes(DataPath);
        if (key.Length != KeyLength || data.Length < NonceLength + TagLength)
            throw new CryptographicException("As credenciais salvas estão inválidas.");

        var plaintext = new byte[data.Length - NonceLength - TagLength];
        using var aes = new AesGcm(key, TagLength);
        aes.Decrypt(data.AsSpan(0, NonceLength), data.AsSpan(NonceLength + TagLength),
            data.AsSpan(NonceLength, TagLength), plaintext, AssociatedData);
        var legacy = JsonSerializer.Deserialize<SavedCredentialsLegacy>(plaintext);
        if (legacy is null)
            return null;
        return new SavedCredentials(legacy.Identifier, legacy.Password);
    }

    public static void Save(SavedCredentials credentials)
    {
        Directory.CreateDirectory(DirectoryPath);
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(DirectoryPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        if (!File.Exists(KeyPath))
            WritePrivateFile(KeyPath, RandomNumberGenerator.GetBytes(KeyLength), overwrite: false);

        var key = File.ReadAllBytes(KeyPath);
        if (key.Length != KeyLength)
            throw new CryptographicException("A chave das credenciais salvas está inválida.");

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(credentials);
        var data = new byte[NonceLength + TagLength + plaintext.Length];
        RandomNumberGenerator.Fill(data.AsSpan(0, NonceLength));
        using (var aes = new AesGcm(key, TagLength))
            aes.Encrypt(data.AsSpan(0, NonceLength), plaintext,
                data.AsSpan(NonceLength + TagLength), data.AsSpan(NonceLength, TagLength), AssociatedData);

        var temporaryPath = Path.Combine(DirectoryPath, $"credentials-{Guid.NewGuid():N}.tmp");
        try
        {
            WritePrivateFile(temporaryPath, data, overwrite: false);
            File.Move(temporaryPath, DataPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public static void Clear()
    {
        if (File.Exists(DataPath))
            File.Delete(DataPath);
        if (File.Exists(KeyPath))
            File.Delete(KeyPath);
    }

    private sealed class SavedCredentialsLegacy
    {
        public string Identifier { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    private static void WritePrivateFile(string path, byte[] data, bool overwrite)
    {
        var options = new FileStreamOptions
        {
            Mode = overwrite ? FileMode.Create : FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };
        if (OperatingSystem.IsLinux())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        using var file = new FileStream(path, options);
        file.Write(data);
    }
}
