using System.Collections.Concurrent;
using RecruiterAI.Models;

namespace RecruiterAI.Services;

public class JobService
{
    private readonly ILogger<JobService> _logger;
    private readonly ConcurrentDictionary<string, AnalysisJob> _jobs = new();
    private readonly TimeSpan _jobRetention = TimeSpan.FromMinutes(30);

    public JobService(ILogger<JobService> logger)
    {
        _logger = logger;
        _ = Task.Run(CleanupLoop);
    }

    public AnalysisJob CreateJob(int totalCandidates)
    {
        var job = new AnalysisJob
        {
            Id = Guid.NewGuid().ToString("N"),
            Status = "running",
            Current = 0,
            Total = totalCandidates,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Results = new List<UnifiedCandidateResult>()
        };

        _jobs[job.Id] = job;
        _logger.LogInformation("Job creado {JobId} con {Total} candidatos", job.Id, totalCandidates);
        return job;
    }

    public void UpdateProgress(string jobId, int current, UnifiedCandidateResult? result = null)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Current = current;
            job.UpdatedAt = DateTime.UtcNow;
            if (result != null)
            {
                lock (job.Results)
                {
                    job.Results.Add(result);
                }
            }
        }
    }

    public void CompleteJob(string jobId, List<UnifiedCandidateResult> finalResults)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = "completed";
            job.Results = finalResults;
            job.UpdatedAt = DateTime.UtcNow;
            _logger.LogInformation("Job {JobId} completado con {Count} resultados", jobId, finalResults.Count);
        }
    }

    public void FailJob(string jobId, string error)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = "failed";
            job.Error = error;
            job.UpdatedAt = DateTime.UtcNow;
            _logger.LogError("Job {JobId} falló: {Error}", jobId, error);
        }
    }

    public AnalysisJob? GetJob(string jobId)
    {
        _jobs.TryGetValue(jobId, out var job);
        return job;
    }

    private async Task CleanupLoop()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromMinutes(5));
            var cutoff = DateTime.UtcNow - _jobRetention;
            var toRemove = _jobs
                .Where(kv => kv.Value.UpdatedAt < cutoff && kv.Value.Status != "running")
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in toRemove)
            {
                _jobs.TryRemove(key, out _);
            }

            if (toRemove.Count > 0)
            {
                _logger.LogInformation("Cleanup: removidos {Count} jobs antiguos", toRemove.Count);
            }
        }
    }
}