using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Bladez.Core;

/// <summary>Owns the child processes of one profile. Never discovers or kills unrelated servers.</summary>
public sealed class ServerManager(string directory, string serverDirectory, IProgress<string>? progress = null)
{
    private readonly string root = Path.GetFullPath(directory);
    private readonly OwnedProcessGroup processes = new();
    private LocalMySql? database;
    private LocalMySql mysql => database ??= new LocalMySql(directory, progress, processes.Track);
    private Process? web;
    private FileStream? profileLock;
    public bool WebRunning => web is { HasExited: false };
    public bool DatabaseRunning => mysql.IsRunning;
    public bool Configured => File.Exists(Path.Combine(root, "initialized"));
    public HostSettings Settings => HostSettings.Load(root);

    private void Lock()
    {
        if (profileLock is not null) return;
        PrivateFiles.Directory(root);
        try { profileLock = new FileStream(Path.Combine(root, "host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new InvalidOperationException("Этой папкой данных уже управляет другое окно Bladez Host. Откройте его через значок в трее."); }
    }

    public async Task InitializeAsync(object owner, HostSettings settings, string? cachedArchive = null)
    {
        Lock();
        if (Configured) throw new InvalidOperationException("Сервер уже настроен. Учётная запись владельца сохранена.");
        var vault = new DataVault(root);
        if (File.Exists(Path.Combine(root, "secrets.vault"))) settings = HostSettings.Load(root);
        settings.Save(root);
        try
        {
            await mysql.DownloadAsync(cachedArchive);
            await mysql.InitializeAsync(settings, vault);
            await mysql.StartAsync(settings, vault);
            await MigrateAsync(vault, owner);
            PrivateFiles.Write(Path.Combine(root, "initialized"), Encoding.UTF8.GetBytes("1\n"));
            await StartWebAsync();
        }
        catch { await StopAsync(); throw; }
    }

    public async Task StartAsync(string? cachedArchive = null)
    {
        Lock();
        if (!Configured) throw new InvalidOperationException("Сначала завершите мастер настройки.");
        if (WebRunning && mysql.IsRunning) return;
        try
        {
            var vault = new DataVault(root);
            await mysql.DownloadAsync(cachedArchive);
            await mysql.StartAsync(Settings, vault);
            await MigrateAsync(vault, null);
            await StartWebAsync();
        }
        catch { await StopAsync(); throw; }
    }

    private ProcessStartInfo WebInfo()
    {
        var executable = Path.Combine(Path.GetFullPath(serverDirectory), "BarberBooking.Web.exe");
        var info = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetFullPath(serverDirectory), UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        info.Environment["BLADEZ_DATA"] = root;
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        foreach (var name in new[] { "ConnectionStrings__DefaultConnection", "BLADEZ_CONNECTION_FILE", "BLADEZ_BOOTSTRAP_FILE", "BLADEZ_LISTEN_URL", "BLADEZ_PUBLIC_URL", "BLADEZ_TRUSTED_PROXY" }) info.Environment.Remove(name);
        return info;
    }

    private async Task MigrateAsync(DataVault vault, object? owner)
    {
        var connection = await mysql.CreateMigrationConnectionAsync(Settings, vault);
        var path = Path.Combine(root, "migration-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            PrivateFiles.Write(path, Encoding.UTF8.GetBytes(connection));
            var info = WebInfo(); info.Environment["BLADEZ_CONNECTION_FILE"] = path;
            info.ArgumentList.Add(owner is null ? "--migrate" : "--initialize");
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Не найден компонент сервера. Переустановите программу, сохранив папку данных.");
            processes.Track(process);
            var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
            if (owner is not null) await process.StandardInput.WriteAsync(JsonSerializer.Serialize(owner));
            process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { if (!process.HasExited) process.Kill(true); throw; }
            var message = await output; await errors; // Bootstrap output can contain diagnostic SQL; never display it in the GUI.
            if (process.ExitCode != 0 || !message.Contains("BLADEZ_INITIALIZED"))
                throw new InvalidOperationException("Настройка схемы/владельца не завершена. Проверьте пароль: 12–128 символов, большие и маленькие латинские буквы, цифры, минимум 5 разных символов. Существующие данные не удалены.");
        }
        finally { if (File.Exists(path)) File.Delete(path); await mysql.RemoveMigrationUserAsync(Settings, vault); }
    }

    private async Task StartWebAsync()
    {
        if (WebRunning) return;
        var settings = Settings;
        // Refuse to take another process's health response as proof that our child started.
        var uri = new Uri(settings.ListenUrl);
        using (var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, uri.Port))
        { try { listener.Start(); } catch (System.Net.Sockets.SocketException) { throw new InvalidOperationException("Порт сайта занят. Закройте второе окно сервера или выберите свободный порт в server.json при остановленном сервере."); } }
        web = Process.Start(WebInfo()) ?? throw new InvalidOperationException("Не удалось запустить сайт.");
        processes.Track(web);
        web.StandardInput.Close();
        web.OutputDataReceived += (_, _) => { }; web.ErrorDataReceived += (_, _) => { };
        web.BeginOutputReadLine(); web.BeginErrorReadLine();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        for (var attempt = 0; attempt < 80; attempt++)
        {
            if (web.HasExited) throw new InvalidOperationException("Сайт остановился при запуске. Проверьте наличие файлов сервера и соответствие ключа базе.");
            try { using var response = await client.GetAsync(settings.ListenUrl + "/health/ready"); if (response.IsSuccessStatusCode) { progress?.Report("Сайт готов. База доступна, ключ данных проверен."); return; } }
            catch (HttpRequestException) { } catch (TaskCanceledException) { }
            await Task.Delay(500);
        }
        throw new TimeoutException("Сайт не подтвердил готовность. Данные сохранены.");
    }

    public async Task RecoverOwnerAsync(string userName, string password)
    {
        Lock();
        var vault = new DataVault(root);
        await mysql.StartAsync(Settings, vault);
        var info = WebInfo(); info.ArgumentList.Add("--recover-owner");
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Не удалось запустить восстановление.");
        processes.Track(process);
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new { UserName = userName, Password = password })); process.StandardInput.Close();
        await process.WaitForExitAsync(); var message = await output; await errors;
        if (process.ExitCode != 0 || !message.Contains("BLADEZ_OWNER_RECOVERED")) throw new InvalidOperationException("Восстановление не выполнено. Проверьте существующий логин владельца и требования к новому паролю.");
        progress?.Report("Пароль владельца изменён, старые сеансы и MFA сброшены. Подключите второй фактор заново.");
    }

    public async Task StopAsync()
    {
        try
        {
            if (WebRunning)
            {
                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                    using var request = new HttpRequestMessage(HttpMethod.Post, Settings.ListenUrl + "/internal/stop");
                    request.Headers.Add("X-Bladez-Control", new DataVault(root).ReadSecrets().ControlKey);
                    using var response = await client.SendAsync(request);
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { }
                try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12)); await web!.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { if (WebRunning) web!.Kill(true); }
            }
            web?.Dispose(); web = null;
            if (mysql.IsRunning) await mysql.StopAsync(Settings, new DataVault(root));
        }
        finally { processes.Dispose(); profileLock?.Dispose(); profileLock = null; }
    }
}
