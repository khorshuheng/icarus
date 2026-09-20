namespace Icarus.Core.Provider;

/// <summary>
/// A typed provider failure, surfaced clearly at the loop boundary. The set
/// mirrors CRAB's <c>ProviderError</c> (ICARUS-102/103).
/// </summary>
public abstract class ProviderException : Exception
{
    protected ProviderException(string message) : base(message) { }
}

/// <summary>Authentication failed (bad or missing key).</summary>
public sealed class ProviderAuthException(string message) : ProviderException(message);

/// <summary>The request timed out, or the provider was transiently unavailable.</summary>
public sealed class ProviderTimeoutException(string message) : ProviderException(message);

/// <summary>The provider returned a response that could not be parsed.</summary>
public sealed class ProviderMalformedException(string message) : ProviderException(message);

/// <summary>An HTTP-level failure that is neither auth nor timeout.</summary>
public sealed class ProviderHttpException(string message) : ProviderException(message);

/// <summary>The provider does not support the requested capability.</summary>
public sealed class ProviderUnsupportedException(string message) : ProviderException(message);
