using Moq;
using PracticalWork.Reports.Abstractions.Storage;
using PracticalWork.Reports.Enums;
using PracticalWork.Reports.Features.Books.Return;
using PracticalWork.Reports.Models;
using PracticalWork.Shared.Contracts.Events.Books;
using Xunit;

namespace PracticalWork.Reports.UnitTests.Handlers;

public class BookReturnedEventHandlerTests
{
    private readonly Mock<IActivityLogRepository> _activityLogRepository;
    private readonly BookReturnedEventHandler _handler;

    public BookReturnedEventHandlerTests()
    {
        _activityLogRepository = new Mock<IActivityLogRepository>();

        _handler = new BookReturnedEventHandler(
            _activityLogRepository.Object);
    }

    [Fact]
    public async Task HandleAsync_ShouldCreateActivityLogAndSaveIt()
    {
        var bookId = Guid.NewGuid();
        var readerId = Guid.NewGuid();

        var message = new BookReturnedEvent(
            bookId,
            readerId,
            "Clean Code",
            "Ivan Ivanov",
            DateOnly.FromDateTime(DateTime.UtcNow));

        ActivityLog? capturedLog = null;

        _activityLogRepository
            .Setup(x => x.Add(
                It.IsAny<ActivityLog>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>()))
            .Callback<ActivityLog, CancellationToken, Guid?, Guid?>(
                (log, _, _, _) => capturedLog = log)
            .ReturnsAsync(Guid.NewGuid());

        await _handler.HandleAsync(message, CancellationToken.None);

        _activityLogRepository.Verify(x => x.Add(
                It.IsAny<ActivityLog>(),
                It.IsAny<CancellationToken>(),
                bookId,
                readerId),
            Times.Once);

        Assert.NotNull(capturedLog);
        Assert.Equal(ActivityEventType.BookReturned, capturedLog!.EventType);

        var json = capturedLog.Metadata.RootElement;

        Assert.Equal(bookId, json.GetProperty("BookId").GetGuid());
        Assert.Equal(readerId, json.GetProperty("ReaderId").GetGuid());
        Assert.Equal("Clean Code", json.GetProperty("BookTitle").GetString());
        Assert.Equal("Ivan Ivanov", json.GetProperty("ReaderName").GetString());
    }
}