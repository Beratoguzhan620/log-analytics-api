using LogAnalytics.Core.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LogAnalytics.Infrastructure.Persistence.Configurations;

internal sealed class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> builder)
    {
        builder.ToTable("sources");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Name).HasMaxLength(Source.MaxNameLength);
        builder.Property(s => s.KeyPrefix).HasMaxLength(ApiKey.PrefixLength);
        builder.Property(s => s.KeyHash).HasMaxLength(32);
        builder.HasIndex(s => s.KeyPrefix).IsUnique();
        builder.HasIndex(s => s.Name).IsUnique();
        builder.Ignore(s => s.IsActive);
    }
}
