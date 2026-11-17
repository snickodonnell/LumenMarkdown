using System.Windows;
using System.IO;

namespace LumenMarkdown;

public partial class App : Application
{
    private SingleInstanceCoordinator? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = e.Args.Where(File.Exists).Select(Path.GetFullPath).ToArray();
        _singleInstance = new SingleInstanceCoordinator();
        if (!_singleInstance.IsPrimary)
        {
            _singleInstance.SendToPrimaryAsync(paths).GetAwaiter().GetResult();
            Shutdown();
            return;
        }

        var window = new MainWindow(paths);
        MainWindow = window;
        window.Show();
        _singleInstance.StartListening(received => Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (received.Length > 0) await window.OpenFilesAsync(received);
            window.BringToFront();
        })));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
