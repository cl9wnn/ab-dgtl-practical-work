using FluentAssertions;
using Moq;
using PracticalWork.Reports.Abstractions.Storage;
using PracticalWork.Reports.Enums;
using PracticalWork.Reports.Features.Books.Archive;
using PracticalWork.Reports.Models;
using PracticalWork.Shared.Contracts.Events.Books;
using Xunit;

namespace PracticalWork.Reports.UnitTests.Handlers;

public class BookArchivedEventHandlerTests
{
    private readonly Mock<IActivityLogRepository> _repository = new();

    [Fact]
    public async Task HandleAsync_CreatesActivityLog_AndSavesIt()
    {
        var handler = new BookArchivedEventHandler(_repository.Object);

        var message = new BookArchivedEvent(
            BookId: Guid.NewGuid(),
            Title: "Clean Code",
            ArchivedAt: DateTime.UtcNow);

        ActivityLog? capturedLog = null;

        _repository
            .Setup(x => x.Add(
                It.IsAny<ActivityLog>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>()))
            .Callback<ActivityLog, CancellationToken, Guid?, Guid?>(
                (log, _, _, _) => capturedLog = log);

        await handler.HandleAsync(message, CancellationToken.None);

        _repository.Verify(
            x => x.Add(
                It.IsAny<ActivityLog>(),
                It.IsAny<CancellationToken>(),
                message.BookId,
                null),
            Times.Once);

        capturedLog.Should().NotBeNull();
        capturedLog!.EventType.Should().Be(ActivityEventType.BookArchived);

        capturedLog.Metadata.RootElement
            .GetProperty("Title")
            .GetString()
            .Should()
            .Be("Clean Code");
    }
}