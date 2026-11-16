namespace Mcp.SqlServer.Services;

public static class WriteSql
{
    public const string InsertedKeysVariable = "@mcp_inserted_keys";

    public static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";

    public static string ToText(string expression, WriteColumn column) => column.TypeName switch
    {
        "datetime" or "smalldatetime" => $"CONVERT(nvarchar(max), {expression}, 126)",
        "float" or "real" => $"CONVERT(nvarchar(max), {expression}, 3)",
        "money" or "smallmoney" => $"CONVERT(nvarchar(max), {expression}, 2)",
        "binary" or "varbinary" or "image" or "timestamp" => $"CONVERT(nvarchar(max), CONVERT(varbinary(max), {expression}), 1)",
        _ => $"CONVERT(nvarchar(max), {expression})"
    };

    public static string Literal(WriteColumn column, string? value)
    {
        if (value is null)
            return "NULL";

        return column.IsBinary
            ? $"CONVERT({column.Declaration}, '{value.Replace("'", "''")}', 1)"
            : $"CAST(N'{value.Replace("'", "''")}' AS {column.Declaration})";
    }

    public static string BeforeImageQuery(WriteTable table, WriteStatement statement, int limit)
    {
        var columns = string.Join(", ", table.ReadableColumns.Select(column => $"{ToText(Quote(column.Name), column)} AS {Quote(column.Name)}"));
        var order = string.Join(", ", table.KeyColumns.Select(column => Quote(column.Name)));
        return $"SELECT TOP ({limit}) {columns} FROM {statement.TableName} WHERE {statement.WhereClause} ORDER BY {order}";
    }

    public static string InsertWithReturnedKeys(WriteTable table, WriteStatement statement)
    {
        var declarations = string.Join(", ", table.KeyColumns.Select(column => $"{Quote(column.Name)} {column.Declaration}"));
        var outputColumns = string.Join(", ", table.KeyColumns.Select(column => $"INSERTED.{Quote(column.Name)}"));
        var keyText = string.Join(", ", table.KeyColumns.Select(column => ToText(Quote(column.Name), column)));
        var insert = $"{statement.Sql[..statement.InsertSourceIndex]}OUTPUT {outputColumns} INTO {InsertedKeysVariable} {statement.Sql[statement.InsertSourceIndex..]}";

        return $"DECLARE {InsertedKeysVariable} TABLE ({declarations});\n{insert};\nSELECT {keyText} FROM {InsertedKeysVariable};";
    }

    public static string InverseOfDelete(WriteTable table, IReadOnlyList<string?[]> beforeImage)
    {
        var columns = table.InsertableColumns;
        var columnList = string.Join(", ", columns.Select(column => Quote(column.Name)));
        var inserts = beforeImage.Select(row =>
        {
            var values = string.Join(", ", columns.Select(column => Literal(column, ValueOf(table, row, column))));
            return $"INSERT INTO {table.QualifiedName} ({columnList}) VALUES ({values});";
        });

        return columns.Any(column => column.IsIdentity)
            ? string.Join("\n", [$"SET IDENTITY_INSERT {table.QualifiedName} ON;", .. inserts, $"SET IDENTITY_INSERT {table.QualifiedName} OFF;"])
            : string.Join("\n", inserts);
    }

    public static string InverseOfUpdate(WriteTable table, IReadOnlyList<WriteColumn> targets, IReadOnlyList<string?[]> beforeImage) =>
        string.Join("\n", beforeImage.Select(row =>
        {
            var assignments = string.Join(", ", targets.Select(column => $"{Quote(column.Name)} = {Literal(column, ValueOf(table, row, column))}"));
            return $"UPDATE {table.QualifiedName} SET {assignments} WHERE {KeyPredicate(table, column => ValueOf(table, row, column))};";
        }));

    public static string InverseOfInsert(WriteTable table, IReadOnlyList<string?[]> insertedKeys) =>
        string.Join("\n", insertedKeys.Select(keys =>
            $"DELETE FROM {table.QualifiedName} WHERE {KeyPredicate(table, column => keys[IndexOf(table.KeyColumns, column)])};"));

    private static string KeyPredicate(WriteTable table, Func<WriteColumn, string?> valueOf) =>
        string.Join(" AND ", table.KeyColumns.Select(column => $"{Quote(column.Name)} = {Literal(column, valueOf(column))}"));

    private static string? ValueOf(WriteTable table, string?[] row, WriteColumn column) => row[IndexOf(table.ReadableColumns, column)];

    private static int IndexOf(IReadOnlyList<WriteColumn> columns, WriteColumn column)
    {
        for (var index = 0; index < columns.Count; index++)
        {
            if (columns[index] == column)
                return index;
        }

        throw new InvalidOperationException($"Column {column.Name} is not part of the expected column list.");
    }
}
