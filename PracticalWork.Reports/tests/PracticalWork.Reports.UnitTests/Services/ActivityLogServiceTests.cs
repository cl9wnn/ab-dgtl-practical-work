using FluentAssertions;
using Moq;
using PracticalWork.Reports.Abstractions.Storage;
using PracticalWork.Reports.Dtos;
using PracticalWork.Reports.Models;
using PracticalWork.Reports.Services;
using Xunit;

namespace PracticalWork.Reports.UnitTests.Services;

public class ActivityLogServiceTests
{
    private readonly Mock<IActivityLogRepository> _activityLogRepository = new();

    private readonly ActivityLogService _service;

    public ActivityLogServiceTests()
    {
        _service = new ActivityLogService(_activityLogRepository.Object);
    }

    [Fact]
    public async Task GetPagedActivityLogs_ReturnsPageDto_WithRepositoryData()
    {
        var filter = new ActivityLogFilterDto();

        var pagination = new PaginationDto
        {
            Page = 2,
            PageSize = 15
        };

        var logs = new List<ActivityLog>
        {
            new(),
            new()
        };

        _activityLogRepository
            .Setup(x => x.GetActivityLogs(
                filter,
                pagination,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(logs);

        var result = await _service.GetPagedActivityLogs(
            filter,
            pagination,
            CancellationToken.None);

        result.Should().NotBeNull();
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(15);
        result.Items.Should().BeEquivalentTo(logs);

        _activityLogRepository.Verify(
            x => x.GetActivityLogs(
                filter,
                pagination,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetPagedActivityLogs_ReturnsEmptyPage_WhenRepositoryReturnsEmptyCollection()
    {
        var filter = new ActivityLogFilterDto();

        var pagination = new PaginationDto
        {
            Page = 1,
            PageSize = 10
        };

        var logs = Array.Empty<ActivityLog>();

        _activityLogRepository
            .Setup(x => x.GetActivityLogs(
                It.IsAny<ActivityLogFilterDto>(),
                It.IsAny<PaginationDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(logs);

        var result = await _service.GetPagedActivityLogs(
            filter,
            pagination,
            CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(10);
    }
}