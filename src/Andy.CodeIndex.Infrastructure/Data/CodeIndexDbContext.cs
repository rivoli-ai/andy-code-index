using Andy.CodeIndex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Andy.CodeIndex.Infrastructure.Data;

public class CodeIndexDbContext : DbContext
{
    public CodeIndexDbContext(DbContextOptions<CodeIndexDbContext> options)
        : base(options)
    {
    }

    public DbSet<Repository> Repositories => Set<Repository>();
    public DbSet<Commit> Commits => Set<Commit>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<RepositoryFile> RepositoryFiles => Set<RepositoryFile>();
    public DbSet<Enrichment> Enrichments => Set<Enrichment>();
    public DbSet<ContentEmbedding> ContentEmbeddings => Set<ContentEmbedding>();
    public DbSet<IndexingTask> IndexingTasks => Set<IndexingTask>();
    public DbSet<ChunkLineRange> ChunkLineRanges => Set<ChunkLineRange>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<IndexingRun> IndexingRuns => Set<IndexingRun>();
    public DbSet<SettingsChangeLog> SettingsChangeLogs => Set<SettingsChangeLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var isNpgsql = Database.IsNpgsql();

        if (isNpgsql)
        {
            modelBuilder.HasPostgresExtension("vector");
        }

        ConfigureRepository(modelBuilder, isNpgsql);
        ConfigureCommit(modelBuilder, isNpgsql);
        ConfigureBranch(modelBuilder, isNpgsql);
        ConfigureTag(modelBuilder, isNpgsql);
        ConfigureRepositoryFile(modelBuilder, isNpgsql);
        ConfigureEnrichment(modelBuilder, isNpgsql);
        ConfigureContentEmbedding(modelBuilder, isNpgsql);
        ConfigureIndexingTask(modelBuilder, isNpgsql);
        ConfigureChunkLineRange(modelBuilder, isNpgsql);
        ConfigureUserSettings(modelBuilder);
        ConfigureIndexingRun(modelBuilder);
        ConfigureSettingsChangeLog(modelBuilder);
    }

    private static void ConfigureRepository(ModelBuilder modelBuilder, bool isNpgsql)
    {
        modelBuilder.Entity<Repository>(builder =>
        {
            builder.HasKey(r => r.Id);

            builder.Property(r => r.Name).IsRequired().HasMaxLength(256);
            builder.Property(r => r.Url).IsRequired().HasMaxLength(2048);
            builder.Property(r => r.CloneUrl).HasMaxLength(2048);
            builder.Property(r => r.DefaultBranch).HasMaxLength(256);
            builder.Property(r => r.PersonalAccessToken).HasMaxLength(512);
            builder.Property(r => r.LastIndexedCommitSha).HasMaxLength(40);
            builder.Property(r => r.Status).IsRequired().HasMaxLength(32);
            builder.Property(r => r.Provider).HasConversion<string>().HasMaxLength(32);

            builder.HasIndex(r => r.Url).IsUnique();
            builder.HasIndex(r => r.Name);
            builder.HasIndex(r => r.Status);
        });
    }

    private static void ConfigureCommit(ModelBuilder modelBuilder, bool isNpgsql)
    {
        modelBuilder.Entity<Commit>(builder =>
        {
            builder.HasKey(c => c.Id);

            builder.Property(c => c.Sha).IsRequired().HasMaxLength(40);
            builder.Property(c => c.Message).IsRequired();
            builder.Property(c => c.AuthorName).HasMaxLength(256);
            builder.Property(c => c.AuthorEmail).HasMaxLength(256);

            if (isNpgsql)
            {
                // CreatedAt set in application code — no server default to avoid EF concurrency issues
            }

            builder.HasOne(c => c.Repository)
                .WithMany(r => r.Commits)
                .HasForeignKey(c => c.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(c => c.Sha);
            builder.HasIndex(c => new { c.RepositoryId, c.Sha }).IsUnique();
            builder.HasIndex(c => c.CommittedAt);
        });
    }

    private static void ConfigureBranch(ModelBuilder modelBuilder, bool isNpgsql)
    {
        modelBuilder.Entity<Branch>(builder =>
        {
            builder.HasKey(b => b.Id);

            builder.Property(b => b.Name).IsRequired().HasMaxLength(256);
            builder.Property(b => b.HeadCommitSha).HasMaxLength(40);

            if (isNpgsql)
            {
                // CreatedAt set in application code
            }

            builder.HasOne(b => b.Repository)
                .WithMany(r => r.Branches)
                .HasForeignKey(b => b.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(b => new { b.RepositoryId, b.Name }).IsUnique();
        });
    }

    private static void ConfigureTag(ModelBuilder modelBuilder, bool isNpgsql)
    {
        modelBuilder.Entity<Tag>(builder =>
        {
            builder.HasKey(t => t.Id);

            builder.Property(t => t.Name).IsRequired().HasMaxLength(256);
            builder.Property(t => t.CommitSha).IsRequired().HasMaxLength(40);

            if (isNpgsql)
            {
                // CreatedAt set in application code
            }

            builder.HasOne(t => t.Repository)
                .WithMany(r => r.Tags)
                .HasForeignKey(t => t.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(t => new { t.RepositoryId, t.Name }).IsUnique();
        });
    }

    private static void ConfigureRepositoryFile(ModelBuilder modelBuilder, bool isNpgsql)
    {
        modelBuilder.Entity<RepositoryFile>(builder =>
        {
            builder.HasKey(f => f.Id);

            builder.Property(f => f.Path).IsRequired().HasMaxLength(1024);
            builder.Property(f => f.Language).HasMaxLength(64);
            builder.Property(f => f.Hash).HasMaxLength(64);

            if (isNpgsql)
            {
                // CreatedAt set in application code
            }

            builder.HasOne(f => f.Commit)
                .WithMany(c => c.Files)
                .HasForeignKey(f => f.CommitId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(f => new { f.CommitId, f.Path }).IsUnique();
            builder.HasIndex(f => f.Language);
        });
    }

    private static void ConfigureEnrichment(ModelBuilder modelBuilder, bool isNpgsql)
    {
        modelBuilder.Entity<Enrichment>(builder =>
        {
            builder.HasKey(e => e.Id);

            builder.Property(e => e.Type).HasConversion<string>().IsRequired().HasMaxLength(32);
            builder.Property(e => e.Subtype).HasConversion<string>().IsRequired().HasMaxLength(32);
            builder.Property(e => e.Title).HasMaxLength(512);
            builder.Property(e => e.Content).IsRequired();
            builder.Property(e => e.FilePath).HasMaxLength(1024);
            builder.Property(e => e.Language).HasMaxLength(64);

            if (isNpgsql)
            {
                // CreatedAt set in application code

                // tsvector generated column for BM25 full-text search
                builder.Property(e => e.SearchVector)
                    .HasColumnType("tsvector")
                    .HasComputedColumnSql(
                        "to_tsvector('english', coalesce(\"Content\", ''))",
                        stored: true);

                // GIN index on tsvector for full-text search performance
                builder.HasIndex(e => e.SearchVector).HasMethod("GIN");
            }
            else
            {
                builder.Ignore(e => e.SearchVector);
            }

            builder.HasOne(e => e.Repository)
                .WithMany(r => r.Enrichments)
                .HasForeignKey(e => e.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(e => e.Commit)
                .WithMany(c => c.Enrichments)
                .HasForeignKey(e => e.CommitId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasIndex(e => new { e.Type, e.Subtype });
            builder.HasIndex(e => e.RepositoryId);
            builder.HasIndex(e => e.CommitId);
            builder.HasIndex(e => e.Language);
            builder.HasIndex(e => e.FilePath);
        });
    }

    private static void ConfigureContentEmbedding(ModelBuilder modelBuilder, bool isNpgsql)
    {
        modelBuilder.Entity<ContentEmbedding>(builder =>
        {
            builder.HasKey(e => e.Id);

            builder.Property(e => e.IndexType).HasConversion<string>().IsRequired().HasMaxLength(16);

            if (isNpgsql)
            {
                builder.Property(e => e.EmbeddingVector)
                    .IsRequired()
                    .HasColumnType("vector(1536)");

                // CreatedAt set in application code

                // HNSW index for cosine similarity search
                builder.HasIndex(e => e.EmbeddingVector)
                    .HasMethod("hnsw")
                    .HasOperators("vector_cosine_ops");
            }
            else
            {
                builder.Ignore(e => e.EmbeddingVector);
            }

            builder.HasOne(e => e.Enrichment)
                .WithMany(en => en.Embeddings)
                .HasForeignKey(e => e.EnrichmentId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(e => e.IndexType);
            builder.HasIndex(e => e.EnrichmentId);
        });
    }

    private static void ConfigureIndexingTask(ModelBuilder modelBuilder, bool isNpgsql)
    {
        modelBuilder.Entity<IndexingTask>(builder =>
        {
            builder.HasKey(t => t.Id);

            builder.Property(t => t.Operation).HasConversion<string>().IsRequired().HasMaxLength(64);
            builder.Property(t => t.Status).HasConversion<string>().IsRequired().HasMaxLength(32);
            builder.Property(t => t.ErrorMessage).HasMaxLength(4096);

            if (isNpgsql)
            {
                // CreatedAt set in application code
            }

            builder.HasOne(t => t.Repository)
                .WithMany(r => r.IndexingTasks)
                .HasForeignKey(t => t.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(t => t.Status);
            builder.HasIndex(t => t.ChainId);
            builder.HasIndex(t => new { t.Status, t.Priority, t.CreatedAt });
        });
    }

    private static void ConfigureChunkLineRange(ModelBuilder modelBuilder, bool isNpgsql)
    {
        modelBuilder.Entity<ChunkLineRange>(builder =>
        {
            builder.HasKey(c => c.Id);

            builder.HasOne(c => c.Enrichment)
                .WithMany(e => e.LineRanges)
                .HasForeignKey(c => c.EnrichmentId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(c => c.EnrichmentId);
        });
    }

    private static void ConfigureUserSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserSettings>(builder =>
        {
            builder.HasKey(s => s.Id);

            builder.Property(s => s.UserId).IsRequired().HasMaxLength(256);
            builder.Property(s => s.EmbeddingApiKey).HasMaxLength(1024);
            builder.Property(s => s.EmbeddingModel).HasMaxLength(128);
            builder.Property(s => s.LlmApiKey).HasMaxLength(1024);

            builder.HasIndex(s => s.UserId).IsUnique();
        });
    }

    private static void ConfigureIndexingRun(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IndexingRun>(builder =>
        {
            builder.HasKey(r => r.Id);

            builder.Property(r => r.Status).IsRequired().HasMaxLength(32);
            builder.Property(r => r.ErrorMessage).HasMaxLength(4096);

            builder.HasOne(r => r.Repository)
                .WithMany()
                .HasForeignKey(r => r.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(r => r.RepositoryId);
            builder.HasIndex(r => r.ChainId);
            builder.HasIndex(r => r.StartedAt);
        });
    }

    private static void ConfigureSettingsChangeLog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SettingsChangeLog>(builder =>
        {
            builder.HasKey(l => l.Id);
            builder.Property(l => l.UserId).IsRequired().HasMaxLength(256);
            builder.Property(l => l.Field).IsRequired().HasMaxLength(128);
            builder.Property(l => l.OldValue).HasMaxLength(256);
            builder.Property(l => l.NewValue).HasMaxLength(256);
            builder.Property(l => l.Action).IsRequired().HasMaxLength(32);
            builder.HasIndex(l => l.UserId);
            builder.HasIndex(l => l.CreatedAt);
        });
    }
}
