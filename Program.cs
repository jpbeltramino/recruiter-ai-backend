using RecruiterAI.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Register services
builder.Services.AddSingleton<ClaudeService>();
builder.Services.AddSingleton<CvParserService>();
builder.Services.AddSingleton<RateLimitService>();
builder.Services.AddSingleton<FileValidationService>();

// CORS — allow all origins for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? Array.Empty<string>();

        if (allowedOrigins.Length == 0)
        {
            throw new InvalidOperationException(
                "Cors:AllowedOrigins no está configurado. " +
                "Definir al menos un origen permitido."
            );
        }

        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders(
                  "X-RateLimit-Limit",
                  "X-RateLimit-Used",
                  "X-RateLimit-Reset"
              );
    });
});

var app = builder.Build();

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        var error = System.Text.Json.JsonSerializer.Serialize(new { error = ex.Message });
        await context.Response.WriteAsync(error);
    }
});

app.UseCors();

app.Use(async (context, next) =>
{
    if (context.Request.Method == "OPTIONS")
    {
        await next();
        return;
    }

    if (context.Request.Path.StartsWithSegments("/api"))
    {
        var token = context.Request.Headers["X-Api-Token"].ToString();
        var config = context.RequestServices.GetRequiredService<IConfiguration>();
        var validTokens = config
            .GetSection("Auth:ValidTokens")
            .Get<List<string>>() ?? new List<string>();

        if (!validTokens.Contains(token))
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\": \"Token inválido\"}");
            return;
        }

        var rateLimit = context.RequestServices.GetRequiredService<RateLimitService>();
        var result = rateLimit.CheckAndIncrement(token);

        context.Response.Headers["X-RateLimit-Limit"] = result.Limit.ToString();
        context.Response.Headers["X-RateLimit-Used"] = result.Used.ToString();
        context.Response.Headers["X-RateLimit-Reset"] = result.ResetAt.ToString("o");

        if (!result.Allowed)
        {
            context.Response.StatusCode = 429;
            context.Response.ContentType = "application/json";
            var error = $"{{\"error\": \"Límite diario alcanzado ({result.Limit} análisis). Se renueva mañana.\"}}";
            await context.Response.WriteAsync(error);
            return;
        }
    }
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
