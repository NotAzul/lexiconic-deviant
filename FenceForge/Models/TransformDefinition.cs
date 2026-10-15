namespace FenceForge.Models;

public enum TransformKind
{
    Gemini,
    GeminiReply,
    EscapeXml,
    Sponge,
    CustomEntry,
}

public sealed record TransformDefinition(
    string Id,
    string Name,
    string Prompt,
    TransformKind Kind,
    string Group,
    bool IsSavedCustom = false)
{
    public bool UsesGemini => Kind is TransformKind.Gemini or TransformKind.GeminiReply;
    public bool IsLocal => Kind is TransformKind.EscapeXml or TransformKind.Sponge;
}
