using System.Data;
using System.Data.Common;
using System.Globalization;

namespace TechieRag.Persistence;

/// <summary>
/// Plain ADO.NET helpers for the relational stores: explicit parameters in, explicit readers out.
/// </summary>
/// <remarks>
/// <para><b>Why not Dapper (REQ-FN-056, MISS-TechieRag-20260924-09).</b> Dapper builds its
/// parameter and row mappers with Reflection.Emit. An ahead-of-time compiled app — every iOS and
/// Mac Catalyst Release build — has no Reflection.Emit, so the first store call threw
/// <see cref="PlatformNotSupportedException"/>. Everything here is ordinary code the AOT compiler
/// sees, and every store that can run on a phone or a Mac (the SQLite vector store and the
/// relational conversation and workspace stores) goes through it.</para>
/// <para>The helpers take any <see cref="DbConnection"/>, so the same code serves SQLite and
/// PostgreSQL. A parameter name carries no prefix; both providers match it to <c>@Name</c> in the
/// SQL, as Dapper's names did.</para>
/// </remarks>
internal static class DbCommandExtensions
{
    /// <summary>Runs a statement that returns no rows.</summary>
    /// <param name="connection">An open connection.</param>
    /// <param name="sql">The statement.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <param name="parameters">The statement's parameters.</param>
    /// <returns>The number of rows affected.</returns>
    internal static Task<int> ExecuteAsync(
        this DbConnection connection,
        string sql,
        CancellationToken cancellationToken,
        params DbParam[] parameters) =>
        connection.ExecuteAsync(sql, null, cancellationToken, parameters);

    /// <summary>Runs a statement that returns no rows, inside a transaction.</summary>
    /// <param name="connection">An open connection.</param>
    /// <param name="sql">The statement.</param>
    /// <param name="transaction">The transaction to take part in, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <param name="parameters">The statement's parameters.</param>
    /// <returns>The number of rows affected.</returns>
    internal static async Task<int> ExecuteAsync(
        this DbConnection connection,
        string sql,
        DbTransaction? transaction,
        CancellationToken cancellationToken,
        params DbParam[] parameters)
    {
        await using var command = CreateCommand(connection, sql, transaction, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs a query and returns the first column of the first row.</summary>
    /// <param name="connection">An open connection.</param>
    /// <param name="sql">The query.</param>
    /// <param name="transaction">The transaction to take part in, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <param name="parameters">The query's parameters.</param>
    /// <returns>The value, or <see langword="null"/> when there is no row or the value is SQL NULL.</returns>
    internal static async Task<object?> ScalarAsync(
        this DbConnection connection,
        string sql,
        DbTransaction? transaction,
        CancellationToken cancellationToken,
        params DbParam[] parameters)
    {
        await using var command = CreateCommand(connection, sql, transaction, parameters);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is DBNull ? null : value;
    }

    /// <summary>Runs a query and maps every row.</summary>
    /// <typeparam name="T">The mapped row type.</typeparam>
    /// <param name="connection">An open connection.</param>
    /// <param name="sql">The query.</param>
    /// <param name="map">Maps the reader's current row.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <param name="parameters">The query's parameters.</param>
    /// <returns>The mapped rows in reader order.</returns>
    internal static async Task<List<T>> QueryAsync<T>(
        this DbConnection connection,
        string sql,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken,
        params DbParam[] parameters)
    {
        await using var command = CreateCommand(connection, sql, null, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    /// <summary>Runs a query expected to return at most one row and maps it.</summary>
    /// <typeparam name="T">The mapped row type.</typeparam>
    /// <param name="connection">An open connection.</param>
    /// <param name="sql">The query.</param>
    /// <param name="map">Maps the reader's current row.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <param name="parameters">The query's parameters.</param>
    /// <returns>The mapped row, or <see langword="default"/> when there is none.</returns>
    /// <exception cref="InvalidOperationException">The query returned more than one row.</exception>
    internal static async Task<T?> QuerySingleOrDefaultAsync<T>(
        this DbConnection connection,
        string sql,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken,
        params DbParam[] parameters)
        where T : class
    {
        await using var command = CreateCommand(connection, sql, null, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var row = map(reader);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Sequence contains more than one element");
        }

        return row;
    }

    /// <summary>Reads a text column; SQL NULL becomes <see langword="null"/>.</summary>
    /// <param name="reader">The reader, positioned on a row.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <returns>The value as a string.</returns>
    internal static string? GetNullableString(this DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DBNull => null,
            string text => text,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    /// <summary>Reads an integer column of any width; SQL NULL becomes <see langword="null"/>.</summary>
    /// <param name="reader">The reader, positioned on a row.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <returns>The value widened to 64 bits.</returns>
    internal static long? GetNullableInt64(this DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value is DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    /// <summary>Reads a floating-point column of any width; SQL NULL becomes <see langword="null"/>.</summary>
    /// <param name="reader">The reader, positioned on a row.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <returns>The value as a double.</returns>
    internal static double? GetNullableDouble(this DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value is DBNull ? null : Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    /// <summary>Reads a BLOB column; SQL NULL becomes <see langword="null"/>.</summary>
    /// <param name="reader">The reader, positioned on a row.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <returns>The bytes.</returns>
    internal static byte[]? GetNullableBytes(this DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value as byte[];
    }

    private static DbCommand CreateCommand(
        DbConnection connection,
        string sql,
        DbTransaction? transaction,
        DbParam[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;

        foreach (var parameter in parameters)
        {
            var dbParameter = command.CreateParameter();
            dbParameter.ParameterName = parameter.Name;
            dbParameter.DbType = parameter.Type;
            dbParameter.Value = parameter.Value ?? DBNull.Value;
            command.Parameters.Add(dbParameter);
        }

        return command;
    }
}
