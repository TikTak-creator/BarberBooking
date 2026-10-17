using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Bladez.Core;
using MySql.Data.MySqlClient;

namespace Bladez.Tests;

internal static class HostSuite
{
    private static int Port() { using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
    internal static async Task RunAsync(string repo, bool browser)
    {
        var output = Path.Combine(repo, ".artifacts", "host-qa-" + Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(output, "original"); PrivateFiles.Directory(output);
        var server = Path.Combine(repo, ".artifacts", "publish", "windows", "server");
        var archive = Path.Combine(repo, ".artifacts", "downloads", "mysql-8.4.10-winx64.zip");
        var settings = new HostSettings { DatabasePort = Port(), ListenUrl = "http://127.0.0.1:" + Port(), DatabaseName = "bladez_qa" }; settings.PublicUrl = settings.ListenUrl;
        var password = "Qa_A1_" + Guid.NewGuid().ToString("N");
        var checks = new List<string>();
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException("FAIL: " + label); checks.Add(label); Console.WriteLine("PASS: " + label); }
        var manager = new ServerManager(profile, server, new Progress<string>(Console.WriteLine));
        try
        {
            await manager.InitializeAsync(new { UserName = "qa_owner", DisplayName = "Владелец Bladez", Password = password, ShopName = "Bladez", SeedCatalog = true }, settings, archive);
            Check(manager.Configured && manager.WebRunning && manager.DatabaseRunning, "Real Windows host provisions MySQL, schema, owner and self-contained site");
            var competing = new ServerManager(profile, server);
            var denied = false; try { await competing.StartAsync(); } catch (InvalidOperationException) { denied = true; }
            Check(denied, "Second host cannot manage the same profile");
            using var client = new HttpClient();
            Check((await client.GetAsync(settings.PublicUrl + "/health/ready")).IsSuccessStatusCode, "Actual Kestrel readiness validates database and key");
            await manager.RecoverOwnerAsync("qa_owner", password);
            Check(true, "Offline owner recovery works with local host access");
            if (browser)
            {
                var info = new ProcessStartInfo("node") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
                info.ArgumentList.Add(Path.Combine(repo, "scripts", "browser-check.cjs"));
                info.Environment["BLADEZ_PUPPETEER_MODULE"] = Environment.GetEnvironmentVariable("BLADEZ_PUPPETEER_MODULE") ?? "puppeteer";
                using var process = Process.Start(info)!;
                await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new { url = settings.PublicUrl, owner = "qa_owner", password, output = Path.Combine(repo, "docs", "screenshots") })); process.StandardInput.Close();
                await process.WaitForExitAsync(); Check(process.ExitCode == 0, "Browser booking, account, owner pages and responsive screenshots");
            }
            await manager.StopAsync();
            var vault = new DataVault(profile); var fingerprint = vault.Fingerprint("qa", "restore");
            var backup = Path.Combine(output, "profile.bladez");
            var backupPassword = DataVault.RandomSecret();
            await Task.Run(() => ProfileBackup.Create(profile, backup, backupPassword));
            Check(File.Exists(backup) && !File.ReadAllBytes(backup).AsSpan().Contains("DPAPI1:"u8), "Cold profile backup encrypted under separate passphrase");
            var restored = Path.Combine(output, "restored"); denied = false;
            try { ProfileBackup.Restore(backup, restored, "WrongPassword_1234"); } catch (CryptographicException) { denied = true; }
            Check(denied && !Directory.Exists(restored), "Wrong backup password writes no profile");
            ProfileBackup.Restore(backup, restored, backupPassword);
            Check(new DataVault(restored).Fingerprint("qa", "restore") == fingerprint, "Restore preserves field key and rewraps it for current OS account");
            denied = false; try { ProfileBackup.Restore(backup, restored, backupPassword); } catch (InvalidOperationException) { denied = true; }
            Check(denied, "Restore refuses to overwrite an existing profile");
            var restoredManager = new ServerManager(restored, server, new Progress<string>(Console.WriteLine));
            try
            {
                await restoredManager.StartAsync(archive);
                Check((await client.GetAsync(settings.PublicUrl + "/health/ready")).IsSuccessStatusCode, "Restored physical MySQL and self-contained site start successfully");
                await using var sql = new MySqlConnection(new DataVault(restored).ReadSecrets().ConnectionString); await sql.OpenAsync();
                await using var command = sql.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM AspNetUsers";
                Check(Convert.ToInt32(await command.ExecuteScalarAsync()) >= 1, "Owner account exists after restore");
            }
            finally { await restoredManager.StopAsync(); }
            var broken = Path.Combine(output, "missing-key"); var brokenVault = new DataVault(broken); brokenVault.SaveSecrets(new HostSecrets()); File.Delete(Path.Combine(broken, "master.key"));
            denied = false; try { _ = new DataVault(broken); } catch (CryptographicException) { denied = true; }
            Check(denied && !File.Exists(Path.Combine(broken, "master.key")), "Lost key fails closed without silently creating a replacement");
        }
        finally
        {
            await manager.StopAsync();
            File.WriteAllText(Path.Combine(output, "host-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Host report: " + output);
        }
    }
}
