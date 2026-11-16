using System.Text;
using System.Text.RegularExpressions;

namespace Mcp.SqlServer.Services;

public static partial class SqlText
{
    public static MatchCollection Tokens(string sql) => Token().Matches(sql);

    public static string RemoveComments(string sql) =>
        Token().Replace(sql, match => match.Groups["comment"].Success ? " " : match.Value);

    public static string MaskQuotedTextAndComments(string sql) =>
        Token().Replace(sql, match => match.Groups["comment"].Success
            ? new string(' ', match.Length)
            : match.Length < 2
                ? match.Value
                : $"{match.Value[0]}{new string('_', match.Length - 2)}{match.Value[^1]}");

    public static string TopLevel(string maskedSql)
    {
        var topLevelSql = new StringBuilder(maskedSql.Length);
        var depth = 0;

        foreach (var character in maskedSql)
        {
            if (character == '(')
                depth++;
            else if (character == ')')
                depth = Math.Max(0, depth - 1);

            topLevelSql.Append(depth == 0 && character != ')' ? character : ' ');
        }

        return topLevelSql.ToString();
    }

    [GeneratedRegex("""(?<comment>--[^\r\n]*|/\*.*?\*/)|'(?:[^']|'')*'|"(?:[^"]|"")*"|\[[^\]]*\]""", RegexOptions.Singleline)]
    private static partial Regex Token();
}
