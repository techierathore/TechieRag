using System.Reflection;

namespace TechieRag.Tests.PublicApi;

/// <summary>
/// Finds public overloads that one positional call could bind to while they give that call's
/// arguments different meanings (Sevak TR-RAG-049).
/// </summary>
/// <remarks>
/// <para>1.1.2 added <c>WithPersistence(StoreProvider provider, string defaultUserId)</c> beside
/// <c>WithPersistence(StoreProvider provider, string connectionString, string defaultUserId = "default")</c>.
/// Every existing call <c>WithPersistence(provider, "Data Source=…")</c> recompiled against 1.1.2 bound to the
/// new overload, cleanly and silently, and its connection string became a user id.</para>
/// <para>The rule: two overloads of one name conflict when, for some argument count both accept, the
/// parameter types at those positions are identical and the parameter names differ. Same names mean the
/// same argument (an overload that adds a trailing optional is harmless); different names mean one call
/// shape carries two meanings, and which one a caller gets depends on which overload exists.</para>
/// <para>Shared by every package's test project (linked as a file), so each package checks its own assembly.</para>
/// </remarks>
internal static class OverloadShapeCheck
{
    /// <summary>Lists every conflicting overload pair in an assembly's public surface.</summary>
    /// <param name="assembly">The package assembly.</param>
    /// <returns>One line per conflict naming the type, both signatures and the argument count; empty when none.</returns>
    public static IReadOnlyList<string> FindConflicts(Assembly assembly)
    {
        var conflicts = new List<string>();
        foreach (var type in assembly.GetExportedTypes())
        {
            var members = type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Cast<MethodBase>()
                .Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
            foreach (var group in members.GroupBy(m => m.Name))
            {
                var overloads = group.ToList();
                for (var i = 0; i < overloads.Count; i++)
                {
                    for (var j = i + 1; j < overloads.Count; j++)
                    {
                        if (FindConflictingArity(overloads[i], overloads[j]) is { } arity)
                        {
                            conflicts.Add($"{type.FullName}: {Describe(overloads[i])} and {Describe(overloads[j])} both take {arity} positional argument(s) of the same types under different names");
                        }
                    }
                }
            }
        }

        return conflicts;
    }

    private static int? FindConflictingArity(MethodBase first, MethodBase second)
    {
        var a = first.GetParameters();
        var b = second.GetParameters();
        var from = Math.Max(RequiredCount(a), RequiredCount(b));
        var to = Math.Min(a.Length, b.Length);
        for (var arity = Math.Max(from, 1); arity <= to; arity++)
        {
            var sameTypes = Enumerable.Range(0, arity).All(k => a[k].ParameterType == b[k].ParameterType);
            var differentNames = Enumerable.Range(0, arity).Any(k => a[k].Name != b[k].Name);
            if (sameTypes && differentNames)
            {
                return arity;
            }
        }

        return null;
    }

    private static int RequiredCount(ParameterInfo[] parameters) =>
        parameters.Count(p => !p.IsOptional && !p.IsDefined(typeof(ParamArrayAttribute)));

    private static string Describe(MethodBase method) =>
        $"{method.Name}({string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})";
}
