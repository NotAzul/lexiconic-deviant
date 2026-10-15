using System.IO;
using System.Text.Json;
using FenceForge.Models;

namespace FenceForge.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public SettingsStore(string? directoryPath = null)
    {
        DirectoryPath = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FenceForge");
        FilePath = Path.Combine(DirectoryPath, "settings.json");
    }

    public string DirectoryPath { get; }

    public string FilePath { get; }

    public AppSettings Load(out string? warning)
    {
        warning = null;
        if (!File.Exists(FilePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            Normalize(settings);
            return settings;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            warning = $"FenceForge could not read its settings file. Defaults will be used, and the existing file has been preserved.\n\n{error.Message}";
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);
        Directory.CreateDirectory(DirectoryPath);

        var temporaryPath = FilePath + ".tmp";
        var backupPath = FilePath + ".bak";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            if (File.Exists(FilePath))
            {
                File.Replace(temporaryPath, FilePath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, FilePath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static void Normalize(AppSettings settings)
    {
        var model = settings.Model?.Trim();
        settings.Model = GeminiClient.IsValidModel(model)
            ? model!
            : AppSettings.DefaultModel;
        settings.CustomTransforms ??= [];
        settings.CustomTransforms = settings.CustomTransforms
            .Where(item => item is not null && TransformCatalog.IsValidCustomName(item.Name) &&
                           !TransformCatalog.IsReservedName(item.Name) &&
                           !string.IsNullOrWhiteSpace(item.Prompt))
            .GroupBy(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new SavedTransform
            {
                Name = group.First().Name.Trim(),
                Prompt = group.First().Prompt,
            })
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
