using RecruiterAI.Services;
using Serilog;
using Sentry.AspNetCore;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
    )
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseSentry(options =>
{
    options.Dsn = builder.Configuration["Sentry:Dsn"] ?? string.Empty;
    options.Environment = builder.Environment.EnvironmentName;
    options.TracesSampleRate = 0.2;
    options.MinimumEventLevel = LogLevel.Warning;
    options.AttachStacktrace = true;
    options.SendDefaultPii = false;
});

builder.Host.UseSerilog();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Register services
builder.Services.AddSingleton<ClaudeService>();
builder.Services.AddSingleton<CvParserService>();
builder.Services.AddSingleton<RateLimitService>();
builder.Services.AddSingleton<FileValidationService>();
builder.Services.AddSingleton<JobService>();

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

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "{RequestMethod} {RequestPath} {StatusCode} en {Elapsed:0}ms";
    options.GetLevel = (httpContext, elapsed, ex) =>
    {
        if (ex != null) return Serilog.Events.LogEventLevel.Error;
        if (httpContext.Response.StatusCode >= 500) return Serilog.Events.LogEventLevel.Error;
        if (httpContext.Response.StatusCode >= 400) return Serilog.Events.LogEventLevel.Warning;
        return Serilog.Events.LogEventLevel.Information;
    };
});

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        SentrySdk.CaptureException(ex);

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

try
{
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "La aplicación terminó inesperadamente");
}
finally
{
    Log.CloseAndFlush();
}
