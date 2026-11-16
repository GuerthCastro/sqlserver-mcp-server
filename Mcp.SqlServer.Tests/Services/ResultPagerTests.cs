using System.Data;
using AwesomeAssertions;
using Mcp.SqlServer.Services;

namespace Mcp.SqlServer.Tests.Services;

public class ResultPagerTests
{
    [Fact]
    public async Task ReadPageAsync_ShouldReturnConsecutivePagesWithoutOverlapOrGaps_WhenFollowingNextOffsetWithRowLimit()
    {
        // Arrange
        var table = CreateTable(1000, _ => "value");

        // Act
        var (pages, ids) = await ReadAllPagesAsync(table, maxRows: 200, maxCharacters: 20_000);

        // Assert
        pages.Should().Be(5);
        ids.Should().Equal(Enumerable.Range(1, 1000));
    }

    [Fact]
    public async Task ReadPageAsync_ShouldReturnConsecutivePagesWithoutOverlapOrGaps_WhenFollowingNextOffsetWithCharacterLimit()
    {
        // Arrange
        var table = CreateTable(300, id => new string('x', id % 50 * 10));

        // Act
        var (pages, ids) = await ReadAllPagesAsync(table, maxRows: 200, maxCharacters: 2_000);

        // Assert
        pages.Should().BeGreaterThan(2);
        ids.Should().Equal(Enumerable.Range(1, 300));
    }

    [Fact]
    public async Task ReadPageAsync_ShouldStopAtMaxRows_WhenRowsFitInCharacterLimit()
    {
        // Arrange
        var table = CreateTable(1000, _ => "value");
        using var reader = table.CreateDataReader();

        // Act
        var page = await ResultPager.ReadPageAsync(reader, offset: 0, maxRows: 200, maxCharacters: 20_000);

        // Assert
        page.RowCount.Should().Be(200);
        FirstLine(page).Should().Be("rows 1-200 · nextOffset: 200 · hasMore: true");
    }

    [Fact]
    public async Task ReadPageAsync_ShouldStopAtMaxCharacters_WhenNextRowDoesNotFit()
    {
        // Arrange
        var table = CreateSingleColumnTable(Enumerable.Repeat(new string('x', 99), 50));
        using var reader = table.CreateDataReader();

        // Act
        var page = await ResultPager.ReadPageAsync(reader, offset: 0, maxRows: 200, maxCharacters: 1_000);

        // Assert
        page.RowCount.Should().Be(10);
        FirstLine(page).Should().Be("rows 1-10 · nextOffset: 10 · hasMore: true");
    }

    [Fact]
    public async Task ReadPageAsync_ShouldReturnTruncatedRowAndAdvanceOffset_WhenSingleRowExceedsMaxCharacters()
    {
        // Arrange
        var table = CreateSingleColumnTable([new string('x', 5_000)]);
        using var reader = table.CreateDataReader();

        // Act
        var page = await ResultPager.ReadPageAsync(reader, offset: 0, maxRows: 200, maxCharacters: 1_000);

        // Assert
        page.RowCount.Should().Be(1);
        FirstLine(page).Should().Be("rows 1-1 · nextOffset: 1 · hasMore: false");
        page.Text.Should().Contain($"{new string('x', 1_000)} [ROW TRUNCATED: showing 1000 of 5000 characters]");
    }

    [Fact]
    public async Task ReadPageAsync_ShouldContinueAfterTruncatedRow_WhenOffsetIsNextOffset()
    {
        // Arrange
        var table = CreateSingleColumnTable([new string('x', 5_000), "a", "b", "c"]);
        using var firstReader = table.CreateDataReader();
        var firstPage = await ResultPager.ReadPageAsync(firstReader, offset: 0, maxRows: 200, maxCharacters: 1_000);
        using var secondReader = table.CreateDataReader();

        // Act
        var secondPage = await ResultPager.ReadPageAsync(secondReader, firstPage.NextOffset, maxRows: 200, maxCharacters: 1_000);

        // Assert
        FirstLine(firstPage).Should().Be("rows 1-1 · nextOffset: 1 · hasMore: true");
        FirstLine(secondPage).Should().Be("rows 2-4 · nextOffset: 4 · hasMore: false");
        RowLines(secondPage).Should().Equal("a", "b", "c");
    }

    [Fact]
    public async Task ReadPageAsync_ShouldReportHasMoreFalse_WhenLastPageEndsExactlyAtRowLimit()
    {
        // Arrange
        var table = CreateTable(400, _ => "value");
        using var reader = table.CreateDataReader();

        // Act
        var page = await ResultPager.ReadPageAsync(reader, offset: 200, maxRows: 200, maxCharacters: 20_000);

        // Assert
        page.RowCount.Should().Be(200);
        FirstLine(page).Should().Be("rows 201-400 · nextOffset: 400 · hasMore: false");
    }

    [Fact]
    public async Task ReadPageAsync_ShouldReturnNoRows_WhenOffsetIsBeyondTheEnd()
    {
        // Arrange
        var table = CreateTable(100, _ => "value");
        using var reader = table.CreateDataReader();

        // Act
        var page = await ResultPager.ReadPageAsync(reader, offset: 500, maxRows: 200, maxCharacters: 20_000);

        // Assert
        page.RowCount.Should().Be(0);
        FirstLine(page).Should().Be("rows none · nextOffset: 500 · hasMore: false");
    }

    private static async Task<(int Pages, List<int> Ids)> ReadAllPagesAsync(DataTable table, int maxRows, int maxCharacters)
    {
        var ids = new List<int>();
        var offset = 0;
        var pages = 0;
        var hasMore = true;

        while (hasMore)
        {
            using var reader = table.CreateDataReader();
            var page = await ResultPager.ReadPageAsync(reader, offset, maxRows, maxCharacters);
            ids.AddRange(RowLines(page).Select(line => int.Parse(line.Split(" | ")[0])));
            offset = page.NextOffset;
            hasMore = page.HasMore;
            pages++;
        }

        return (pages, ids);
    }

    private static DataTable CreateTable(int rowCount, Func<int, string> payload)
    {
        var table = new DataTable();
        table.Columns.Add("id", typeof(int));
        table.Columns.Add("payload", typeof(string));

        for (var id = 1; id <= rowCount; id++)
            table.Rows.Add(id, payload(id));

        return table;
    }

    private static DataTable CreateSingleColumnTable(IEnumerable<string> values)
    {
        var table = new DataTable();
        table.Columns.Add("value", typeof(string));

        foreach (var value in values)
            table.Rows.Add(value);

        return table;
    }

    private static string FirstLine(ResultPage page) => page.Text.Split('\n')[0];

    private static string[] RowLines(ResultPage page) =>
        page.Text.Split('\n').Skip(3).Where(line => line.Length > 0).ToArray();
}
