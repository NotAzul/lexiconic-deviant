namespace FenceForge.Models;

public sealed class AppSettings
{
    public const string DefaultModel = "gemini-3.8-flash";

    public string Model { get; set; } = DefaultModel;

    public List<SavedTransform> CustomTransforms { get; set; } = [];
}

public sealed class SavedTransform
{
    public string Name { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;
}
