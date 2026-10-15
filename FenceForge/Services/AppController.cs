using System.Windows;
using FenceForge.Models;
using FenceForge.Views;

namespace FenceForge.Services;

public sealed class AppController : IDisposable
{
    public const int MaximumInputCharacters = 100_000;

    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly CredentialStore _credentialStore;
    private readonly ClipboardService _clipboardService;
    private readonly GeminiClient _geminiClient;
    private readonly TransformCoordinator _coordinator;
    private readonly GlobalHotkeyService _hotkeyService;
    private TrayIconService? _trayIcon;
    private ForgeWindow? _forgeWindow;
    private SettingsWindow? _settingsWindow;
    private bool _openingSession;
    private bool _disposed;

    public AppController(AppSettings settings, SettingsStore settingsStore)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _credentialStore = new CredentialStore();
        _clipboardService = new ClipboardService();
        _geminiClient = new GeminiClient();
        _coordinator = new TransformCoordinator(_geminiClient, _credentialStore, _settings);
        _hotkeyService = new GlobalHotkeyService();
    }

    public void Initialize()
    {
        _hotkeyService.Pressed += HotkeyPressed;
        _hotkeyService.Register();
        _trayIcon = new TrayIconService(
            open: () => OpenSession(),
            settings: () => OpenSettings(null),
            exit: ExitApplication);
    }

    private void HotkeyPressed(object? sender, EventArgs args) => OpenSession();

    public async void OpenSession()
    {
        if (_disposed || _openingSession)
        {
            return;
        }

        if (_forgeWindow is { IsVisible: true })
        {
            _forgeWindow.Activate();
            return;
        }

        _openingSession = true;
        try
        {
            var clipboard = await _clipboardService.ReadTextAsync();
            if (!clipboard.Succeeded)
            {
                System.Windows.MessageBox.Show(
                    clipboard.Error ?? "The clipboard does not contain text.",
                    "FenceForge",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (clipboard.Text!.Length > MaximumInputCharacters)
            {
                System.Windows.MessageBox.Show(
                    $"The clipboard contains {clipboard.Text.Length:N0} characters. FenceForge accepts at most {MaximumInputCharacters:N0} characters per session.",
                    "FenceForge",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var session = new TransformSession(clipboard.Text);
            _forgeWindow = new ForgeWindow(
                session,
                _settings,
                _settingsStore,
                _credentialStore,
                _coordinator,
                _clipboardService,
                OpenSettings);
            _forgeWindow.Closed += (_, _) => _forgeWindow = null;
            _forgeWindow.Show();
            _forgeWindow.Activate();
        }
        catch (Exception error)
        {
            System.Windows.MessageBox.Show(
                $"FenceForge could not open a transformation session.\n\n{error.Message}",
                "FenceForge",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _openingSession = false;
        }
    }

    public void OpenSettings(Window? owner)
    {
        if (_disposed)
        {
            return;
        }

        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, _settingsStore, _credentialStore);
        if (owner is { IsVisible: true })
        {
            _settingsWindow.Owner = owner;
            _settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.ShowDialog();
    }

    private void ExitApplication()
    {
        _forgeWindow?.Close();
        _settingsWindow?.Close();
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hotkeyService.Pressed -= HotkeyPressed;
        _trayIcon?.Dispose();
        _hotkeyService.Dispose();
        _geminiClient.Dispose();
    }
}
