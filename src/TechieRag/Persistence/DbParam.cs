using System.Data;

namespace TechieRag.Persistence;

/// <summary>
/// One explicitly typed command parameter (REQ-FN-056).
/// </summary>
/// <param name="Name">The parameter name without a prefix; the SQL refers to it as <c>@Name</c>.</param>
/// <param name="Value">The value; <see langword="null"/> is sent as SQL NULL.</param>
/// <param name="Type">The database type, the one Dapper inferred from the same CLR value.</param>
internal readonly record struct DbParam(string Name, object? Value, DbType Type)
{
    /// <summary>A text parameter.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value.</param>
    /// <returns>The parameter.</returns>
    internal static DbParam Text(string name, string? value) => new(name, value, DbType.String);

    /// <summary>A 32-bit integer parameter.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value.</param>
    /// <returns>The parameter.</returns>
    internal static DbParam Int32(string name, int? value) => new(name, value, DbType.Int32);

    /// <summary>A 64-bit integer parameter.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value.</param>
    /// <returns>The parameter.</returns>
    internal static DbParam Int64(string name, long? value) => new(name, value, DbType.Int64);

    /// <summary>A double-precision parameter.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value.</param>
    /// <returns>The parameter.</returns>
    internal static DbParam Double(string name, double? value) => new(name, value, DbType.Double);

    /// <summary>A binary parameter.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value.</param>
    /// <returns>The parameter.</returns>
    internal static DbParam Blob(string name, byte[]? value) => new(name, value, DbType.Binary);
}
