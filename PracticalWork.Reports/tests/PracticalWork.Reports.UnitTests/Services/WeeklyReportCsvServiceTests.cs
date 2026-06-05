using System.Text;
using PracticalWork.Reports.Dtos;
using PracticalWork.Reports.Services;
using Xunit;

namespace PracticalWork.Reports.UnitTests.Services;

public class WeeklyReportCsvServiceTests
{
    private readonly WeeklyReportCsvService _weeklyReportCsvService;

    public WeeklyReportCsvServiceTests()
    {
        _weeklyReportCsvService = new WeeklyReportCsvService();
    }
    
    [Fact]
    public void Generate_ShouldGenerateExpectedCsv()
    {
        var stats = new WeeklyStatisticsDto
        {
            NewBooksCount = 10,
            NewReadersCount = 5,
            BorrowedBooksCount = 15,
            ReturnedBooksCount = 12,
            OverdueBooksCount = 3
        };

        var bytes = _weeklyReportCsvService.Generate(stats);
        var csv = Encoding.UTF8.GetString(bytes);

        Assert.Contains("Количество новых книг,10", csv);
        Assert.Contains("Количество новых читателей,5", csv);
        Assert.Contains("Количество выданных книг,15", csv);
        Assert.Contains("Количество возвращенных книг,12", csv);
        Assert.Contains("Количество просроченных выдач,3", csv);
    }
}