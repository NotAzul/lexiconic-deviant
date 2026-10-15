using System.Collections.ObjectModel;
using System.Windows;
using FenceForge.Models;
using FenceForge.Services;

namespace FenceForge.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly CredentialStore _credentialStore;
    private readonly ObservableCollection<SavedTransform> _customTransforms;

    public SettingsWindow(
        AppSettings settings,
        SettingsStore settingsStore,
        CredentialStore credentialStore)
    {
        InitializeComponent();
        _settings = settings;
        _settingsStore = settingsStore;
        _credentialStore = credentialStore;
        ModelBox.Text = settings.Model;
        _customTransforms = new ObservableCollection<SavedTransform>(
            settings.CustomTransforms.Select(item => new SavedTransform
            {
                Name = item.Name,
                Prompt = item.Prompt,
            }));
        CustomTransformsList.ItemsSource = _customTransforms;
        RefreshCredentialStatus();
    }

    private void RefreshCredentialStatus()
    {
        try
        {
            var stored = _credentialStore.HasApiKey();
            ApiKeyStatus.Text = stored
                ? "An API key is stored in Windows Credential Manager. Leave this field blank to keep it."
                : "No API key is currently stored.";
            RemoveApiKeyButton.IsEnabled = stored;
        }
        catch (Exception error)
        {
            ApiKeyStatus.Text = $"Credential status unavailable: {error.Message}";
            RemoveApiKeyButton.IsEnabled = false;
        }
    }

    private void RemoveApiKeyClick(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show(
                this,
                "Remove the stored Gemini API key?",
                "FenceForge",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _credentialStore.DeleteApiKey();
            ApiKeyBox.Clear();
            RefreshCredentialStatus();
            ErrorText.Text = string.Empty;
        }
        catch (Exception error)
        {
            ErrorText.Text = error.Message;
        }
    }

    private void RemoveCustomClick(object sender, RoutedEventArgs e)
    {
        if (CustomTransformsList.SelectedItem is SavedTransform selected)
        {
            _customTransforms.Remove(selected);
        }
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var model = ModelBox.Text.Trim();
        if (!GeminiClient.IsValidModel(model))
        {
            ErrorText.Text = "Enter a valid Gemini model identifier.";
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(ApiKeyBox.Password))
            {
                _credentialStore.WriteApiKey(ApiKeyBox.Password);
            }

            var updated = new AppSettings
            {
                Model = model,
                CustomTransforms = _customTransforms.Select(item => new SavedTransform
                {
                    Name = item.Name,
                    Prompt = item.Prompt,
                }).ToList(),
            };
            _settingsStore.Save(updated);
            _settings.Model = updated.Model;
            _settings.CustomTransforms = updated.CustomTransforms;
            DialogResult = true;
        }
        catch (Exception error)
        {
            ErrorText.Text = error.Message;
        }
    }
}
