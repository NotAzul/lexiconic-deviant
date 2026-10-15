using System.Threading;
using System.Windows;
using FenceForge.Services;

namespace FenceForge;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\FenceForge.SingleInstance",
            createdNew: out _ownsMutex);
        if (!_ownsMutex)
        {
            System.Windows.MessageBox.Show(
                "FenceForge is already running. Press Ctrl+Shift+G or use its tray icon.",
                "FenceForge",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            var settingsStore = new SettingsStore();
            var settings = settingsStore.Load(out var warning);
            _controller = new AppController(settings, settingsStore);
            _controller.Initialize();

            if (warning is not null)
            {
                System.Windows.MessageBox.Show(warning, "FenceForge settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception error)
        {
            System.Windows.MessageBox.Show(
                $"FenceForge could not start.\n\n{error.Message}",
                "FenceForge",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        if (_ownsMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
