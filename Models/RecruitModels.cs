namespace RecruiterAI.Models;

// ─── CV RANKER ───────────────────────────────────────────────────────────────

public record RankCvsRequest(
    string JobDescription,
    List<CvInput> Candidates
);

public record CvInput(
    string Name,
    string? Text,       // plain text (optional if PDF provided)
    string? PdfBase64   // base64-encoded PDF (optional)
);

public record RankCvsResponse(
    List<CandidateRanking> Rankings
);

public record CandidateRanking(
    string Name,
    int Score,
    List<string> Strengths,
    List<string> Weaknesses,
    string Verdict        // AVANZAR | REVISAR | DESCARTAR
);

// ─── INCONSISTENCY DETECTOR ──────────────────────────────────────────────────

public record DetectInconsistenciesRequest(
    string? CvText,
    string? PdfBase64
);

public record DetectInconsistenciesResponse(
    List<InconsistencyFinding> Findings,
    string Summary
);

public record InconsistencyFinding(
    string Category,
    string Description,
    string RiskLevel,       // ALTO | MEDIO | BAJO
    string SuggestedQuestion
);

// ─── INTERVIEW QUESTION GENERATOR ────────────────────────────────────────────

public record GenerateQuestionsRequest(
    string JobDescription,
    string? CvText,
    string? PdfBase64
);

public record GenerateQuestionsResponse(
    List<InterviewQuestion> Technical,
    List<InterviewQuestion> Cultural,
    List<InterviewQuestion> WeaknessValidation
);

public record InterviewQuestion(
    string Question,
    string WhatToValidate,
    string StrongAnswerIndicator
);

// ─── LINKEDIN PROFILE ANALYZER ───────────────────────────────────────────────

public record AnalyzeLinkedInRequest(
    string JobDescription,
    string ProfileText     // pasted text or URL content
);

public record AnalyzeLinkedInResponse(
    string AlignmentLevel,      // ALTO | MEDIO | BAJO
    List<string> PositiveSignals,
    List<string> RedFlags,
    List<string> ScreeningQuestions,
    string Recommendation,      // CONTACTAR | EVALUAR MÁS | NO CONTACTAR
    string RecommendationReason
);

// ─── SHARED ERROR ─────────────────────────────────────────────────────────────

public record ErrorResponse(string Error);

public record UnifiedAnalysisRequest(
    string JobDescription,
    List<CvInput> Candidates
);

public record UnifiedCandidateResult(
    string Name,
    int Score,
    List<string> Strengths,
    List<string> Weaknesses,
    string Verdict,
    DetectInconsistenciesResponse Inconsistencies,
    GenerateQuestionsResponse Questions
);

public record UnifiedAnalysisResponse(
    List<UnifiedCandidateResult> Candidates
);


