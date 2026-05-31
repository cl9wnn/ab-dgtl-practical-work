using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using PracticalWork.Library.Abstractions.Services.Infrastructure;
using PracticalWork.Library.Abstractions.Storage;
using PracticalWork.Library.Dtos;
using PracticalWork.Library.Exceptions;
using PracticalWork.Library.Models;
using PracticalWork.Library.Options.Cache;
using PracticalWork.Library.Services;
using PracticalWork.Shared.Contracts.Events.Readers;
using Xunit;

namespace PracticalWork.Library.UnitTests;

public class ReaderServiceTests
{
    private readonly Mock<IReaderRepository> _readerRepository = new();
    private readonly Mock<ICacheService> _cacheService = new();
    private readonly Mock<IOptions<ReadersCacheOptions>> _options = new();
    private readonly Mock<IMessageBrokerProducer> _kafkaProducer = new();

    private readonly ReaderService _service;

    public ReaderServiceTests()
    {
        _options.Setup(x => x.Value)
            .Returns(new ReadersCacheOptions
            {
                ReadersCacheVersionPrefix = "readers-v1",
                ReadersBooksCache = new CacheEntryOptions
                {
                    KeyPrefix = "reader-books",
                    TtlMinutes = 10
                }
            });

        _service = new ReaderService(
            _readerRepository.Object,
            _cacheService.Object,
            _options.Object,
            _kafkaProducer.Object);
    }
    
    [Fact]
    public async Task GetById_ReturnsReader()
    {
        var id = Guid.NewGuid();

        var reader = new Reader
        {
            FullName = "Test"
        };

        _readerRepository
            .Setup(x => x.GetById(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reader);

        var result = await _service.GetById(id, CancellationToken.None);

        result.Should().BeSameAs(reader);
    }
    
    [Fact]
    public async Task Exists_DoesNothing_WhenReaderExists()
    {
        var id = Guid.NewGuid();

        _readerRepository
            .Setup(x => x.Exists(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _service.Exists(id, CancellationToken.None);
    }
    
    [Fact]
    public async Task Exists_Throws_WhenReaderNotExists()
    {
        var id = Guid.NewGuid();

        _readerRepository
            .Setup(x => x.Exists(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = () => _service.Exists(id, CancellationToken.None);

        await act.Should()
            .ThrowAsync<EntityNotFoundException>();
    }
    
    [Fact]
    public async Task CreateReader_CreatesReader()
    {
        var id = Guid.NewGuid();

        var reader = new Reader
        {
            FullName = "Test",
            PhoneNumber = "123",
            Email = "test@test.com",
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
        };

        _readerRepository
            .Setup(x => x.Exists(reader.PhoneNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _readerRepository
            .Setup(x => x.Add(reader, It.IsAny<CancellationToken>()))
            .ReturnsAsync(id);

        var result = await _service.CreateReader(reader, CancellationToken.None);

        result.Should().Be(id);

        reader.IsActive.Should().BeTrue();

        _readerRepository.Verify(
            x => x.Add(reader, It.IsAny<CancellationToken>()),
            Times.Once);

        _kafkaProducer.Verify(
            x => x.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<ReaderCreatedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
    
    [Fact]
    public async Task CreateReader_Throws_WhenPhoneAlreadyExists()
    {
        var reader = new Reader
        {
            PhoneNumber = "123"
        };

        _readerRepository
            .Setup(x => x.Exists(reader.PhoneNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var act = () => _service.CreateReader(reader, CancellationToken.None);

        await act.Should()
            .ThrowAsync<ReaderServiceException>();

        _readerRepository.Verify(
            x => x.Add(It.IsAny<Reader>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
    
    [Fact]
    public async Task ExtendReader_UpdatesReader()
    {
        var id = Guid.NewGuid();

        var reader = new Reader
        {
            IsActive = true,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1))
        };

        var newDate = reader.ExpiryDate.AddMonths(1);

        _readerRepository
            .Setup(x => x.GetById(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reader);

        await _service.ExtendReader(id, newDate, CancellationToken.None);

        reader.ExpiryDate.Should().Be(newDate);

        _readerRepository.Verify(
            x => x.Update(id, reader, It.IsAny<CancellationToken>()),
            Times.Once);
    }
    
    [Fact]
    public async Task ExtendReader_Throws_WhenReaderInvalid()
    {
        var id = Guid.NewGuid();

        var reader = new Reader
        {
            IsActive = false,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1))
        };

        _readerRepository
            .Setup(x => x.GetById(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reader);

        var act = () => _service.ExtendReader(
            id,
            DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            CancellationToken.None);

        await act.Should()
            .ThrowAsync<ReaderServiceException>();
    }
    
    [Fact]
    public async Task CloseReader_ClosesReader()
    {
        var id = Guid.NewGuid();

        var reader = new Reader
        {
            FullName = "Reader",
            IsActive = true,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
        };

        _readerRepository
            .Setup(x => x.GetById(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reader);

        _readerRepository
            .Setup(x => x.GetBorrowedBooks(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _service.CloseReader(id, CancellationToken.None);

        reader.IsActive.Should().BeFalse();

        _readerRepository.Verify(
            x => x.Update(id, reader, It.IsAny<CancellationToken>()),
            Times.Once);

        _kafkaProducer.Verify(
            x => x.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<ReaderClosedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
    
    [Fact]
    public async Task CloseReader_Throws_WhenReaderHasBorrowedBooks()
    {
        var id = Guid.NewGuid();

        var reader = new Reader
        {
            IsActive = true,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
        };

        _readerRepository
            .Setup(x => x.GetById(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reader);

        _readerRepository
            .Setup(x => x.GetBorrowedBooks(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BorrowedBookDto>
            {
                new()
            });

        var act = () => _service.CloseReader(id, CancellationToken.None);

        await act.Should()
            .ThrowAsync<ReaderServiceException>();

        _readerRepository.Verify(
            x => x.Update(
                It.IsAny<Guid>(),
                It.IsAny<Reader>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
    
    [Fact]
    public async Task GetBorrowedBooks_ReturnsFromCache()
    {
        var id = Guid.NewGuid();

        IReadOnlyList<BorrowedBookDto> cached =
        [
            new()
        ];

        _cacheService
            .Setup(x => x.GetAsync<Guid, IReadOnlyList<BorrowedBookDto>>(
                It.IsAny<string>(),
                It.IsAny<string>(),
                id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        var result = await _service.GetBorrowedBooks(id, CancellationToken.None);

        result.Should().BeEquivalentTo(cached);

        _readerRepository.Verify(
            x => x.GetBorrowedBooks(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
    
    [Fact]
    public async Task GetBorrowedBooks_LoadsFromRepository_WhenCacheMiss()
    {
        var id = Guid.NewGuid();

        IReadOnlyList<BorrowedBookDto> books =
        [
            new()
        ];

        _cacheService
            .Setup(x => x.GetAsync<Guid, IReadOnlyList<BorrowedBookDto>>(
                It.IsAny<string>(),
                It.IsAny<string>(),
                id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<BorrowedBookDto>)null!);

        _readerRepository
            .Setup(x => x.GetBorrowedBooks(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(books);

        var result = await _service.GetBorrowedBooks(id, CancellationToken.None);

        result.Should().BeEquivalentTo(books);

        _cacheService.Verify(
            x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                id,
                books,
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}