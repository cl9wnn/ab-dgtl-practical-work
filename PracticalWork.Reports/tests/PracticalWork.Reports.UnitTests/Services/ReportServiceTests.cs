using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using PracticalWork.Reports.Abstractions.Services.Domain;
using PracticalWork.Reports.Abstractions.Services.Infrastructure;
using PracticalWork.Reports.Abstractions.Storage;
using PracticalWork.Reports.Dtos;
using PracticalWork.Reports.Enums;
using PracticalWork.Reports.Models;
using PracticalWork.Reports.Options.Cache;
using PracticalWork.Reports.Services;
using Xunit;

namespace PracticalWork.Reports.UnitTests.Services;

public class ReportServiceTests
{
    private readonly Mock<IReportRepository> _reportRepository = new();
    private readonly Mock<IActivityLogRepository> _activityLogRepository = new();
    private readonly Mock<IActivityLogService> _activityLogService = new();
    private readonly Mock<ITabularCsvExportService<ActivityLog>> _tabularCsvExport = new();
    private readonly Mock<IKeyValueCsvExportService<WeeklyStatisticsDto>> _keyValueCsvExport = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<ICacheService> _cache = new();

    private readonly ReportService _service;

    public ReportServiceTests()
    {
        var reportOptionsMock = new Mock<IOptions<BooksCacheOptions>>();

        reportOptionsMock.Setup(x => x.Value)
            .Returns(new BooksCacheOptions
            {
                ReportsCacheVersionPrefix = "reports-v1",

                ReportsListCache = new CacheEntryOptions
                {
                    KeyPrefix = "reports-list",
                    TtlMinutes = 10
                }
            });

        _service = new ReportService(
            _reportRepository.Object,
            _tabularCsvExport.Object,
            _fileStorage.Object,
            _activityLogRepository.Object,
            _cache.Object,
            reportOptionsMock.Object,
            _keyValueCsvExport.Object,
            _activityLogService.Object);
    }
    
    [Fact]
    public async Task GenerateLibraryActivityReport_ShouldGenerateReport()
    {
        var dto = new GenerateLibraryActivityReportDto
        {
            PeriodFrom = new DateOnly(2025, 1, 1),
            PeriodTo = new DateOnly(2025, 1, 31),
            EventType = ActivityEventType.BookBorrowed
        };

        var logs = new List<ActivityLog>
        {
            new()
        };

        _activityLogRepository
            .Setup(x => x.GetActivityLogsByPeriod(
                dto.PeriodFrom,
                dto.PeriodTo,
                dto.EventType,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(logs);

        _tabularCsvExport
            .Setup(x => x.Generate(logs))
            .Returns([1, 2, 3]);

        var result = await _service.GenerateLibraryActivityReport(
            dto,
            CancellationToken.None);

        result.Should().NotBeNull();

        _reportRepository.Verify(
            x => x.Add(It.IsAny<Report>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _cache.Verify(
            x => x.IncrementVersionAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
    
    [Fact]
    public async Task GenerateWeeklyReport_ShouldReturnWeeklyReport()
    {
        var dto = new GenerateWeeklyReportDto
        {
            PeriodFrom = new DateOnly(2025, 1, 1),
            PeriodTo = new DateOnly(2025, 1, 7),

            WeeklyStatistics = new WeeklyStatisticsDto()
        };

        _keyValueCsvExport
            .Setup(x => x.Generate(dto.WeeklyStatistics))
            .Returns([1, 2]);

        _reportRepository
            .Setup(x => x.GetByName(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Report
            {
                FilePath = "path.csv"
            });

        _fileStorage
            .Setup(x => x.GetFilePathAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("url");

        var result = await _service.GenerateWeeklyReport(
            dto,
            CancellationToken.None);

        result.DownloadUrl.Should().Be("url");
        result.Name.Should().Contain("weekly_report");
    }
    
    [Fact]
    public async Task GetAll_ShouldReturnCachedReports()
    {
        IReadOnlyList<Report> reports =
        [
            new Report()
        ];

        _cache
            .Setup(x => x.GetAsync<IReadOnlyList<Report>>(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(reports);

        var result = await _service.GetAll(
            CancellationToken.None);

        result.Should().BeEquivalentTo(reports);

        _reportRepository.Verify(
            x => x.GetAll(It.IsAny<CancellationToken>()),
            Times.Never);
    }
    
    [Fact]
    public async Task GetAll_ShouldLoadFromRepository_WhenCacheMiss()
    {
        IReadOnlyList<Report> reports =
        [
            new Report()
        ];

        _cache
            .Setup(x => x.GetAsync<IReadOnlyList<Report>>(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<IReadOnlyList<Report>>(null!));

        _reportRepository
            .Setup(x => x.GetAll(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(reports);

        var result = await _service.GetAll(
            CancellationToken.None);

        result.Should().BeEquivalentTo(reports);

        _cache.Verify(
            x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                reports,
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
    
    [Fact]
    public async Task GetDownloadUrl_ShouldReturnUrl()
    {
        _reportRepository
            .Setup(x => x.GetByName(
                "report",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Report
            {
                FilePath = "reports/file.csv"
            });

        _fileStorage
            .Setup(x => x.GetFilePathAsync(
                "reports/file.csv",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://url");

        var result = await _service.GetDownloadUrl(
            "report",
            CancellationToken.None);

        result.Should().Be("https://url");
    }
}