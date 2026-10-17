using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Bladez.Core;

/// <summary>Authenticated field encryption. The key is separate from MySQL and never sent to a browser.</summary>
public sealed class DataVault
{
    private readonly byte[] key;
    public string DirectoryPath { get; }

    public DataVault(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory);
        PrivateFiles.Directory(DirectoryPath);
        var keyPath = Path.Combine(DirectoryPath, "master.key");
        if (!File.Exists(keyPath))
        {
            if (File.Exists(Path.Combine(DirectoryPath, "secrets.vault")))
                throw new CryptographicException("The existing profile has lost its data key. Restore the complete profile backup; a replacement key cannot decrypt it.");
            var candidate = RandomNumberGenerator.GetBytes(32);
            var stored = OperatingSystem.IsWindows()
                ? Encoding.UTF8.GetBytes("DPAPI1:" + Convert.ToBase64String(ProtectedData.Protect(candidate, null, DataProtectionScope.CurrentUser)))
                : Encoding.UTF8.GetBytes("UNIX1:" + Convert.ToBase64String(candidate));
            var temporary = keyPath + "." + Guid.NewGuid().ToString("N");
            try
            {
                PrivateFiles.Write(temporary, stored, false);
                try { File.Move(temporary, keyPath, false); }
                catch (IOException) when (File.Exists(keyPath)) { /* Another process atomically initialized this profile. */ }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); CryptographicOperations.ZeroMemory(candidate); }
        }
        var content = File.ReadAllText(keyPath);
        if (content.StartsWith("DPAPI1:", StringComparison.Ordinal) && OperatingSystem.IsWindows())
            key = ProtectedData.Unprotect(Convert.FromBase64String(content[7..]), null, DataProtectionScope.CurrentUser);
        else if (content.StartsWith("UNIX1:", StringComparison.Ordinal) && !OperatingSystem.IsWindows())
        {
            if ((File.GetUnixFileMode(keyPath) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                throw new InvalidOperationException("master.key must be private (chmod 600).");
            key = Convert.FromBase64String(content[6..]);
        }
        else throw new InvalidOperationException("The data key belongs to another operating system. Use encrypted backup/restore to transfer it.");
        if (key.Length != 32) throw new CryptographicException("Invalid data key.");
    }

    public string Encrypt(string value, string purpose)
    {
        var clear = Encoding.UTF8.GetBytes(value);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[clear.Length]; var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, clear, cipher, tag, Encoding.UTF8.GetBytes("Bladez/v1/" + purpose));
        CryptographicOperations.ZeroMemory(clear);
        return "enc1:" + Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    public string Decrypt(string value, string purpose)
    {
        if (!value.StartsWith("enc1:", StringComparison.Ordinal))
            throw new CryptographicException("Unencrypted or unsupported data. Import legacy data explicitly; do not bypass encryption.");
        var bytes = Convert.FromBase64String(value[5..]);
        if (bytes.Length < 28) throw new CryptographicException("Invalid encrypted field.");
        var clear = new byte[bytes.Length - 28];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), clear, Encoding.UTF8.GetBytes("Bladez/v1/" + purpose));
        try { return Encoding.UTF8.GetString(clear); } finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public string Fingerprint(string value, string purpose) => Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(purpose + "\0" + value)));

    public void SaveSecrets(HostSecrets secrets)
    {
        var encrypted = Encrypt(JsonSerializer.Serialize(secrets), "HostSecrets");
        var target = Path.Combine(DirectoryPath, "secrets.vault");
        var temp = target + "." + Guid.NewGuid().ToString("N");
        PrivateFiles.Write(temp, Encoding.UTF8.GetBytes(encrypted));
        File.Move(temp, target, true);
    }

    public HostSecrets ReadSecrets() => JsonSerializer.Deserialize<HostSecrets>(Decrypt(File.ReadAllText(Path.Combine(DirectoryPath, "secrets.vault")), "HostSecrets"))
        ?? throw new InvalidOperationException("The host configuration is unavailable.");

    public static string RandomSecret(int bytes = 32) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Used only inside a passphrase-encrypted, offline host backup. Never expose through MVC/API.
    internal byte[] ExportBackupKey() => key.ToArray();
    internal static void ImportBackupKey(string directory, byte[] restored)
    {
        if (restored.Length != 32) throw new CryptographicException("Invalid backup key.");
        PrivateFiles.Directory(directory);
        var stored = OperatingSystem.IsWindows()
            ? "DPAPI1:" + Convert.ToBase64String(ProtectedData.Protect(restored, null, DataProtectionScope.CurrentUser))
            : "UNIX1:" + Convert.ToBase64String(restored);
        PrivateFiles.Write(Path.Combine(directory, "master.key"), Encoding.UTF8.GetBytes(stored), false);
    }
}

public sealed class HostSecrets
{
    public string ConnectionString { get; set; } = "";
    public string DatabaseAdminPassword { get; set; } = "";
    public string DatabaseUserPassword { get; set; } = "";
    public string ControlKey { get; set; } = "";
    public string SmtpPassword { get; set; } = "";
}
