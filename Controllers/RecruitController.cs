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
    ILogger<RecruitController> logger) : ControllerBase
{
    private readonly ClaudeService _claude = claude;
    private readonly CvParserService _parser = parser;
    private readonly ILogger<RecruitController> _logger = logger;

    private readonly FileValidationService _validator = validator;

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

            var tasks = resolved.Select(c =>
                _claude.AnalyzeCandidateFullAsync(request.JobDescription, c.Name, c.CvText)
            );

            var results = await Task.WhenAll(tasks);

            var response = new UnifiedAnalysisResponse(
                Candidates: results.OrderByDescending(c => c.Score).ToList()
            );

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en AnalyzeUnified");
            return StatusCode(500, new { error = ex.Message });
        }
    }
   
}
