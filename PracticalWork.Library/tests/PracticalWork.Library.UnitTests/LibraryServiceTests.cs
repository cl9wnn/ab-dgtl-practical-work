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

public class LibraryServiceTests
{
    private readonly Mock<IBookRepository> _bookRepository = new();
    private readonly Mock<IReaderRepository> _readerRepository = new();
    private readonly Mock<IBookBorrowRepository> _borrowRepository = new();
    private readonly Mock<ICacheService> _cacheService = new();
    private readonly Mock<IFileStorageService> _storageService = new();
    private readonly Mock<IMessageBrokerProducer> _producer = new();

    private readonly LibraryService _service;

    public LibraryServiceTests()
    {
         var booksOptionsMock = new Mock<IOptions<BooksCacheOptions>>();
         var readersOptionsMock = new Mock<IOptions<ReadersCacheOptions>>();
         
        booksOptionsMock.Setup(x => x.Value)
            .Returns(new BooksCacheOptions
            {
                BooksCacheVersionPrefix = "books",

                LibraryBooksCache = new CacheEntryOptions
                {
                    KeyPrefix = "library-books",
                    TtlMinutes = 10
                },

                BookDetailsCache = new CacheEntryOptions
                {
                    KeyPrefix = "book-details",
                    TtlMinutes = 15
                },

                BooksListCache = new CacheEntryOptions
                {
                    KeyPrefix = "books-list",
                    TtlMinutes = 5
                }
            });

        readersOptionsMock.Setup(x => x.Value)
            .Returns(new ReadersCacheOptions
            {
                ReadersCacheVersionPrefix = "readers"
            });

        _service = new LibraryService(
            _bookRepository.Object,
            _readerRepository.Object,
            _borrowRepository.Object,
            booksOptionsMock.Object,
            readersOptionsMock.Object,
            _cacheService.Object,
            _storageService.Object,
            _producer.Object);
    }

    [Fact]
    public async Task GetLibraryBooksPage_ReturnsCachedBooks_WhenCacheHit()
    {
        var books = new List<LibraryBookDto>
        {
            new()
        };

        _cacheService
            .Setup(x => x.GetByModelAsync<SearchBooksDto, IReadOnlyList<LibraryBookDto>>(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SearchBooksDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(books);

        var pagination = new PaginationDto { Page = 1, PageSize = 10 };

        var result = await _service.GetLibraryBooksPage(
            new BookFilterDto(),
            pagination,
            CancellationToken.None);

        result.Items.Should().BeEquivalentTo(books);

        _bookRepository.Verify(
            x => x.GetLibraryBooks(
                It.IsAny<BookFilterDto>(),
                It.IsAny<PaginationDto>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetLibraryBooksPage_LoadsFromRepository_WhenCacheMiss()
    {
        var books = new List<LibraryBookDto>
        {
            new()
        };

        _cacheService
            .Setup(x => x.GetByModelAsync<SearchBooksDto, IReadOnlyList<LibraryBookDto>>(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SearchBooksDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<LibraryBookDto>)null!);

        _bookRepository
            .Setup(x => x.GetLibraryBooks(
                It.IsAny<BookFilterDto>(),
                It.IsAny<PaginationDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(books);

        var result = await _service.GetLibraryBooksPage(
            new BookFilterDto(),
            new PaginationDto(),
            CancellationToken.None);

        result.Items.Should().BeEquivalentTo(books);

        _bookRepository.Verify(
            x => x.GetLibraryBooks(
                It.IsAny<BookFilterDto>(),
                It.IsAny<PaginationDto>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _cacheService.Verify(
            x => x.SetByModelAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SearchBooksDto>(),
                It.Is<IReadOnlyList<LibraryBookDto>>(b => b == books),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BorrowBook_ShouldCreateBorrowAndSendEvent()
    {
        var bookId = Guid.NewGuid();
        var readerId = Guid.NewGuid();
        var borrowId = Guid.NewGuid();

        var book = new Book
        {
            Title = "Book",
            Status = BookStatus.Available,
            IsArchived = false
        };

        var reader = new Reader
        {
            FullName = "Reader",
            IsActive = true,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10))
        };

        _bookRepository
            .Setup(x => x.GetById(bookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(book);

        _readerRepository
            .Setup(x => x.GetById(readerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reader);

        _borrowRepository
            .Setup(x => x.Create(
                bookId,
                readerId,
                It.IsAny<Borrow>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(borrowId);

        var result = await _service.BorrowBook(
            bookId,
            readerId,
            CancellationToken.None);

        result.Should().Be(borrowId);

        _producer.Verify(
            x => x.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<BookBorrowedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BorrowBook_ShouldThrow_WhenReaderIsInvalid()
    {
        var reader = new Reader
        {
            IsActive = true,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1))
        };

        _bookRepository
            .Setup(x => x.GetById(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Book
            {
                Status = BookStatus.Available
            });

        _readerRepository
            .Setup(x => x.GetById(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(reader);

        var action = () => _service.BorrowBook(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None);

        await action.Should()
            .ThrowAsync<LibraryServiceException>();
    }

    [Fact]
    public async Task ReturnBook_ShouldUpdateBorrowAndSendEvent()
    {
        var bookId = Guid.NewGuid();

        var borrow = Borrow.Create();

        var readerInfo = new ReaderInfoDto
        {
            Id = Guid.NewGuid(),
            FullName = "Reader"
        };

        var book = new Book
        {
            Title = "Book",
            Status = BookStatus.Borrow
        };

        _borrowRepository
            .Setup(x => x.GetActiveBorrowByBookId(
                bookId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(borrow);

        _borrowRepository
            .Setup(x => x.GetReaderInfoByBorrowedBookId(
                bookId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(readerInfo);

        _bookRepository
            .Setup(x => x.GetById(
                bookId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(book);

        await _service.ReturnBook(
            bookId,
            CancellationToken.None);

        _producer.Verify(
            x => x.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<BookReturnedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetBookDetailsById_ReturnsCache_WhenExists()
    {
        var dto = new BookDetailsDto
        {
            Title = "Cached"
        };

        _cacheService
            .Setup(x => x.GetAsync<Guid, BookDetailsDto>(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _service.GetBookDetailsById(
            Guid.NewGuid(),
            CancellationToken.None);

        result.Should().BeSameAs(dto);
    }

    [Fact]
    public async Task GetBookDetailsById_LoadsAndCaches_WhenCacheMiss()
    {
        var bookId = Guid.NewGuid();

        var book = new Book
        {
            Title = "Book",
            Description = "Desc",
            CoverImagePath = "cover.jpg",
            Authors = ["Author"]
        };

        _bookRepository
            .Setup(x => x.GetById(bookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(book);

        _storageService
            .Setup(x => x.GetFilePathAsync(
                "cover.jpg",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("url");

        var result = await _service.GetBookDetailsById(
            bookId,
            CancellationToken.None);

        result.Title.Should().Be("Book");
        result.CoverImagePath.Should().Be("url");

        _cacheService.Verify(
            x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                bookId,
                It.IsAny<BookDetailsDto>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetBookDetailsByTitle_ShouldReplaceCoverPath()
    {
        var dto = new BookDetailsDto
        {
            Title = "Book",
            CoverImagePath = "cover.jpg"
        };

        _bookRepository
            .Setup(x => x.GetByTitle(
                "Book",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        _storageService
            .Setup(x => x.GetFilePathAsync(
                "cover.jpg",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("url");

        var result = await _service.GetBookDetailsByTitle(
            "Book",
            CancellationToken.None);

        result.CoverImagePath.Should().Be("url");
    }

    [Fact]
    public async Task GetLibraryStatistics_ShouldAggregateAllValues()
    {
        _bookRepository
            .Setup(x => x.GetNewBooksCount(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);

        _readerRepository
            .Setup(x => x.GetNewReadersCount(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        _borrowRepository
            .Setup(x => x.GetBorrowedBooksCount(
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);

        _borrowRepository
            .Setup(x => x.GetReturnedBooksCount(
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(6);

        _borrowRepository
            .Setup(x => x.GetOverdueBooksCount(
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var result = await _service.GetLibraryStatistics(
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow),
            CancellationToken.None);

        result.NewBooksCount.Should().Be(10);
        result.NewReadersCount.Should().Be(5);
        result.BorrowedBooksCount.Should().Be(7);
        result.ReturnedBooksCount.Should().Be(6);
        result.OverdueBooksCount.Should().Be(2);
    }
}