namespace RecruiterAI.Models;

// ============================================================================
// DTOs de los módulos de gestión (Postulantes, Posiciones, Clientes, Pipeline).
// Todas las requests viajan con el header "X-Workspace-Id" (ver
// WorkspaceResolutionMiddleware en Program.cs) — estos records no repiten
// workspaceId porque ya se resuelve ahí.
// ============================================================================

// ─── CLIENTES (CRM) ─────────────────────────────────────────────────────────

public record CreateClientRequest(
    string Name,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    string? Notes
);

public record UpdateClientRequest(
    string Name,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    string? Notes
);

public record ClientDto(
    Guid Id,
    string Name,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    string? Notes,
    int OpenPositionsCount,
    DateTime CreatedAt
);

// ─── POSICIONES / VACANTES ──────────────────────────────────────────────────

public record CreatePositionRequest(
    string Title,
    string Description,
    Guid? ClientId
);

public record UpdatePositionRequest(
    string Title,
    string Description,
    string Status, // abierta | pausada | cerrada
    Guid? ClientId
);

public record PositionDto(
    Guid Id,
    string Title,
    string Description,
    string Status,
    Guid? ClientId,
    string? ClientName,
    int CandidateCount,
    DateTime CreatedAt,
    DateTime? ClosedAt
);

// ─── POSTULANTES ─────────────────────────────────────────────────────────────

public record CreateCandidateRequest(
    string FullName,
    string? Email,
    string? Phone,
    string? LinkedinUrl,
    string? CvText,
    string? Source
);

public record UpdateCandidateRequest(
    string FullName,
    string? Email,
    string? Phone,
    string? LinkedinUrl,
    string? Source
);

public record AddCandidateNoteRequest(string Note);

public record CandidateNoteDto(Guid Id, string Note, DateTime CreatedAt);

public record CandidateDto(
    Guid Id,
    string FullName,
    string? Email,
    string? Phone,
    string? LinkedinUrl,
    string? Source,
    DateTime CreatedAt,
    List<CandidateNoteDto> Notes,
    List<CandidatePositionSummaryDto> Applications
);

public record CandidatePositionSummaryDto(
    Guid CandidatePositionId,
    Guid PositionId,
    string PositionTitle,
    Guid StageId,
    string StageName,
    DateTime AppliedAt
);

// ─── PIPELINE / KANBAN ───────────────────────────────────────────────────────

public record PipelineStageDto(
    Guid Id,
    string Name,
    int OrderIndex,
    bool IsTerminal
);

public record ApplyCandidateToPositionRequest(Guid CandidateId, Guid PositionId);

public record MoveCandidateStageRequest(Guid StageId, string? Notes);

public record KanbanCardDto(
    Guid CandidatePositionId,
    Guid CandidateId,
    string CandidateName,
    Guid StageId,
    DateTime AppliedAt
);

public record KanbanColumnDto(
    Guid StageId,
    string StageName,
    int OrderIndex,
    bool IsTerminal,
    List<KanbanCardDto> Cards
);

public record KanbanBoardDto(
    Guid PositionId,
    string PositionTitle,
    List<KanbanColumnDto> Columns
);

public record StageHistoryEntryDto(
    Guid StageId,
    string StageName,
    DateTime ChangedAt,
    string? Notes
);
