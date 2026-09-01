using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruiterAI.Data;
using RecruiterAI.Models;
using RecruiterAI.Models.Entities;

namespace RecruiterAI.Controllers;

/// <summary>
/// CRM de clientes — para agencias/headhunters que llevan varias búsquedas
/// para distintos clientes (punto 5 del roadmap de producto).
/// </summary>
[Route("api/clients")]
public class ClientsController(RecruiterAIDbContext db, ILogger<ClientsController> logger)
    : ManagementControllerBase
{
    private readonly RecruiterAIDbContext _db = db;
    private readonly ILogger<ClientsController> _logger = logger;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (!TryGetWorkspaceId(out var workspaceId, out var err)) return err!;

        var clients = await _db.Clients
            .Where(c => c.WorkspaceId == workspaceId)
            .OrderBy(c => c.Name)
            .Select(c => new ClientDto(
                c.Id, c.Name, c.ContactName, c.ContactEmail, c.ContactPhone, c.Notes,
                c.Positions.Count(p => p.Status == "abierta"),
                c.CreatedAt
            ))
            .ToListAsync();

        return Ok(clients);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        if (!TryGetWorkspaceId(out var workspaceId, out var err)) return err!;

        var client = await _db.Clients
            .Where(c => c.Id == id && c.WorkspaceId == workspaceId)
            .Select(c => new ClientDto(
                c.Id, c.Name, c.ContactName, c.ContactEmail, c.ContactPhone, c.Notes,
                c.Positions.Count(p => p.Status == "abierta"),
                c.CreatedAt
            ))
            .FirstOrDefaultAsync();

        if (client == null) return NotFound(new { error = "Cliente no encontrado." });
        return Ok(client);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateClientRequest request)
    {
        if (!TryGetWorkspaceId(out var workspaceId, out var err)) return err!;

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "El nombre del cliente es obligatorio." });

        var client = new Client
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = request.Name.Trim(),
            ContactName = request.ContactName,
            ContactEmail = request.ContactEmail,
            ContactPhone = request.ContactPhone,
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Cliente creado {ClientId} en workspace {WorkspaceId}", client.Id, workspaceId);

        return CreatedAtAction(nameof(Get), new { id = client.Id },
            new ClientDto(client.Id, client.Name, client.ContactName, client.ContactEmail,
                client.ContactPhone, client.Notes, 0, client.CreatedAt));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateClientRequest request)
    {
        if (!TryGetWorkspaceId(out var workspaceId, out var err)) return err!;

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id && c.WorkspaceId == workspaceId);
        if (client == null) return NotFound(new { error = "Cliente no encontrado." });

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "El nombre del cliente es obligatorio." });

        client.Name = request.Name.Trim();
        client.ContactName = request.ContactName;
        client.ContactEmail = request.ContactEmail;
        client.ContactPhone = request.ContactPhone;
        client.Notes = request.Notes;
        client.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new ClientDto(client.Id, client.Name, client.ContactName, client.ContactEmail,
            client.ContactPhone, client.Notes,
            await _db.Positions.CountAsync(p => p.ClientId == client.Id && p.Status == "abierta"),
            client.CreatedAt));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!TryGetWorkspaceId(out var workspaceId, out var err)) return err!;

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id && c.WorkspaceId == workspaceId);
        if (client == null) return NotFound(new { error = "Cliente no encontrado." });

        _db.Clients.Remove(client);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
