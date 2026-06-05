using System.Text;
using System.Text.Json;
using PracticalWork.Reports.Enums;
using PracticalWork.Reports.Models;
using PracticalWork.Reports.Services;
using Xunit;

namespace PracticalWork.Reports.UnitTests.Services;

public class LibraryActivityReportCsvServiceTests
{
    private readonly LibraryActivityReportCsvService _libraryActivityReportCsvService;

    public LibraryActivityReportCsvServiceTests()
    {
        _libraryActivityReportCsvService = new LibraryActivityReportCsvService();
    }
    
    [Fact]
    public void Generate_WhenLogsEmpty_ShouldGenerateOnlyHeader()
    {
        var result = _libraryActivityReportCsvService.Generate([]);

        var csv = Encoding.UTF8.GetString(result);

        Assert.Contains("Тип события", csv);
        Assert.Contains("Дата события", csv);
        Assert.Contains("Дополнительная информация", csv);

        Assert.DoesNotContain("BookCreated", csv);
    }
    
    [Fact]
    public void Generate_WhenLogsExist_ShouldGenerateCsvWithData()
    {
        var logs = new List<ActivityLog>
        {
            new()
            {
                EventType = ActivityEventType.BookCreated,
                EventDate = new DateOnly(2025, 6, 5),
                Metadata = JsonDocument.Parse("""
                                              {
                                                  "BookId": 1,
                                                  "Title": "Clean Code"
                                              }
                                              """)
            }
        };

        var result = _libraryActivityReportCsvService.Generate(logs);
        var csv = Encoding.UTF8.GetString(result);

        Assert.Contains("Тип события", csv);
        Assert.Contains("Дата события", csv);
        Assert.Contains("Дополнительная информация", csv);

        Assert.Contains("BookCreated", csv);
        Assert.Contains("2025-06-05", csv);

        Assert.Contains("BookId", csv);
        Assert.Contains("1", csv);
        Assert.Contains("Title", csv);
        Assert.Contains("Clean Code", csv);
    }
}