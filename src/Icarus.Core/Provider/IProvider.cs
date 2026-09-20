using System.Text.Json.Nodes;

namespace Icarus.Core.Provider;

/// <summary>
/// A chat + tool-calling backend. This is ICARUS's own seam; the MEAI adapter
/// (ICARUS-102) is the only implementation that names <c>Microsoft.Extensions.AI</c>.
/// Implementations must be safe to call from the runtime worker thread.
/// </summary>
public interface IProvider
{
    /// <summary>
    /// Run one completion. Streamed fragments are delivered to <paramref name="onDelta"/>
    /// in order; the returned task completes with the final response, the
    /// provider-reported input tokens (or <c>null</c>), and whether the
    /// generation was aborted.
    /// </summary>
    /// <param name="history">The canonical conversation history.</param>
    /// <param name="tools">JSON-Schema tool definitions offered to the model.</param>
    /// <param name="effortParams">Provider-flavoured thinking parameters (empty object = none).</param>
    /// <param name="cancellationToken">Per-session cancellation.</param>
    /// <param name="onDelta">Receives streamed text/thinking fragments.</param>
    Task<Completion> CompleteAsync(
        IReadOnlyList<Message> history,
        IReadOnlyList<ToolDefinition> tools,
        JsonNode effortParams,
        CancellationToken cancellationToken,
        Action<StreamDelta> onDelta);

    /// <summary>
    /// Discover the provider's available model ids. The default reports the
    /// capability as unsupported, so only providers that can list implement it.
    /// </summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<string>>(
            new ProviderUnsupportedException("provider does not support model listing"));
}
