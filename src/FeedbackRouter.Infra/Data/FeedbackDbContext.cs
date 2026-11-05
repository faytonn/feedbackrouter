using FeedbackRouter.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FeedbackRouter.Infra.Data;

public sealed class FeedbackDbContext(DbContextOptions<FeedbackDbContext> options) : DbContext(options)
{
    public DbSet<FeedbackItem> FeedbackItems => Set<FeedbackItem>();
    public DbSet<TriageProposal> TriageProposals => Set<TriageProposal>();
    public DbSet<TriageDecision> TriageDecisions => Set<TriageDecision>();
    public DbSet<WorkItemLink> WorkItemLinks => Set<WorkItemLink>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Enums are stored by name so the tables read well in a query window.
        builder.Properties<FeedbackStatus>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<FeedbackSource>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<Area>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<FeedbackType>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<DecisionAction>().HaveConversion<string>().HaveMaxLength(16);

        // Every timestamp is UTC. datetime2 does not store DateTimeKind, so mark
        // values as UTC on the way out or they serialize without a 'Z'.
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<FeedbackItem>(e =>
        {
            e.ToTable("FeedbackItems");
            e.Property(x => x.RawText).IsRequired();
            e.Property(x => x.SubmitterEmail).HasMaxLength(320);
            e.Property(x => x.TriageError).HasMaxLength(1000);
            e.Property(x => x.ReviewerName).HasMaxLength(200);
            e.Property(x => x.Embedding)
                .HasConversion(EmbeddingSerializer.Converter, EmbeddingComparer);
            e.HasIndex(x => x.Status);

            // A tracked save of an item fails if its status changed underneath,
            // e.g. a reviewer dismissed it while the triage worker was running.
            e.Property(x => x.Status).IsConcurrencyToken();
            e.HasOne<FeedbackItem>().WithMany().HasForeignKey(x => x.MergedIntoId).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Proposals).WithOne().HasForeignKey(p => p.FeedbackItemId);
            e.HasOne(x => x.Decision).WithOne().HasForeignKey<TriageDecision>(d => d.FeedbackItemId);
            e.HasOne(x => x.WorkItemLink).WithOne().HasForeignKey<WorkItemLink>(l => l.FeedbackItemId);
        });

        model.Entity<TriageProposal>(e =>
        {
            e.ToTable("TriageProposals");
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Summary).HasMaxLength(1000);
            e.Property(x => x.ModelName).HasMaxLength(100);
            e.Property(x => x.PromptVersion).HasMaxLength(50);
            e.HasIndex(x => new { x.FeedbackItemId, x.ProposedAt });
            e.OwnsMany(x => x.DuplicateCandidates, d =>
            {
                d.ToTable("DuplicateCandidates");
                d.WithOwner().HasForeignKey("TriageProposalId");
                d.HasKey("TriageProposalId", nameof(DuplicateCandidate.FeedbackItemId));
            });
        });

        model.Entity<TriageDecision>(e =>
        {
            e.ToTable("TriageDecisions");
            e.HasKey(x => x.FeedbackItemId);
            e.Property(x => x.ReviewerName).HasMaxLength(200);
            e.Property(x => x.FinalTitle).HasMaxLength(200);
            e.Property(x => x.FinalSummary).HasMaxLength(1000);
            e.Property(x => x.Notes).HasMaxLength(2000);
        });

        // One link per item, enforced by the primary key: the database refuses
        // a second filing even if the application check were ever bypassed.
        model.Entity<WorkItemLink>(e =>
        {
            e.ToTable("WorkItemLinks");
            e.HasKey(x => x.FeedbackItemId);
            e.Property(x => x.AdoUrl).HasMaxLength(500);
        });
    }

    private static readonly ValueComparer<float[]?> EmbeddingComparer = new(
        (a, b) => a == null ? b == null : b != null && Enumerable.SequenceEqual(a, b),
        v => v == null ? 0 : HashCode.Combine(v.Length, v.Length > 0 ? v[0] : 0f),
        v => v == null ? null : v.ToArray());
}
