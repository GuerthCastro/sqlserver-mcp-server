namespace Mcp.SqlServer.Services;

public static class SqlServerQueries
{
    public const string ListDatabases = """
        SELECT name
        FROM sys.databases
        WHERE name NOT IN ('master', 'tempdb', 'model', 'msdb')
        ORDER BY name
        """;

    public const string ListSchemas = """
        SELECT name
        FROM sys.schemas
        WHERE name NOT IN ('sys', 'INFORMATION_SCHEMA', 'guest', 'db_owner', 'db_accessadmin',
                           'db_securityadmin', 'db_ddladmin', 'db_backupoperator', 'db_datareader',
                           'db_datawriter', 'db_denydatareader', 'db_denydatawriter')
        ORDER BY name
        """;

    public const string ListTables = """
        SELECT TABLE_NAME
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA = @schema
          AND TABLE_TYPE = 'BASE TABLE'
        ORDER BY TABLE_NAME
        """;

    public const string DescribeTable = """
        SELECT
            c.COLUMN_NAME,
            c.DATA_TYPE,
            c.IS_NULLABLE,
            c.COLUMN_DEFAULT,
            CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 'YES' ELSE 'NO' END AS IS_PRIMARY_KEY,
            CASE WHEN fk.COLUMN_NAME IS NOT NULL THEN 'YES' ELSE 'NO' END AS IS_FOREIGN_KEY,
            fk.FOREIGN_TABLE_NAME,
            fk.FOREIGN_COLUMN_NAME
        FROM INFORMATION_SCHEMA.COLUMNS c
        LEFT JOIN (
            SELECT ku.COLUMN_NAME
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
            JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
            WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
              AND tc.TABLE_SCHEMA = @schema
              AND tc.TABLE_NAME = @table
        ) pk ON c.COLUMN_NAME = pk.COLUMN_NAME
        LEFT JOIN (
            SELECT
                ku.COLUMN_NAME,
                ccu.TABLE_NAME AS FOREIGN_TABLE_NAME,
                ccu.COLUMN_NAME AS FOREIGN_COLUMN_NAME
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
            JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
            JOIN INFORMATION_SCHEMA.CONSTRAINT_COLUMN_USAGE ccu
                ON tc.CONSTRAINT_NAME = ccu.CONSTRAINT_NAME
            WHERE tc.CONSTRAINT_TYPE = 'FOREIGN KEY'
              AND tc.TABLE_SCHEMA = @schema
              AND tc.TABLE_NAME = @table
        ) fk ON c.COLUMN_NAME = fk.COLUMN_NAME
        WHERE c.TABLE_SCHEMA = @schema
          AND c.TABLE_NAME = @table
        ORDER BY c.ORDINAL_POSITION
        """;

    public static readonly string[] ForbiddenKeywords = ["INSERT", "UPDATE", "DELETE", "DROP", "TRUNCATE", "ALTER", "CREATE", "GRANT", "REVOKE"];

    public const int MaxOutputCharacters = 50_000;
}