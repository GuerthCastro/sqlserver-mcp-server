using AwesomeAssertions;
using Mcp.SqlServer.Services;

namespace Mcp.SqlServer.Tests.Services;

public class WriteSqlTests
{
    private static readonly WriteColumn Id = new("id", "int", "int", false, true);
    private static readonly WriteColumn Name = new("name", "nvarchar", "nvarchar(100)", false, false);
    private static readonly WriteColumn Created = new("created", "datetime", "datetime", false, false);
    private static readonly WriteColumn Data = new("data", "varbinary", "varbinary(16)", false, false);
    private static readonly WriteColumn Total = new("total", "decimal", "decimal(10, 2)", true, false);
    private static readonly WriteColumn Location = new("location", "geography", "geography", false, false);

    private static readonly WriteTable Items = new("dbo", "items", [Id, Name, Created, Data, Total, Location], [Id], "primary key (id)");

    [Fact]
    public void Literal_ShouldCastUnicodeTextToDeclaredType_WhenValueHasQuotes()
    {
        // Arrange
        const string value = "it's";

        // Act
        var literal = WriteSql.Literal(Name, value);

        // Assert
        literal.Should().Be("CAST(N'it''s' AS nvarchar(100))");
    }

    [Fact]
    public void Literal_ShouldConvertHexWithStyleOne_WhenColumnIsBinary()
    {
        // Arrange
        const string value = "0x00FF10";

        // Act
        var literal = WriteSql.Literal(Data, value);

        // Assert
        literal.Should().Be("CONVERT(varbinary(16), '0x00FF10', 1)");
    }

    [Theory]
    [InlineData("datetime", "CONVERT(nvarchar(max), [c], 126)")]
    [InlineData("float", "CONVERT(nvarchar(max), [c], 3)")]
    [InlineData("money", "CONVERT(nvarchar(max), [c], 2)")]
    [InlineData("varbinary", "CONVERT(nvarchar(max), CONVERT(varbinary(max), [c]), 1)")]
    [InlineData("datetime2", "CONVERT(nvarchar(max), [c])")]
    public void ToText_ShouldUseLosslessStyle_WhenTypeNeedsIt(string typeName, string expected)
    {
        // Arrange
        var column = new WriteColumn("c", typeName, typeName, false, false);

        // Act
        var expression = WriteSql.ToText("[c]", column);

        // Assert
        expression.Should().Be(expected);
    }

    [Fact]
    public void InverseOfDelete_ShouldWrapWithIdentityInsertAndSkipComputedColumns_WhenTableHasIdentity()
    {
        // Arrange
        var table = new WriteTable("dbo", "items", [Id, Name, Created, Data, Total], [Id], "primary key (id)");
        string?[] row = ["7", "it's", "2026-01-01T10:00:00.123", null, "1.00"];

        // Act
        var inverse = WriteSql.InverseOfDelete(table, [row]);

        // Assert
        inverse.Should().Be(
            "SET IDENTITY_INSERT [dbo].[items] ON;\n" +
            "INSERT INTO [dbo].[items] ([id], [name], [created], [data]) VALUES (CAST(N'7' AS int), CAST(N'it''s' AS nvarchar(100)), CAST(N'2026-01-01T10:00:00.123' AS datetime), NULL);\n" +
            "SET IDENTITY_INSERT [dbo].[items] OFF;");
    }

    [Fact]
    public void InverseOfUpdate_ShouldRestoreTargetsByKey_WhenRowsWereUpdated()
    {
        // Arrange
        string?[] row = ["3", "old", "2026-01-01T10:00:00", "0x01", "2.00"];

        // Act
        var inverse = WriteSql.InverseOfUpdate(Items, [Name, Data], [row]);

        // Assert
        inverse.Should().Be("UPDATE [dbo].[items] SET [name] = CAST(N'old' AS nvarchar(100)), [data] = CONVERT(varbinary(16), '0x01', 1) WHERE [id] = CAST(N'3' AS int);");
    }

    [Fact]
    public void InsertWithReturnedKeys_ShouldCaptureKeysWithOutputIntoTableVariable_WhenStatementIsInsert()
    {
        // Arrange
        const string sql = "INSERT INTO dbo.items (name) SELECT name FROM dbo.staging";
        var statement = new WriteStatement(WriteOperation.Insert, sql, "dbo.items", null, [], sql.IndexOf("SELECT", StringComparison.Ordinal));

        // Act
        var batch = WriteSql.InsertWithReturnedKeys(Items, statement);

        // Assert
        batch.Should().Be(
            "DECLARE @mcp_inserted_keys TABLE ([id] int);\n" +
            "INSERT INTO dbo.items (name) OUTPUT INSERTED.[id] INTO @mcp_inserted_keys SELECT name FROM dbo.staging;\n" +
            "SELECT CONVERT(nvarchar(max), [id]) FROM @mcp_inserted_keys;");
    }

    [Fact]
    public void BeforeImageQuery_ShouldSkipUnsupportedColumnsAndLimitRows_WhenStatementIsDelete()
    {
        // Arrange
        var statement = new WriteStatement(WriteOperation.Delete, "DELETE FROM dbo.items WHERE id < 3", "dbo.items", "id < 3", []);

        // Act
        var query = WriteSql.BeforeImageQuery(Items, statement, 51);

        // Assert
        query.Should().Be(
            "SELECT TOP (51) CONVERT(nvarchar(max), [id]) AS [id], CONVERT(nvarchar(max), [name]) AS [name], CONVERT(nvarchar(max), [created], 126) AS [created], " +
            "CONVERT(nvarchar(max), CONVERT(varbinary(max), [data]), 1) AS [data], CONVERT(nvarchar(max), [total]) AS [total] FROM dbo.items WHERE id < 3 ORDER BY [id]");
    }
}
