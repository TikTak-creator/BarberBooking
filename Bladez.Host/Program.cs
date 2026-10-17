using Bladez.Core;

namespace Bladez.Host;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try { ProcessGuard.Attach(); }
        catch (Exception error) { MessageBox.Show(error.Message, "Bladez Host", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "Bladez Host", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new HostForm(args));
    }
}
