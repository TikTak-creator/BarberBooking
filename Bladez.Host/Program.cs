using Bladez.Core;

namespace Bladez.Host;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "Bladez Host", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new HostForm(args));
    }
}
