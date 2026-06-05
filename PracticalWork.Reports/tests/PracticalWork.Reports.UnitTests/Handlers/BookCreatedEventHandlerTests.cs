using Moq;
using PracticalWork.Reports.Abstractions.Storage;
using PracticalWork.Reports.Enums;
using PracticalWork.Reports.Features.Books.Create;
using PracticalWork.Reports.Models;
using PracticalWork.Shared.Contracts.Events.Books;
using Xunit;

namespace PracticalWork.Reports.UnitTests.Handlers;

public class BookCreatedEventHandlerTests
{
    private readonly Mock<IActivityLogRepository> _activityLogRepository;
    private readonly BookCreatedEventHandler _handler;

    public BookCreatedEventHandlerTests()
    {
        _activityLogRepository = new Mock<IActivityLogRepository>();

        _handler = new BookCreatedEventHandler(
            _activityLogRepository.Object);
    }

    [Fact]
    public async Task HandleAsync_ShouldSaveActivityLog()
    {
        var bookId = Guid.NewGuid();

        var message = new BookCreatedEvent(
            bookId,
            "Clean Code",
            "Programming",
            ["Robert Martin"],
            2008,
            DateTime.UtcNow);

        _activityLogRepository
            .Setup(x => x.Add(
                It.IsAny<ActivityLog>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>()))
            .ReturnsAsync(Guid.NewGuid());

        await _handler.HandleAsync(message, CancellationToken.None);

        _activityLogRepository.Verify(x => x.Add(
                It.Is<ActivityLog>(l =>
                    l.EventType == ActivityEventType.BookCreated),
                It.IsAny<CancellationToken>(),
                bookId,
                null),
            Times.Once);
    }
}