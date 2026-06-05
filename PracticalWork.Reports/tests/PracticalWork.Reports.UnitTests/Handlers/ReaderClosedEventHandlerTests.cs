using Moq;
using PracticalWork.Reports.Abstractions.Storage;
using PracticalWork.Reports.Enums;
using PracticalWork.Reports.Features.Readers.Close;
using PracticalWork.Reports.Models;
using PracticalWork.Shared.Contracts.Events.Readers;
using Xunit;

namespace PracticalWork.Reports.UnitTests.Handlers;

public class ReaderClosedEventHandlerTests
{
    private readonly Mock<IActivityLogRepository> _activityLogRepository;
    private readonly ReaderClosedEventHandler _handler;

    public ReaderClosedEventHandlerTests()
    {
        _activityLogRepository = new Mock<IActivityLogRepository>();

        _handler = new ReaderClosedEventHandler(
            _activityLogRepository.Object);
    }

    [Fact]
    public async Task HandleAsync_ShouldCreateActivityLogAndSaveIt()
    {
        var readerId = Guid.NewGuid();

        var message = new ReaderClosedEvent(
            readerId,
            "Ivan Ivanov",
            DateTime.UtcNow);

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
                null,
                readerId),
            Times.Once);

        Assert.NotNull(capturedLog);
        Assert.Equal(ActivityEventType.ReaderClosed, capturedLog!.EventType);

        var json = capturedLog.Metadata.RootElement;

        Assert.Equal(readerId, json.GetProperty("ReaderId").GetGuid());
        Assert.Equal("Ivan Ivanov", json.GetProperty("FullName").GetString());
    }
}