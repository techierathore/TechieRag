namespace TechieRag.Local.Tests.TestDoubles;

/// <summary>Builds a <see cref="LocalLlmProvider"/> over the scripted runtime and a host-supplied folder model.</summary>
internal static class FakeProviderFactory
{
    /// <summary>Creates a provider whose model lives in a folder that needs no download.</summary>
    /// <param name="runtime">The scripted runtime.</param>
    /// <param name="configure">Optional settings.</param>
    /// <param name="freeMemory">What the memory reading returns; null means unknown.</param>
    /// <param name="isPhone">Whether to behave as on Android or iOS.</param>
    /// <returns>The provider.</returns>
    public static LocalLlmProvider Create(
        FakeLocalLlmRuntime? runtime,
        Action<LocalLlmOptions>? configure = null,
        long? freeMemory = null,
        bool isPhone = false)
    {
        var options = new LocalLlmOptions
        {
            Model = LocalModel.FromFolder(Path.Combine(Path.GetTempPath(), "techierag-local-tests", "fake-model"), LocalChatTemplate.ChatMl)
        };
        configure?.Invoke(options);
        return new LocalLlmProvider(options, null, runtime, new MemoryGate(() => freeMemory), LocalModelStore.Shared, isPhone);
    }
}
