using AwesomeAssertions;
using Mcp.SqlServer.Services;

namespace Mcp.SqlServer.Tests.Services;

public class QueryGuardPagingTests
{
    private const string MissingOrderByError =
        "offset > 0 requires an ORDER BY in the outer query. Without it the database may return rows in a different order on each call, so pages could overlap or skip rows. Add ORDER BY on a unique key, or use offset 0 with a keyset filter such as WHERE id > last_id ORDER BY id.";

    [Theory]
    [InlineData("SELECT id FROM t")]
    [InlineData("SELECT ROW_NUMBER() OVER (ORDER BY id) AS n FROM t")]
    [InlineData("SELECT id FROM (SELECT id FROM t ORDER BY id) AS s")]
    [InlineData("SELECT 'ORDER BY id' AS label FROM t")]
    [InlineData("SELECT id FROM t -- ORDER BY id")]
    [InlineData("SELECT id FROM t /* ORDER BY id */")]
    public void ValidatePaging_ShouldRejectQuery_WhenOffsetIsPositiveAndOuterQueryHasNoOrderBy(string sql)
    {
        // Arrange
        QueryGuard.Validate(sql, SqlServerQueries.ForbiddenKeywords, out var normalizedSql).Should().BeNull();

        // Act
        var error = QueryGuard.ValidatePaging(normalizedSql, offset: 200);

        // Assert
        error.Should().Be(MissingOrderByError);
    }

    [Theory]
    [InlineData("SELECT id FROM t ORDER BY id")]
    [InlineData("select id from t order\n  by id desc")]
    [InlineData("WITH c AS (SELECT id FROM t) SELECT id FROM c ORDER BY id")]
    [InlineData("SELECT id, (SELECT max(x) FROM u) AS m FROM t ORDER BY id")]
    public void ValidatePaging_ShouldAcceptQuery_WhenOffsetIsPositiveAndOuterQueryHasOrderBy(string sql)
    {
        // Arrange
        QueryGuard.Validate(sql, SqlServerQueries.ForbiddenKeywords, out var normalizedSql).Should().BeNull();

        // Act
        var error = QueryGuard.ValidatePaging(normalizedSql, offset: 200);

        // Assert
        error.Should().BeNull();
    }

    [Fact]
    public void ValidatePaging_ShouldAcceptQueryWithoutOrderBy_WhenOffsetIsZero()
    {
        // Arrange
        const string normalizedSql = "SELECT id FROM t";

        // Act
        var error = QueryGuard.ValidatePaging(normalizedSql, offset: 0);

        // Assert
        error.Should().BeNull();
    }

    [Fact]
    public void ValidatePaging_ShouldRejectQuery_WhenOffsetIsNegative()
    {
        // Arrange
        const string normalizedSql = "SELECT id FROM t ORDER BY id";

        // Act
        var error = QueryGuard.ValidatePaging(normalizedSql, offset: -1);

        // Assert
        error.Should().Be("offset must be zero or greater.");
    }
}
