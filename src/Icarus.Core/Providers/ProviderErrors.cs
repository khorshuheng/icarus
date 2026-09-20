using Icarus.Core.Config;
using Icarus.Core.Provider;

namespace Icarus.Core.Providers;

/// <summary>
/// Classifies arbitrary SDK/adapter exceptions into the canonical
/// <see cref="ProviderException"/> kinds (ICARUS-102), without taking a
/// dependency on any particular vendor exception type. It also decides
/// retryability: quota/billing is never retried, transient statuses are.
/// </summary>
public static class ProviderErrors
{
    private static readonly string[] QuotaPatterns =
    [
        "insufficient_quota",
        "out of budget",
        "quota exceeded",
        "billing",
        "usage limit",
        "available balance",
        "free usage limit",
        "monthly usage",
    ];

    public static ProviderException Classify(Exception error)
    {
        var status = StatusOf(error);
        var text = error.Message;
        var lower = text.ToLowerInvariant();

        if (IsQuota(lower))
        {
            return new ProviderHttpException($"quota or billing limit (status {status}): {text}");
        }

        if (status is 401 or 403 || lower.Contains("api key") || lower.Contains("unauthorized")
            || lower.Contains("authentication"))
        {
            return new ProviderAuthException(text);
        }

        if (status is 408 or 429)
        {
            return new ProviderTimeoutException(text);
        }

        if (status is not null)
        {
            return new ProviderHttpException($"status {status}: {text}");
        }

        return error switch
        {
            System.Text.Json.JsonException => new ProviderMalformedException(text),
            NotSupportedException => new ProviderUnsupportedException(text),
            _ => new ProviderHttpException(text),
        };
    }

    public static bool IsTransient(Exception error)
    {
        if (IsQuota(error.Message.ToLowerInvariant()))
        {
            return false;
        }

        var status = StatusOf(error);
        return status is 408 or 429 || status is >= 500 and <= 599
            || error is HttpRequestException or TimeoutException or TaskCanceledException;
    }

    private static bool IsQuota(string lowerText) => QuotaPatterns.Any(lowerText.Contains);

    private static int? StatusOf(Exception error)
    {
        // Adapters surface HTTP status on a Status/StatusCode property.
        var property = error.GetType().GetProperty("Status")
            ?? error.GetType().GetProperty("StatusCode");
        var value = property?.GetValue(error);
        return value switch
        {
            int code => code,
            System.Net.HttpStatusCode http => (int)http,
            _ => null,
        };
    }
}
