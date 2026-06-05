using Moq;
using PracticalWork.Reports.Abstractions.Storage;
using PracticalWork.Reports.Enums;
using PracticalWork.Reports.Features.Books.Borrow;
using PracticalWork.Reports.Models;
using PracticalWork.Shared.Contracts.Events.Books;
using Xunit;

namespace PracticalWork.Reports.UnitTests.Handlers;

public class BookBorrowedEventHandlerTests
{
    private readonly Mock<IActivityLogRepository> _repository = new();

    [Fact]
    public async Task HandleAsync_CreatesActivityLog_AndSavesIt()
    {
        var handler = new BookBorrowedEventHandler(_repository.Object);

        var message = new BookBorrowedEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Clean Code",
            "Robert Martin",
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)));

        await handler.HandleAsync(message, CancellationToken.None);

        _repository.Verify(
            x => x.Add(
                It.Is<ActivityLog>(log =>
                    log.EventType == ActivityEventType.BookBorrowed),
                It.IsAny<CancellationToken>(),
                message.BookId,
                message.ReaderId),
            Times.Once);
    }
}