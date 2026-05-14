using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using RecruiterAI.Models;
using System.Text.Json;

namespace RecruiterAI.Services;

public class ClaudeService
{
    private readonly AnthropicClient _client;
    private readonly ILogger<ClaudeService> _logger;
    private const string Model = "claude-haiku-4-5-20251001";
    private readonly int _batchSize;
    private readonly int _delayBetweenBatchesMs;
    

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public ClaudeService(IConfiguration config, ILogger<ClaudeService> logger)
    {
        var apiKey = config["Anthropic:ApiKey"]
            ?? throw new InvalidOperationException("Falta Anthropic:ApiKey en appsettings.json");
        _client = new AnthropicClient(apiKey);
        _logger = logger;

        _batchSize = config.GetValue<int>("Parallelism:BatchSize", 5);
        _delayBetweenBatchesMs = config.GetValue<int>("Parallelism:DelayBetweenBatchesMs", 1000);
    }

    private async Task<string> CallClaude(string userMessage, int maxTokens = 2048)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Llamada a Claude iniciada. MaxTokens={MaxTokens}, MessageLength={MessageLength}",
            maxTokens, userMessage.Length);

        var messages = new List<Message>
        {
            new Message
            {
                Role = RoleType.User,
                Content = new List<ContentBase> { new TextContent { Text = userMessage } }
            }
        };

        var parameters = new MessageParameters
        {
            Model = Model,
            MaxTokens = maxTokens,
            Messages = messages
        };

        try
        {
            var response = await _client.Messages.GetClaudeMessageAsync(parameters);
            var text = response.Content.OfType<TextContent>().FirstOrDefault()?.Text
                    ?? throw new Exception("Claude no devolvió texto.");

            _logger.LogInformation("Llamada a Claude completada en {ElapsedMs}ms. ResponseLength={ResponseLength}",
                sw.ElapsedMilliseconds, text.Length);

            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Llamada a Claude falló después de {ElapsedMs}ms", sw.ElapsedMilliseconds);
            throw;
        }
    }

    private static T ParseJson<T>(string raw)
    {
        var clean = raw.Trim();
        if (clean.StartsWith("```"))
        {
            var start = clean.IndexOf('\n') + 1;
            var end = clean.LastIndexOf("```");
            if (end > start) clean = clean[start..end].Trim();
        }
        return JsonSerializer.Deserialize<T>(clean, JsonOpts)
               ?? throw new Exception("No se pudo parsear la respuesta de Claude.");
    }

    private static string RankCvsPrompt(string jobDescription, string candidatesXml)
    {
        var currentYear = DateTime.UtcNow.Year;
        var currentMonth = DateTime.UtcNow.ToString("MMMM", new System.Globalization.CultureInfo("es-AR"));

        return
            $"Fecha actual: {currentMonth} {currentYear}.\n\n" +
            "Sos experto en reclutamiento. Evaluá candidatos para el puesto.\n\n" +
            "==================================================================\n" +
            "CÓMO CALCULAR AÑOS DE EXPERIENCIA — DOS NÚMEROS DISTINTOS:\n" +
            "==================================================================\n\n" +
            "Para cada candidato, calculá DOS valores:\n\n" +
            "1. EXPERIENCIA TOTAL: desde la fecha del primer empleo hasta hoy.\n" +
            $"   Ej: primer empleo en Oct 2017 → {currentYear - 2017} años de experiencia total.\n\n" +
            "2. EXPERIENCIA RELEVANTE: solo los empleos que aplican al puesto buscado.\n" +
            "   Identificá qué skills/roles pide el puesto y contá SOLO los empleos donde el " +
            "candidato trabajó en esos skills o roles directamente relacionados.\n" +
            "   Trabajos en áreas completamente distintas al puesto NO cuentan como experiencia " +
            "relevante.\n\n" +
            "==================================================================\n" +
            "QUÉ NÚMERO USAR PARA EVALUAR:\n" +
            "==================================================================\n\n" +
            "El score se basa en la EXPERIENCIA RELEVANTE, no en la total.\n\n" +
            "Ejemplos generales:\n" +
            "- Puesto pide 8 años de [skill X], candidato tiene 8 años de [skill X] → cumple\n" +
            "- Puesto pide 8 años de [skill X], candidato tiene 5 años de [skill X] + 10 años en " +
            "otro rol no relacionado → NO cumple (5 años relevantes, no 15)\n" +
            "- Puesto pide 5 años de [skill X], candidato tiene 8 años de [skill Y] → NO cumple " +
            "(skills distintos, no son intercambiables)\n" +
            "- Puesto pide [skill X], candidato tiene experiencia en [skill X] desde hace 4 años " +
            "trabajando en paralelo con otros roles → cuentan los 4 años de [skill X], independiente " +
            "de la duración de los otros roles.\n\n" +
            "IGNORÁ totalmente:\n" +
            "- Frases como '+5 años' o 'más de X años' del perfil profesional " +
            "(suelen ser textos no actualizados).\n" +
            "- Trabajos simultáneos: NO restan experiencia. Si dos empleos relevantes corren " +
            "en paralelo, cuentan como un solo período (no se suman, pero tampoco se restan).\n\n" +
            "==================================================================\n" +
            "CÓMO ASIGNAR EL SCORE:\n" +
            "==================================================================\n\n" +
            "Comparás EXPERIENCIA RELEVANTE contra requisitos del puesto.\n\n" +
            "Score 9-10: cumple todos los requisitos y excede en algo importante\n" +
            "Score 7-8: cumple todos los requisitos ajustadamente\n" +
            "Score 5-6: le falta algo importante (ej: pide 8 años relevantes, tiene 5)\n" +
            "Score 3-4: le faltan varios requisitos\n" +
            "Score 1-2: no cumple los básicos\n\n" +
            "Si la experiencia RELEVANTE no llega a los años solicitados → score < 7 (sin excepciones).\n\n" +
            "Veredictos:\n" +
            "- AVANZAR: score >= 7\n" +
            "- REVISAR: score 5-6\n" +
            "- DESCARTAR: score <= 4\n\n" +
            "==================================================================\n" +
            "FORMATO JSON:\n" +
            "==================================================================\n\n" +
            "Por candidato:\n" +
            "- name: string\n" +
            "- score: entero 1-10\n" +
            "- strengths: 3 strings concretos (que reflejen experiencia RELEVANTE al puesto)\n" +
            "- weaknesses: 2 strings concretos (brechas reales respecto al puesto)\n" +
            "- verdict: AVANZAR | REVISAR | DESCARTAR\n\n" +
            "Respondé SOLO con: {\"rankings\": [...]}\n\n" +
            "<puesto>\n" + jobDescription + "\n</puesto>\n\n" +
            "<candidatos>\n" + candidatesXml + "\n</candidatos>";
    }

    private static string InconsistenciasPrompt(string cvText)
    {
        return
            "Sos experto en verificación de CVs. Detectá inconsistencias REALES.\n\n" +
            "==================================================================\n" +
            "PRINCIPIO RECTOR: si dudás, NO reportes.\n" +
            "==================================================================\n\n" +
            "Es mucho peor reportar un falso positivo que dejar pasar algo dudoso. " +
            "Los recruiters confían en este sistema. Reportá SOLO si estás 100% seguro.\n\n" +
            "==================================================================\n" +
            "QUÉ NO REPORTAR (lo más importante):\n" +
            "==================================================================\n\n" +
            "1. Trabajos simultáneos con tipos compatibles (freelance + full-time, " +
            "part-time, consultoría). NO los reportes — son comunes y válidos.\n\n" +
            "2. Solapamientos de fechas de 2 meses o menos. NO son solapamientos, " +
            "son transiciones normales. Mismo mes de cierre/apertura = transición, no solape.\n\n" +
            "3. Diferencias menores a 3 meses entre duración declarada y calculada. Ignorar.\n\n" +
            "4. Frases del perfil tipo '+5 años de experiencia'. NO las compares con la suma " +
            "cronológica. Es CV desactualizado, no inconsistencia.\n\n" +
            "5. Cálculos basados en la fecha actual. Solo reportá inconsistencias INTERNAS al CV.\n\n" +
            "6. Cualquier cosa que no esté EXPLÍCITA en el CV. No inventes ni infieras.\n\n" +
            "==================================================================\n" +
            "QUÉ SÍ REPORTAR:\n" +
            "==================================================================\n\n" +
            "- Gaps de más de 6 meses sin empleo y sin explicación\n" +
            "- Saltos de seniority injustificados (junior → CTO en 1 año)\n" +
            "- Fechas internamente imposibles (un empleo termina antes de empezar)\n" +
            "- Solapamientos VERIFICADOS de más de 2 meses entre roles físicamente incompatibles " +
            "(ej: dos full-time presenciales en ciudades distintas)\n" +
            "- Skills muy avanzadas que no aparecen en ningún empleo\n" +
            "- Logros con números sospechosos sin contexto\n" +
            "- Contradicciones internas reales (un dato del CV contradice otro)\n\n" +
            "==================================================================\n" +
            "VALIDACIÓN OBLIGATORIA POR CADA FINDING:\n" +
            "==================================================================\n\n" +
            "Antes de incluir un finding, respondé estas 3 preguntas mentalmente:\n\n" +
            "1. ¿La descripción se contradice consigo misma en algún punto? → si SÍ, NO REPORTAR\n" +
            "2. ¿Puedo apuntar a dos textos exactos del CV que se contradicen? → si NO, NO REPORTAR\n" +
            "3. ¿Mi razonamiento aplica una regla del bloque 'QUÉ NO REPORTAR'? → si SÍ, NO REPORTAR\n\n" +
            "==================================================================\n" +
            "FORMATO JSON:\n" +
            "==================================================================\n\n" +
            "{\n" +
            "  \"findings\": [\n" +
            "    {\n" +
            "      \"category\": \"...\",\n" +
            "      \"description\": \"...\",\n" +
            "      \"riskLevel\": \"ALTO|MEDIO|BAJO\",\n" +
            "      \"suggestedQuestion\": \"...\"\n" +
            "    }\n" +
            "  ],\n" +
            "  \"summary\": \"...\"\n" +
            "}\n\n" +
            "Sin markdown. Solo JSON. Si no hay nada relevante, findings: [] y summary positivo.\n\n" +
            "<cv>\n" + cvText + "\n</cv>";
    }
    private static string QuestionsPrompt(string jobDescription, string cvText)
    {
        return
            "<instrucciones>\n" +
            "Sos un experto en entrevistas tecnicas y de competencias con experiencia en RRHH.\n" +
            "Genera preguntas de entrevista personalizadas basadas en el CV y el puesto.\n\n" +
            "Debes generar exactamente:\n" +
            "- 5 preguntas tecnicas (sobre skills, herramientas, metodologias del puesto)\n" +
            "- 3 preguntas de motivacion y fit cultural\n" +
            "- 2 preguntas para validar puntos debiles o gaps del CV\n\n" +
            "Cada pregunta debe tener:\n" +
            "- question: string (la pregunta)\n" +
            "- whatToValidate: string (que competencia se evalua)\n" +
            "- strongAnswerIndicator: string (que responderia un candidato fuerte)\n\n" +
            "Responde UNICAMENTE con JSON valido:\n" +
            "{\"technical\": [...], \"cultural\": [...], \"weaknessValidation\": [...]}\n" +
            "</instrucciones>\n\n" +
            "<puesto>\n" + jobDescription + "\n</puesto>\n\n" +
            "<cv>\n" + cvText + "\n</cv>\n\n" +
            "<tarea>\n" +
            "Genera preguntas personalizadas para este candidato y puesto especifico.\n" +
            "Responde SOLO con el JSON pedido.\n" +
            "</tarea>";
    }

  
    // ─── 1. RANKEADOR DE CVs ─────────────────────────────────────────────────

    public async Task<RankCvsResponse> RankCvsAsync(
        string jobDescription,
        List<(string Name, string CvText)> candidates)
    {
        var candidatesXml = string.Join("\n", candidates.Select((c, i) =>
            "<candidato id=\"" + (i + 1) + "\" nombre=\"" +
            System.Security.SecurityElement.Escape(c.Name) + "\">\n" +
            c.CvText + "\n</candidato>"));

        var prompt = RankCvsPrompt(jobDescription, candidatesXml);
        var raw = await CallClaude(prompt, 3000);

        var result = ParseJson<JsonElement>(raw);
        var rankings = result.GetProperty("rankings")
            .EnumerateArray()
            .Select(r => new CandidateRanking(
                Name: r.GetProperty("name").GetString()!,
                Score: r.GetProperty("score").GetInt32(),
                Strengths: r.GetProperty("strengths").EnumerateArray()
                            .Select(s => s.GetString()!).ToList(),
                Weaknesses: r.GetProperty("weaknesses").EnumerateArray()
                             .Select(s => s.GetString()!).ToList(),
                Verdict: r.GetProperty("verdict").GetString()!
            ))
            .OrderByDescending(r => r.Score)
            .ToList();

        return new RankCvsResponse(rankings);
    }

    // ─── 2. DETECTOR DE INCONSISTENCIAS ──────────────────────────────────────

    public async Task<DetectInconsistenciesResponse> DetectInconsistenciesAsync(string cvText)
    {
        var prompt = InconsistenciasPrompt(cvText);
        var raw = await CallClaude(prompt, 2500);
        var result = ParseJson<JsonElement>(raw);

        var findings = result.GetProperty("findings")
            .EnumerateArray()
            .Select(f => new InconsistencyFinding(
                Category: f.GetProperty("category").GetString()!,
                Description: f.GetProperty("description").GetString()!,
                RiskLevel: f.GetProperty("riskLevel").GetString()!,
                SuggestedQuestion: f.GetProperty("suggestedQuestion").GetString()!
            ))
            .ToList();

        var summary = result.GetProperty("summary").GetString()!;
        return new DetectInconsistenciesResponse(findings, summary);
    }

    // ─── 3. GENERADOR DE PREGUNTAS ───────────────────────────────────────────

    public async Task<GenerateQuestionsResponse> GenerateQuestionsAsync(
        string jobDescription,
        string cvText)
    {
        var prompt = QuestionsPrompt(jobDescription, cvText);
        var raw = await CallClaude(prompt, 3000);
        var result = ParseJson<JsonElement>(raw);

        static List<InterviewQuestion> ParseQuestions(JsonElement arr) =>
            arr.EnumerateArray()
               .Select(q => new InterviewQuestion(
                   Question: q.GetProperty("question").GetString()!,
                   WhatToValidate: q.GetProperty("whatToValidate").GetString()!,
                   StrongAnswerIndicator: q.GetProperty("strongAnswerIndicator").GetString()!
               ))
               .ToList();

        return new GenerateQuestionsResponse(
            Technical: ParseQuestions(result.GetProperty("technical")),
            Cultural: ParseQuestions(result.GetProperty("cultural")),
            WeaknessValidation: ParseQuestions(result.GetProperty("weaknessValidation"))
        );
    }


 public async Task<UnifiedCandidateResult> AnalyzeCandidateFullAsync(
        string jobDescription,
        string name,
        string cvText,
        string mode = "smart")
    {
        var rankResult = await RankCvsAsync(jobDescription, new List<(string, string)> { (name, cvText) });
        var ranking = rankResult.Rankings.First();

        bool shouldRunDeep = mode switch
        {
            "ranking" => false,
            "full" => true,
            _ => ranking.Score >= 7
        };

        if (!shouldRunDeep)
        {
            return new UnifiedCandidateResult(
                Name: name,
                Score: ranking.Score,
                Strengths: ranking.Strengths,
                Weaknesses: ranking.Weaknesses,
                Verdict: ranking.Verdict,
                Inconsistencies: new DetectInconsistenciesResponse(
                    new List<InconsistencyFinding>(),
                    string.Empty
                ),
                Questions: new GenerateQuestionsResponse(
                    new List<InterviewQuestion>(),
                    new List<InterviewQuestion>(),
                    new List<InterviewQuestion>()
                ),
                HasDeepAnalysis: false
            );
        }

        await Task.Delay(500);
        var inconsResult = await DetectInconsistenciesAsync(cvText);
        await Task.Delay(500);
        var questResult = await GenerateQuestionsAsync(jobDescription, cvText);

        return new UnifiedCandidateResult(
            Name: name,
            Score: ranking.Score,
            Strengths: ranking.Strengths,
            Weaknesses: ranking.Weaknesses,
            Verdict: ranking.Verdict,
            Inconsistencies: inconsResult,
            Questions: questResult,
            HasDeepAnalysis: true
        );
    }
        public async Task<List<UnifiedCandidateResult>> AnalyzeCandidatesParallelAsync(
    string jobDescription,
    List<(string Name, string CvText)> candidates)
    {
        _logger.LogInformation("Procesando {Count} candidatos en paralelo (lotes de 5)",
            candidates.Count);

        var results = await ProcessInBatchesAsync(
            candidates,
            batchSize: _batchSize,
            delayBetweenBatchesMs: _delayBetweenBatchesMs,
            processor: async candidate =>
            {
                try
                {
                    return await AnalyzeCandidateFullAsync(jobDescription, candidate.Name, candidate.CvText);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error analizando candidato {Name}", candidate.Name);
                    return new UnifiedCandidateResult(
                        Name: candidate.Name,
                        Score: 0,
                        Strengths: new List<string>(),
                        Weaknesses: new List<string> { "Error al procesar este candidato" },
                        Verdict: "DESCARTAR",
                        Inconsistencies: new DetectInconsistenciesResponse(new List<InconsistencyFinding>(), string.Empty),
                        Questions: new GenerateQuestionsResponse(new List<InterviewQuestion>(), new List<InterviewQuestion>(), new List<InterviewQuestion>()),
                        HasDeepAnalysis: false
                    );
                }
            }
        );

        return results;
    }

    public async Task<DeepAnalysisResponse> AnalyzeCandidateDeepAsync(
        string jobDescription,
        string cvText)
        {
            var inconsResult = await DetectInconsistenciesAsync(cvText);
            await Task.Delay(500);
            var questResult = await GenerateQuestionsAsync(jobDescription, cvText);

            return new DeepAnalysisResponse(inconsResult, questResult);
        }
    private static async Task<List<T>> ProcessInBatchesAsync<TInput, T>(
    IEnumerable<TInput> items,
    int batchSize,
    int delayBetweenBatchesMs,
    Func<TInput, Task<T>> processor)
    {
        var results = new List<T>();
        var batches = items
            .Select((item, index) => new { item, index })
            .GroupBy(x => x.index / batchSize)
            .Select(g => g.Select(x => x.item).ToList())
            .ToList();

        for (int i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            var tasks = batch.Select(processor);
            var batchResults = await Task.WhenAll(tasks);
            results.AddRange(batchResults);

            if (i < batches.Count - 1)
                await Task.Delay(delayBetweenBatchesMs);
        }

        return results;
    }

    public async Task<List<UnifiedCandidateResult>> AnalyzeCandidatesParallelWithProgressAsync(
    string jobDescription,
    List<(string Name, string CvText)> candidates,
    Func<UnifiedCandidateResult, int, Task> onCandidateCompleted)
    {
        _logger.LogInformation(
            "Procesando {Count} candidatos con progreso (lotes de {BatchSize})",
            candidates.Count, _batchSize);

        var results = new List<UnifiedCandidateResult>();
        var completedCount = 0;
        var lockObj = new object();

        var batches = candidates
            .Select((c, i) => new { Candidate = c, Index = i })
            .GroupBy(x => x.Index / _batchSize)
            .Select(g => g.Select(x => x.Candidate).ToList())
            .ToList();

        for (int i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];

            var tasks = batch.Select(async candidate =>
            {
                UnifiedCandidateResult result;
                try
                {
                    result = await AnalyzeCandidateFullAsync(jobDescription, candidate.Name, candidate.CvText);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error analizando candidato {Name}", candidate.Name);
                    result = new UnifiedCandidateResult(
                        Name: candidate.Name,
                        Score: 0,
                        Strengths: [],
                        Weaknesses: ["Error al procesar este candidato"],
                        Verdict: "DESCARTAR",
                        Inconsistencies: new DetectInconsistenciesResponse([], string.Empty),
                        Questions: new GenerateQuestionsResponse([], [], []),
                        HasDeepAnalysis: false
                    );
                }

                int currentCount;
                lock (lockObj)
                {
                    results.Add(result);
                    completedCount++;
                    currentCount = completedCount;
                }

                await onCandidateCompleted(result, currentCount);
                return result;
            });

            await Task.WhenAll(tasks);

            if (i < batches.Count - 1)
                await Task.Delay(_delayBetweenBatchesMs);
        }

        return results;
    }

    public async Task<List<UnifiedCandidateResult>> AnalyzeCandidatesWithJobAsync(
    string jobDescription,
    List<(string Name, string CvText)> candidates,
    string jobId,
    JobService jobService,
    string mode = "smart")
    {
        _logger.LogInformation(
            "Job {JobId}: Procesando {Count} candidatos en modo {Mode} (lotes de {BatchSize})",
            jobId, candidates.Count, mode, _batchSize);

        var results = new List<UnifiedCandidateResult>();
        var completedCount = 0;
        var lockObj = new object();

        var batches = candidates
            .Select((c, i) => new { Candidate = c, Index = i })
            .GroupBy(x => x.Index / _batchSize)
            .Select(g => g.Select(x => x.Candidate).ToList())
            .ToList();

        for (int i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];

            var tasks = batch.Select(async candidate =>
            {
                UnifiedCandidateResult result;
                try
                {
                    result = await AnalyzeCandidateFullAsync(jobDescription, candidate.Name, candidate.CvText, mode);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Job {JobId}: Error en candidato {Name}", jobId, candidate.Name);
                    result = new UnifiedCandidateResult(
                        Name: candidate.Name,
                        Score: 0,
                        Strengths: new List<string>(),
                        Weaknesses: new List<string> { "Error al procesar este candidato" },
                        Verdict: "DESCARTAR",
                        Inconsistencies: new DetectInconsistenciesResponse(new List<InconsistencyFinding>(), string.Empty),
                        Questions: new GenerateQuestionsResponse(new List<InterviewQuestion>(), new List<InterviewQuestion>(), new List<InterviewQuestion>()),
                        HasDeepAnalysis: false
                    );
                }

                int currentCount;
                lock (lockObj)
                {
                    results.Add(result);
                    completedCount++;
                    currentCount = completedCount;
                }

                jobService.UpdateProgress(jobId, currentCount, result);
                return result;
            });

            await Task.WhenAll(tasks);

            if (i < batches.Count - 1)
                await Task.Delay(_delayBetweenBatchesMs);
        }

        return results;
    }
}