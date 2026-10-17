using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Bladez.Host;

internal static class RuntimeCheck
{
    internal static async Task EnsureAsync(IWin32Window window)
    {
        if (NativeLibrary.TryLoad("vcruntime140.dll", out var first) && NativeLibrary.TryLoad("msvcp140.dll", out var second))
        { NativeLibrary.Free(first); NativeLibrary.Free(second); return; }
        var installer = Path.Combine(AppContext.BaseDirectory, "prerequisites", "vc_redist.x64.exe");
        if (!File.Exists(installer)) throw new InvalidOperationException("Для MySQL нужен Microsoft Visual C++ 2015–2022 x64 Runtime. Используйте полный ZIP/Setup из релиза или установите компонент с сайта Microsoft (ссылка в руководстве).");
        using (var stream = File.OpenRead(installer))
            if (Convert.ToHexStringLower(SHA256.HashData(stream)) != "cc0ff0eb1dc3f5188ae6300faef32bf5beeba4bdd6e8e445a9184072096b713b")
                throw new CryptographicException("Microsoft Runtime не прошёл проверку. Повторно скачайте официальный релиз Bladez.");
        if (MessageBox.Show(window, "MySQL требуется Microsoft Visual C++ Runtime. Установить включённый компонент Microsoft? Windows запросит права администратора для этого компонента.", "Компонент Microsoft", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
            throw new OperationCanceledException("Установка компонента отменена.");
        using var process = Process.Start(new ProcessStartInfo(installer, "/install /passive /norestart") { UseShellExecute = true, Verb = "runas" }) ?? throw new InvalidOperationException("Не удалось запустить установку Microsoft Runtime.");
        await process.WaitForExitAsync();
        if (process.ExitCode is not (0 or 1638 or 3010)) throw new InvalidOperationException("Microsoft Runtime не установлен. Код: " + process.ExitCode);
        if (process.ExitCode == 3010) throw new InvalidOperationException("Перезагрузите Windows после установки Microsoft Runtime, затем снова откройте Bladez Host.");
    }
}
