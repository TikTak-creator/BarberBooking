using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using MySql.Data.MySqlClient;

namespace Bladez.Core;

/// <summary>Manages only the private MySQL instance created in this host profile.</summary>
public sealed class LocalMySql(string dataDirectory, IProgress<string>? progress = null, Action<Process>? track = null)
{
    public const string Version = "8.4.10";
    public const string ArchiveUrl = "https://cdn.mysql.com/Downloads/MySQL-8.4/mysql-8.4.10-winx64.zip";
    public const string ArchiveSha256 = "3b950db31c33fb59252568c012bd9ee5fac50811e778ca7c8f1a0dc91686cd6f";
    public const string OracleFingerprint = "BCA43417C3B485DD128EC6D4B7B3B788A8D3785C";
    private readonly string root = Path.GetFullPath(dataDirectory);
    private Process? process;
    public string BinaryDirectory => Path.Combine(root, "mysql", "mysql-" + Version + "-winx64", "bin");
    public string DatabaseDirectory => Path.Combine(root, "mysql-data");
    public bool IsRunning => process is { HasExited: false };

    public async Task DownloadAsync(string? verifiedArchive = null, CancellationToken cancellation = default)
    {
        if (File.Exists(Path.Combine(BinaryDirectory, "mysqld.exe"))) return;
        PrivateFiles.Directory(root);
        var archive = verifiedArchive ?? Path.Combine(root, "mysql-" + Version + ".zip");
        if (!File.Exists(archive))
        {
            progress?.Report("Загружаем официальный MySQL " + Version + " (около 281 МБ)…");
            var temp = archive + ".partial";
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            using var response = await client.GetAsync(ArchiveUrl, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            await using (var input = await response.Content.ReadAsStreamAsync(cancellation))
            {
                var buffer = new byte[128 * 1024]; long count = 0, last = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellation)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellation); count += read;
                    if (count - last > 8_000_000) { progress?.Report($"Загружаем MySQL: {count / 1_000_000} / {response.Content.Headers.ContentLength / 1_000_000} МБ"); last = count; }
                }
            }
            File.Move(temp, archive, true);
        }
        progress?.Report("Проверяем SHA-256 MySQL…");
        await using (var stream = File.OpenRead(archive))
            if (Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellation)) != ArchiveSha256)
                throw new InvalidOperationException("Архив MySQL не прошёл проверку SHA-256. Он не будет запущен. Удалите только загруженный ZIP и повторите настройку.");
        progress?.Report("Распаковываем MySQL…");
        var destination = Path.Combine(root, "mysql");
        Directory.CreateDirectory(destination);
        ZipFile.ExtractToDirectory(archive, destination, overwriteFiles: true);
        if (!File.Exists(Path.Combine(BinaryDirectory, "mysqld.exe"))) throw new InvalidOperationException("В проверенном архиве отсутствует mysqld.exe.");
    }

    public async Task InitializeAsync(HostSettings settings, DataVault vault, CancellationToken cancellation = default)
    {
        Validate(settings);
        if (Directory.Exists(Path.Combine(DatabaseDirectory, "mysql")))
        {
            if (!File.Exists(Path.Combine(root, "secrets.vault"))) throw new InvalidOperationException("Данные MySQL уже существуют, но настройки доступа отсутствуют. Используйте восстановление из резервной копии.");
            return;
        }
        var listener = new TcpListener(IPAddress.Loopback, settings.DatabasePort);
        try { listener.Start(); } catch (SocketException) { throw new InvalidOperationException("Выбранный порт MySQL занят. Укажите другой порт; работающий MySQL не изменён."); }
        finally { listener.Stop(); }
        var secrets = new HostSecrets { DatabaseAdminPassword = DataVault.RandomSecret(), DatabaseUserPassword = DataVault.RandomSecret(), ControlKey = DataVault.RandomSecret() };
        secrets.ConnectionString = Connection(settings, secrets.DatabaseUserPassword, settings.DatabaseUser).ConnectionString;
        vault.SaveSecrets(secrets); settings.Save(root);
        PrivateFiles.Directory(DatabaseDirectory);
        WriteConfig(settings);
        progress?.Report("Инициализируем отдельный MySQL. Другие базы не меняются…");
        var start = Info(Path.Combine(BinaryDirectory, "mysqld.exe"));
        start.ArgumentList.Add("--defaults-file=" + Path.Combine(root, "mysql.ini")); start.ArgumentList.Add("--initialize-insecure");
        await RunAsync(start, "MySQL не удалось инициализировать. Проверьте журнал в папке данных и Microsoft Visual C++ 2015–2022 x64 Runtime.", cancellation);
        var sql = $"ALTER USER 'root'@'localhost' IDENTIFIED BY '{secrets.DatabaseAdminPassword}';\n" +
            $"CREATE USER IF NOT EXISTS 'root'@'127.0.0.1' IDENTIFIED BY '{secrets.DatabaseAdminPassword}';\nGRANT ALL PRIVILEGES ON *.* TO 'root'@'127.0.0.1' WITH GRANT OPTION;\n" +
            $"CREATE DATABASE `{settings.DatabaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;\n" +
            string.Join("\n", new[] { "localhost", "127.0.0.1" }.Select(h => $"CREATE USER '{settings.DatabaseUser}'@'{h}' IDENTIFIED BY '{secrets.DatabaseUserPassword}';\nGRANT SELECT, INSERT, UPDATE, DELETE ON `{settings.DatabaseName}`.* TO '{settings.DatabaseUser}'@'{h}';"));
        PrivateFiles.Write(Path.Combine(root, "mysql-first-start.sql"), Encoding.UTF8.GetBytes(sql));
    }

    public async Task StartAsync(HostSettings settings, DataVault vault, CancellationToken cancellation = default)
    {
        if (IsRunning) return;
        Validate(settings); WriteConfig(settings);
        var init = Path.Combine(root, "mysql-first-start.sql");
        var info = Info(Path.Combine(BinaryDirectory, "mysqld.exe"));
        info.ArgumentList.Add("--defaults-file=" + Path.Combine(root, "mysql.ini"));
        if (File.Exists(init)) info.ArgumentList.Add("--init-file=" + init);
        process = Process.Start(info) ?? throw new InvalidOperationException("Не удалось запустить отдельный MySQL.");
        track?.Invoke(process);
        process.OutputDataReceived += (_, _) => { }; process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        var secrets = vault.ReadSecrets();
        for (var attempt = 0; attempt < 120; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (process.HasExited) throw new InvalidOperationException("MySQL остановился при запуске. Проверьте порт, журнал и Microsoft Visual C++ x64 Runtime.");
            try
            {
                await using var connection = new MySqlConnection(Connection(settings, secrets.DatabaseAdminPassword, "root", false).ConnectionString);
                await connection.OpenAsync(cancellation);
                if (File.Exists(init)) File.Delete(init);
                progress?.Report("MySQL запущен. TLS проверен по сертификату отдельной базы."); return;
            }
            catch (MySqlException) { await Task.Delay(500, cancellation); }
        }
        throw new TimeoutException("MySQL не стал доступен за 60 секунд. Данные сохранены; проверьте журнал.");
    }

    public async Task<string> CreateMigrationConnectionAsync(HostSettings settings, DataVault vault, CancellationToken cancellation = default)
    {
        var password = DataVault.RandomSecret();
        await using var connection = new MySqlConnection(Connection(settings, vault.ReadSecrets().DatabaseAdminPassword, "root", false).ConnectionString);
        await connection.OpenAsync(cancellation);
        foreach (var h in new[] { "localhost", "127.0.0.1" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE USER IF NOT EXISTS 'bladez_migrate'@'{h}' IDENTIFIED BY @password; ALTER USER 'bladez_migrate'@'{h}' IDENTIFIED BY @password; GRANT ALL PRIVILEGES ON `{settings.DatabaseName}`.* TO 'bladez_migrate'@'{h}';";
            command.Parameters.AddWithValue("@password", password); await command.ExecuteNonQueryAsync(cancellation);
        }
        return Connection(settings, password, "bladez_migrate").ConnectionString;
    }
    public async Task RemoveMigrationUserAsync(HostSettings settings, DataVault vault)
    {
        await using var connection = new MySqlConnection(Connection(settings, vault.ReadSecrets().DatabaseAdminPassword, "root", false).ConnectionString);
        await connection.OpenAsync(); await using var command = connection.CreateCommand();
        command.CommandText = "DROP USER IF EXISTS 'bladez_migrate'@'localhost', 'bladez_migrate'@'127.0.0.1'"; await command.ExecuteNonQueryAsync();
    }
    public async Task StopAsync(HostSettings settings, DataVault vault)
    {
        if (!IsRunning) return;
        try
        {
            await using var connection = new MySqlConnection(Connection(settings, vault.ReadSecrets().DatabaseAdminPassword, "root", false).ConnectionString);
            await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "SHUTDOWN"; await command.ExecuteNonQueryAsync();
        }
        catch (MySqlException) { /* SHUTDOWN can close the connection before acknowledging the command. */ }
        try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)); await process!.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); }
        process?.Dispose(); process = null;
    }

    private MySqlConnectionStringBuilder Connection(HostSettings settings, string password, string user, bool useDatabase = true) => new()
    {
        Server = "127.0.0.1", Port = (uint)settings.DatabasePort, UserID = user, Password = password, Database = useDatabase ? settings.DatabaseName : "",
        SslMode = MySqlSslMode.VerifyCA, SslCa = Path.Combine(DatabaseDirectory, "ca.pem"), ConnectionTimeout = 3, DefaultCommandTimeout = 30, Pooling = false
    };
    private void WriteConfig(HostSettings settings)
    {
        var basedir = Path.GetDirectoryName(BinaryDirectory)!.Replace('\\', '/'); var datadir = DatabaseDirectory.Replace('\\', '/');
        var text = $"[mysqld]\nbasedir=\"{basedir}\"\ndatadir=\"{datadir}\"\nport={settings.DatabasePort}\nbind-address=127.0.0.1\nmysqlx=OFF\nrequire_secure_transport=ON\nlocal_infile=OFF\nsecure_file_priv=NULL\nlog-error=\"{datadir}/mysql.err\"\nmax_connections=60\ncharacter-set-server=utf8mb4\ncollation-server=utf8mb4_0900_ai_ci\n";
        PrivateFiles.Write(Path.Combine(root, "mysql.ini"), Encoding.UTF8.GetBytes(text));
    }
    private static void Validate(HostSettings settings)
    {
        if (settings.DatabasePort is < 1024 or > 65535 || !System.Text.RegularExpressions.Regex.IsMatch(settings.DatabaseName, "^[a-z][a-z0-9_]{2,50}$") || settings.DatabaseUser != "bladez_app")
            throw new ArgumentException("Проверьте имя базы и порт. Разрешён отдельный пользователь bladez_app.");
    }
    private static ProcessStartInfo Info(string executable) => new(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
    private async Task RunAsync(ProcessStartInfo info, string error, CancellationToken cancellation)
    {
        using var process = Process.Start(info) ?? throw new InvalidOperationException(error);
        track?.Invoke(process);
        var output = process.StandardOutput.ReadToEndAsync(cancellation); var stderr = process.StandardError.ReadToEndAsync(cancellation);
        await process.WaitForExitAsync(cancellation); await output; await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
    }
}
