using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruiterAI.Data;
using RecruiterAI.Models.Entities;

namespace RecruiterAI.Controllers;

/// <summary>
/// Bootstrap de workspaces (multi-tenancy, punto 11). Minimal a propósito:
/// todavía no hay auth real ni workspace_members reforzado en el middleware
/// (ver Database/SCHEMA.md). Alcanza para crear el workspace inicial y
/// obtener su id para usarlo en el header X-Workspace-Id del resto de la API.
/// </summary>
[ApiController]
[Produces("application/json")]
[Route("api/workspaces")]
public class WorkspacesController(RecruiterAIDbContext db, ILogger<WorkspacesController> logger) : ControllerBase
{
    private readonly RecruiterAIDbContext _db = db;
    private readonly ILogger<WorkspacesController> _logger = logger;

    public record CreateWorkspaceRequest(string Name, string PlanTier = "free");
    public record WorkspaceDto(Guid Id, string Name, string PlanTier, DateTime CreatedAt);

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var workspaces = await _db.Workspaces
            .OrderBy(w => w.Name)
            .Select(w => new WorkspaceDto(w.Id, w.Name, w.PlanTier, w.CreatedAt))
            .ToListAsync();

        return Ok(workspaces);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWorkspaceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "El nombre del workspace es obligatorio." });

        var validTiers = new[] { "free", "solo", "agencia" };
        if (!validTiers.Contains(request.PlanTier))
            return BadRequest(new { error = $"plan_tier inválido. Debe ser uno de: {string.Join(", ", validTiers)}." });

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            PlanTier = request.PlanTier,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // El INSERT dispara el trigger seed_default_pipeline_stages() en Postgres
        // (ver Database/schema.sql), que crea las 6 etapas default del pipeline.
        _db.Workspaces.Add(workspace);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Workspace creado {WorkspaceId} ({Name})", workspace.Id, workspace.Name);

        return CreatedAtAction(nameof(List), null,
            new WorkspaceDto(workspace.Id, workspace.Name, workspace.PlanTier, workspace.CreatedAt));
    }
}
