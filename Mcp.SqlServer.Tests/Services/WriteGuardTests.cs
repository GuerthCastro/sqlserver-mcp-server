using AwesomeAssertions;
using Mcp.SqlServer.Services;

namespace Mcp.SqlServer.Tests.Services;

public class WriteGuardTests
{
    [Theory]
    [InlineData("UPDATE dbo.items SET price = 0", "UPDATE without WHERE is not allowed.")]
    [InlineData("UPDATE dbo.items SET note = N'WHERE id = 1'", "UPDATE without WHERE is not allowed.")]
    [InlineData("DELETE FROM dbo.items", "DELETE without WHERE is not allowed.")]
    [InlineData("DELETE FROM dbo.items -- WHERE id = 1", "DELETE without WHERE is not allowed.")]
    [InlineData("UPDATE dbo.items SET price = (SELECT MAX(price) FROM dbo.items WHERE id = 1)", "UPDATE without WHERE is not allowed.")]
    public void Analyze_ShouldRejectStatement_WhenUpdateOrDeleteHasNoTopLevelWhere(string sql, string expectedError)
    {
        // Arrange
        var statementSql = sql;

        // Act
        var error = WriteGuard.Analyze(statementSql, out _);

        // Assert
        error.Should().Be(expectedError);
    }

    [Theory]
    [InlineData("DROP TABLE dbo.items", "ExecuteWrite only accepts a single INSERT, UPDATE or DELETE statement.")]
    [InlineData("TRUNCATE TABLE dbo.items", "ExecuteWrite only accepts a single INSERT, UPDATE or DELETE statement.")]
    [InlineData("CREATE LOGIN intruder WITH PASSWORD = 'x'", "ExecuteWrite only accepts a single INSERT, UPDATE or DELETE statement.")]
    [InlineData("ALTER LOGIN sa ENABLE", "ExecuteWrite only accepts a single INSERT, UPDATE or DELETE statement.")]
    [InlineData("GRANT CONTROL ON dbo.items TO public", "ExecuteWrite only accepts a single INSERT, UPDATE or DELETE statement.")]
    [InlineData("EXEC sp_addlogin 'intruder'", "ExecuteWrite only accepts a single INSERT, UPDATE or DELETE statement.")]
    [InlineData("DELETE FROM dbo.items WHERE id = 1; DROP TABLE dbo.items", "Multiple statements are not allowed. Send a single INSERT, UPDATE or DELETE without ';'.")]
    [InlineData("DELETE FROM dbo.items WHERE id = 1 EXEC sp_who", "Statement contains forbidden keyword: EXEC. ExecuteWrite only accepts a single INSERT, UPDATE or DELETE.")]
    [InlineData("INSERT INTO dbo.items (name) OUTPUT INSERTED.id VALUES (N'x')", "OUTPUT is not allowed. ExecuteWrite adds its own OUTPUT ... INTO clause to INSERT to capture the inserted keys.")]
    [InlineData("UPDATE dbo.items SET price = 1 OUTPUT DELETED.price WHERE id = 1", "OUTPUT is not allowed. ExecuteWrite adds its own OUTPUT ... INTO clause to INSERT to capture the inserted keys.")]
    [InlineData("INSERT INTO dbo.items (name) EXEC dbo.load_items", "Statement contains forbidden keyword: EXEC. ExecuteWrite only accepts a single INSERT, UPDATE or DELETE.")]
    [InlineData("MERGE dbo.items AS t USING dbo.staging AS s ON t.id = s.id WHEN MATCHED THEN DELETE", "ExecuteWrite only accepts a single INSERT, UPDATE or DELETE statement.")]
    [InlineData("UPDATE TOP (5) dbo.items SET price = 1 WHERE id > 0", "Unsupported UPDATE form. Use: UPDATE <table> SET <column> = <value>[, ...] WHERE <condition> (no alias, TOP, FROM, joins or table hints).")]
    [InlineData("UPDATE dbo.items WITH (ROWLOCK) SET price = 1 WHERE id = 1", "Unsupported UPDATE form. Use: UPDATE <table> SET <column> = <value>[, ...] WHERE <condition> (no alias, TOP, FROM, joins or table hints).")]
    [InlineData("UPDATE i SET price = 1 FROM dbo.items i JOIN dbo.prices p ON p.id = i.id WHERE p.id = 1", "Unsupported UPDATE form. Use: UPDATE <table> SET <column> = <value>[, ...] WHERE <condition> (no alias, TOP, FROM, joins or table hints).")]
    [InlineData("UPDATE otherdb.dbo.items SET price = 1 WHERE id = 1", "Unsupported UPDATE form. Use: UPDATE <table> SET <column> = <value>[, ...] WHERE <condition> (no alias, TOP, FROM, joins or table hints).")]
    [InlineData("DELETE TOP (5) FROM dbo.items WHERE id > 0", "Unsupported DELETE form. Use: DELETE FROM <table> WHERE <condition> (no alias, TOP, joins or table hints).")]
    [InlineData("DELETE FROM dbo.items FROM dbo.items JOIN dbo.prices ON prices.id = items.id WHERE prices.id = 1", "Unsupported DELETE form. Use: DELETE FROM <table> WHERE <condition> (no alias, TOP, joins or table hints).")]
    [InlineData("INSERT INTO dbo.items WITH (TABLOCK) (name) VALUES (N'x')", "Unsupported INSERT form. Use: INSERT INTO <table> [(columns)] VALUES ... | SELECT ... | DEFAULT VALUES (no TOP, OUTPUT, EXEC or table hints).")]
    [InlineData("UPDATE dbo.items SET @total = price WHERE id = 1", "Unsupported SET assignment. Assign each column separately: SET <column> = <value>[, ...]. Variables and method calls such as .WRITE are not supported.")]
    [InlineData("UPDATE dbo.items SET note.WRITE(N'x', 0, 1) WHERE id = 1", "Unsupported SET assignment. Assign each column separately: SET <column> = <value>[, ...]. Variables and method calls such as .WRITE are not supported.")]
    [InlineData("SELECT * INTO dbo.copy FROM dbo.items", "ExecuteWrite only accepts a single INSERT, UPDATE or DELETE statement.")]
    public void Analyze_ShouldRejectStatement_WhenStatementIsNotASupportedSingleWrite(string sql, string expectedError)
    {
        // Arrange
        var statementSql = sql;

        // Act
        var error = WriteGuard.Analyze(statementSql, out var statement);

        // Assert
        error.Should().Be(expectedError);
        statement.Should().BeNull();
    }

    [Fact]
    public void Analyze_ShouldExtractTableWhereAndTargets_WhenUpdateIsValid()
    {
        // Arrange
        const string sql = "UPDATE [dbo].[Order Items] SET price += 2, [Note] = N'a, b = c' WHERE id IN (SELECT id FROM t WHERE x = 'WHERE') -- comment";

        // Act
        var error = WriteGuard.Analyze(sql, out var statement);

        // Assert
        error.Should().BeNull();
        statement!.Operation.Should().Be(WriteOperation.Update);
        statement.TableName.Should().Be("[dbo].[Order Items]");
        statement.SetTargets.Should().Equal("price", "Note");
        statement.WhereClause.Should().Be("id IN (SELECT id FROM t WHERE x = 'WHERE')");
    }

    [Theory]
    [InlineData("INSERT INTO dbo.items (name, price) VALUES (N'a', 1), (N'b', 2)", "VALUES (N'a', 1), (N'b', 2)")]
    [InlineData("INSERT INTO dbo.items(name) SELECT name FROM dbo.staging WHERE ready = 1", "SELECT name FROM dbo.staging WHERE ready = 1")]
    [InlineData("INSERT INTO [dbo].[audited] DEFAULT VALUES", "DEFAULT VALUES")]
    public void Analyze_ShouldLocateInsertSource_WhenInsertUsesSupportedForm(string sql, string expectedSource)
    {
        // Arrange
        var statementSql = sql;

        // Act
        var error = WriteGuard.Analyze(statementSql, out var statement);

        // Assert
        error.Should().BeNull();
        statement!.Operation.Should().Be(WriteOperation.Insert);
        statement.Sql[statement.InsertSourceIndex..].Should().Be(expectedSource);
    }

    [Theory]
    [InlineData("Price", "Price")]
    [InlineData("[Order Total]", "Order Total")]
    [InlineData("dbo.items.[a]]b]", "a]b")]
    [InlineData("\"Note\"", "Note")]
    public void UnquoteIdentifier_ShouldUnquoteBracketsAndDoubleQuotes_WhenIdentifierIsQuoted(string identifier, string expected)
    {
        // Arrange
        var input = identifier;

        // Act
        var name = WriteGuard.UnquoteIdentifier(input);

        // Assert
        name.Should().Be(expected);
    }
}
