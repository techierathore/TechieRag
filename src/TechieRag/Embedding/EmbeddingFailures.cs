using TechieRag.Models;

namespace TechieRag.Embedding;

/// <summary>
/// Turns "the embedding service is not there" into <see cref="EmbeddingUnavailableException"/> for every HTTP
/// embedding provider (REQ-RAG-170, TrStudio TR-RAG-004).
/// </summary>
internal static class EmbeddingFailures
{
    /// <summary>Sends a request, translating a network failure or a timeout.</summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The request.</param>
    /// <param name="providerName">The provider's name, for the message.</param>
    /// <param name="address">The service's address, for the message; only scheme, host and port are shown.</param>
    /// <param name="cancellationToken">The caller's token; a cancellation it asked for stays a cancellation.</param>
    /// <returns>The response, whatever its status.</returns>
    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, string providerName, Uri? address, CancellationToken cancellationToken) =>
        GuardAsync(() => client.SendAsync(request, cancellationToken), providerName, address ?? request.RequestUri, cancellationToken);

    /// <summary>Runs a send, translating a network failure or a timeout.</summary>
    /// <param name="send">The send.</param>
    /// <param name="providerName">The provider's name, for the message.</param>
    /// <param name="address">The service's address, for the message; only scheme, host and port are shown.</param>
    /// <param name="cancellationToken">The caller's token; a cancellation it asked for stays a cancellation.</param>
    /// <returns>The response, whatever its status.</returns>
    public static async Task<HttpResponseMessage> GuardAsync(Func<Task<HttpResponseMessage>> send, string providerName, Uri? address, CancellationToken cancellationToken)
    {
        try
        {
            return await send().ConfigureAwait(false);
        }
        catch (Exception exception) when (Unavailable(exception, providerName, address, message: null, cancellationToken) is { } unavailable)
        {
            throw unavailable;
        }
    }

    /// <summary>
    /// Returns the exception to throw when a failure means the service could not be reached, or null when it does
    /// not (a status the service sent, the caller's own cancellation, or an exception already translated).
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <param name="providerName">The provider's name.</param>
    /// <param name="address">The service's address.</param>
    /// <param name="message">A message of the provider's own, or null for the standard one.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The translated exception, or null.</returns>
    public static EmbeddingUnavailableException? Unavailable(Exception exception, string providerName, Uri? address, string? message, CancellationToken cancellationToken)
    {
        var where = Describe(address);
        return exception switch
        {
            EmbeddingUnavailableException => null,
            HttpRequestException { StatusCode: null } network => new EmbeddingUnavailableException(
                providerName, message ?? $"The {providerName} embedding service at {where} cannot be reached: {network.Message}", network),
            TaskCanceledException timeout when !cancellationToken.IsCancellationRequested => new EmbeddingUnavailableException(
                providerName, message ?? $"The {providerName} embedding service at {where} did not answer in time.", timeout),
            _ => null
        };
    }

    /// <summary>Shows only scheme, host and port, so no user name, password, path or query reaches a message.</summary>
    private static string Describe(Uri? address) =>
        address is { IsAbsoluteUri: true } ? $"{address.Scheme}://{address.Host}:{address.Port}" : "its configured address";
}
