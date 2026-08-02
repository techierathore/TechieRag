using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TechieDesk.Services;
using TechieDesk.Services.Hosting;
using Xunit;

namespace TechieDesk.Tests.Startup;

/// <summary>
/// Regression coverage for REQ-FN-049 — the launch deadlock that presented zero windows on every
/// install that had ever saved LLM/RAG settings.
/// </summary>
/// <remarks>
/// <para>
/// The composition root used to call <c>ragManager.InitializeAsync().GetAwaiter().GetResult()</c> on
/// the UIKit launch delegate. <see cref="TechieRagManager"/> then awaited
/// <c>File.ReadAllTextAsync(techierag-config.json)</c> without <c>ConfigureAwait(false)</c>, so the
/// continuation was posted straight back to the single-threaded main-thread
/// <see cref="SynchronizationContext"/> that the blocking wait was occupying. Neither side could
/// move, <c>CreateWindow</c> was never reached, and the process ran forever with no UI.
/// </para>
/// <para>
/// It stayed invisible because the whole chain only runs when the config file EXISTS — with no file
/// <c>File.Exists</c> is false, that await never happens and startup completes. Every one of the
/// 1,622 tests in this suite ran against an empty data directory, so none of them ever entered the
/// deadlocking branch. That is exactly what these tests close: they seed a saved configuration and
/// then drive the manager from a genuine single-threaded UI-style context.
/// </para>
/// <para>
/// The assertion is COMPLETION, not success. Whether the vector store or the embedding provider can
/// actually be stood up on the test host is irrelevant to this defect — a thrown exception means the
/// call returned, which is the opposite of the bug. Only a wait that never ends fails these tests.
/// </para>
/// </remarks>
public sealed class StartupDeadlockTests
{
    /// <summary>How long a non-deadlocked initialization is allowed to take before it is a hang.</summary>
    /// <remarks>
    /// Generous on purpose. A cold sqlite-vec extension load is slow the first time; a deadlock is
    /// infinite. Anything between the two would be a different defect and should not be papered over
    /// by a tight budget here.
    /// </remarks>
    private static readonly TimeSpan CompletionBudget = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Proves the manager can be driven to completion from a UI-style synchronization context even
    /// when a blocking wait occupies that thread, with <c>techierag-config.json</c> present.
    /// </summary>
    /// <remarks>
    /// This is the exact shape of the old <c>MauiProgram.InitializeRagStore</c>. The composition root
    /// no longer blocks at all (that is the primary fix), so this test is defence in depth: it keeps
    /// the library side honest so re-introducing a blocking wait anywhere — a component, a helper
    /// process, the scheduler host — cannot resurrect the hang.
    /// </remarks>
    [Fact]
    public void RagInitializeCompletesWhenBlockedOnFromAUiThreadWithSavedConfig()
    {
        using var host = new ConfigEncryptionTestHost();
        SeedSavedConfig(host);

        using var manager = CreateManager(host);
        using var uiThread = new SingleThreadedContext();

        var completed = uiThread.Run(
            () => manager.InitializeAsync().GetAwaiter().GetResult(), CompletionBudget);

        Assert.True(completed,
            "TechieRagManager.InitializeAsync deadlocked when blocked on from a UI-style "
            + "synchronization context with techierag-config.json present (REQ-FN-049).");
    }

    /// <summary>
    /// Proves the same call path completes with no saved configuration — the case that always
    /// worked and must not regress.
    /// </summary>
    [Fact]
    public void RagInitializeCompletesOnAUiThreadWithNoSavedConfig()
    {
        using var host = new ConfigEncryptionTestHost();
        Assert.False(File.Exists(host.ConfigFilePath));

        using var manager = CreateManager(host);
        using var uiThread = new SingleThreadedContext();

        var completed = uiThread.Run(
            () => manager.InitializeAsync().GetAwaiter().GetResult(), CompletionBudget);

        Assert.True(completed,
            "TechieRagManager.InitializeAsync did not complete on a UI-style synchronization "
            + "context with no saved configuration (REQ-FN-049).");
    }

    /// <summary>
    /// Proves the awaited (non-blocking) path also completes with a saved configuration present,
    /// which is how the composition root now drives it.
    /// </summary>
    [Fact]
    public async Task RagInitializeCompletesAsynchronouslyWithSavedConfig()
    {
        using var host = new ConfigEncryptionTestHost();
        SeedSavedConfig(host);

        using var manager = CreateManager(host);

        var initialize = Task.Run(async () =>
        {
            try
            {
                await manager.InitializeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Provider availability is not what this test is about; see the class remarks.
            }
        });

        var finished = await Task.WhenAny(initialize, Task.Delay(CompletionBudget));

        Assert.Same(initialize, finished);
    }

    /// <summary>Builds a manager pointed entirely at the throwaway sandbox.</summary>
    /// <param name="host">The sandbox supplying the data directory and key ring.</param>
    /// <returns>A manager that touches nothing outside the sandbox.</returns>
    private static TechieRagManager CreateManager(ConfigEncryptionTestHost host)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TechieDeskDb.DataDirectory.ConfigKey] = host.DataDirectoryPath
            })
            .Build();

        return new TechieRagManager(
            new AppEnvironment(host.ContentRootPath),
            NullLoggerFactory.Instance,
            NullLogger<TechieRagManager>.Instance,
            host.CreateProvider(),
            configuration);
    }

    /// <summary>
    /// Writes a saved configuration matching the shape a real install produces once LLM/RAG settings
    /// have been saved — the precondition REQ-FN-049 needs and no other test ever created.
    /// </summary>
    /// <param name="host">The sandbox whose data directory receives the file.</param>
    /// <remarks>
    /// Written as literal JSON rather than serialized from <c>TechieRagConfig</c> so the file stays a
    /// fixture of what is on disk, independent of the model's defaults. The embedding source is
    /// Ollama and the LLM source is None so that building the instance stays local and cheap — the
    /// defect is in reading the file, not in what the file selects.
    /// </remarks>
    private static void SeedSavedConfig(ConfigEncryptionTestHost host)
    {
        var vectorDb = Path.Combine(host.DataDirectoryPath, TechieDeskDb.DataDirectory.VectorDbFileName);
        var json = $$"""
            {
              "embedding": {
                "source": 2,
                "endpoint": "http://127.0.0.1:11434",
                "apiKey": null,
                "model": "bge-m3",
                "dimensions": 1024,
                "requestDelayMs": 200
              },
              "vectorStore": {
                "type": 0,
                "connectionString": "Data Source={{vectorDb.Replace("\\", "\\\\")}}",
                "apiKey": null
              },
              "processing": {
                "defaultChunkSize": 500,
                "defaultChunkOverlap": 50,
                "chunkingStrategy": 0
              },
              "llm": { "source": 0, "model": "", "temperature": 0.7, "maxTokens": 2048 },
              "llmFallback": null,
              "usageTracking": { "enabled": true, "alertThreshold": 0.8 },
              "resilience": { "maxRetries": 3, "timeoutSeconds": 120 },
              "rerank": { "enabled": false, "source": 0, "candidateCount": 20 }
            }
            """;

        Directory.CreateDirectory(Path.GetDirectoryName(host.ConfigFilePath)!);
        File.WriteAllText(host.ConfigFilePath, json);
    }

    /// <summary>
    /// A single-threaded <see cref="SynchronizationContext"/> that behaves like a platform UI thread.
    /// </summary>
    /// <remarks>
    /// The essential property is that posted callbacks run ONLY while the thread is sitting in its
    /// message loop. A delegate that blocks — <c>GetAwaiter().GetResult()</c>, <c>.Result</c>,
    /// <c>.Wait()</c> — stops the pump, so any continuation posted back here while it blocks can
    /// never run. That is precisely the UIKit main thread during
    /// <c>application:willFinishLaunchingWithOptions:</c>, and reproducing it is the only way a
    /// net10.0 test project can cover a MAUI launch defect.
    /// </remarks>
    private sealed class SingleThreadedContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> queue = new();
        private readonly Thread thread;

        /// <summary>Starts the emulated UI thread and its message loop.</summary>
        public SingleThreadedContext()
        {
            // Background so a deadlocked run cannot hold the test host open after the assertion fails.
            thread = new Thread(Pump) { IsBackground = true, Name = "req-fn-049-ui-thread" };
            thread.Start();
        }

        /// <inheritdoc />
        public override void Post(SendOrPostCallback d, object? state) => Enqueue(d, state);

        /// <inheritdoc />
        public override void Send(SendOrPostCallback d, object? state) => Enqueue(d, state);

        /// <summary>Runs a delegate on the emulated UI thread and waits for it to finish.</summary>
        /// <param name="action">The work to run, exactly as the launch delegate would.</param>
        /// <param name="timeout">How long to wait before declaring the thread hung.</param>
        /// <returns>True when the delegate returned or threw; false when it never came back.</returns>
        public bool Run(Action action, TimeSpan timeout)
        {
            using var finished = new ManualResetEventSlim(false);
            Enqueue(_ =>
            {
                try
                {
                    action();
                }
                catch (Exception)
                {
                    // Completion is the assertion, not success — see the class remarks.
                }
                finally
                {
                    finished.Set();
                }
            }, null);

            return finished.Wait(timeout);
        }

        /// <summary>Stops the message loop.</summary>
        public void Dispose()
        {
            try
            {
                queue.CompleteAdding();
            }
            catch (ObjectDisposedException)
            {
                // Already torn down.
            }
        }

        private void Enqueue(SendOrPostCallback callback, object? state)
        {
            if (!queue.IsAddingCompleted)
            {
                queue.Add((callback, state));
            }
        }

        private void Pump()
        {
            SetSynchronizationContext(this);
            foreach (var work in queue.GetConsumingEnumerable())
            {
                work.Callback(work.State);
            }
        }
    }
}
