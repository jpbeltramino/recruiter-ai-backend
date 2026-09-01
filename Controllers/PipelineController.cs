using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruiterAI.Data;
using RecruiterAI.Models;
using RecruiterAI.Models.Entities;

namespace RecruiterAI.Controllers;

/// <summary>
/// Pipeline por etapas / kanban (punto 4 del roadmap de producto).
/// Las etapas de una posición son las suyas propias si tiene stages custom
/// (pipeline_stages.position_id = la posición), o si no las del workspace
/// (pipeline_stages.position_id IS NULL, sembradas automáticamente al crear
/// el workspace — ver el trigger en Database/schema.sql).
/// </summary>
[Route("api/pipeline")]
public class PipelineController(RecruiterAIDbContext db, ILogger<PipelineController> logger)
    : ManagementControllerBase
{
    private readonly RecruiterAIDbContext _db = db;
    private readonly ILogger<PipelineController> _logger = logger;

    /// <summary>GET /api/pipeline/positions/{positionId}/stages — las etapas que aplican a esta posición.</summary>
    [HttpGet("positions/{positionId:guid}/stages")]
    public async Task<IActionResult> GetStages(Guid positionId)
    {
        if (!TryGetWorkspaceId(out var workspaceId, out var err)) return err!;

        var position = await _db.Positions.FirstOrDefaultAsync(p => p.Id == positionId && p.WorkspaceId == workspaceId);
        if (position == null) return NotFound(new { error = "Posición no encontrada." });

        var stages = await ResolveStagesAsync(workspaceId, positionId);
        return Ok(stages.Select(s => new PipelineStageDto(s.Id, s.Name, s.OrderIndex, s.IsTerminal)));
    }

    /// <summary>GET /api/pipeline/positions/{positionId}/board — el kanban completo de la posición.</summary>
    [HttpGet("positions/{positionId:guid}/board")]
    public async Task<IActionResult> GetBoard(Guid positionId)
    {
        if (!TryGetWorkspaceId(out var workspaceId, out var err)) return err!;

        var position = await _db.Positions.FirstOrDefaultAsync(p => p.Id == positionId && p.WorkspaceId == workspaceId);
        if (position == null) return NotFound(new { error = "Posición no encontrada." });

        var stages = await ResolveStagesAsync(workspaceId, positionId);

        var cards = await _db.CandidatePositions
            .Where(cp => cp.PositionId == positionId)
            .Select(cp => new
            {
                cp.Id,
                cp.CandidateId,
                CandidateName = cp.Candidate!.FullName,
                cp.CurrentStageId,
                cp.AppliedAt
            })
            .ToListAsync();

        var columns = stages.Select(s => new KanbanColumnDto(
            s.Id, s.Name, s.OrderIndex, s.IsTerminal,
            cards.Where(c => c.CurrentStageId == s.Id)
                 .Select(c => new KanbanCardDto(c.Id, c.CandidateId, c.CandidateName, c.CurrentStageId, c.AppliedAt))
                 .OrderBy(c => c.AppliedAt)
                 .ToList()
        )).ToList();

        return Ok(new KanbanBoardDto(position.Id, position.Title, columns));
    }

    /// <summary>
    /// POST /api/pipeline/applications — postula/asigna un candidato a una posición.
    /// Lo crea en la primera etapa (menor order_index) del pipeline que le corresponda.
    /// </summary>
    [HttpPost("applications")]
    public async Task<IActionResult> ApplyCandidate([FromBody] ApplyCandidateToPositionRequest request)
    {
        if (!TryGetWorkspaceId(out var workspaceId, out var err)) return err!;

        var candidate = await _db.Candidates.FirstOrDefaultAsync(c => c.Id == request.CandidateId && c.WorkspaceId == workspaceId);
        if (candidate == null) return BadRequest(new { error = "El postulante no existe en este workspace." });

        var position = await _db.Positions.FirstOrDefaultAsync(p => p.Id == request.PositionId && p.WorkspaceId == workspaceId);
        if (position == null) return BadRequest(new { error = "La posición no existe en este workspace." });

        var alreadyApplied = await _db.CandidatePositions
            .AnyAsync(cp => cp.CandidateId == request.CandidateId && cp.PositionId == request.PositionId);
        if (alreadyApplied)
            return Conflict(new { error = "El postulante ya está en el pipeline de esta posición." });

        var stages = await ResolveStagesAsync(workspaceId, request.PositionId);
        var firstStage = stages.FirstOrDefault();
        if (firstStage == null)
            return StatusCode(500, new { error = "El workspace no tiene etapas de pipeline configuradas." });

        var now = DateTime.UtcNow;
        var candidatePosition = new CandidatePosition
        {
            Id = Guid.NewGuid(),
            CandidateId = request.CandidateId,
            PositionId = request.PositionId,
            CurrentStageId = firstStage.Id,
            AppliedAt = now,
            UpdatedAt = now
        };

        _db.CandidatePositions.Add(candidatePosition);
        _db.CandidateStageHistories.Add(new CandidateStageHistory
        {
            Id = Guid.NewGuid(),
            CandidatePositionId = candidatePosition.Id,
            StageId = firstStage.Id,
            ChangedAt = now,
            Notes = "Postulación creada."
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation("Candidato {CandidateId} postulado a posición {PositionId} en etapa {StageId}",
            request.CandidateId, request.PositionId, firstStage.Id);

        return Ok(new KanbanCardDto(candidatePosition.Id, candidate.Id, candidate.FullName, firstStage.Id, now));
    }

    /// <summary>PATCH /api/pipeline/applications/{candidatePositionId}/stage — mueve el card en el kanban.</summary>
    [HttpPatch("applications/{candidatePositionId:guid}/stage")]
    public async Task<IActionResult> MoveStage(Guid candidatePositionId, [FromBody] MoveCandidateStageRequest request)
    {
        if (!TryGetWorkspaceId(out var workspaceId, out var err)) return err!;

        var candidatePosition = await _db.CandidatePositions
            .Include(cp => cp.Position)
            .FirstOrDefaultAsync(cp => cp.Id == candidatePositionId && cp.Position!.WorkspaceId == workspaceId);

        if (candidatePosition == null) return NotFound(new { error = "Postulación no encontrada." });

        var targetStage = await _db.PipelineStages
            .FirstOrDefaultAsync(s => s.Id == request.StageId && s.WorkspaceId == workspaceId);
        if (targetStage == null) return BadRequest(new { error = "La etapa indicada no existe en este workspace." });

        candidatePosition.CurrentStageId = targetStage.Id;
        candidatePosition.UpdatedAt = DateTime.UtcNow;

        _db.CandidateStageHistories.Add(new CandidateStageHistory
        {
            Id = Guid.NewGuid(),
            CandidatePositionId = candidatePosition.Id,
            StageId = targetStage.Id,
            ChangedAt = DateTime.UtcNow,
            Notes = request.Notes
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation("Postulación {CandidatePositionId} movida a etapa {StageId}", candidatePositionId, targetStage.Id);

        return Ok(new { candidatePositionId, stageId = targetStage.Id, stageName = targetStage.Name });
    }

    /// <summary>GET /api/pipeline/applications/{candidatePositionId}/history — auditoría de etapas.</summary>
    [HttpGet("applications/{candidatePositionId:guid}/history")]
    public async Task<IActionResult> GetHistory(Guid candidatePositionId)
    {
        if (!TryGetWorkspaceId(out var workspaceId, out var err)) return err!;

        var exists = await _db.CandidatePositions
            .AnyAsync(cp => cp.Id == candidatePositionId && cp.Position!.WorkspaceId == workspaceId);
        if (!exists) return NotFound(new { error = "Postulación no encontrada." });

        var history = await _db.CandidateStageHistories
            .Where(h => h.CandidatePositionId == candidatePositionId)
            .OrderBy(h => h.ChangedAt)
            .Select(h => new StageHistoryEntryDto(h.StageId, h.Stage!.Name, h.ChangedAt, h.Notes))
            .ToListAsync();

        return Ok(history);
    }

    /// <summary>
    /// Etapas custom de la posición si existen; si no, las default del workspace
    /// (position_id IS NULL), ordenadas por order_index.
    /// </summary>
    private async Task<List<PipelineStage>> ResolveStagesAsync(Guid workspaceId, Guid positionId)
    {
        var customStages = await _db.PipelineStages
            .Where(s => s.WorkspaceId == workspaceId && s.PositionId == positionId)
            .OrderBy(s => s.OrderIndex)
            .ToListAsync();

        if (customStages.Count > 0) return customStages;

        return await _db.PipelineStages
            .Where(s => s.WorkspaceId == workspaceId && s.PositionId == null)
            .OrderBy(s => s.OrderIndex)
            .ToListAsync();
    }
}
