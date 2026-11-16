using AwesomeAssertions;
using Mcp.SqlServer.Services;

namespace Mcp.SqlServer.Tests.Services;

public class QueryGuardTests
{
    [Theory]
    [InlineData("SELECT * INTO x FROM y", "INTO")]
    [InlineData("SELECT 1 EXEC sp_who", "EXEC")]
    [InlineData("SELECT 1 EXECUTE sp_who", "EXECUTE")]
    [InlineData("WITH s AS (SELECT 1 AS id) MERGE t USING s ON t.id = s.id", "MERGE")]
    [InlineData("SELECT * FROM OPENROWSET('SQLNCLI', 'Server=x;Trusted_Connection=yes;', 'SELECT 1')", "OPENROWSET")]
    [InlineData("SELECT * FROM OPENQUERY(linked, 'SELECT 1')", "OPENQUERY")]
    [InlineData("SELECT 1 WAITFOR DELAY '00:00:10'", "WAITFOR")]
    [InlineData("SELECT 1 /* comment */ DROP TABLE x", "DROP")]
    public void Validate_ShouldRejectQuery_WhenQueryContainsForbiddenKeyword(string sql, string expectedKeyword)
    {
        // Arrange
        var forbiddenKeywords = SqlServerQueries.ForbiddenKeywords;

        // Act
        var error = QueryGuard.Validate(sql, forbiddenKeywords, out _);

        // Assert
        error.Should().Be($"Query contains forbidden keyword: {expectedKeyword}. Only read-only queries are allowed.");
    }

    [Theory]
    [InlineData("SELECT 1; DROP TABLE x")]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1 -- comment\n; SELECT 2")]
    public void Validate_ShouldRejectQuery_WhenQueryContainsMultipleStatements(string sql)
    {
        // Arrange
        var forbiddenKeywords = SqlServerQueries.ForbiddenKeywords;

        // Act
        var error = QueryGuard.Validate(sql, forbiddenKeywords, out _);

        // Assert
        error.Should().Be("Multiple statements are not allowed. Send a single query without ';'.");
    }

    [Theory]
    [InlineData("EXEC sp_who")]
    [InlineData("DELETE FROM x")]
    [InlineData("-- SELECT\nDELETE FROM x")]
    public void Validate_ShouldRejectQuery_WhenQueryDoesNotStartWithSelectOrWith(string sql)
    {
        // Arrange
        var forbiddenKeywords = SqlServerQueries.ForbiddenKeywords;

        // Act
        var error = QueryGuard.Validate(sql, forbiddenKeywords, out _);

        // Assert
        error.Should().Be("Only read-only queries starting with SELECT or WITH are allowed.");
    }

    [Theory]
    [InlineData("SELECT * FROM audit WHERE action = 'DELETE; EXEC sp_who'")]
    [InlineData("SELECT N'O''Brien; DROP TABLE x'")]
    [InlineData("SELECT [into], [exec], [merge] FROM [order]")]
    [InlineData("SELECT \"update\" FROM t")]
    [InlineData("SELECT 1 -- DROP TABLE x; EXEC sp_who")]
    [InlineData("SELECT 1 /* ; DELETE FROM x; */")]
    [InlineData("WITH cte AS (SELECT 1 AS id) SELECT id FROM cte;")]
    public void Validate_ShouldAcceptQuery_WhenForbiddenTextAppearsOnlyInsideStringsCommentsOrIdentifiers(string sql)
    {
        // Arrange
        var forbiddenKeywords = SqlServerQueries.ForbiddenKeywords;

        // Act
        var error = QueryGuard.Validate(sql, forbiddenKeywords, out _);

        // Assert
        error.Should().BeNull();
    }

    [Fact]
    public void Validate_ShouldRemoveCommentsFromNormalizedSql_WhenQueryContainsComments()
    {
        // Arrange
        const string sql = "SELECT 1 -- DELETE; DROP\n/* INSERT; */ , '--kept', [a--b] ;";

        // Act
        var error = QueryGuard.Validate(sql, SqlServerQueries.ForbiddenKeywords, out var normalizedSql);

        // Assert
        error.Should().BeNull();
        normalizedSql.Should().Be("SELECT 1  \n  , '--kept', [a--b]");
    }
}
