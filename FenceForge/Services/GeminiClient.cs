using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FenceForge.Services;

public enum GeminiFailureKind
{
    Authentication,
    Network,
    Timeout,
    RateLimited,
    ServiceUnavailable,
    Api,
    MalformedResponse,
    EmptyResponse,
    Cancelled,
}

public sealed class GeminiFailure : Exception
{
    public GeminiFailure(GeminiFailureKind kind, bool retryable, string? safeDetail = null, Exception? inner = null)
        : base(kind.ToString(), inner)
    {
        Kind = kind;
        Retryable = retryable;
        SafeDetail = safeDetail;
    }

    public GeminiFailureKind Kind { get; }

    public bool Retryable { get; }

    public string? SafeDetail { get; }
}

public sealed partial class GeminiClient : IDisposable
{
    private const string ApiRoot = "https://generativelanguage.googleapis.com/v1beta/models/";
    private const int MaximumAttempts = 3;
    private const int MaximumResponseCharacters = 2_000_000;
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(40);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public GeminiClient(HttpClient? httpClient = null)
    {
        if (httpClient is not null)
        {
            _httpClient = httpClient;
            _ownsClient = false;
            return;
        }

        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _ownsClient = true;
    }

    public static bool IsValidModel(string? model) =>
        model is not null && ModelPattern().IsMatch(model);

    public async Task<string> GenerateAsync(
        string apiKey,
        string model,
        string systemInstruction,
        string input,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new GeminiFailure(GeminiFailureKind.Authentication, retryable: false);
        }

        if (!IsValidModel(model))
        {
            throw new GeminiFailure(GeminiFailureKind.Api, retryable: false, "The configured model name is invalid.");
        }

        var requestBody = BuildRequestJson(systemInstruction, input);
        GeminiFailure? lastFailure = null;

        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await RequestOnceAsync(apiKey, model, requestBody, cancellationToken).ConfigureAwait(false);
            }
            catch (GeminiFailure failure)
            {
                lastFailure = failure;
                if (!failure.Retryable || attempt + 1 >= MaximumAttempts)
                {
                    throw;
                }

                var delay = TimeSpan.FromMilliseconds((350 << attempt) + Random.Shared.Next(250));
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException error)
                {
                    throw new GeminiFailure(GeminiFailureKind.Cancelled, retryable: false, inner: error);
                }
            }
            catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested)
            {
                throw new GeminiFailure(GeminiFailureKind.Cancelled, retryable: false, inner: error);
            }
        }

        throw lastFailure ?? new GeminiFailure(GeminiFailureKind.Api, retryable: false);
    }

    internal static string BuildRequestJson(string systemInstruction, string input)
    {
        var request = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = systemInstruction } },
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = input } },
                },
            },
            safetySettings = new[]
            {
                new { category = "HARM_CATEGORY_HARASSMENT", threshold = "BLOCK_ONLY_HIGH" },
                new { category = "HARM_CATEGORY_HATE_SPEECH", threshold = "BLOCK_ONLY_HIGH" },
                new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_ONLY_HIGH" },
                new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_ONLY_HIGH" },
            },
        };

        return JsonSerializer.Serialize(request);
    }

    internal static string ParseResponse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
            {
                throw new GeminiFailure(GeminiFailureKind.EmptyResponse, retryable: false);
            }

            var candidate = candidates[0];
            if (!candidate.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.ValueKind != JsonValueKind.Array)
            {
                throw new GeminiFailure(GeminiFailureKind.EmptyResponse, retryable: false);
            }

            var text = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var partText) &&
                    partText.ValueKind == JsonValueKind.String)
                {
                    text.Append(partText.GetString());
                }
            }

            return SanitizeOutput(text.ToString());
        }
        catch (GeminiFailure)
        {
            throw;
        }
        catch (JsonException error)
        {
            throw new GeminiFailure(GeminiFailureKind.MalformedResponse, retryable: false, inner: error);
        }
    }

    internal static string SanitizeOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            throw new GeminiFailure(GeminiFailureKind.EmptyResponse, retryable: false);
        }

        var trimmed = output.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return output;
        }

        var contentStart = trimmed.IndexOf('\n');
        var fenceStart = trimmed.LastIndexOf("\n```", StringComparison.Ordinal);
        if (contentStart < 0 || fenceStart <= contentStart ||
            !string.IsNullOrWhiteSpace(trimmed[(fenceStart + 4)..]))
        {
            return output;
        }

        var unfenced = trimmed[(contentStart + 1)..fenceStart];
        if (string.IsNullOrWhiteSpace(unfenced))
        {
            throw new GeminiFailure(GeminiFailureKind.EmptyResponse, retryable: false);
        }

        return unfenced;
    }

    private async Task<string> RequestOnceAsync(
        string apiKey,
        string model,
        string requestBody,
        CancellationToken cancellationToken)
    {
        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptCts.CancelAfter(AttemptTimeout);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{ApiRoot}{Uri.EscapeDataString(model)}:generateContent");
        request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, attemptCts.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(attemptCts.Token).ConfigureAwait(false);
            if (body.Length > MaximumResponseCharacters)
            {
                throw new GeminiFailure(GeminiFailureKind.Api, retryable: false, "The response was unexpectedly large.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw ClassifyHttpFailure(response.StatusCode, body);
            }

            return ParseResponse(body);
        }
        catch (GeminiFailure)
        {
            throw;
        }
        catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested)
        {
            throw new GeminiFailure(GeminiFailureKind.Cancelled, retryable: false, inner: error);
        }
        catch (OperationCanceledException error)
        {
            throw new GeminiFailure(GeminiFailureKind.Timeout, retryable: true, inner: error);
        }
        catch (HttpRequestException error)
        {
            throw new GeminiFailure(GeminiFailureKind.Network, retryable: true, inner: error);
        }
    }

    private static GeminiFailure ClassifyHttpFailure(HttpStatusCode status, string errorBody)
    {
        var numericStatus = (int)status;
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ||
            (status == HttpStatusCode.BadRequest &&
             (errorBody.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase) ||
              errorBody.Contains("API key not valid", StringComparison.OrdinalIgnoreCase))))
        {
            return new GeminiFailure(GeminiFailureKind.Authentication, retryable: false);
        }

        if (status == HttpStatusCode.RequestTimeout)
        {
            return new GeminiFailure(GeminiFailureKind.Timeout, retryable: true);
        }

        if (numericStatus == 429)
        {
            return new GeminiFailure(GeminiFailureKind.RateLimited, retryable: true);
        }

        if (numericStatus >= 500)
        {
            return new GeminiFailure(GeminiFailureKind.ServiceUnavailable, retryable: true);
        }

        return new GeminiFailure(GeminiFailureKind.Api, retryable: false, $"Gemini returned HTTP {numericStatus}.");
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex ModelPattern();
}
