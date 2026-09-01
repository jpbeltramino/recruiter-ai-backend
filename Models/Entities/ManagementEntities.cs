namespace RecruiterAI.Models.Entities;

// ============================================================================
// Entidades EF Core del schema v2 (ver Database/schema.sql y Database/SCHEMA.md).
// Cada clase mapea 1:1 contra una tabla — el mapeo explícito de nombres de
// tabla/columna vive en Data/RecruiterAIDbContext.cs (OnModelCreating).
// ============================================================================

public class Workspace
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PlanTier { get; set; } = "free"; // free | solo | agencia
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<WorkspaceMember> Members { get; set; } = new();
    public List<Client> Clients { get; set; } = new();
    public List<Position> Positions { get; set; } = new();
    public List<Candidate> Candidates { get; set; } = new();
    public List<PipelineStage> PipelineStages { get; set; } = new();
    public List<Subscription> Subscriptions { get; set; } = new();
}

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class WorkspaceMember
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string Role { get; set; } = "recruiter"; // owner | admin | recruiter
    public DateTime CreatedAt { get; set; }
}

public class Client
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<Position> Positions { get; set; } = new();
}

public class Position
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public Guid? ClientId { get; set; }
    public Client? Client { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "abierta"; // abierta | pausada | cerrada
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    public List<PipelineStage> CustomStages { get; set; } = new();
    public List<CandidatePosition> CandidatePositions { get; set; } = new();
    public List<JobAdGeneration> JobAdGenerations { get; set; } = new();
}

public class PipelineStage
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public Guid? PositionId { get; set; } // null = plantilla default del workspace
    public Position? Position { get; set; }
    public string Name { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
    public bool IsTerminal { get; set; }
    public string? Color { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<CandidatePosition> CandidatePositionsInStage { get; set; } = new();
    public List<CandidateStageHistory> StageHistoryEntries { get; set; } = new();
}

public class Candidate
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? LinkedinUrl { get; set; }
    public string? CvText { get; set; }
    public string? CvFileUrl { get; set; }
    public string? Source { get; set; } // manual | linkedin | portal | referido
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<CandidateNote> Notes { get; set; } = new();
    public List<CandidatePosition> CandidatePositions { get; set; } = new();
    public List<CvAnalysis> CvAnalyses { get; set; } = new();
    public List<InconsistencyReport> InconsistencyReports { get; set; } = new();
    public List<InterviewQuestionSet> InterviewQuestionSets { get; set; } = new();
    public List<LinkedInAnalysis> LinkedInAnalyses { get; set; } = new();
}

public class CandidateNote
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public Candidate? Candidate { get; set; }
    public Guid? AuthorId { get; set; }
    public string Note { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// La postulación de un candidato a una posición — el "card" del kanban.
/// </summary>
public class CandidatePosition
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public Candidate? Candidate { get; set; }
    public Guid PositionId { get; set; }
    public Position? Position { get; set; }
    public Guid CurrentStageId { get; set; }
    public PipelineStage? CurrentStage { get; set; }
    public DateTime AppliedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<CandidateStageHistory> StageHistory { get; set; } = new();
}

public class CandidateStageHistory
{
    public Guid Id { get; set; }
    public Guid CandidatePositionId { get; set; }
    public CandidatePosition? CandidatePosition { get; set; }
    public Guid StageId { get; set; }
    public PipelineStage? Stage { get; set; }
    public Guid? ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? Notes { get; set; }
}

// ─── Resultados de IA persistidos ──────────────────────────────────────────

public class CvAnalysis
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public Candidate? Candidate { get; set; }
    public Guid? PositionId { get; set; }
    public Position? Position { get; set; }
    public int Score { get; set; }
    public List<string> Strengths { get; set; } = new();
    public List<string> Weaknesses { get; set; } = new();
    public string Verdict { get; set; } = string.Empty; // AVANZAR | REVISAR | DESCARTAR
    public string? Reasoning { get; set; } // por qué del score — IA transparente
    public string? RawResponse { get; set; } // JSON crudo de Claude, para auditoría
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class InconsistencyReport
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public Candidate? Candidate { get; set; }
    public List<InconsistencyFindingJson> Findings { get; set; } = new();
    public string? Summary { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Forma serializada de cada finding dentro de InconsistencyReport.Findings (jsonb).</summary>
public record InconsistencyFindingJson(
    string Category,
    string Description,
    string RiskLevel,
    string SuggestedQuestion
);

public class InterviewQuestionSet
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public Candidate? Candidate { get; set; }
    public Guid? PositionId { get; set; }
    public Position? Position { get; set; }
    public List<InterviewQuestionJson> Technical { get; set; } = new();
    public List<InterviewQuestionJson> Cultural { get; set; } = new();
    public List<InterviewQuestionJson> WeaknessValidation { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public record InterviewQuestionJson(
    string Question,
    string WhatToValidate,
    string StrongAnswerIndicator
);

public class LinkedInAnalysis
{
    public Guid Id { get; set; }
    public Guid? CandidateId { get; set; } // nullable: se puede analizar antes de crear el candidato
    public Candidate? Candidate { get; set; }
    public Guid? PositionId { get; set; }
    public Position? Position { get; set; }
    public string? ProfileText { get; set; }
    public string AlignmentLevel { get; set; } = string.Empty; // ALTO | MEDIO | BAJO
    public List<string> PositiveSignals { get; set; } = new();
    public List<string> RedFlags { get; set; } = new();
    public List<string> ScreeningQuestions { get; set; } = new();
    public string Recommendation { get; set; } = string.Empty; // CONTACTAR | EVALUAR MAS | NO CONTACTAR
    public string? RecommendationReason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class JobAdGeneration
{
    public Guid Id { get; set; }
    public Guid PositionId { get; set; }
    public Position? Position { get; set; }
    public string? Platform { get; set; } // linkedin | computrabajo | zonajobs | generico
    public string GeneratedText { get; set; } = string.Empty;
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ─── Monetización ───────────────────────────────────────────────────────────

public class Subscription
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public string? MpPreapprovalId { get; set; }
    public string PlanTier { get; set; } = string.Empty; // free | solo | agencia
    public string Status { get; set; } = "pending"; // pending | authorized | paused | cancelled
    public DateTime? CurrentPeriodEnd { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
