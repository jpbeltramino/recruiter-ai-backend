using RecruiterAI.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Register services
builder.Services.AddSingleton<ClaudeService>();
builder.Services.AddSingleton<CvParserService>();

// CORS — allow all origins for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Global JSON error handling — never return HTML
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

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        var token = context.Request.Headers["X-Api-Token"].ToString();
        var config = context.RequestServices.GetRequiredService<IConfiguration>();
        var validTokens = config
            .GetSection("Auth:ValidTokens")
            .Get<List<string>>() ?? [];

        if (!validTokens.Contains(token))
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\": \"Token inválido\"}");
            return;
        }
    }
    await next();
});

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

// Fallback: serve index.html for SPA routing
app.MapFallbackToFile("index.html");

app.Run();
