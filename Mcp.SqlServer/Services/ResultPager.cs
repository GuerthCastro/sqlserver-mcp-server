using System.Data.Common;
using System.Text;

namespace Mcp.SqlServer.Services;

public sealed record ResultPage(string Text, int RowCount, int NextOffset, bool HasMore);

public static class ResultPager
{
    private const string ColumnSeparator = " | ";

    public static async Task<ResultPage> ReadPageAsync(DbDataReader reader, int offset, int maxRows, int maxCharacters)
    {
        var skippedRows = 0;
        while (skippedRows < offset && await reader.ReadAsync())
            skippedRows++;

        var rows = new StringBuilder();
        var rowCount = 0;
        var hasPendingRow = skippedRows == offset && await reader.ReadAsync();

        while (hasPendingRow && rowCount < maxRows)
        {
            var line = FormatRow(reader);

            if (rowCount == 0 && line.Length > maxCharacters)
            {
                rows.Append(TruncateRow(line, maxCharacters)).Append('\n');
                rowCount++;
                hasPendingRow = await reader.ReadAsync();
                break;
            }

            if (rowCount > 0 && rows.Length + line.Length + 1 > maxCharacters)
                break;

            rows.Append(line).Append('\n');
            rowCount++;
            hasPendingRow = await reader.ReadAsync();
        }

        var nextOffset = offset + rowCount;
        var text = new StringBuilder()
            .Append(FormatSummary(offset, rowCount, nextOffset, hasPendingRow)).Append('\n')
            .Append(string.Join(ColumnSeparator, Enumerable.Range(0, reader.FieldCount).Select(reader.GetName))).Append('\n')
            .Append(new string('-', 80)).Append('\n')
            .Append(rows)
            .ToString();

        return new ResultPage(text, rowCount, nextOffset, hasPendingRow);
    }

    private static string FormatSummary(int offset, int rowCount, int nextOffset, bool hasMore)
    {
        var range = rowCount == 0 ? "none" : $"{offset + 1}-{offset + rowCount}";
        return $"rows {range} · nextOffset: {nextOffset} · hasMore: {(hasMore ? "true" : "false")}";
    }

    private static string FormatRow(DbDataReader reader)
    {
        var values = Enumerable.Range(0, reader.FieldCount)
            .Select(index => reader.IsDBNull(index) ? "NULL" : reader.GetValue(index).ToString() ?? "NULL");
        return string.Join(ColumnSeparator, values);
    }

    private static string TruncateRow(string line, int maxCharacters) =>
        $"{line[..maxCharacters]} [ROW TRUNCATED: showing {maxCharacters} of {line.Length} characters]";
}
