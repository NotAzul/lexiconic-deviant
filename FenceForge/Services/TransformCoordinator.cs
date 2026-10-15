using FenceForge.Models;

namespace FenceForge.Services;

public sealed class TransformCoordinator
{
    private readonly GeminiClient _geminiClient;
    private readonly CredentialStore _credentialStore;
    private readonly AppSettings _settings;

    public TransformCoordinator(
        GeminiClient geminiClient,
        CredentialStore credentialStore,
        AppSettings settings)
    {
        _geminiClient = geminiClient;
        _credentialStore = credentialStore;
        _settings = settings;
    }

    public async Task<string> ExecuteAsync(
        TransformSession session,
        bool retry,
        CancellationToken cancellationToken)
    {
        var definition = session.SubmittedTransform ??
            throw new InvalidOperationException("No transformation has been submitted for this session.");

        cancellationToken.ThrowIfCancellationRequested();
        switch (definition.Kind)
        {
            case TransformKind.EscapeXml:
                return LocalTransforms.EscapeXml(session.OriginalSource);
            case TransformKind.Sponge:
                return LocalTransforms.Sponge(session.OriginalSource);
            case TransformKind.Gemini:
            case TransformKind.GeminiReply:
            {
                var apiKey = _credentialStore.ReadApiKey();
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    throw new GeminiFailure(GeminiFailureKind.Authentication, retryable: false,
                        "No Gemini API key is stored.");
                }

                var systemInstruction = TransformCatalog.ComposeSystemInstruction(definition, retry);
                return await _geminiClient.GenerateAsync(
                    apiKey, _settings.Model, systemInstruction,
                    session.OriginalSource, cancellationToken).ConfigureAwait(false);
            }
            default:
                throw new InvalidOperationException("The selected transformation cannot be executed.");
        }
    }
}
