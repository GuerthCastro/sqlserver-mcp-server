using System.Text.RegularExpressions;

namespace Mcp.SqlServer.Services;

public enum WriteOperation
{
    Insert,
    Update,
    Delete
}

public sealed record WriteStatement(
    WriteOperation Operation,
    string Sql,
    string TableName,
    string? WhereClause,
    IReadOnlyList<string> SetTargets,
    int InsertSourceIndex = -1);

public static partial class WriteGuard
{
    private const string TablePattern = @"(?<table>(?:\[[^\]]*\]|""[^""]*""|[A-Z_][A-Z0-9_@$#]*)(?:\s*\.\s*(?:\[[^\]]*\]|""[^""]*""|[A-Z_][A-Z0-9_@$#]*))?)";

    private static readonly string[] AlwaysForbiddenKeywords = SqlServerQueries.ForbiddenKeywords
        .Except(["INSERT", "UPDATE", "DELETE", "INTO"])
        .ToArray();

    public static string? Analyze(string sql, out WriteStatement? statement)
    {
        statement = null;

        var normalizedSql = SqlText.RemoveComments(sql ?? string.Empty).Trim().TrimEnd(';').Trim();
        if (normalizedSql.Length == 0)
            return "Statement is empty.";

        var upperSql = SqlText.MaskQuotedTextAndComments(normalizedSql).ToUpperInvariant();
        var topLevelSql = SqlText.TopLevel(upperSql);

        if (upperSql.Contains(';'))
            return "Multiple statements are not allowed. Send a single INSERT, UPDATE or DELETE without ';'.";

        var operation = ReadOperation(upperSql);
        if (operation is null)
            return "ExecuteWrite only accepts a single INSERT, UPDATE or DELETE statement.";

        if (Output().IsMatch(upperSql))
            return "OUTPUT is not allowed. ExecuteWrite adds its own OUTPUT ... INTO clause to INSERT to capture the inserted keys.";

        var forbidden = QueryGuard.FindForbiddenKeyword(upperSql, AlwaysForbiddenKeywords);
        if (forbidden is not null)
            return $"Statement contains forbidden keyword: {forbidden}. ExecuteWrite only accepts a single INSERT, UPDATE or DELETE.";

        if (DataModifyingKeyword().Matches(upperSql).Count != 1)
            return "Only one INSERT, UPDATE or DELETE is allowed. Nested or combined data-modifying statements are not supported.";

        if (Into().Matches(upperSql).Count != (operation == WriteOperation.Insert ? 1 : 0))
            return "INTO is only allowed as part of INSERT INTO.";

        return operation switch
        {
            WriteOperation.Delete => AnalyzeDelete(normalizedSql, topLevelSql, out statement),
            WriteOperation.Update => AnalyzeUpdate(normalizedSql, topLevelSql, out statement),
            _ => AnalyzeInsert(normalizedSql, topLevelSql, out statement)
        };
    }

    public static string UnquoteIdentifier(string identifier)
    {
        var lastPart = IdentifierPart().Matches(identifier).LastOrDefault()?.Value ?? identifier.Trim();

        if (lastPart.StartsWith('['))
            return lastPart[1..^1].Replace("]]", "]");

        return lastPart.StartsWith('"')
            ? lastPart[1..^1].Replace("\"\"", "\"")
            : lastPart;
    }

    private static WriteOperation? ReadOperation(string upperSql) =>
        LeadingVerb().Match(upperSql).Groups["verb"].Value switch
        {
            "INSERT" => WriteOperation.Insert,
            "UPDATE" => WriteOperation.Update,
            "DELETE" => WriteOperation.Delete,
            _ => null
        };

    private static string? AnalyzeDelete(string normalizedSql, string topLevelSql, out WriteStatement? statement)
    {
        statement = null;

        if (!TopLevelWhere().IsMatch(topLevelSql))
            return "DELETE without WHERE is not allowed.";

        var match = DeleteStatement().Match(topLevelSql);
        if (!match.Success)
            return "Unsupported DELETE form. Use: DELETE FROM <table> WHERE <condition> (no alias, TOP, joins or table hints).";

        statement = new WriteStatement(
            WriteOperation.Delete,
            normalizedSql,
            Slice(normalizedSql, match.Groups["table"]),
            normalizedSql[match.Groups["where"].Index..],
            []);
        return null;
    }

    private static string? AnalyzeUpdate(string normalizedSql, string topLevelSql, out WriteStatement? statement)
    {
        statement = null;

        if (!TopLevelWhere().IsMatch(topLevelSql))
            return "UPDATE without WHERE is not allowed.";

        var match = UpdateStatement().Match(topLevelSql);
        if (!match.Success || TopLevelFrom().IsMatch(match.Groups["set"].Value))
            return "Unsupported UPDATE form. Use: UPDATE <table> SET <column> = <value>[, ...] WHERE <condition> (no alias, TOP, FROM, joins or table hints).";

        var targets = new List<string>();
        var set = match.Groups["set"];
        var start = set.Index;

        for (var position = set.Index; position <= set.Index + set.Length; position++)
        {
            if (position < set.Index + set.Length && topLevelSql[position] != ',')
                continue;

            var assignment = topLevelSql[start..position];
            var equals = assignment.IndexOf('=');
            var target = equals > 0 ? normalizedSql.Substring(start, equals).Trim().TrimEnd('+', '-', '*', '/', '%', '&', '|', '^').Trim() : string.Empty;

            if (target.Length == 0 || target.Contains('(') || target.StartsWith('@'))
                return "Unsupported SET assignment. Assign each column separately: SET <column> = <value>[, ...]. Variables and method calls such as .WRITE are not supported.";

            targets.Add(UnquoteIdentifier(target));
            start = position + 1;
        }

        statement = new WriteStatement(
            WriteOperation.Update,
            normalizedSql,
            Slice(normalizedSql, match.Groups["table"]),
            normalizedSql[match.Groups["where"].Index..],
            targets);
        return null;
    }

    private static string? AnalyzeInsert(string normalizedSql, string topLevelSql, out WriteStatement? statement)
    {
        statement = null;

        var match = InsertStatement().Match(topLevelSql);
        if (!match.Success)
            return "Unsupported INSERT form. Use: INSERT INTO <table> [(columns)] VALUES ... | SELECT ... | DEFAULT VALUES (no TOP, OUTPUT, EXEC or table hints).";

        statement = new WriteStatement(
            WriteOperation.Insert,
            normalizedSql,
            Slice(normalizedSql, match.Groups["table"]),
            null,
            [],
            match.Groups["source"].Index);
        return null;
    }

    private static string Slice(string sql, Group group) => sql.Substring(group.Index, group.Length);

    [GeneratedRegex(@"^(?<verb>INSERT|UPDATE|DELETE)\b")]
    private static partial Regex LeadingVerb();

    [GeneratedRegex(@"\b(?:INSERT|UPDATE|DELETE)\b")]
    private static partial Regex DataModifyingKeyword();

    [GeneratedRegex(@"\bOUTPUT\b")]
    private static partial Regex Output();

    [GeneratedRegex(@"\bINTO\b")]
    private static partial Regex Into();

    [GeneratedRegex(@"\bWHERE\b")]
    private static partial Regex TopLevelWhere();

    [GeneratedRegex(@"\bFROM\b")]
    private static partial Regex TopLevelFrom();

    [GeneratedRegex(@"^DELETE\s+FROM\s+" + TablePattern + @"\s+WHERE\s+(?<where>\S.*)$", RegexOptions.Singleline)]
    private static partial Regex DeleteStatement();

    [GeneratedRegex(@"^UPDATE\s+" + TablePattern + @"\s+SET\s+(?<set>\S.*?)\s+WHERE\s+(?<where>\S.*)$", RegexOptions.Singleline)]
    private static partial Regex UpdateStatement();

    [GeneratedRegex(@"^INSERT\s+INTO\s+" + TablePattern + @"\s+(?<source>VALUES|SELECT|DEFAULT\s+VALUES)\b", RegexOptions.Singleline)]
    private static partial Regex InsertStatement();

    [GeneratedRegex(@"\[(?:[^\]]|\]\])*\]|""(?:[^""]|"""")*""|[^\s.\[""]+")]
    private static partial Regex IdentifierPart();
}
