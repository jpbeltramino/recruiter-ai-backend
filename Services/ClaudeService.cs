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
    return
        "<instrucciones>\n" +
        "Sos un experto en verificacion de antecedentes laborales. Tu objetivo es detectar " +
        "inconsistencias REALES y RELEVANTES en el CV, no errores menores o imprecisiones normales.\n\n" +
        "REGLAS IMPORTANTES antes de reportar algo:\n" +
        "1. La experiencia total se calcula desde la fecha del primer empleo hasta hoy, " +
        "sumando todos los periodos. NO restes periodos de trabajo simultaneo — es normal " +
        "tener trabajos en paralelo (freelance, part-time, etc).\n" +
        "2. Solapamientos de fechas MENORES A 2 MESES no son inconsistencias — pueden ser " +
        "transiciones normales entre trabajos o imprecision al redondear meses.\n" +
        "3. Diferencias en duracion declarada MENORES A 3 MESES no son inconsistencias — " +
        "es normal redondear '1 año 11 meses' a '2 años'.\n" +
        "4. Tener dos trabajos simultaneos NO es inconsistencia por si solo — puede ser " +
        "freelance, part-time, o consultoria. Solo reportalo si la simultaneidad parece " +
        "imposible (ej: dos roles full-time en ciudades distintas al mismo tiempo).\n" +
        "5. Solo reporta lo que realmente esta en el texto del CV. No supongas ni inventes.\n\n" +
        "SI vale la pena reportar:\n" +
        "- Gaps de tiempo SIN EXPLICACION de mas de 6 meses entre empleos\n" +
        "- Saltos de seniority muy bruscos sin experiencia que lo justifique (ej: junior a CTO)\n" +
        "- Fechas que matematicamente son imposibles\n" +
        "- Habilidades tecnicas muy avanzadas que no aparecen en ninguna experiencia laboral\n" +
        "- Logros con numeros muy especificos y llamativos sin contexto (ej: 'reduje costos 94%')\n" +
        "- Titulos o instituciones educativas vagas o no verificables\n\n" +
        "Para cada hallazgo RELEVANTE genera un objeto con:\n" +
        "- category: string (nombre corto del tipo de inconsistencia)\n" +
        "- description: string (explicacion clara y especifica, citando los datos del CV)\n" +
        "- riskLevel: \"ALTO\", \"MEDIO\" o \"BAJO\"\n" +
        "- suggestedQuestion: string (pregunta concreta para validar en entrevista)\n\n" +
        "Tambien incluye un \"summary\" de 2-3 oraciones con el perfil de riesgo general.\n\n" +
        "Responde UNICAMENTE con JSON valido:\n" +
        "{\"findings\": [...], \"summary\": \"...\"}\n\n" +
        "Si no hay inconsistencias relevantes, devuelve findings vacio con summary positivo. " +
        "Es mejor no reportar nada que reportar falsos positivos.\n" +
        "</instrucciones>\n\n" +
        "<cv>\n" + cvText + "\n</cv>\n\n" +
        "<tarea>\n" +
        "Analiza el CV aplicando estrictamente las reglas de relevancia. " +
        "Ante la duda de si algo es inconsistencia real o no, NO lo reportes. " +
        "Responde SOLO con el JSON pedido.\n" +
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
    string cvText)
        {
            var rankResult = await RankCvsAsync(jobDescription, new List<(string, string)> { (name, cvText) });
            var ranking = rankResult.Rankings.First();

            if (ranking.Score < 7)
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

    public async Task<DeepAnalysisResponse> AnalyzeCandidateDeepAsync(
        string jobDescription,
        string cvText)
    {
        var inconsResult = await DetectInconsistenciesAsync(cvText);
        await Task.Delay(500);
        var questResult = await GenerateQuestionsAsync(jobDescription, cvText);

        return new DeepAnalysisResponse(inconsResult, questResult);
    }
}