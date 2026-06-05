using System.Text.Json;
using PracticalWork.Reports.Abstractions.Storage;
using PracticalWork.Reports.Enums;
using PracticalWork.Reports.Models;
using PracticalWork.Shared.Abstractions.Interfaces;
using PracticalWork.Shared.Contracts.Events.Readers;

namespace PracticalWork.Reports.Features.Readers.Create;

/// <summary>
/// Обработчик события создания новой карточки читателя
/// </summary>
public class ReaderCreatedEventHandler: IEventHandler<ReaderCreatedEvent>
{
    private readonly IActivityLogRepository _activityLogRepository;

    public ReaderCreatedEventHandler(IActivityLogRepository activityLogRepository)
    {
        _activityLogRepository = activityLogRepository;
    }

    /// <inheritdoc cref="IEventHandler{T}.HandleAsync"/>
    public async Task HandleAsync(ReaderCreatedEvent message, CancellationToken cancellationToken)
    {
        var metadata = JsonDocument.Parse(JsonSerializer.Serialize(message));
        var activityLog = ActivityLog.Create(ActivityEventType.ReaderCreated, metadata);
        
        await _activityLogRepository.Add(activityLog, cancellationToken, readerId: message.ReaderId);
    }
}