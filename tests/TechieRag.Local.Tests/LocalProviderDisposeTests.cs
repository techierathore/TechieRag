using System.Runtime.CompilerServices;
using TechieRag.Local.Runtime;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Local.Tests;

/// <summary>
/// Disposing a <see cref="LocalLlmProvider"/> while calls are inside it (REQ-RAG-129, Sevak TR-RAG-053):
/// every call ends with <see cref="ObjectDisposedException"/>, and the model is freed once, after the last
/// call has left, never under a running token.
/// </summary>
public sealed class LocalProviderDisposeTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// One call is in the middle of a token and two wait in the queue when the provider is disposed. The
    /// two waiting calls end at once with <see cref="ObjectDisposedException"/>; the model stays alive until
    /// the running token finishes; that call then ends the same way; the model is freed exactly once, nothing
    /// touches it afterwards, and <see cref="LocalLlmProvider.DisposeAsync"/> completes.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-129 DisposeWithTwoQueuedCallsEndsAllWithObjectDisposed")]
    public async Task DisposeWithTwoQueuedCallsEndsAllWithObjectDisposed()
    {
        var runtime = new GatedRuntime();
        var provider = Create(runtime);

        var running = provider.ChatAsync([ChatMessage.User("first")]);
        await runtime.TokenStarted.Task.WaitAsync(Wait);
        var queuedOne = provider.ChatAsync([ChatMessage.User("second")]);
        var queuedTwo = provider.ChatAsync([ChatMessage.User("third")]);
        await WaitUntilAsync(() => provider.ActiveCalls == 3);

        provider.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => queuedOne.WaitAsync(Wait));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => queuedTwo.WaitAsync(Wait));
        Assert.Equal(0, runtime.Model!.DisposeCount);
        Assert.False(running.IsCompleted);

        runtime.FinishToken.SetResult();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => running.WaitAsync(Wait));
        await provider.DisposeAsync().AsTask().WaitAsync(Wait);
        Assert.Equal(1, runtime.Model.DisposeCount);
        Assert.Equal(1, runtime.Model.GenerateCount);
        Assert.False(runtime.Model.UsedAfterDispose);
        Assert.Equal(0, provider.ActiveCalls);
    }

    /// <summary>
    /// After the dispose, every entry point refuses with <see cref="ObjectDisposedException"/> without
    /// touching the model, and a second dispose does nothing.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-129 CallsAfterDisposeAreRefusedBeforeTheModel")]
    public async Task CallsAfterDisposeAreRefusedBeforeTheModel()
    {
        var runtime = new GatedRuntime { Gated = false };
        var provider = Create(runtime);
        await provider.ChatAsync([ChatMessage.User("warm up")]);

        await provider.DisposeAsync();
        provider.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => provider.ChatAsync([ChatMessage.User("again")]));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => provider.LoadAsync());
        Assert.Throws<ObjectDisposedException>(() => provider.EstimateTokenCount("count me"));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await foreach (var _ in provider.ChatStreamEventsAsync([ChatMessage.User("stream")]))
            {
            }
        });
        Assert.Equal(1, runtime.Model!.DisposeCount);
        Assert.False(runtime.Model.UsedAfterDispose);
    }

    /// <summary>
    /// A host that disposes the provider while it is suspended between two streamed pieces gets
    /// <see cref="ObjectDisposedException"/> on its next read; the model is freed when the stream is left.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-129 StreamResumedAfterDisposeThrowsObjectDisposed")]
    public async Task StreamResumedAfterDisposeThrowsObjectDisposed()
    {
        var runtime = new GatedRuntime { Gated = false };
        var provider = Create(runtime);
        var seen = 0;

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await foreach (var streamEvent in provider.ChatStreamEventsAsync([ChatMessage.User("stream")]))
            {
                seen++;
                provider.Dispose();
                Assert.Equal(0, runtime.Model!.DisposeCount);
            }
        });

        Assert.Equal(1, seen);
        Assert.Equal(1, runtime.Model!.DisposeCount);
        Assert.False(runtime.Model.UsedAfterDispose);
    }

    /// <summary>
    /// A dispose during the load lets the load finish off-thread, then the call ends with
    /// <see cref="ObjectDisposedException"/> and the model it loaded is freed, never kept.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-129 DisposeDuringLoadFreesTheLoadedModel")]
    public async Task DisposeDuringLoadFreesTheLoadedModel()
    {
        var runtime = new GatedRuntime { Gated = false, HoldLoad = new ManualResetEventSlim(false) };
        var provider = Create(runtime);

        var call = provider.ChatAsync([ChatMessage.User("first")]);
        await runtime.LoadStarted.Task.WaitAsync(Wait);
        provider.Dispose();
        runtime.HoldLoad.Set();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => call.WaitAsync(Wait));
        await provider.DisposeAsync().AsTask().WaitAsync(Wait);
        Assert.Equal(1, runtime.Model!.DisposeCount);
        Assert.Equal(0, runtime.Model.GenerateCount);
    }

    private static LocalLlmProvider Create(GatedRuntime runtime) =>
        new(
            new LocalLlmOptions { Model = LocalModel.FromFolder(Path.Combine(Path.GetTempPath(), "techierag-local-tests", "gated-model"), LocalChatTemplate.ChatMl) },
            null,
            runtime,
            new MemoryGate(() => null),
            LocalModelStore.Shared,
            isPhone: false);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The calls did not reach the provider in time.");
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// A runtime whose model can hold a token in flight (as a native step that cannot be interrupted) and
    /// whose load can be held, recording any use of the model after it was disposed.
    /// </summary>
    private sealed class GatedRuntime : ILocalLlmRuntime
    {
        /// <summary>Gets or sets whether the first token waits for <see cref="FinishToken"/>.</summary>
        public bool Gated { get; init; } = true;

        /// <summary>Gets or sets a gate the load waits on; null loads at once.</summary>
        public ManualResetEventSlim? HoldLoad { get; init; }

        /// <summary>Gets a signal set when the first token starts.</summary>
        public TaskCompletionSource TokenStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Gets the gate the first token waits on; it ignores cancellation, as a native step does.</summary>
        public TaskCompletionSource FinishToken { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Gets a signal set when the load starts.</summary>
        public TaskCompletionSource LoadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Gets the model loaded, once loaded.</summary>
        public GatedModel? Model { get; private set; }

        /// <inheritdoc/>
        public string Name => "gated";

        /// <inheritdoc/>
        public LocalModelFormat Format => LocalModelFormat.Test;

        /// <inheritdoc/>
        public ILocalTokenizer LoadTokenizer(string modelDirectory) => throw new NotSupportedException();

        /// <inheritdoc/>
        public ILocalLlmModel Load(string modelDirectory, int contextSize, int? threads)
        {
            LoadStarted.TrySetResult();
            HoldLoad?.Wait(Wait);
            Model = new GatedModel(this);
            return Model;
        }
    }

    /// <summary>The model a <see cref="GatedRuntime"/> loads.</summary>
    private sealed class GatedModel(GatedRuntime runtime) : ILocalLlmModel
    {
        private volatile bool disposed;

        /// <summary>Gets how many times the model was disposed.</summary>
        public int DisposeCount { get; private set; }

        /// <summary>Gets how many generations started.</summary>
        public int GenerateCount { get; private set; }

        /// <summary>Gets whether any member ran after the model was disposed (a use-after-free in a native model).</summary>
        public bool UsedAfterDispose { get; private set; }

        /// <inheritdoc/>
        public int CountTokens(string text)
        {
            Touch();
            return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<string> GenerateAsync(
            string prompt,
            LocalGenerationSettings settings,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Touch();
            GenerateCount++;
            for (var i = 0; i < 3; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (i == 0 && runtime.Gated)
                {
                    runtime.TokenStarted.TrySetResult();
                    await runtime.FinishToken.Task;
                }
                else
                {
                    await Task.Yield();
                }

                Touch();
                yield return $"piece{i} ";
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            DisposeCount++;
            disposed = true;
        }

        private void Touch()
        {
            if (disposed)
            {
                UsedAfterDispose = true;
            }
        }
    }
}
