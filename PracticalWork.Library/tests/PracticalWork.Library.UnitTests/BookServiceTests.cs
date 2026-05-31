using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using PracticalWork.Library.Abstractions.Services.Infrastructure;
using PracticalWork.Library.Abstractions.Storage;
using PracticalWork.Library.Dtos;
using PracticalWork.Library.Enums;
using PracticalWork.Library.Exceptions;
using PracticalWork.Library.Models;
using PracticalWork.Library.Options.Cache;
using PracticalWork.Library.Services;
using PracticalWork.Shared.Contracts.Events.Books;
using Xunit;

namespace PracticalWork.Library.UnitTests;

public class BookServiceTests
{
    private readonly Mock<IBookRepository> _repositoryMock = new();
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly Mock<IFileStorageService> _storageMock = new();
    private readonly Mock<IMessageBrokerProducer> _producerMock = new();
    
    
    private readonly BookService _service;
    
    public BookServiceTests()
    {
        var optionsMock = new Mock<IOptions<BooksCacheOptions>>();

        optionsMock
            .Setup(x => x.Value)
            .Returns(new BooksCacheOptions
            {
                BooksCacheVersionPrefix = "books",
                BooksListCache = new CacheEntryOptions
                {
                    KeyPrefix = "books-list",
                    TtlMinutes = 10
                }
            });
        _service = new BookService(
            _repositoryMock.Object,
            _cacheMock.Object,
            _storageMock.Object,
            optionsMock.Object,
            _producerMock.Object);
    }
    
    [Fact]
    public async Task Exists_BookExists_DoesNotThrow()
    {
        var id = Guid.NewGuid();

        _repositoryMock
            .Setup(x => x.Exists(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var action = () => _service.Exists(id, CancellationToken.None);

        await action.Should().NotThrowAsync();
    }
    
    [Fact]
    public async Task Exists_BookNotExists_ThrowsEntityNotFoundException()
    {
        var id = Guid.NewGuid();

        _repositoryMock
            .Setup(x => x.Exists(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var action = () => _service.Exists(id, CancellationToken.None);

        await action.Should()
            .ThrowAsync<EntityNotFoundException>();
    }
    
    [Fact]
    public async Task CreateBook_ValidBook_ReturnsCreatedBookId()
    {
        var id = Guid.NewGuid();

        var book = new Book
        {
            Title = "CLR via C#",
            Authors = ["Richter"],
            Category = BookCategory.ScientificBook,
            Year = 2024
        };

        _repositoryMock
            .Setup(x => x.Add(book, It.IsAny<CancellationToken>()))
            .ReturnsAsync(id);

        var result = await _service.CreateBook(book, CancellationToken.None);

        result.Should().Be(id);
        book.Status.Should().Be(BookStatus.Available);
    }
    
    [Fact]
    public async Task CreateBook_ValidBook_InvalidatesCache()
    {
        var id = Guid.NewGuid();

        var book = new Book
        {
            Title = "Test",
            Authors = ["Author"]
        };

        _repositoryMock
            .Setup(x => x.Add(book, It.IsAny<CancellationToken>()))
            .ReturnsAsync(id);

        await _service.CreateBook(book, CancellationToken.None);

        _cacheMock.Verify(
            x => x.IncrementVersionAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
    
    [Fact]
    public async Task CreateBook_ValidBook_SendsKafkaEvent()
    {
        var id = Guid.NewGuid();

        var book = new Book
        {
            Title = "Test",
            Authors = ["Author"]
        };

        _repositoryMock
            .Setup(x => x.Add(book, It.IsAny<CancellationToken>()))
            .ReturnsAsync(id);

        await _service.CreateBook(book, CancellationToken.None);

        _producerMock.Verify(
            x => x.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<BookCreatedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
    
    [Fact]
    public async Task UpdateBook_ArchivedBook_ThrowsException()
    {
        var id = Guid.NewGuid();

        var archivedBook = new Book
        {
            IsArchived = true
        };

        _repositoryMock
            .Setup(x => x.GetById(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(archivedBook);

        var action = () =>
            _service.UpdateBook(id, new Book(), CancellationToken.None);

        await action.Should()
            .ThrowAsync<BookServiceException>();
    }
    
    [Fact]
    public async Task UpdateBook_ValidBook_UpdatesRepository()
    {
        var id = Guid.NewGuid();

        var existingBook = new Book();

        var updatedBook = new Book
        {
            Title = "Updated"
        };

        _repositoryMock
            .Setup(x => x.GetById(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingBook);

        await _service.UpdateBook(id, updatedBook, CancellationToken.None);

        _repositoryMock.Verify(
            x => x.Update(id, existingBook,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
    
    [Fact]
    public async Task ArchiveBook_ValidBook_ReturnsArchivedDto()
    {
        var id = Guid.NewGuid();

        var book = new Book
        {
            Title = "Test",
            Status = BookStatus.Available
        };

        _repositoryMock
            .Setup(x => x.GetById(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(book);

        var result = await _service.ArchiveBook(id, CancellationToken.None);

        result.Id.Should().Be(id);

        book.IsArchived.Should().BeTrue();
        book.Status.Should().Be(BookStatus.Archived);
    }
    
    [Fact]
    public async Task ArchiveBook_AlreadyArchived_ThrowsException()
    {
        var id = Guid.NewGuid();

        var book = new Book
        {
            Status = BookStatus.Archived,
            IsArchived = true
        };

        _repositoryMock
            .Setup(x => x.GetById(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(book);

        var action = () =>
            _service.ArchiveBook(id, CancellationToken.None);

        await action.Should()
            .ThrowAsync<BookServiceException>();
    }
    
    [Fact]
    public async Task ArchiveOldBooks_AllBooksArchived_CreatesCorrectReport()
    {
        var books = new List<(Guid, Book)>
        {
            (
                Guid.NewGuid(),
                new Book
                {
                    Title = "Book1",
                    Status = BookStatus.Available
                }
            ),
            (
                Guid.NewGuid(),
                new Book
                {
                    Title = "Book2",
                    Status = BookStatus.Available
                }
            )
        };

        _repositoryMock
            .Setup(x => x.GetBooksForArchiving(
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(books);

        foreach (var (id, book) in books)
        {
            _repositoryMock
                .Setup(x => x.GetById(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(book);
        }

        var report = await _service.ArchiveOldBooks(
            5,
            10,
            CancellationToken.None);

        report.TotalProcessed.Should().Be(2);
        report.SuccessfullyArchived.Should().Be(2);
        report.Skipped.Should().Be(0);
    }
    
    [Fact]
    public async Task ArchiveOldBooks_OneBookFails_AddsSkippedRecord()
    {
        var validBookId = Guid.NewGuid();
        var failedBookId = Guid.NewGuid();

        var books = new List<(Guid, Book)>
        {
            (
                validBookId,
                new Book
                {
                    Title = "Valid",
                    Status = BookStatus.Available
                }
            ),
            (
                failedBookId,
                new Book
                {
                    Title = "Archived",
                    Status = BookStatus.Archived,
                    IsArchived = true
                }
            )
        };

        _repositoryMock
            .Setup(x => x.GetBooksForArchiving(
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(books);

        _repositoryMock
            .Setup(x => x.GetById(validBookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(books[0].Item2);

        _repositoryMock
            .Setup(x => x.GetById(failedBookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(books[1].Item2);

        var report = await _service.ArchiveOldBooks(
            5,
            10,
            CancellationToken.None);

        report.TotalProcessed.Should().Be(2);
        report.SuccessfullyArchived.Should().Be(1);
        report.Skipped.Should().Be(1);

        report.SkippedDetails.Should().HaveCount(1);
    }
    
    [Fact]
    public async Task GetBooksPage_DataInCache_ReturnsCachedData()
    {
        var cachedBooks = new List<BookListDto>
        {
            new()
        };

        _cacheMock
            .Setup(x => x.GetByModelAsync<SearchBooksDto,
                IReadOnlyList<BookListDto>>(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SearchBooksDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedBooks);

        var result = await _service.GetBooksPage(
            new BookFilterDto(),
            new PaginationDto(),
            CancellationToken.None);

        result.Items.Should().BeEquivalentTo(cachedBooks);

        _repositoryMock.Verify(
            x => x.GetBooks(
                It.IsAny<BookFilterDto>(),
                It.IsAny<PaginationDto>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
    
    [Fact]
    public async Task GetBooksPage_CacheMiss_LoadsFromRepositoryAndCaches()
    {
        var books = new List<BookListDto>
        {
            new()
        };

        _repositoryMock
            .Setup(x => x.GetBooks(
                It.IsAny<BookFilterDto>(),
                It.IsAny<PaginationDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(books);

        var result = await _service.GetBooksPage(
            new BookFilterDto(),
            new PaginationDto(),
            CancellationToken.None);

        result.Items.Should().HaveCount(1);

        _cacheMock.Verify(
            x => x.SetByModelAsync<SearchBooksDto, IReadOnlyList<BookListDto>>(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SearchBooksDto>(),
                It.Is<IReadOnlyList<BookListDto>>(b => b.Count == 1),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
    
    [Fact]
    public async Task AddBookDetails_ArchivedBook_ThrowsException()
    {
        var id = Guid.NewGuid();

        var book = new Book
        {
            Status = BookStatus.Archived,
            IsArchived = true
        };

        _repositoryMock
            .Setup(x => x.GetById(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(book);

        using var stream = new MemoryStream();

        var action = () =>
            _service.AddBookDetails(
                id,
                "desc",
                stream,
                ".jpg",
                CancellationToken.None);

        await action.Should()
            .ThrowAsync<BookServiceException>();
    }
    
    [Fact]
    public async Task AddBookDetails_ValidData_UploadsFileAndUpdatesBook()
    {
        var id = Guid.NewGuid();

        var book = new Book();

        _repositoryMock
            .Setup(x => x.GetById(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(book);

        using var stream = new MemoryStream();

        await _service.AddBookDetails(
            id,
            "description",
            stream,
            ".jpg",
            CancellationToken.None);

        _storageMock.Verify(
            x => x.UploadFileAsync(
                It.IsAny<string>(),
                stream,
                ".jpg",
                It.IsAny<CancellationToken>()),
            Times.Once);

        _repositoryMock.Verify(
            x => x.Update(
                id,
                book,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

}