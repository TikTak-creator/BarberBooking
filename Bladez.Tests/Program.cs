using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Bladez.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using MySql.Data.MySqlClient;

namespace Bladez.Tests;
public static class TestRunner
{
public static async Task Main(string[] args)
{
var repo = FindRepository();
if (args.Contains("--wait-child")) { await Task.Delay(TimeSpan.FromMinutes(2)); return; }
if (args.Contains("--host")) { await HostSuite.RunAsync(repo, args.Contains("--browser")); return; }
var output = Path.Combine(repo, ".artifacts", "tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BLADEZ_DATA", output);
Environment.SetEnvironmentVariable("Security__AuthPermitLimit", "1000");
var settings = new HostSettings { DatabaseName = "bladez_test_" + Guid.NewGuid().ToString("N")[..12] };
using (var listener = new TcpListener(IPAddress.Loopback, 0)) { listener.Start(); settings.DatabasePort = ((IPEndPoint)listener.LocalEndpoint).Port; }
settings.Save(output);
var vault = new DataVault(output);
var mysql = new LocalMySql(output, new Progress<string>(Console.WriteLine));
var external = Environment.GetEnvironmentVariable("BLADEZ_TEST_ADMIN_CONNECTION");
MySqlConnection? admin = null;
try
{
    var archive = Environment.GetEnvironmentVariable("BLADEZ_TEST_MYSQL_ARCHIVE") ?? Path.Combine(repo, ".artifacts", "downloads", "mysql-8.4.10-winx64.zip");
    string connection;
    if (external is null)
    {
        await mysql.DownloadAsync(archive);
        await mysql.InitializeAsync(settings, vault);
        await mysql.StartAsync(settings, vault);
        connection = await mysql.CreateMigrationConnectionAsync(settings, vault);
    }
    else
    {
        admin = new MySqlConnection(external); await admin.OpenAsync();
        var runtimePassword = DataVault.RandomSecret(); var migrationPassword = DataVault.RandomSecret();
        await using var command = admin.CreateCommand();
        command.CommandText = $"CREATE DATABASE `{settings.DatabaseName}` CHARACTER SET utf8mb4; CREATE USER 'bladez_app'@'%' IDENTIFIED BY '{runtimePassword}'; GRANT SELECT, INSERT, UPDATE, DELETE ON `{settings.DatabaseName}`.* TO 'bladez_app'@'%'; CREATE USER 'bladez_migrate'@'%' IDENTIFIED BY '{migrationPassword}'; GRANT ALL PRIVILEGES ON `{settings.DatabaseName}`.* TO 'bladez_migrate'@'%';";
        await command.ExecuteNonQueryAsync();
        var builder = new MySqlConnectionStringBuilder(external) { Database = settings.DatabaseName, UserID = "bladez_app", Password = runtimePassword };
        vault.SaveSecrets(new HostSecrets { ConnectionString = builder.ConnectionString });
        builder.UserID = "bladez_migrate"; builder.Password = migrationPassword; connection = builder.ConnectionString;
    }
    var connectionFile = Path.Combine(output, "migration-connection.tmp");
    PrivateFiles.Write(connectionFile, System.Text.Encoding.UTF8.GetBytes(connection));
    var ownerPassword = "TestOnly_" + Guid.NewGuid() + "aA1";
    var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
    info.ArgumentList.Add(typeof(global::Program).Assembly.Location); info.ArgumentList.Add("--initialize");
    info.Environment["BLADEZ_CONNECTION_FILE"] = connectionFile;
    using (var process = Process.Start(info)!)
    {
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new { UserName = "testowner", DisplayName = "Test owner", Password = ownerPassword, ShopName = "Bladez QA", SeedCatalog = true })); process.StandardInput.Close();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new Exception("Bootstrap failed:\n" + await stdout + "\n" + await stderr);
        Console.WriteLine("PASS: real MySQL migration and first-owner initialization");
    }
    if (admin is null) await mysql.RemoveMigrationUserAsync(settings, vault);
    else { await using var command = admin.CreateCommand(); command.CommandText = "DROP USER 'bladez_migrate'@'%'"; await command.ExecuteNonQueryAsync(); }
    File.Delete(connectionFile);
    await using (var sql = new MySqlConnection(vault.ReadSecrets().ConnectionString))
    { await sql.OpenAsync(); await using var command = sql.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM AspNetUsers"; if(Convert.ToInt32(await command.ExecuteScalarAsync()) != 1) throw new Exception("Owner missing."); }
    using var factory = new TestApplication(repo);
    using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    var home = await client.GetAsync("/"); if(home.StatusCode != HttpStatusCode.OK) throw new Exception("Homepage failed: " + home.StatusCode + " " + await home.Content.ReadAsStringAsync());
    Console.WriteLine("PASS: homepage using the least-privilege runtime account");
    await TestSuite.RunAsync(factory, settings, vault, ownerPassword, repo, output);
    Console.WriteLine("QA output: " + output);
}
finally
{
    await mysql.StopAsync(settings,vault);
    if (admin is not null)
    {
        await using var command = admin.CreateCommand(); command.CommandText = $"DROP DATABASE `{settings.DatabaseName}`; DROP USER IF EXISTS 'bladez_app'@'%', 'bladez_migrate'@'%';"; await command.ExecuteNonQueryAsync(); await admin.DisposeAsync();
    }
}

}
static string FindRepository()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "BarberBooking.slnx"))) return directory.FullName;
    throw new InvalidOperationException("Repository root not found.");
}
}
sealed class TestApplication(string repo) : WebApplicationFactory<global::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Production").UseContentRoot(Path.Combine(repo, "BarberBooking.Web"));
}
