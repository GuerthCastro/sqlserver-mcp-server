using AwesomeAssertions;
using Mcp.SqlServer.Services;

namespace Mcp.SqlServer.Tests.Services;

public class PendingWriteStoreTests
{
    private static readonly WriteStatement Statement =
        new(WriteOperation.Update, "UPDATE items SET name = 'x' WHERE id = 1", "items", "id = 1", ["name"]);

    [Fact]
    public void Validate_ShouldReturnValid_WhenWriteIdConnectionAndTimeMatch()
    {
        // Arrange
        var (store, _) = CreateStore();
        var pending = Register(store, code: "123456");

        // Act
        var (status, found) = store.Validate(pending.WriteId, "connection-1");

        // Assert
        status.Should().Be(PendingWriteStatus.Valid);
        found.Should().Be(pending);
    }

    [Fact]
    public void Validate_ShouldReturnExpired_WhenFiveMinutesHavePassed()
    {
        // Arrange
        var (store, clock) = CreateStore();
        var pending = Register(store, code: "123456");
        clock.Advance(PendingWriteStore.Lifetime);

        // Act
        var (status, found) = store.Validate(pending.WriteId, "connection-1");

        // Assert
        status.Should().Be(PendingWriteStatus.Expired);
        found.Should().BeNull();
        store.Validate(pending.WriteId, "connection-1").Status.Should().Be(PendingWriteStatus.NotFound);
    }

    [Fact]
    public void Validate_ShouldReturnConnectionMismatch_WhenConnectionIdDiffers()
    {
        // Arrange
        var (store, _) = CreateStore();
        var pending = Register(store, code: "123456");

        // Act
        var (status, _) = store.Validate(pending.WriteId, "connection-2");

        // Assert
        status.Should().Be(PendingWriteStatus.ConnectionMismatch);
    }

    [Fact]
    public void VerifyCode_ShouldReturnValid_WhenCodeMatches()
    {
        // Arrange
        var (store, _) = CreateStore();
        var pending = Register(store, code: "042731");

        // Act
        var (status, _) = store.VerifyCode(pending.WriteId, "042731");

        // Assert
        status.Should().Be(PendingWriteStatus.Valid);
    }

    [Fact]
    public void VerifyCode_ShouldInvalidateWriteId_WhenThreeWrongCodesAreSent()
    {
        // Arrange
        var (store, _) = CreateStore();
        var pending = Register(store, code: "042731");

        // Act
        var first = store.VerifyCode(pending.WriteId, "000000");
        var second = store.VerifyCode(pending.WriteId, "111111");
        var third = store.VerifyCode(pending.WriteId, "222222");
        var correctAfterwards = store.VerifyCode(pending.WriteId, "042731");

        // Assert
        first.Should().Be((PendingWriteStatus.InvalidCode, 2));
        second.Should().Be((PendingWriteStatus.InvalidCode, 1));
        third.Should().Be((PendingWriteStatus.AttemptsExhausted, 0));
        correctAfterwards.Status.Should().Be(PendingWriteStatus.NotFound);
    }

    [Fact]
    public void VerifyCode_ShouldRequireCode_WhenCodeIsMissing()
    {
        // Arrange
        var (store, _) = CreateStore();
        var pending = Register(store, code: "042731");

        // Act
        var (status, remaining) = store.VerifyCode(pending.WriteId, null);

        // Assert
        status.Should().Be(PendingWriteStatus.CodeRequired);
        remaining.Should().Be(PendingWriteStore.MaxCodeAttempts);
    }

    [Fact]
    public void TryConsume_ShouldSucceedOnlyOnce_WhenWriteIdIsUsed()
    {
        // Arrange
        var (store, _) = CreateStore();
        var pending = Register(store, code: null);

        // Act
        var first = store.TryConsume(pending.WriteId);
        var second = store.TryConsume(pending.WriteId);

        // Assert
        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    [Fact]
    public void GenerateCode_ShouldReturnSixDigits_WhenCalled()
    {
        // Arrange
        var codes = Enumerable.Range(0, 200).Select(_ => PendingWriteStore.GenerateCode()).ToList();

        // Act
        var allSixDigits = codes.All(code => code.Length == 6 && code.All(char.IsAsciiDigit));

        // Assert
        allSixDigits.Should().BeTrue();
    }

    private static (PendingWriteStore Store, ManualTimeProvider Clock) CreateStore()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        return (new PendingWriteStore(clock), clock);
    }

    private static PendingWrite Register(PendingWriteStore store, string? code) =>
        store.Register("connection-1", "db.example.test", 1433, "app", Statement, "dbo.items", 1, code);

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}
