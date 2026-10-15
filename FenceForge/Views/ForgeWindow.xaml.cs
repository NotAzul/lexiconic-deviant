using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using FenceForge.Models;
using FenceForge.Services;

namespace FenceForge.Views;

public partial class ForgeWindow : Window
{
    private readonly TransformSession _session;
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly CredentialStore _credentialStore;
    private readonly TransformCoordinator _coordinator;
    private readonly ClipboardService _clipboardService;
    private readonly Action<Window?> _openSettings;
    private CancellationTokenSource? _requestCancellation;
    private bool _busy;
    private bool _hasResult;
    private bool _discardResults;

    public ForgeWindow(
        TransformSession session,
        AppSettings settings,
        SettingsStore settingsStore,
        CredentialStore credentialStore,
        TransformCoordinator coordinator,
        ClipboardService clipboardService,
        Action<Window?> openSettings)
    {
        InitializeComponent();
        _session = session;
        _settings = settings;
        _settingsStore = settingsStore;
        _credentialStore = credentialStore;
        _coordinator = coordinator;
        _clipboardService = clipboardService;
        _openSettings = openSettings;

        SourceBox.Text = session.OriginalSource;
        SourceHeader.Text = $"Captured source — {session.OriginalSource.Length:N0} characters";
        RefreshTransformPicker();

        ContentRendered += (_, _) =>
        {
            PopupPositioner.PositionNearPointer(this);
            TransformPicker.Focus();
        };
        Closing += WindowClosing;
    }

    private void RefreshTransformPicker()
    {
        var definitions = TransformCatalog.GetDefinitions(_settings);
        var view = new ListCollectionView(definitions.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TransformDefinition.Group)));
        TransformPicker.ItemsSource = view;
        TransformPicker.SelectedItem = definitions.FirstOrDefault(item => item.Id == "codify") ?? definitions[0];
    }

    private TransformDefinition? SelectedDefinition => TransformPicker.SelectedItem as TransformDefinition;

    private void TransformSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        CustomPanel.Visibility = SelectedDefinition?.Kind == TransformKind.CustomEntry
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SaveAsChanged(object sender, RoutedEventArgs e)
    {
        SaveAsNameBox.IsEnabled = SaveAsCheckBox.IsChecked == true;
        if (SaveAsNameBox.IsEnabled)
        {
            SaveAsNameBox.Focus();
        }
    }

    private async void TransformClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _session.SubmittedTransform is not null)
        {
            return;
        }

        ErrorText.Text = string.Empty;
        var definition = BuildDefinitionForSubmission();
        if (definition is null)
        {
            return;
        }

        if (definition.UsesGemini && !EnsureApiKey())
        {
            return;
        }

        try
        {
            _session.Submit(definition);
            LockSubmittedControls();
            await ExecuteAsync(retry: false);
        }
        catch (Exception error)
        {
            ShowError(error.Message);
        }
    }

    private TransformDefinition? BuildDefinitionForSubmission()
    {
        var selected = SelectedDefinition;
        if (selected is null)
        {
            ShowError("Choose a transformation.");
            return null;
        }

        if (selected.Kind != TransformKind.CustomEntry)
        {
            return selected;
        }

        var prompt = CustomPromptBox.Text;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            ShowError("Enter a custom transformation prompt.");
            CustomPromptBox.Focus();
            return null;
        }

        var save = SaveAsCheckBox.IsChecked == true;
        var name = save ? SaveAsNameBox.Text.Trim() : "Custom";
        if (!TransformCatalog.IsValidCustomName(name))
        {
            ShowError("Use a custom name from 1 to 64 characters without control characters.");
            SaveAsNameBox.Focus();
            return null;
        }

        if (save && TransformCatalog.IsReservedName(name))
        {
            ShowError("That name is reserved by a built-in transformation.");
            SaveAsNameBox.Focus();
            return null;
        }

        if (save && _settings.CustomTransforms.Any(item =>
                item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowError("A saved transformation already uses that name.");
            SaveAsNameBox.Focus();
            return null;
        }

        if (save)
        {
            try
            {
                var updated = new AppSettings
                {
                    Model = _settings.Model,
                    CustomTransforms = _settings.CustomTransforms
                        .Select(item => new SavedTransform { Name = item.Name, Prompt = item.Prompt })
                        .Append(new SavedTransform { Name = name, Prompt = prompt })
                        .ToList(),
                };
                _settingsStore.Save(updated);
                _settings.CustomTransforms = updated.CustomTransforms;
            }
            catch (Exception error)
            {
                ShowError($"The custom transformation could not be saved. {error.Message}");
                return null;
            }
        }

        return TransformCatalog.CreateAdHocCustom(name, prompt);
    }

    private bool EnsureApiKey()
    {
        try
        {
            if (_credentialStore.HasApiKey())
            {
                return true;
            }

            _openSettings(this);
            if (_credentialStore.HasApiKey())
            {
                return true;
            }

            ShowError("A Gemini API key is required for this transformation.");
            return false;
        }
        catch (Exception error)
        {
            ShowError(error.Message);
            return false;
        }
    }

    private void LockSubmittedControls()
    {
        TransformPicker.IsEnabled = false;
        TransformButton.IsEnabled = false;
        CustomPromptBox.IsEnabled = false;
        SaveAsCheckBox.IsEnabled = false;
        SaveAsNameBox.IsEnabled = false;
    }

    private async void RetryClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _session.SubmittedTransform?.UsesGemini != true)
        {
            return;
        }

        if (!EnsureApiKey())
        {
            return;
        }

        await ExecuteAsync(retry: true);
    }

    private async Task ExecuteAsync(bool retry)
    {
        if (_busy || _discardResults)
        {
            return;
        }

        _busy = true;
        ErrorText.Text = string.Empty;
        StatusText.Text = retry ? "Retrying from the original source…" : "Transforming the captured source…";
        WorkingBar.Visibility = Visibility.Visible;
        SetActionStates();
        _requestCancellation = new CancellationTokenSource();

        try
        {
            var result = await _coordinator.ExecuteAsync(
                _session, retry, _requestCancellation.Token);
            if (_discardResults)
            {
                return;
            }

            ResultBox.Text = result;
            _hasResult = true;
            StatusText.Text = _session.SubmittedTransform?.IsLocal == true
                ? "Local transformation complete."
                : retry
                    ? "Retry complete."
                    : "Transformation complete.";
            ResultBox.Focus();
            ResultBox.Select(0, 0);
        }
        catch (GeminiFailure failure)
        {
            if (!_discardResults && failure.Kind != GeminiFailureKind.Cancelled)
            {
                ShowError(MessageFor(failure));
            }
        }
        catch (OperationCanceledException)
        {
            // Closing the popup intentionally discards this request.
        }
        catch (Exception error)
        {
            if (!_discardResults)
            {
                ShowError($"The transformation failed unexpectedly. {error.Message}");
            }
        }
        finally
        {
            _requestCancellation?.Dispose();
            _requestCancellation = null;
            _busy = false;
            if (!_discardResults)
            {
                WorkingBar.Visibility = Visibility.Collapsed;
                SetActionStates();
            }
        }
    }

    private static string MessageFor(GeminiFailure failure) => failure.Kind switch
    {
        GeminiFailureKind.Authentication =>
            "Gemini authentication failed. Open Gemini settings and set a valid API key.",
        GeminiFailureKind.Network =>
            "FenceForge could not reach Gemini. Check the network connection and try again.",
        GeminiFailureKind.Timeout =>
            "Gemini did not respond before the request timed out. Try again.",
        GeminiFailureKind.RateLimited =>
            "Gemini is rate-limiting requests. Wait briefly and try again.",
        GeminiFailureKind.ServiceUnavailable =>
            "Gemini is temporarily unavailable. Try again shortly.",
        GeminiFailureKind.MalformedResponse =>
            "Gemini returned a response FenceForge could not read.",
        GeminiFailureKind.EmptyResponse =>
            "Gemini returned no usable text.",
        _ => failure.SafeDetail ?? "The Gemini request failed. Check the model setting and try again.",
    };

    private void SetActionStates()
    {
        var submitted = _session.SubmittedTransform;
        RetryButton.IsEnabled = !_busy && submitted?.UsesGemini == true;
        CopyButton.IsEnabled = !_busy && _hasResult;
        ResultBox.IsEnabled = !_busy && _hasResult;
        SettingsButton.IsEnabled = !_busy;
    }

    private async void CopyClick(object sender, RoutedEventArgs e)
    {
        if (_busy || !_hasResult)
        {
            return;
        }

        CopyButton.IsEnabled = false;
        ErrorText.Text = string.Empty;
        var error = await _clipboardService.WriteTextAsync(ResultBox.Text);
        if (error is not null)
        {
            ShowError(error);
            SetActionStates();
            return;
        }

        Close();
    }

    private void CancelClick(object sender, RoutedEventArgs e) => Close();

    private void SettingsClick(object sender, RoutedEventArgs e) => _openSettings(this);

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        _discardResults = true;
        _requestCancellation?.Cancel();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        StatusText.Text = string.Empty;
    }
}
