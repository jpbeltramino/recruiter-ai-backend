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
        var currentDate = DateTime.UtcNow.ToString("MMMM yyyy", new System.Globalization.CultureInfo("es-AR"));
        var currentYear = DateTime.UtcNow.Year;

        return
            "<contexto>\n" +
            $"Fecha actual: {currentDate} ({currentYear}). " +
            $"Para calcular experiencia: desde la fecha de inicio del primer empleo hasta hoy. " +
            $"Ej: si trabaja desde febrero 2021, tiene aprox {currentYear - 2021} años.\n" +
            "</contexto>\n\n" +
            "<instrucciones>\n" +
            "Sos experto en reclutamiento. Evalúa y rankea candidatos para el puesto.\n\n" +
            "Devolvé un JSON por candidato con:\n" +
            "- name: string\n" +
            "- score: entero 1-10\n" +
            "- strengths: 3 strings (puntos fuertes concretos y relevantes al puesto)\n" +
            "- weaknesses: 2 strings (debilidades reales respecto al puesto)\n" +
            "- verdict: \"AVANZAR\" | \"REVISAR\" | \"DESCARTAR\"\n\n" +
            "==================================================================\n" +
            "CÓMO ASIGNAR EL SCORE:\n" +
            "==================================================================\n\n" +
            "Identificá los REQUISITOS EXCLUYENTES del puesto (años de experiencia, " +
            "tecnologías específicas, certificaciones, idiomas).\n\n" +
            "REGLA CLAVE: el score debe REFLEJAR el grado de cumplimiento de los requisitos.\n\n" +
            "- Cumple todos los requisitos excluyentes Y tiene experiencia adicional valiosa → 9-10\n" +
            "- Cumple todos los requisitos excluyentes ajustadamente → 7-8\n" +
            "- Cumple la mayoría pero le falta algo significativo (ej: pide 8 años y tiene 5) → 5-6\n" +
            "- Le faltan varios requisitos excluyentes → 3-4\n" +
            "- No cumple los requisitos básicos → 1-2\n\n" +
            "IMPORTANTE: si un candidato no llega a los años de experiencia solicitados, " +
            "el score NO puede ser 7 o más. Esa es una brecha significativa.\n\n" +
            "Veredictos:\n" +
            "- AVANZAR: score >= 7\n" +
            "- REVISAR: score 5-6\n" +
            "- DESCARTAR: score <= 4\n\n" +
            "Responde SOLO con JSON válido, sin markdown:\n" +
            "{\"rankings\": [...]}\n" +
            "</instrucciones>\n\n" +
            "<puesto>\n" + jobDescription + "\n</puesto>\n\n" +
            "<candidatos>\n" + candidatesXml + "\n</candidatos>\n\n" +
            "Evaluá cada candidato. Identificá requisitos excluyentes del puesto y " +
            "asigná el score según cuánto los cumple. Si no llega a los años pedidos, score < 7.";
    }

    private static string InconsistenciasPrompt(string cvText)
    {
        return
            "Sos experto en verificación de CVs. Detectá inconsistencias REALES y RELEVANTES.\n\n" +
            "==================================================================\n" +
            "REGLAS CRÍTICAS:\n" +
            "==================================================================\n\n" +
            "1. SOLAPAMIENTOS: solo reportá si el solapamiento entre dos empleos es de MÁS DE 2 MESES " +
            "calendario completos. Verificá las fechas DOS VECES leyendo literal del CV.\n" +
            "Ejemplos:\n" +
            "  • Empleo A: Ago 2019 – Oct 2019 / Empleo B: Oct 2019 – Dic 2024 → NO HAY SOLAPAMIENTO (transición)\n" +
            "  • Empleo A: Mar 2020 – Dic 2020 / Empleo B: Dic 2020 – Actualidad → NO HAY SOLAPAMIENTO (mismo mes de cierre/apertura)\n" +
            "  • Empleo A: Ene 2020 – Dic 2022 / Empleo B: Ene 2023 – Actualidad → NO HAY SOLAPAMIENTO\n" +
            "  • Empleo A: Ene 2020 – Jun 2023 / Empleo B: Ene 2023 – Actualidad → SÍ HAY SOLAPAMIENTO (6 meses)\n\n" +
            "2. EXPERIENCIA DECLARADA: ignorá frases como '+5 años', 'experiencia senior en'. " +
            "NO compares texto contra suma cronológica. Si dice '+5 años' y la suma da 8, " +
            "es CV desactualizado, NO inconsistencia.\n\n" +
            "3. DURACIONES: si la diferencia entre duración declarada y calculada es menor a 3 meses, " +
            "NO reportes. Ej: declara '5 años 2 meses', calculás '5 años 1 mes' → NO reportar.\n\n" +
            "4. FECHA ACTUAL: NO menciones la fecha actual ni hagas comparaciones contra hoy. " +
            "Solo inconsistencias INTERNAS al CV.\n\n" +
            "5. TRABAJOS SIMULTÁNEOS: freelance + full-time, part-time, consultoría son válidos. " +
            "Solo reportá si es físicamente imposible (ej: dos full-time presenciales en ciudades distintas).\n\n" +
            "6. NO INVENTES: solo reportá lo que está EXPLÍCITO en el CV. Si dudás, NO reportes.\n\n" +
            "==================================================================\n" +
            "QUÉ SÍ REPORTAR:\n" +
            "==================================================================\n" +
            "- Gaps de más de 6 meses sin explicar\n" +
            "- Saltos de seniority bruscos (junior → CTO)\n" +
            "- Fechas internamente imposibles (termina antes de empezar)\n" +
            "- Solapamientos VERIFICADOS de más de 2 meses entre roles incompatibles\n" +
            "- Skills avanzadas que no aparecen en ningún empleo\n" +
            "- Logros con números sospechosos sin contexto\n" +
            "- Contradicciones internas reales\n\n" +
            "==================================================================\n" +
            "FORMATO JSON:\n" +
            "==================================================================\n" +
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
            "Sin markdown. Sin texto extra. Solo JSON.\n\n" +
            "<cv>\n" + cvText + "\n</cv>\n\n" +
            "AUTO-CHECK antes de cada finding:\n" +
            "- ¿Duración declarada vs calculada difiere menos de 3 meses? → NO REPORTAR\n" +
            "- ¿Solapamiento de 2 meses o menos (incluyendo mismo mes de transición)? → NO REPORTAR\n" +
            "- ¿Comparando '+X años' con suma cronológica? → NO REPORTAR\n" +
            "- ¿Mencionando fecha actual? → NO REPORTAR\n" +
            "- ¿Trabajos pueden ser simultáneos (uno freelance)? → NO REPORTAR\n\n" +
            "Mejor no reportar nada que reportar falso positivo. " +
            "Si pasa el check, incluí solo inconsistencia con datos EXACTOS del CV.";
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