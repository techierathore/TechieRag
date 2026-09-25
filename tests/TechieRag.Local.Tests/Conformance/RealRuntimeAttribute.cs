namespace TechieRag.Local.Tests.Conformance;

/// <summary>
/// Marks a conformance subclass that runs a real engine and a real model, so its
/// <see cref="ConformanceFactAttribute"/> tests are gated like <c>LiveLocalLlmFactAttribute</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RealRuntimeAttribute : Attribute
{
}
