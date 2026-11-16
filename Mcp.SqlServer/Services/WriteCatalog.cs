using Microsoft.Data.SqlClient;

namespace Mcp.SqlServer.Services;

public sealed record WriteColumn(string Name, string TypeName, string Declaration, bool IsComputed, bool IsIdentity)
{
    private static readonly HashSet<string> SupportedTypes =
    [
        "bigint", "int", "smallint", "tinyint", "bit", "decimal", "numeric", "money", "smallmoney", "float", "real",
        "date", "time", "datetime2", "datetimeoffset", "datetime", "smalldatetime",
        "char", "varchar", "nchar", "nvarchar", "text", "ntext", "sysname",
        "binary", "varbinary", "image", "timestamp", "uniqueidentifier", "xml"
    ];

    public bool IsRowVersion => TypeName == "timestamp";
    public bool IsSupported => SupportedTypes.Contains(TypeName);
    public bool IsInsertable => !IsComputed && !IsRowVersion;
    public bool IsBinary => TypeName is "binary" or "varbinary" or "image" or "timestamp";
}

public sealed record WriteTable(
    string Schema,
    string Name,
    IReadOnlyList<WriteColumn> Columns,
    IReadOnlyList<WriteColumn> KeyColumns,
    string KeyDescription)
{
    public string QualifiedName => $"{WriteSql.Quote(Schema)}.{WriteSql.Quote(Name)}";
    public string DisplayName => $"{Schema}.{Name}";
    public IReadOnlyList<WriteColumn> ReadableColumns => Columns.Where(column => column.IsSupported).ToList();
    public IReadOnlyList<WriteColumn> InsertableColumns => Columns.Where(column => column.IsInsertable).ToList();
}

public static class WriteCatalog
{
    private const string ResolveTableSql = """
        SELECT o.object_id, SCHEMA_NAME(o.schema_id), o.name, o.type
        FROM sys.objects o
        WHERE o.object_id = OBJECT_ID(@table)
        """;

    private const string ColumnsSql = """
        SELECT c.name, t.name, c.max_length, c.precision, c.scale, c.is_computed, c.is_identity, t.is_user_defined
        FROM sys.columns c
        JOIN sys.types t ON t.user_type_id = c.user_type_id
        WHERE c.object_id = @objectId
        ORDER BY c.column_id
        """;

    private const string UniqueKeySql = """
        SELECT TOP (1) i.is_primary_key, i.name, STRING_AGG(c.name, NCHAR(31)) WITHIN GROUP (ORDER BY ic.key_ordinal)
        FROM sys.indexes i
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = @objectId
          AND i.is_unique = 1
          AND i.has_filter = 0
          AND i.is_disabled = 0
          AND i.is_hypothetical = 0
        GROUP BY i.index_id, i.name, i.is_primary_key
        HAVING MAX(CAST(c.is_nullable AS int)) = 0
        ORDER BY i.is_primary_key DESC, COUNT(*), i.name
        """;

    public static async Task<(WriteTable? Table, string? Error)> ResolveAsync(SqlConnection connection, SqlTransaction transaction, string tableName)
    {
        int objectId;
        string schema;
        string name;

        await using (var command = new SqlCommand(ResolveTableSql, connection, transaction))
        {
            command.Parameters.AddWithValue("@table", tableName);
            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return (null, $"Table {tableName} was not found in the target database.");

            if (reader.GetString(3).Trim() != "U")
                return (null, $"{tableName} is not a table. ExecuteWrite only modifies tables.");

            objectId = reader.GetInt32(0);
            schema = reader.GetString(1);
            name = reader.GetString(2);
        }

        var columns = new List<WriteColumn>();
        await using (var command = new SqlCommand(ColumnsSql, connection, transaction))
        {
            command.Parameters.AddWithValue("@objectId", objectId);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var typeName = reader.GetBoolean(7) ? $"user-defined:{reader.GetString(1)}" : reader.GetString(1);
                var declaration = Declare(typeName, reader.GetInt16(2), reader.GetByte(3), reader.GetByte(4));
                columns.Add(new WriteColumn(reader.GetString(0), typeName, declaration, reader.GetBoolean(5), reader.GetBoolean(6)));
            }
        }

        await using (var command = new SqlCommand(UniqueKeySql, connection, transaction))
        {
            command.Parameters.AddWithValue("@objectId", objectId);
            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return (null, $"Table {schema}.{name} has no primary key and no unique index on NOT NULL columns, so the write could not be reverted reliably. Writes to this table are rejected.");

            var isPrimaryKey = reader.GetBoolean(0);
            var indexName = reader.GetString(1);
            var keyNames = reader.GetString(2).Split('\u001f');
            var keyColumns = keyNames.Select(keyName => columns.Single(column => column.Name == keyName)).ToList();
            var description = isPrimaryKey
                ? $"primary key ({string.Join(", ", keyNames)})"
                : $"unique index {indexName} ({string.Join(", ", keyNames)})";

            return (new WriteTable(schema, name, columns, keyColumns, description), null);
        }
    }

    private static string Declare(string typeName, short maxLength, byte precision, byte scale) => typeName switch
    {
        "char" or "varchar" or "binary" or "varbinary" => $"{typeName}({(maxLength == -1 ? "max" : maxLength.ToString())})",
        "nchar" or "nvarchar" => $"{typeName}({(maxLength == -1 ? "max" : (maxLength / 2).ToString())})",
        "decimal" or "numeric" => $"{typeName}({precision}, {scale})",
        "datetime2" or "time" or "datetimeoffset" => $"{typeName}({scale})",
        "timestamp" => "binary(8)",
        _ => typeName
    };
}
