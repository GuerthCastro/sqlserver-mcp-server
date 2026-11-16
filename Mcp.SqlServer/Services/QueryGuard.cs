using System.Text.RegularExpressions;

namespace Mcp.SqlServer.Services;

public static partial class QueryGuard
{
    public static string? Validate(string sql, IReadOnlyCollection<string> forbiddenKeywords, out string normalizedSql)
    {
        normalizedSql = SqlText.RemoveComments(sql ?? string.Empty).Trim().TrimEnd(';').Trim();

        if (normalizedSql.Length == 0)
            return "Query is empty.";

        var upperSqlWithoutQuotedText = SqlText.MaskQuotedTextAndComments(normalizedSql).ToUpperInvariant();

        if (!upperSqlWithoutQuotedText.StartsWith("SELECT") && !upperSqlWithoutQuotedText.StartsWith("WITH"))
            return "Only read-only queries starting with SELECT or WITH are allowed.";

        if (upperSqlWithoutQuotedText.Contains(';'))
            return "Multiple statements are not allowed. Send a single query without ';'.";

        var forbidden = FindForbiddenKeyword(upperSqlWithoutQuotedText, forbiddenKeywords);

        return forbidden is null
            ? null
            : $"Query contains forbidden keyword: {forbidden}. Only read-only queries are allowed.";
    }

    public static string? FindForbiddenKeyword(string upperSqlWithoutQuotedText, IReadOnlyCollection<string> forbiddenKeywords) =>
        forbiddenKeywords.FirstOrDefault(keyword => Regex.IsMatch(upperSqlWithoutQuotedText, $@"\b{Regex.Escape(keyword)}\b"));

    public static string? ValidatePaging(string normalizedSql, int offset)
    {
        if (offset < 0)
            return "offset must be zero or greater.";

        if (offset > 0 && !HasTopLevelOrderBy(normalizedSql))
            return "offset > 0 requires an ORDER BY in the outer query. Without it the database may return rows in a different order on each call, so pages could overlap or skip rows. Add ORDER BY on a unique key, or use offset 0 with a keyset filter such as WHERE id > last_id ORDER BY id.";

        return null;
    }

    private static bool HasTopLevelOrderBy(string normalizedSql) =>
        TopLevelOrderBy().IsMatch(SqlText.TopLevel(SqlText.MaskQuotedTextAndComments(normalizedSql)));

    [GeneratedRegex(@"\bORDER\s+BY\b", RegexOptions.IgnoreCase)]
    private static partial Regex TopLevelOrderBy();
}
