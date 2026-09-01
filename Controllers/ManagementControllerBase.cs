using Microsoft.AspNetCore.Mvc;

namespace RecruiterAI.Controllers;

/// <summary>
/// Base para los controllers de gestión (candidates/positions/clients/pipeline).
/// Todos requieren el header "X-Workspace-Id" — la resolución de qué workspaces
/// puede ver cada usuario autenticado queda pendiente (ver Database/SCHEMA.md,
/// sección "lo que esto no resuelve": auth real todavía usa Auth:ValidTokens).
/// Por ahora cualquier token válido puede operar sobre el workspace que pase
/// en el header — es el mismo nivel de confianza que el resto del backend hoy.
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class ManagementControllerBase : ControllerBase
{
    protected const string WorkspaceHeader = "X-Workspace-Id";

    /// <summary>
    /// Devuelve el workspaceId del header, o null si falta/es inválido
    /// (en ese caso ya dejó cargada la BadRequest en <paramref name="error"/>).
    /// </summary>
    protected bool TryGetWorkspaceId(out Guid workspaceId, out IActionResult? error)
    {
        var raw = Request.Headers[WorkspaceHeader].ToString();

        if (string.IsNullOrWhiteSpace(raw))
        {
            workspaceId = Guid.Empty;
            error = BadRequest(new { error = $"Falta el header {WorkspaceHeader}." });
            return false;
        }

        if (!Guid.TryParse(raw, out workspaceId))
        {
            error = BadRequest(new { error = $"{WorkspaceHeader} inválido." });
            return false;
        }

        error = null;
        return true;
    }
}
