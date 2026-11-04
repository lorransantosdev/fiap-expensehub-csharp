using System;
using ExpenseHub.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseHub.Api.Data.Configurations;

/// <summary>
/// Mapeamento relacional de <see cref="ReportRequest"/>.
/// </summary>
public sealed class ReportRequestConfiguration : IEntityTypeConfiguration<ReportRequest>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ReportRequest> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ReportRequests");
        builder.HasKey(report => report.Id);
        builder.Property(report => report.Id).ValueGeneratedNever();

        builder.Property(report => report.RequesterId)
            .IsRequired()
            .HasMaxLength(ExpenseConfiguration.UserIdMaxLength);

        builder.Property(report => report.Scope)
            .HasConversion<string>()
            .HasMaxLength(ExpenseConfiguration.EnumMaxLength);

        builder.Property(report => report.Status)
            .HasConversion<string>()
            .HasMaxLength(ExpenseConfiguration.EnumMaxLength);

        builder.Property(report => report.ErrorCode).HasMaxLength(ReportRequest.ErrorCodeMaxLength);
        builder.Property(report => report.ErrorMessage).HasMaxLength(ReportRequest.ErrorMessageMaxLength);
        builder.Property(report => report.Version).IsConcurrencyToken();

        builder.Ignore(report => report.IsFinished);

        builder.HasIndex(report => report.RequesterId);
    }
}
