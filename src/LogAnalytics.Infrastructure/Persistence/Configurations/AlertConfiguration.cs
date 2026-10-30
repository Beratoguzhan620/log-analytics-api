using LogAnalytics.Core.Alerts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LogAnalytics.Infrastructure.Persistence.Configurations;

internal sealed class AlertRuleConfiguration : IEntityTypeConfiguration<AlertRule>
{
    public void Configure(EntityTypeBuilder<AlertRule> builder)
    {
        builder.ToTable("alert_rules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Name).HasMaxLength(AlertRule.MaxNameLength);
        builder.Property(r => r.Service).HasMaxLength(100);
        builder.Property(r => r.MinLevel).HasConversion<short>();
        builder.Property(r => r.Channel).HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.WebhookUrl).HasMaxLength(500);
        builder.HasIndex(r => r.Name).IsUnique();
    }
}

internal sealed class AlertFiringConfiguration : IEntityTypeConfiguration<AlertFiring>
{
    public void Configure(EntityTypeBuilder<AlertFiring> builder)
    {
        builder.ToTable("alert_firings");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.RuleName).HasMaxLength(AlertRule.MaxNameLength);
        builder.Property(f => f.SampleMessage).HasMaxLength(AlertFiring.MaxTextLength);
        builder.Property(f => f.DeliveryError).HasMaxLength(AlertFiring.MaxTextLength);
        builder.Property(f => f.Channel).HasConversion<string>().HasMaxLength(30);
        builder.HasOne<AlertRule>().WithMany().HasForeignKey(f => f.RuleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(f => f.FiredAt);
    }
}
