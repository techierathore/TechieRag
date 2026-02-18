using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Services;

/// <summary>
/// Wraps primary and fallback ILlmProviders with automatic failover.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Routes LLM requests to the primary provider. If the primary
/// fails, automatically switches to the fallback provider.</para>
/// <para><b>Code Flow:</b> Try primary -> if exception -> log warning -> try fallback -> return result.</para>
/// </remarks>
public class FallbackLlmHandler : ILlmProvider
{
    private readonly ILlmProvider primary;
    private readonly ILlmProvider fallback;
    private readonly ILogger<FallbackLlmHandler> logger;
    private bool usingFallback;

    /// <inheritdoc/>
    public string Name => usingFallback ? $"{fallback.Name} (fallback)" : primary.Name;

    /// <inheritdoc/>
    public string ModelName => usingFallback ? fallback.ModelName : primary.ModelName;

    /// <inheritdoc/>
    public bool SupportsToolCalling => usingFallback ? fallback.SupportsToolCalling : primary.SupportsToolCalling;

    /// <inheritdoc/>
    public bool SupportsStreaming => usingFallback ? fallback.SupportsStreaming : primary.SupportsStreaming;

    /// <inheritdoc/>
    public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted
    {
        add
        {
            primary.OnCompletionCompleted += value;
            fallback.OnCompletionCompleted += value;
        }
        remove
        {
            primary.OnCompletionCompleted -= value;
            fallback.OnCompletionCompleted -= value;
        }
    }

    /// <summary>
    /// Creates a new fallback LLM handler.
    /// </summary>
    /// <param name="primary">The primary LLM provider.</param>
    /// <param name="fallback">The fallback LLM provider.</param>
    /// <param name="logger">Logger instance.</param>
    public FallbackLlmHandler(ILlmProvider primary, ILlmProvider fallback, ILogger<FallbackLlmHandler>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(fallback);

        this.primary = primary;
        this.fallback = fallback;
        this.logger = logger ?? NullLogger<FallbackLlmHandler>.Instance;
    }

    /// <inheritdoc/>
    public async Task<LlmResponse> CompleteAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            usingFallback = false;
            return await primary.CompleteAsync(prompt, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Primary LLM provider ({Provider}) failed. Falling back to {Fallback}",
                primary.Name, fallback.Name);
            usingFallback = true;
            return await fallback.CompleteAsync(prompt, options, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<string> CompleteStreamAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        return StreamWithFallbackAsync(
            () => primary.CompleteStreamAsync(prompt, options, cancellationToken),
            () => fallback.CompleteStreamAsync(prompt, options, cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<LlmResponse> ChatAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            usingFallback = false;
            return await primary.ChatAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Primary LLM provider ({Provider}) failed for chat. Falling back to {Fallback}",
                primary.Name, fallback.Name);
            usingFallback = true;
            return await fallback.ChatAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<string> ChatStreamAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        return StreamWithFallbackAsync(
            () => primary.ChatStreamAsync(messages, options, cancellationToken),
            () => fallback.ChatStreamAsync(messages, options, cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<T> CompleteAsync<T>(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            usingFallback = false;
            return await primary.CompleteAsync<T>(prompt, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Primary LLM provider failed for typed completion. Falling back to {Fallback}", fallback.Name);
            usingFallback = true;
            return await fallback.CompleteAsync<T>(prompt, options, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public int EstimateTokenCount(string text) => primary.EstimateTokenCount(text);

    /// <summary>
    /// Uses a Channel to bridge the async enumerable through try-catch, since
    /// yield return is not allowed in try-catch blocks in C#.
    /// </summary>
    private async IAsyncEnumerable<string> StreamWithFallbackAsync(
        Func<IAsyncEnumerable<string>> primaryFactory,
        Func<IAsyncEnumerable<string>> fallbackFactory,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<string>();
        var writerTask = Task.Run(async () =>
        {
            try
            {
                usingFallback = false;
                await foreach (var token in primaryFactory().WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    await channel.Writer.WriteAsync(token, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Primary streaming failed. Falling back to {Fallback}", fallback.Name);
                usingFallback = true;
                await foreach (var token in fallbackFactory().WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    await channel.Writer.WriteAsync(token, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, cancellationToken);

        await foreach (var token in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return token;
        }

        await writerTask.ConfigureAwait(false);
    }
}
