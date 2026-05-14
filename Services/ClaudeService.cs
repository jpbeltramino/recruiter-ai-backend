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
        return
            "<instrucciones>\n" +
            "Sos un experto en reclutamiento y seleccion de personal con 15 anos de experiencia.\n" +
            "Tu tarea es evaluar y rankear candidatos para un puesto especifico.\n\n" +
            "Para CADA candidato devuelve un objeto JSON con exactamente estos campos:\n" +
            "- name: string (nombre del candidato)\n" +
            "- score: numero entero del 1 al 10\n" +
            "- strengths: array de exactamente 3 strings (puntos fuertes)\n" +
            "- weaknesses: array de exactamente 2 strings (debilidades)\n" +
            "- verdict: string, UNO de estos valores exactos: \"AVANZAR\", \"REVISAR\", \"DESCARTAR\"\n\n" +
            "Criterios de veredicto:\n" +
            "- AVANZAR: score >= 7, candidato claramente apto\n" +
            "- REVISAR: score 5-6, hay dudas pero merece evaluacion\n" +
            "- DESCARTAR: score <= 4, no cumple requisitos minimos\n\n" +
            "Responde UNICAMENTE con un JSON valido, sin texto adicional, sin markdown:\n" +
            "{\"rankings\": [...]}\n" +
            "</instrucciones>\n\n" +
            "<puesto>\n" + jobDescription + "\n</puesto>\n\n" +
            "<candidatos>\n" + candidatesXml + "\n</candidatos>\n\n" +
            "<tarea>\n" +
            "Evalua cada candidato contra el puesto. Se objetivo y especifico.\n" +
            "Los puntos fuertes y debiles deben ser concretos y relevantes al puesto.\n" +
            "Responde SOLO con el JSON pedido.\n" +
            "</tarea>";
    }

    private static string InconsistenciasPrompt(string cvText)
    {
        var currentDate = DateTime.UtcNow.ToString("MMMM yyyy", new System.Globalization.CultureInfo("es-AR"));
        var currentYear = DateTime.UtcNow.Year;

        return
            "<contexto>\n" +
            $"La fecha actual es {currentDate} ({currentYear}). Si necesitás referenciar 'hoy', " +
            $"usá esta fecha como referencia INTERNA solamente. NO la menciones en tus respuestas.\n" +
            "</contexto>\n\n" +
            "<instrucciones>\n" +
            "Sos un experto en verificacion de antecedentes laborales. Tu objetivo es detectar " +
            "inconsistencias REALES y RELEVANTES en el CV.\n\n" +
            "==================================================================\n" +
            "REGLAS ABSOLUTAS — VIOLAR CUALQUIERA ES UN ERROR GRAVE:\n" +
            "==================================================================\n\n" +
            "REGLA 1 (ABSOLUTA): NUNCA reportes diferencias de duracion menores a 3 meses. " +
            "Esto es INNEGOCIABLE. Si el CV dice '5 años 2 meses' y tu cuenta da '5 años 1 mes', " +
            "NO lo reportes. No agregues ningun 'pero', 'aunque', 'sin embargo' para reportarlo igual. " +
            "Si la diferencia es menor a 3 meses, la inconsistencia NO existe.\n\n" +
            "REGLA 2 (ABSOLUTA): NUNCA compares la experiencia declarada en texto " +
            "(frases tipo '+5 años', 'más de X años', 'experiencia senior en') contra la suma " +
            "cronologica de los empleos. Ignora COMPLETAMENTE estas frases al evaluar. " +
            "Si el perfil dice '+5 años' y la suma da 8, eso es CV desactualizado, NO inconsistencia.\n\n" +
            "REGLA 3 (ABSOLUTA): NUNCA menciones la fecha actual en tu respuesta. " +
            "Las inconsistencias deben ser INTERNAS al CV.\n\n" +
            "REGLA 4 (ABSOLUTA): NUNCA reportes solapamientos menores a 2 meses entre empleos.\n" +
            "VERIFICACIÓN OBLIGATORIA antes de reportar un solapamiento:\n" +
            "  1. Tomá la fecha de FIN del empleo anterior\n" +
            "  2. Tomá la fecha de INICIO del empleo siguiente\n" +
            "  3. Si la diferencia es ≤ 2 meses → NO es solapamiento, NO reportar\n" +
            "  4. Si las fechas coinciden o se tocan en el mismo mes → NO es solapamiento, NO reportar\n" +
            "Ejemplos:\n" +
            "  - Empleo A: Ago 2019 – Oct 2019 / Empleo B: Oct 2019 – ... → NO HAY SOLAPAMIENTO (transición)\n" +
            "  - Empleo A: Ene 2020 – Dic 2022 / Empleo B: Ene 2023 – ... → NO HAY SOLAPAMIENTO\n" +
            "  - Empleo A: Ene 2020 – Jun 2023 / Empleo B: Ene 2023 – ... → SI HAY SOLAPAMIENTO (6 meses)\n\n" +
            "REGLA 5 (ABSOLUTA): NUNCA reportes trabajos simultaneos como inconsistencia, A MENOS " +
            "QUE sean fisicamente imposibles (ej: dos full-time presenciales en ciudades " +
            "distintas). Freelance, part-time, consultoria y proyectos paralelos son normales.\n\n" +
            "REGLA 6 (ABSOLUTA): NUNCA infieras, supongas ni calcules cosas que no esten " +
            "escritas explicitamente en el CV. Si tenés dudas sobre fechas exactas, NO reportes.\n\n" +
            "REGLA 7 (ABSOLUTA): Antes de reportar cualquier solapamiento, verificá las fechas " +
            "DOS VECES leyendo el CV literalmente. No inventes ni mezcles fechas de distintos empleos.\n\n" +
            "==================================================================\n" +
            "AUTO-CHECK ANTES DE INCLUIR UN HALLAZGO:\n" +
            "==================================================================\n\n" +
            "Antes de agregar un hallazgo, preguntate:\n" +
            "- ¿Estoy reportando una diferencia menor a 3 meses? → DESCARTAR\n" +
            "- ¿Estoy comparando experiencia declarada contra suma cronologica? → DESCARTAR\n" +
            "- ¿Estoy mencionando la fecha actual? → DESCARTAR\n" +
            "- ¿El 'solapamiento' es menor a 2 meses o son meses que se tocan? → DESCARTAR\n" +
            "- ¿Verifiqué las fechas exactas leyendo el CV literal? → si NO, DESCARTAR\n" +
            "- ¿Estoy reportando trabajos simultaneos que son fisicamente posibles? → DESCARTAR\n" +
            "- ¿Estoy infiriendo algo no escrito en el CV? → DESCARTAR\n\n" +
            "==================================================================\n" +
            "SI VALE LA PENA REPORTAR (despues del auto-check):\n" +
            "==================================================================\n\n" +
            "- Gaps SIN EXPLICACION de mas de 6 meses entre empleos consecutivos\n" +
            "- Saltos de seniority bruscos sin experiencia que lo justifique\n" +
            "- Fechas INTERNAMENTE imposibles (ej: empleo termina antes de empezar)\n" +
            "- Solapamientos largos VERIFICADOS (>2 meses) entre roles fisicamente incompatibles\n" +
            "- Habilidades tecnicas avanzadas que no aparecen en ninguna experiencia\n" +
            "- Logros con numeros sospechosamente especificos sin contexto\n" +
            "- Titulos o instituciones vagas o no verificables\n" +
            "- Contradicciones internas reales (datos del CV que se contradicen entre si)\n\n" +
            "==================================================================\n" +
            "FORMATO DE RESPUESTA:\n" +
            "==================================================================\n\n" +
            "Para cada hallazgo RELEVANTE que paso el auto-check:\n" +
            "- category: string corto\n" +
            "- description: explicacion clara citando datos EXACTOS del CV. " +
            "PROHIBIDO mencionar fecha actual. PROHIBIDO mencionar '+X años' del perfil. " +
            "PROHIBIDO mencionar diferencias menores a 3 meses. " +
            "PROHIBIDO mezclar fechas de distintos empleos.\n" +
            "- riskLevel: \"ALTO\", \"MEDIO\" o \"BAJO\"\n" +
            "- suggestedQuestion: pregunta para validar en entrevista\n\n" +
            "Incluir \"summary\" de 2-3 oraciones. Si no hay nada relevante, summary positivo y " +
            "findings vacio.\n\n" +
            "Responde UNICAMENTE con JSON valido:\n" +
            "{\"findings\": [...], \"summary\": \"...\"}\n" +
            "</instrucciones>\n\n" +
            "<cv>\n" + cvText + "\n</cv>\n\n" +
            "<tarea>\n" +
            "Analiza el CV. Aplica el auto-check a cada hallazgo potencial. Verificá fechas DOS VECES " +
            "antes de reportar solapamientos. Ante CUALQUIER duda, NO reportes.\n" +
            "</tarea>";
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