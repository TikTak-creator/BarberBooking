using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Bladez.Core;

/// <summary>Cold Windows MySQL profile backup. Caller must hold the stopped profile exclusively.</summary>
public static class ProfileBackup
{
    private static readonly byte[] Magic = "BLADEZ-BACKUP-1\n"u8.ToArray();
    private const int Limit = 512 * 1024 * 1024;
    private const int Iterations = 600_000;
    private static void Password(string password)
    { if (password.Length is < 12 or > 128) throw new ArgumentException("Пароль копии: от 12 до 128 символов. Сохраните его отдельно от файла."); }

    public static void Create(string directory, string destination, string password)
    {
        Password(password);
        var root = Path.GetFullPath(directory);
        using var exclusive = new FileStream(Path.Combine(root, "host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var vault = new DataVault(root);
        if (!File.Exists(Path.Combine(root, "initialized"))) throw new InvalidOperationException("Настройка сервера ещё не завершена.");
        var files = new[] { "server.json", "secrets.vault", "initialized" }.Select(f => Path.Combine(root, f))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "mysql-data"), "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".err", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".pid", StringComparison.OrdinalIgnoreCase))).ToArray();
        if (files.Sum(f => new FileInfo(f).Length) > Limit) throw new InvalidOperationException("Этот мастер поддерживает копии до 512 МиБ исходных данных. Для больших баз используйте описанный в документации процесс резервирования.");
        using var memory = new MemoryStream();
        var master = vault.ExportBackupKey();
        try
        {
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                using (var target = zip.CreateEntry("portable.key").Open()) target.Write(master);
                foreach (var file in files) zip.CreateEntryFromFile(file, Path.GetRelativePath(root, file).Replace('\\', '/'), CompressionLevel.Fastest);
            }
            var clear = memory.ToArray();
            var salt = RandomNumberGenerator.GetBytes(16); var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16];
            var derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA512, 32);
            var cipher = new byte[clear.Length];
            try
            {
                using var aes = new AesGcm(derived, 16); aes.Encrypt(nonce, clear, cipher, tag, Magic);
                var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
                try { PrivateFiles.Write(temporary, Magic.Concat(salt).Concat(nonce).Concat(tag).Concat(cipher).ToArray(), false); File.Move(temporary, destination, false); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            finally { CryptographicOperations.ZeroMemory(clear); CryptographicOperations.ZeroMemory(derived); }
        }
        finally { CryptographicOperations.ZeroMemory(master); if (memory.TryGetBuffer(out var buffer)) CryptographicOperations.ZeroMemory(buffer.AsSpan()); }
    }

    public static void Restore(string source, string newDirectory, string password)
    {
        Password(password);
        var destination = Path.GetFullPath(newDirectory);
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new InvalidOperationException("Восстановление разрешено только в новую пустую папку. Рабочая база не будет перезаписана.");
        var size = new FileInfo(source).Length;
        if (size < Magic.Length + 44 || size > Limit + 16_000_000) throw new InvalidDataException("Неверный размер резервной копии.");
        var bytes = File.ReadAllBytes(source);
        if (!bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic)) throw new InvalidDataException("Неизвестный формат копии.");
        var offset = Magic.Length;
        var derived = Rfc2898DeriveBytes.Pbkdf2(password, bytes.AsSpan(offset, 16), Iterations, HashAlgorithmName.SHA512, 32);
        var clear = new byte[bytes.Length - offset - 44];
        var staging = destination + ".restoring-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var aes = new AesGcm(derived, 16)) aes.Decrypt(bytes.AsSpan(offset + 16, 12), bytes.AsSpan(offset + 44), bytes.AsSpan(offset + 28, 16), clear, Magic);
            using var zip = new ZipArchive(new MemoryStream(clear), ZipArchiveMode.Read);
            if (zip.Entries.Sum(e => e.Length) > Limit || zip.Entries.Count > 100_000) throw new InvalidDataException("Копия превышает допустимый размер.");
            PrivateFiles.Directory(staging);
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName;
                if (name == "portable.key") continue;
                if (!(name is "server.json" or "secrets.vault" or "initialized" || name.StartsWith("mysql-data/", StringComparison.Ordinal))) throw new InvalidDataException("Неожиданный файл в копии.");
                var target = Path.GetFullPath(Path.Combine(staging, name));
                if (!target.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Неверный путь в копии.");
                PrivateFiles.Directory(Path.GetDirectoryName(target)!);
                using var input = entry.Open(); using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None); input.CopyTo(output);
            }
            var keyEntry = zip.GetEntry("portable.key") ?? throw new InvalidDataException("В копии отсутствует ключ.");
            if (keyEntry.Length != 32) throw new InvalidDataException("Неверный ключ копии.");
            var master = new byte[32]; using (var stream = keyEntry.Open()) stream.ReadExactly(master);
            try { DataVault.ImportBackupKey(staging, master); } finally { CryptographicOperations.ZeroMemory(master); }
            var vault = new DataVault(staging); var secrets = vault.ReadSecrets();
            var builder = new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(secrets.ConnectionString) { SslCa = Path.Combine(destination, "mysql-data", "ca.pem") };
            secrets.ConnectionString = builder.ConnectionString; secrets.ControlKey = DataVault.RandomSecret(); vault.SaveSecrets(secrets);
            if (!File.Exists(Path.Combine(staging, "initialized")) || !Directory.Exists(Path.Combine(staging, "mysql-data", "mysql"))) throw new InvalidDataException("Копия неполная.");
            // DPAPI authentication cookies are deliberately not restored. All old web sessions expire.
            if (Directory.Exists(destination)) Directory.Delete(destination, false);
            Directory.Move(staging, destination);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear); CryptographicOperations.ZeroMemory(derived);
            // Staging is a freshly created sibling with a random suffix; no existing profile is deleted.
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }
}
