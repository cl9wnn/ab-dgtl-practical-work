using Moq;
using PracticalWork.Reports.Abstractions.Storage;
using PracticalWork.Reports.Enums;
using PracticalWork.Reports.Features.Readers.Create;
using PracticalWork.Reports.Models;
using PracticalWork.Shared.Contracts.Events.Readers;
using Xunit;

namespace PracticalWork.Reports.UnitTests.Handlers;

public class ReaderCreatedEventHandlerTests
{
    private readonly Mock<IActivityLogRepository> _activityLogRepository;
    private readonly ReaderCreatedEventHandler _handler;

    public ReaderCreatedEventHandlerTests()
    {
        _activityLogRepository = new Mock<IActivityLogRepository>();

        _handler = new ReaderCreatedEventHandler(
            _activityLogRepository.Object);
    }

    [Fact]
    public async Task HandleAsync_ShouldCreateActivityLogAndSaveIt()
    {
        // Arrange
        var readerId = Guid.NewGuid();

        var message = new ReaderCreatedEvent(
            readerId,
            "Ivan Ivanov",
            "123456789",
            "test@mail.com",
            DateOnly.FromDateTime(DateTime.UtcNow),
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

        Assert.Equal(ActivityEventType.ReaderCreated, capturedLog!.EventType);

        var json = capturedLog.Metadata.RootElement;

        Assert.Equal(readerId, json.GetProperty("ReaderId").GetGuid());
        Assert.Equal("Ivan Ivanov", json.GetProperty("FullName").GetString());
        Assert.Equal("123456789", json.GetProperty("PhoneNumber").GetString());
        Assert.Equal("test@mail.com", json.GetProperty("Email").GetString());
    }
}