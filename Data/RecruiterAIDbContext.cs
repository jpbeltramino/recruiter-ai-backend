using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using RecruiterAI.Models.Entities;

namespace RecruiterAI.Data;

/// <summary>
/// DbContext del schema v2 (ver Database/schema.sql y Database/SCHEMA.md).
/// El schema real se aplica corriendo schema.sql directamente contra Postgres
/// (ver README) — este mapeo tiene que coincidir 1:1 con esos nombres de
/// tabla/columna. No se usan migraciones de EF Core todavía.
/// </summary>
public class RecruiterAIDbContext(DbContextOptions<RecruiterAIDbContext> options) : DbContext(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<User> Users => Set<User>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<PipelineStage> PipelineStages => Set<PipelineStage>();
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<CandidateNote> CandidateNotes => Set<CandidateNote>();
    public DbSet<CandidatePosition> CandidatePositions => Set<CandidatePosition>();
    public DbSet<CandidateStageHistory> CandidateStageHistories => Set<CandidateStageHistory>();
    public DbSet<CvAnalysis> CvAnalyses => Set<CvAnalysis>();
    public DbSet<InconsistencyReport> InconsistencyReports => Set<InconsistencyReport>();
    public DbSet<InterviewQuestionSet> InterviewQuestionSets => Set<InterviewQuestionSet>();
    public DbSet<LinkedInAnalysis> LinkedInAnalyses => Set<LinkedInAnalysis>();
    public DbSet<JobAdGeneration> JobAdGenerations => Set<JobAdGeneration>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ─── workspaces ─────────────────────────────────────────────────
        modelBuilder.Entity<Workspace>(e =>
        {
            e.ToTable("workspaces");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").IsRequired();
            e.Property(x => x.PlanTier).HasColumnName("plan_tier").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        // ─── users ──────────────────────────────────────────────────────
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Email).HasColumnName("email").IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
            e.Property(x => x.FullName).HasColumnName("full_name").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        // ─── workspace_members ──────────────────────────────────────────
        modelBuilder.Entity<WorkspaceMember>(e =>
        {
            e.ToTable("workspace_members");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Role).HasColumnName("role").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.WorkspaceId, x.UserId }).IsUnique();

            e.HasOne(x => x.Workspace).WithMany(w => w.Members)
                .HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ─── clients ────────────────────────────────────────────────────
        modelBuilder.Entity<Client>(e =>
        {
            e.ToTable("clients");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
            e.Property(x => x.Name).HasColumnName("name").IsRequired();
            e.Property(x => x.ContactName).HasColumnName("contact_name");
            e.Property(x => x.ContactEmail).HasColumnName("contact_email");
            e.Property(x => x.ContactPhone).HasColumnName("contact_phone");
            e.Property(x => x.Notes).HasColumnName("notes");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne(x => x.Workspace).WithMany(w => w.Clients)
                .HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        // ─── positions ──────────────────────────────────────────────────
        modelBuilder.Entity<Position>(e =>
        {
            e.ToTable("positions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.Title).HasColumnName("title").IsRequired();
            e.Property(x => x.Description).HasColumnName("description").IsRequired();
            e.Property(x => x.Status).HasColumnName("status").IsRequired();
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.Property(x => x.ClosedAt).HasColumnName("closed_at");

            e.HasOne(x => x.Workspace).WithMany(w => w.Positions)
                .HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Client).WithMany(c => c.Positions)
                .HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.SetNull);
        });

        // ─── pipeline_stages ────────────────────────────────────────────
        modelBuilder.Entity<PipelineStage>(e =>
        {
            e.ToTable("pipeline_stages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
            e.Property(x => x.PositionId).HasColumnName("position_id");
            e.Property(x => x.Name).HasColumnName("name").IsRequired();
            e.Property(x => x.OrderIndex).HasColumnName("order_index");
            e.Property(x => x.IsTerminal).HasColumnName("is_terminal");
            e.Property(x => x.Color).HasColumnName("color");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.WorkspaceId, x.PositionId, x.OrderIndex }).IsUnique();

            e.HasOne(x => x.Workspace).WithMany(w => w.PipelineStages)
                .HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Position).WithMany(p => p.CustomStages)
                .HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Cascade);
        });

        // ─── candidates ─────────────────────────────────────────────────
        modelBuilder.Entity<Candidate>(e =>
        {
            e.ToTable("candidates");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
            e.Property(x => x.FullName).HasColumnName("full_name").IsRequired();
            e.Property(x => x.Email).HasColumnName("email");
            e.Property(x => x.Phone).HasColumnName("phone");
            e.Property(x => x.LinkedinUrl).HasColumnName("linkedin_url");
            e.Property(x => x.CvText).HasColumnName("cv_text");
            e.Property(x => x.CvFileUrl).HasColumnName("cv_file_url");
            e.Property(x => x.Source).HasColumnName("source");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne(x => x.Workspace).WithMany(w => w.Candidates)
                .HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        // ─── candidate_notes ────────────────────────────────────────────
        modelBuilder.Entity<CandidateNote>(e =>
        {
            e.ToTable("candidate_notes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CandidateId).HasColumnName("candidate_id");
            e.Property(x => x.AuthorId).HasColumnName("author_id");
            e.Property(x => x.Note).HasColumnName("note").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");

            e.HasOne(x => x.Candidate).WithMany(c => c.Notes)
                .HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Cascade);
        });

        // ─── candidate_positions ────────────────────────────────────────
        modelBuilder.Entity<CandidatePosition>(e =>
        {
            e.ToTable("candidate_positions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CandidateId).HasColumnName("candidate_id");
            e.Property(x => x.PositionId).HasColumnName("position_id");
            e.Property(x => x.CurrentStageId).HasColumnName("current_stage_id");
            e.Property(x => x.AppliedAt).HasColumnName("applied_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.CandidateId, x.PositionId }).IsUnique();

            e.HasOne(x => x.Candidate).WithMany(c => c.CandidatePositions)
                .HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Position).WithMany(p => p.CandidatePositions)
                .HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CurrentStage).WithMany(s => s.CandidatePositionsInStage)
                .HasForeignKey(x => x.CurrentStageId).OnDelete(DeleteBehavior.Restrict);
        });

        // ─── candidate_stage_history ────────────────────────────────────
        modelBuilder.Entity<CandidateStageHistory>(e =>
        {
            e.ToTable("candidate_stage_history");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CandidatePositionId).HasColumnName("candidate_position_id");
            e.Property(x => x.StageId).HasColumnName("stage_id");
            e.Property(x => x.ChangedBy).HasColumnName("changed_by");
            e.Property(x => x.ChangedAt).HasColumnName("changed_at");
            e.Property(x => x.Notes).HasColumnName("notes");

            e.HasOne(x => x.CandidatePosition).WithMany(cp => cp.StageHistory)
                .HasForeignKey(x => x.CandidatePositionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Stage).WithMany(s => s.StageHistoryEntries)
                .HasForeignKey(x => x.StageId).OnDelete(DeleteBehavior.Restrict);
        });

        // ─── cv_analyses ────────────────────────────────────────────────
        modelBuilder.Entity<CvAnalysis>(e =>
        {
            e.ToTable("cv_analyses");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CandidateId).HasColumnName("candidate_id");
            e.Property(x => x.PositionId).HasColumnName("position_id");
            e.Property(x => x.Score).HasColumnName("score");
            e.Property(x => x.Strengths).HasColumnName("strengths").HasColumnType("jsonb")
                .HasConversion(JsonConverters.StringList).Metadata.SetValueComparer(JsonConverters.StringListComparer);
            e.Property(x => x.Weaknesses).HasColumnName("weaknesses").HasColumnType("jsonb")
                .HasConversion(JsonConverters.StringList).Metadata.SetValueComparer(JsonConverters.StringListComparer);
            e.Property(x => x.Verdict).HasColumnName("verdict").IsRequired();
            e.Property(x => x.Reasoning).HasColumnName("reasoning");
            e.Property(x => x.RawResponse).HasColumnName("raw_response").HasColumnType("jsonb");
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");

            e.HasOne(x => x.Candidate).WithMany(c => c.CvAnalyses)
                .HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Position).WithMany()
                .HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.SetNull);
        });

        // ─── inconsistency_reports ──────────────────────────────────────
        modelBuilder.Entity<InconsistencyReport>(e =>
        {
            e.ToTable("inconsistency_reports");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CandidateId).HasColumnName("candidate_id");
            e.Property(x => x.Findings).HasColumnName("findings").HasColumnType("jsonb")
                .HasConversion(JsonConverters.FindingsList).Metadata.SetValueComparer(JsonConverters.FindingsListComparer);
            e.Property(x => x.Summary).HasColumnName("summary");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");

            e.HasOne(x => x.Candidate).WithMany(c => c.InconsistencyReports)
                .HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Cascade);
        });

        // ─── interview_question_sets ────────────────────────────────────
        modelBuilder.Entity<InterviewQuestionSet>(e =>
        {
            e.ToTable("interview_question_sets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CandidateId).HasColumnName("candidate_id");
            e.Property(x => x.PositionId).HasColumnName("position_id");
            e.Property(x => x.Technical).HasColumnName("technical").HasColumnType("jsonb")
                .HasConversion(JsonConverters.QuestionsList).Metadata.SetValueComparer(JsonConverters.QuestionsListComparer);
            e.Property(x => x.Cultural).HasColumnName("cultural").HasColumnType("jsonb")
                .HasConversion(JsonConverters.QuestionsList).Metadata.SetValueComparer(JsonConverters.QuestionsListComparer);
            e.Property(x => x.WeaknessValidation).HasColumnName("weakness_validation").HasColumnType("jsonb")
                .HasConversion(JsonConverters.QuestionsList).Metadata.SetValueComparer(JsonConverters.QuestionsListComparer);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");

            e.HasOne(x => x.Candidate).WithMany(c => c.InterviewQuestionSets)
                .HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Position).WithMany()
                .HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.SetNull);
        });

        // ─── linkedin_analyses ──────────────────────────────────────────
        modelBuilder.Entity<LinkedInAnalysis>(e =>
        {
            e.ToTable("linkedin_analyses");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CandidateId).HasColumnName("candidate_id");
            e.Property(x => x.PositionId).HasColumnName("position_id");
            e.Property(x => x.ProfileText).HasColumnName("profile_text");
            e.Property(x => x.AlignmentLevel).HasColumnName("alignment_level").IsRequired();
            e.Property(x => x.PositiveSignals).HasColumnName("positive_signals").HasColumnType("jsonb")
                .HasConversion(JsonConverters.StringList).Metadata.SetValueComparer(JsonConverters.StringListComparer);
            e.Property(x => x.RedFlags).HasColumnName("red_flags").HasColumnType("jsonb")
                .HasConversion(JsonConverters.StringList).Metadata.SetValueComparer(JsonConverters.StringListComparer);
            e.Property(x => x.ScreeningQuestions).HasColumnName("screening_questions").HasColumnType("jsonb")
                .HasConversion(JsonConverters.StringList).Metadata.SetValueComparer(JsonConverters.StringListComparer);
            e.Property(x => x.Recommendation).HasColumnName("recommendation").IsRequired();
            e.Property(x => x.RecommendationReason).HasColumnName("recommendation_reason");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");

            e.HasOne(x => x.Candidate).WithMany(c => c.LinkedInAnalyses)
                .HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Position).WithMany()
                .HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.SetNull);
        });

        // ─── job_ad_generations ─────────────────────────────────────────
        modelBuilder.Entity<JobAdGeneration>(e =>
        {
            e.ToTable("job_ad_generations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.PositionId).HasColumnName("position_id");
            e.Property(x => x.Platform).HasColumnName("platform");
            e.Property(x => x.GeneratedText).HasColumnName("generated_text").IsRequired();
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");

            e.HasOne(x => x.Position).WithMany(p => p.JobAdGenerations)
                .HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Cascade);
        });

        // ─── subscriptions ──────────────────────────────────────────────
        modelBuilder.Entity<Subscription>(e =>
        {
            e.ToTable("subscriptions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
            e.Property(x => x.MpPreapprovalId).HasColumnName("mp_preapproval_id");
            e.HasIndex(x => x.MpPreapprovalId).IsUnique();
            e.Property(x => x.PlanTier).HasColumnName("plan_tier").IsRequired();
            e.Property(x => x.Status).HasColumnName("status").IsRequired();
            e.Property(x => x.CurrentPeriodEnd).HasColumnName("current_period_end");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne(x => x.Workspace).WithMany(w => w.Subscriptions)
                .HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>
/// Conversores para las columnas jsonb: List&lt;T&gt; en C# &lt;-&gt; texto JSON en la
/// base. Con ValueComparer explícito para que EF Core detecte cambios en el
/// contenido de las listas (por default solo compara la referencia).
/// </summary>
internal static class JsonConverters
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static readonly Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<string>, string> StringList =
        new(
            v => JsonSerializer.Serialize(v, Options),
            v => JsonSerializer.Deserialize<List<string>>(v, Options) ?? new List<string>()
        );

    public static readonly ValueComparer<List<string>> StringListComparer = new(
        (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
        v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
        v => v.ToList()
    );

    public static readonly Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<InconsistencyFindingJson>, string> FindingsList =
        new(
            v => JsonSerializer.Serialize(v, Options),
            v => JsonSerializer.Deserialize<List<InconsistencyFindingJson>>(v, Options) ?? new List<InconsistencyFindingJson>()
        );

    public static readonly ValueComparer<List<InconsistencyFindingJson>> FindingsListComparer = new(
        (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
        v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
        v => v.ToList()
    );

    public static readonly Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<InterviewQuestionJson>, string> QuestionsList =
        new(
            v => JsonSerializer.Serialize(v, Options),
            v => JsonSerializer.Deserialize<List<InterviewQuestionJson>>(v, Options) ?? new List<InterviewQuestionJson>()
        );

    public static readonly ValueComparer<List<InterviewQuestionJson>> QuestionsListComparer = new(
        (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
        v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
        v => v.ToList()
    );
}
