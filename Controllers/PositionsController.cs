using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruiterAI.Data;
using RecruiterAI.Models;
using RecruiterAI.Models.Entities;

namespace RecruiterAI.Controllers;

/// <summary>Módulo de Posiciones/Vacantes (punto 3 del roadmap de producto).</summary>
[Route("api/positions")]
public class PositionsController(RecruiterAIDbContext db, ILogger<PositionsController> logger)
    : ManagementControllerBase
{
    private readonly RecruiterAIDbContext _db = db;
    private readonly ILogger<PositionsController> _logger = logger;

    private static readonly string[] ValidStatuses = { "abierta", "pausada", "cerrada" };

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status)
    {
        if (!TryGetWorkspaceId(out var workspaceId, out var err)) return err!;

        var query = _db.Positions.Where(p => p.WorkspaceId == workspaceId);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(p => p.Status == status);

        var positions = await query
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PositionDto(
                p.Id, p.Title, p.Description, p.Status, p.ClientId,
                p.Client != null ? p.Client.Name : null,
                p.CandidatePositions.Count,
                p.CreatedAt, p.ClosedAt
            ))
            .ToListAsync();

        return Ok(positions);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        if (!TryGetWorkspaceId(out var workspaceId, out var err)) return err!;

        var position = await _db.Positions
            .Where(p => p.Id == id && p.WorkspaceId == workspaceId)
            .Select(p => new PositionDto(
                p.Id, p.Title, p.Description, p.Status, p.ClientId,
                p.Client != null ? p.Client.Name : null,
                p.CandidatePositions.Count,
                p.CreatedAt, p.ClosedAt
            ))
            .FirstOrDefaultAsync();

        if (position == null) return NotFound(new { error = "Posición no encontrada." });
        return Ok(position);
    }

    /// <summary>
    /// Crea la posición y le copia el pipeline default del workspace
    /// (las etapas con position_id NULL) para que el kanban funcione
    /// desde el primer candidato.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePositionRequest request)
    {
        if (!TryGetWorkspaceId(out var workspaceId, out var err)) return err!;

        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { error = "El título de la posición es obligatorio." });
        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest(new { error = "La descripción del puesto es obligatoria." });

        if (request.ClientId.HasValue)
        {
            var clientExists = await _db.Clients.AnyAsync(c => c.Id == request.ClientId && c.WorkspaceId == workspaceId);
            if (!clientExists) return BadRequest(new { error = "El cliente indicado no existe en este workspace." });
        }

        var position = new Position
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ClientId = request.ClientId,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            Status = "abierta",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Positions.Add(position);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Posición creada {PositionId} en workspace {WorkspaceId}", position.Id, workspaceId);

        var clientName = request.ClientId.HasValue
            ? await _db.Clients.Where(c => c.Id == request.ClientId).Select(c => c.Name).FirstOrDefaultAsync()
            : null;

        return CreatedAtAction(nameof(Get), new { id = position.Id },
            new PositionDto(position.Id, position.Title, position.Description, position.Status,
                position.ClientId, clientName, 0, position.CreatedAt, position.ClosedAt));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePositionRequest request)
    {
        if (!TryGetWorkspaceId(out var workspaceId, out var err)) return err!;

        var position = await _db.Positions.FirstOrDefaultAsync(p => p.Id == id && p.WorkspaceId == workspaceId);
        if (position == null) return NotFound(new { error = "Posición no encontrada." });

        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { error = "El título de la posición es obligatorio." });

        if (!ValidStatuses.Contains(request.Status))
            return BadRequest(new { error = $"Status inválido. Debe ser uno de: {string.Join(", ", ValidStatuses)}." });

        position.Title = request.Title.Trim();
        position.Description = request.Description.Trim();
        position.ClientId = request.ClientId;

        if (position.Status != "cerrada" && request.Status == "cerrada")
            position.ClosedAt = DateTime.UtcNow;
        else if (request.Status != "cerrada")
            position.ClosedAt = null;

        position.Status = request.Status;
        position.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var clientName = position.ClientId.HasValue
            ? await _db.Clients.Where(c => c.Id == position.ClientId).Select(c => c.Name).FirstOrDefaultAsync()
            : null;

        return Ok(new PositionDto(position.Id, position.Title, position.Description, position.Status,
            position.ClientId, clientName,
            await _db.CandidatePositions.CountAsync(cp => cp.PositionId == position.Id),
            position.CreatedAt, position.ClosedAt));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!TryGetWorkspaceId(out var workspaceId, out var err)) return err!;

        var position = await _db.Positions.FirstOrDefaultAsync(p => p.Id == id && p.WorkspaceId == workspaceId);
        if (position == null) return NotFound(new { error = "Posición no encontrada." });

        _db.Positions.Remove(position);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
