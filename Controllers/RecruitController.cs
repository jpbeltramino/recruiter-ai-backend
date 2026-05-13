using Microsoft.AspNetCore.Mvc;
using RecruiterAI.Models;
using RecruiterAI.Services;

namespace RecruiterAI.Controllers;

[ApiController]
[Route("api/recruit")]
[Produces("application/json")]
public class RecruitController(
    ClaudeService claude,
    CvParserService parser,
    FileValidationService validator,
    JobService jobService,
    ILogger<RecruitController> logger) : ControllerBase
{
    private readonly ClaudeService _claude = claude;
    private readonly CvParserService _parser = parser;
    private readonly ILogger<RecruitController> _logger = logger;
    private readonly FileValidationService _validator = validator;
    private readonly JobService _jobService = jobService;

    // ─── 1. RANKEADOR DE CVs ─────────────────────────────────────────────────


    /// <summary>
    /// POST /api/recruit/rank-cvs
    /// Recibe varios CVs + descripción del puesto, devuelve ranking con puntajes.
    /// </summary>
    [HttpPost("rank-cvs")]
    public async Task<IActionResult> RankCvs([FromBody] RankCvsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.JobDescription))
            return BadRequest(new { error = "La descripción del puesto es obligatoria." });

        if (request.Candidates == null || request.Candidates.Count == 0)
            return BadRequest(new { error = "Debe proporcionar al menos un candidato." });

        try
        {
            var resolved = new List<(string Name, string CvText)>();

            foreach (var candidate in request.Candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate.Name))
                    return BadRequest(new { error = "Cada candidato debe tener un nombre." });

                var text = _parser.Resolve(candidate.Text, candidate.PdfBase64);
                resolved.Add((candidate.Name, text));
            }

            var result = await _claude.RankCvsAsync(request.JobDescription, resolved);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en RankCvs");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ─── 2. DETECTOR DE INCONSISTENCIAS ──────────────────────────────────────

    /// <summary>
    /// POST /api/recruit/detect-inconsistencies
    /// Analiza un CV en busca de inconsistencias, gaps y red flags.
    /// </summary>
    [HttpPost("detect-inconsistencies")]
    public async Task<IActionResult> DetectInconsistencies(
        [FromBody] DetectInconsistenciesRequest request)
    {
        try
        {
            var cvText = _parser.Resolve(request.CvText, request.PdfBase64);
            var result = await _claude.DetectInconsistenciesAsync(cvText);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en DetectInconsistencies");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ─── 3. GENERADOR DE PREGUNTAS ───────────────────────────────────────────

    /// <summary>
    /// POST /api/recruit/generate-questions
    /// Genera preguntas de entrevista personalizadas para el candidato y el puesto.
    /// </summary>
    [HttpPost("generate-questions")]
    public async Task<IActionResult> GenerateQuestions(
        [FromBody] GenerateQuestionsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.JobDescription))
            return BadRequest(new { error = "La descripción del puesto es obligatoria." });

        try
        {
            var cvText = _parser.Resolve(request.CvText, request.PdfBase64);
            var result = await _claude.GenerateQuestionsAsync(request.JobDescription, cvText);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en GenerateQuestions");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("analyze-unified")]
    public async Task<IActionResult> AnalyzeUnified([FromBody] UnifiedAnalysisRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.JobDescription))
            return BadRequest(new { error = "La descripción del puesto es obligatoria." });

        if (request.Candidates == null || request.Candidates.Count == 0)
            return BadRequest(new { error = "Debe proporcionar al menos un candidato." });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("AnalyzeUnified iniciado con {CandidateCount} candidatos",
            request.Candidates.Count);

        try
        {
            _validator.ValidateCandidateCount(request.Candidates.Count);

            var resolved = new List<(string Name, string CvText)>();
            foreach (var candidate in request.Candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate.Name))
                    return BadRequest(new { error = "Cada candidato debe tener un nombre." });

                if (!string.IsNullOrWhiteSpace(candidate.PdfBase64))
                    _validator.ValidatePdfBase64(candidate.PdfBase64, candidate.Name);

                var text = _parser.Resolve(candidate.Text, candidate.PdfBase64);
                text = _validator.TruncateText(text);
                resolved.Add((candidate.Name, text));
            }

            var results = await _claude.AnalyzeCandidatesParallelAsync(
                request.JobDescription,
                resolved
            );

            var response = new UnifiedAnalysisResponse(
                Candidates: results.OrderByDescending(c => c.Score).ToList()
            );

            _logger.LogInformation("AnalyzeUnified completado en {ElapsedMs}ms para {CandidateCount} candidatos",
                sw.ElapsedMilliseconds, request.Candidates.Count);

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Validación falló: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en AnalyzeUnified después de {ElapsedMs}ms", sw.ElapsedMilliseconds);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("analyze-deep")]
    public async Task<IActionResult> AnalyzeDeep([FromBody] DeepAnalysisRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.JobDescription))
            return BadRequest(new { error = "La descripción del puesto es obligatoria." });

        if (string.IsNullOrWhiteSpace(request.CandidateName))
            return BadRequest(new { error = "El nombre del candidato es obligatorio." });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("AnalyzeDeep iniciado para candidato {CandidateName}",
            request.CandidateName);

        try
        {
            if (!string.IsNullOrWhiteSpace(request.PdfBase64))
                _validator.ValidatePdfBase64(request.PdfBase64, request.CandidateName);

            var text = _parser.Resolve(request.Text, request.PdfBase64);
            text = _validator.TruncateText(text);

            var result = await _claude.AnalyzeCandidateDeepAsync(request.JobDescription, text);

            _logger.LogInformation("AnalyzeDeep completado en {ElapsedMs}ms para {CandidateName}",
                sw.ElapsedMilliseconds, request.CandidateName);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Validación falló en AnalyzeDeep: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en AnalyzeDeep después de {ElapsedMs}ms",
                sw.ElapsedMilliseconds);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("validate-token")]
    public IActionResult ValidateToken()
    {
        return Ok(new { valid = true });
    }

    [HttpPost("analyze-unified-stream")]
    public async Task AnalyzeUnifiedStream([FromBody] UnifiedAnalysisRequest request)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");
        Response.Headers.Append("X-Accel-Buffering", "no");

        if (string.IsNullOrWhiteSpace(request.JobDescription))
        {
            await WriteEventAsync(new { type = "error", message = "La descripción del puesto es obligatoria." });
            return;
        }

        if (request.Candidates == null || request.Candidates.Count == 0)
        {
            await WriteEventAsync(new { type = "error", message = "Debe proporcionar al menos un candidato." });
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("AnalyzeUnifiedStream iniciado con {CandidateCount} candidatos",
            request.Candidates.Count);

        try
        {
            _validator.ValidateCandidateCount(request.Candidates.Count);

            var resolved = new List<(string Name, string CvText)>();
            foreach (var candidate in request.Candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate.Name))
                {
                    await WriteEventAsync(new { type = "error", message = "Cada candidato debe tener un nombre." });
                    return;
                }

                if (!string.IsNullOrWhiteSpace(candidate.PdfBase64))
                    _validator.ValidatePdfBase64(candidate.PdfBase64, candidate.Name);

                var text = _parser.Resolve(candidate.Text, candidate.PdfBase64);
                text = _validator.TruncateText(text);
                resolved.Add((candidate.Name, text));
            }

            await WriteEventAsync(new
            {
                type = "start",
                total = resolved.Count
            });

            var results = await _claude.AnalyzeCandidatesParallelWithProgressAsync(
                request.JobDescription,
                resolved,
                async (result, current) =>
                {
                    await WriteEventAsync(new
                    {
                        type = "progress",
                        current,
                        total = resolved.Count,
                        candidateName = result.Name
                    });
                }
            );

            await WriteEventAsync(new
            {
                type = "complete",
                candidates = results.OrderByDescending(c => c.Score).ToList()
            });

            _logger.LogInformation("AnalyzeUnifiedStream completado en {ElapsedMs}ms para {CandidateCount} candidatos",
                sw.ElapsedMilliseconds, request.Candidates.Count);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Validación falló en stream: {Message}", ex.Message);
            await WriteEventAsync(new { type = "error", message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en AnalyzeUnifiedStream después de {ElapsedMs}ms", sw.ElapsedMilliseconds);
            await WriteEventAsync(new { type = "error", message = ex.Message });
        }
    }

    private async Task WriteEventAsync(object data)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
        await Response.WriteAsync($"data: {json}\n\n");
        await Response.Body.FlushAsync();
    }

    [HttpPost("analyze-job")]
    public IActionResult StartAnalysisJob([FromBody] UnifiedAnalysisRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.JobDescription))
            return BadRequest(new { error = "La descripción del puesto es obligatoria." });

        if (request.Candidates == null || request.Candidates.Count == 0)
            return BadRequest(new { error = "Debe proporcionar al menos un candidato." });

        try
        {
            _validator.ValidateCandidateCount(request.Candidates.Count);

            var resolved = new List<(string Name, string CvText)>();
            foreach (var candidate in request.Candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate.Name))
                    return BadRequest(new { error = "Cada candidato debe tener un nombre." });

                if (!string.IsNullOrWhiteSpace(candidate.PdfBase64))
                    _validator.ValidatePdfBase64(candidate.PdfBase64, candidate.Name);

                var text = _parser.Resolve(candidate.Text, candidate.PdfBase64);
                text = _validator.TruncateText(text);
                resolved.Add((candidate.Name, text));
            }

            var job = _jobService.CreateJob(resolved.Count);

            _ = Task.Run(async () =>
            {
                try
                {
                    var results = await _claude.AnalyzeCandidatesWithJobAsync(
                        request.JobDescription,
                        resolved,
                        job.Id,
                        _jobService
                    );

                    var ordered = results.OrderByDescending(c => c.Score).ToList();
                    _jobService.CompleteJob(job.Id, ordered);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error procesando job {JobId}", job.Id);
                    _jobService.FailJob(job.Id, ex.Message);
                }
            });

            return Ok(new JobStartedResponse(job.Id));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error iniciando job");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("jobs/{jobId}")]
    public IActionResult GetJobStatus(string jobId)
    {
        var job = _jobService.GetJob(jobId);
        if (job == null)
            return NotFound(new { error = "Job no encontrado o expirado." });

        return Ok(new JobStatusResponse(
            Id: job.Id,
            Status: job.Status,
            Current: job.Current,
            Total: job.Total,
            Results: job.Results,
            Error: job.Error
        ));
    }
}
