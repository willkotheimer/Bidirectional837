using Translator.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Translator.Domain.Persistence;

/// <summary>
/// EF Core Code-First context for the governed 837P schema (governance.txt Section 2).
/// </summary>
public class ClaimsDbContext : DbContext
{
    public ClaimsDbContext(DbContextOptions<ClaimsDbContext> options) : base(options) { }

    public DbSet<ClaimHeader> Claims => Set<ClaimHeader>();
    public DbSet<ClaimLineItem> ClaimLineItems => Set<ClaimLineItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // PROVENANCE: ADR-004 - governed monetary and quantity columns are stored as exact
        // integer minor units.
        //
        // SQLite assigns NUMERIC affinity to a column declared decimal(18,2) and will coerce a
        // stored decimal to a float, which is lossy: 9999999999999999.99 returned as
        // 10000000000000000, and 1.00 returned as 1. Both outcomes break the Section 1
        // Reversibility Guarantee - the first corrupts a charge amount outright, the second
        // changes the text the 837 amount element is rendered from.
        //
        // Scaling to integer minor units keeps every value exact, keeps the column orderable and
        // comparable in SQL (which a TEXT encoding would not), and stores losslessly under
        // NUMERIC affinity. The governed column type declaration from Section 2 is therefore
        // preserved verbatim; only the storage encoding beneath it changes.
        modelBuilder.Entity<ClaimHeader>()
            .Property(c => c.CLM02_TotalClaimChargeAmount)
            .HasConversion(MinorUnits(2));

        modelBuilder.Entity<ClaimLineItem>()
            .Property(l => l.SV102_LineItemChargeAmount)
            .HasConversion(MinorUnits(2));

        modelBuilder.Entity<ClaimLineItem>()
            .Property(l => l.SV104_ServiceUnitCount)
            .HasConversion(MinorUnits(4));
    }

    /// <summary>
    /// Converts a decimal to and from integer minor units at the governed scale.
    /// </summary>
    /// <remarks>
    /// Reading multiplies by a literal of the target scale rather than dividing. Decimal
    /// multiplication adds the operand scales, so 100 * 0.01m yields 1.00m rather than 1m:
    /// the governed scale is restored exactly, including trailing zeros. Division would drop
    /// them and change the rendered 837 amount.
    /// </remarks>
    private static ValueConverter<decimal, long> MinorUnits(int scale)
    {
        var factor = 1m;
        for (var i = 0; i < scale; i++) factor *= 10m;
        var inverse = 1m / factor;

        return new ValueConverter<decimal, long>(
            value => decimal.ToInt64(decimal.Round(value * factor, 0, MidpointRounding.AwayFromZero)),
            stored => stored * inverse);
    }
}
