using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruiterAI.Data;
using RecruiterAI.Models;
using RecruiterAI.Models.Entities;

namespace RecruiterAI.Controllers;

/// <summary>Módulo de Postulantes persistentes (punto 2 del roadmap de producto).</summary>
[Route("api/candidates")]
public class CandidatesController(RecruiterAIDbContext db, ILogger<CandidatesController> logger)
    : ManagementControllerBase
{
    private readonly RecruiterAIDbContext _db = db;
    private readonly ILogger<CandidatesController> _logger = logger;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search)
    {
        if (!TryGetWorkspaceId(out var workspaceId, out var err)) return err!;

        var query = CandidatesWithDetails().Where(c => c.WorkspaceId == workspaceId);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c => c.FullName.ToLower().Contains(search.ToLower()));

        var candidates = await query
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        return Ok(candidates.Select(ToDto));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        if (!TryGetWorkspaceId(out var workspaceId, out var err)) return err!;

        var candidate = await CandidatesWithDetails()
            .FirstOrDefaultAsync(c => c.Id == id && c.WorkspaceId == workspaceId);

        if (candidate == null) return NotFound(new { error = "Postulante no encontrado." });
        return Ok(ToDto(candidate));
    }

    /// <summary>
    /// Incluye las navegaciones que necesita ToDto — EF Core no puede traducir
    /// una llamada a un método C# arbitrario dentro de un Select() a SQL, así
    /// que el mapeo a DTO siempre se hace en memoria, después de traer los
    /// datos con Include().
    /// </summary>
    private IQueryable<Candidate> CandidatesWithDetails() =>
        _db.Candidates
            .Include(c => c.Notes)
            .Include(c => c.CandidatePositions).ThenInclude(cp => cp.Position)
            .Include(c => c.CandidatePositions).ThenInclude(cp => cp.CurrentStage);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCandidateRequest request)
    {
        if (!TryGetWorkspaceId(out var workspaceId, out var err)) return err!;

        if (string.IsNullOrWhiteSpace(request.FullName))
            return BadRequest(new { error = "El nombre del postulante es obligatorio." });

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            FullName = request.FullName.Trim(),
            Email = request.Email,
            Phone = request.Phone,
            LinkedinUrl = request.LinkedinUrl,
            CvText = request.CvText,
            Source = request.Source ?? "manual",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Candidates.Add(candidate);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Postulante creado {CandidateId} en workspace {WorkspaceId}", candidate.Id, workspaceId);

        // Recién creado: no tiene notas ni postulaciones todavía, no hace falta reconsultar.
        candidate.Notes = new List<CandidateNote>();
        candidate.CandidatePositions = new List<CandidatePosition>();

        return CreatedAtAction(nameof(Get), new { id = candidate.Id }, ToDto(candidate));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCandidateRequest request)
    {
        if (!TryGetWorkspaceId(out var workspaceId, out var err)) return err!;

        var candidate = await _db.Candidates.FirstOrDefaultAsync(c => c.Id == id && c.WorkspaceId == workspaceId);
        if (candidate == null) return NotFound(new { error = "Postulante no encontrado." });

        if (string.IsNullOrWhiteSpace(request.FullName))
            return BadRequest(new { error = "El nombre del postulante es obligatorio." });

        candidate.FullName = request.FullName.Trim();
        candidate.Email = request.Email;
        candidate.Phone = request.Phone;
        candidate.LinkedinUrl = request.LinkedinUrl;
        candidate.Source = request.Source;
        candidate.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var full = await CandidatesWithDetails().FirstAsync(c => c.Id == candidate.Id);
        return Ok(ToDto(full));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!TryGetWorkspaceId(out var workspaceId, out var err)) return err!;

        var candidate = await _db.Candidates.FirstOrDefaultAsync(c => c.Id == id && c.WorkspaceId == workspaceId);
        if (candidate == null) return NotFound(new { error = "Postulante no encontrado." });

        _db.Candidates.Remove(candidate);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/notes")]
    public async Task<IActionResult> AddNote(Guid id, [FromBody] AddCandidateNoteRequest request)
    {
        if (!TryGetWorkspaceId(out var workspaceId, out var err)) return err!;

        var candidateExists = await _db.Candidates.AnyAsync(c => c.Id == id && c.WorkspaceId == workspaceId);
        if (!candidateExists) return NotFound(new { error = "Postulante no encontrado." });

        if (string.IsNullOrWhiteSpace(request.Note))
            return BadRequest(new { error = "La nota no puede estar vacía." });

        var note = new CandidateNote
        {
            Id = Guid.NewGuid(),
            CandidateId = id,
            Note = request.Note.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _db.CandidateNotes.Add(note);
        await _db.SaveChangesAsync();

        return Ok(new CandidateNoteDto(note.Id, note.Note, note.CreatedAt));
    }

    private static CandidateDto ToDto(Candidate c) => new(
        c.Id, c.FullName, c.Email, c.Phone, c.LinkedinUrl, c.Source, c.CreatedAt,
        c.Notes
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new CandidateNoteDto(n.Id, n.Note, n.CreatedAt))
            .ToList(),
        c.CandidatePositions
            .Select(cp => new CandidatePositionSummaryDto(
                cp.Id, cp.PositionId, cp.Position!.Title,
                cp.CurrentStageId, cp.CurrentStage!.Name, cp.AppliedAt))
            .ToList()
    );
}
