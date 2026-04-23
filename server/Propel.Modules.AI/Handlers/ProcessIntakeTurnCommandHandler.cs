using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;
using Propel.Modules.AI.Commands;
using Propel.Modules.AI.Dtos;
using Propel.Modules.AI.Exceptions;
using Propel.Modules.AI.Interfaces;
using Propel.Modules.AI.Models;
using Propel.Modules.AI.Services;

namespace Propel.Modules.AI.Handlers;

/// <summary>
/// Handles <see cref="ProcessIntakeTurnCommand"/> for <c>POST /api/intake/ai/message</c>
/// (US_028, AC-2, AC-3, AIR-O02).
/// <list type="number">
///   <item><b>Load session</b>: retrieve from <see cref="IntakeSessionStore"/> and validate ownership (OWASP A01).</item>
///   <item><b>Append user turn</b>: add the patient utterance to conversation history.</item>
///   <item><b>Delegate to AI service</b>: call <see cref="IAiIntakeService.ProcessTurnAsync"/>.</item>
///   <item><b>Circuit-breaker path</b>: when <see cref="AiServiceUnavailableException"/> is thrown,
///         return <c>{ isFallback: true, preservedFields: [...] }</c> with HTTP 200 (AIR-O02).</item>
///   <item><b>Normal path</b>: append assistant turn; merge extracted fields; return
///         <see cref="AiTurnResponseDto"/> with <c>IsFallback = false</c>.</item>
///   <item><b>Audit</b>: log <c>"AiIntakeTurnProcessed"</c> with session and field stats (AD-7).</item>
/// </list>
/// </summary>
public sealed class ProcessIntakeTurnCommandHandler
    : IRequestHandler<ProcessIntakeTurnCommand, AiTurnResponseDto>
{
    private readonly IntakeSessionStore _sessionStore;
    private readonly IAiIntakeService _aiIntakeService;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly ILogger<ProcessIntakeTurnCommandHandler> _logger;

    public ProcessIntakeTurnCommandHandler(
        IntakeSessionStore sessionStore,
        IAiIntakeService aiIntakeService,
        IAuditLogRepository auditLogRepo,
        ILogger<ProcessIntakeTurnCommandHandler> logger)
    {
        _sessionStore = sessionStore;
        _aiIntakeService = aiIntakeService;
        _auditLogRepo = auditLogRepo;
        _logger = logger;
    }

    public async Task<AiTurnResponseDto> Handle(
        ProcessIntakeTurnCommand request,
        CancellationToken cancellationToken)
    {
        // Step 1 — Load session and validate ownership (OWASP A01).
        var session = _sessionStore.GetSession(request.SessionId)
            ?? throw new KeyNotFoundException(
                $"AI intake session '{request.SessionId}' was not found or has expired.");

        if (session.PatientId != request.PatientId)
        {
            _logger.LogWarning(
                "ProcessIntakeTurn_Forbidden: PatientId={PatientId} attempted to access " +
                "session {SessionId} owned by PatientId={OwnerPatientId}",
                request.PatientId, request.SessionId, session.PatientId);

            throw new AiForbiddenAccessException(
                $"AI intake session '{request.SessionId}' does not belong to the requesting patient.");
        }

        // Step 2 — Append user turn to history.
        var userTurn = new ConversationTurn("user", request.UserMessage);
        _sessionStore.AddTurn(request.SessionId, userTurn);

        // Step 3 — Delegate to AI service; handle circuit-breaker gracefully (AIR-O02).
        IReadOnlyList<ConversationTurn> historySnapshot;
        IReadOnlyList<ExtractedField> currentFieldsSnapshot;

        lock (session.History)
        {
            historySnapshot = [.. session.History];
        }

        lock (session.ExtractedFields)
        {
            currentFieldsSnapshot = [.. session.ExtractedFields];
        }

        try
        {
            var result = await _aiIntakeService.ProcessTurnAsync(
                historySnapshot,
                currentFieldsSnapshot,
                cancellationToken);

            // Step 4 — Append assistant turn and merge extracted fields.
            if (!string.IsNullOrEmpty(result.AiResponse))
            {
                var assistantTurn = new ConversationTurn("assistant", result.AiResponse);
                _sessionStore.AddTurn(request.SessionId, assistantTurn);
            }

            if (result.ExtractedFields.Count > 0)
            {
                _sessionStore.MergeFields(request.SessionId, result.ExtractedFields);
            }

            var extractedDtos = result.ExtractedFields
                .Select(f => new ExtractedFieldDto(f.FieldName, f.Value, f.Confidence, f.NeedsClarification))
                .ToList();

            var avgConfidence = extractedDtos.Count > 0
                ? extractedDtos.Average(f => f.Confidence)
                : 0.0;

            // Step 5 — Audit log for the processed turn.
            await _auditLogRepo.AppendAsync(new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = request.PatientId,
                PatientId = request.PatientId,
                Role = "Patient",
                Action = "AiIntakeTurnProcessed",
                EntityType = "IntakeSession",
                EntityId = request.SessionId,
                Details = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    sessionId = request.SessionId,
                    fieldsExtractedCount = extractedDtos.Count,
                    avgConfidence = Math.Round(avgConfidence, 3),
                    isFallback = false
                })),
                Timestamp = DateTime.UtcNow
            }, cancellationToken);

            _logger.LogInformation(
                "AI intake turn processed: SessionId={SessionId} FieldsExtracted={Count} AvgConfidence={Confidence:F3}",
                request.SessionId, extractedDtos.Count, avgConfidence);

            return new AiTurnResponseDto(
                IsFallback: false,
                AiResponse: result.AiResponse,
                ExtractedFields: extractedDtos,
                PreservedFields: null);
        }
        catch (AiServiceUnavailableException ex)
        {
            // Circuit-breaker path: return HTTP 200 with isFallback=true so the frontend
            // can switch to manual intake without surfacing a 5xx error (AIR-O02).
            _logger.LogWarning(ex,
                "AI service unavailable for SessionId={SessionId} — returning graceful fallback.",
                request.SessionId);

            var preserved = currentFieldsSnapshot
                .Select(f => new ExtractedFieldDto(f.FieldName, f.Value, f.Confidence, f.NeedsClarification))
                .ToList();

            await _auditLogRepo.AppendAsync(new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = request.PatientId,
                PatientId = request.PatientId,
                Role = "Patient",
                Action = "AiIntakeTurnProcessed",
                EntityType = "IntakeSession",
                EntityId = request.SessionId,
                Details = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    sessionId = request.SessionId,
                    fieldsExtractedCount = 0,
                    avgConfidence = 0.0,
                    isFallback = true,
                    reason = ex.Message
                })),
                Timestamp = DateTime.UtcNow
            }, cancellationToken);

            return new AiTurnResponseDto(
                IsFallback: true,
                AiResponse: null,
                ExtractedFields: null,
                PreservedFields: preserved);
        }
    }
}
