using System.Reflection;
using Governance.Domain.Entities;
using Governance.Domain.Persistence;
using Governance.Domain.Tests.Corpus;
using Microsoft.EntityFrameworkCore;

namespace Governance.Domain.Tests;

/// <summary>
/// The database leg of the governance Section 1 Reversibility Guarantee: a claim written to
/// the store and read back through a fresh context must be field-identical. If the store
/// mutates a value, no amount of care in the EDI serializer can restore byte equivalence,
/// so these invariants are proven here before any 837 text is produced.
/// </summary>
public class PersistenceRoundTripTheories
{
    private static readonly PropertyInfo[] HeaderScalars = GovernedScalars(typeof(ClaimHeader));
    private static readonly PropertyInfo[] LineScalars = GovernedScalars(typeof(ClaimLineItem));

    /// <summary>Monetary edge values that a decimal(18,2) column must carry without drift.</summary>
    public static IEnumerable<object[]> MonetaryEdgeValues() =>
    [
        [0.00m],
        [0.01m],
        [0.10m],
        [1.00m],
        [12.34m],
        [999.99m],
        [100_000.00m],
        [9_999_999_999_999_999.99m],
    ];

    /// <summary>Unit counts that a decimal(18,4) column must carry without drift.</summary>
    public static IEnumerable<object[]> UnitCountEdgeValues() =>
    [
        [0.0001m],
        [0.2500m],
        [1.0000m],
        [7.5000m],
        [99.9999m],
    ];

    [Theory]
    [MemberData(nameof(GovernedClaimCorpus.ClaimIndices), MemberType = typeof(GovernedClaimCorpus))]
    public async Task Stored_claim_header_is_field_identical_when_reloaded(int claimIndex, int lineItemCount)
    {
        var original = GovernedClaimCorpus.Build(claimIndex, lineItemCount);
        using var store = new EphemeralClaimStore();

        await using (var writeContext = store.CreateContext())
        {
            writeContext.Claims.Add(original);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = store.CreateContext();
        var reloaded = await readContext.Claims
            .Include(c => c.LineItems)
            .SingleAsync(c => c.Id == original.Id);

        foreach (var property in HeaderScalars)
        {
            Assert.Equal(property.GetValue(original), property.GetValue(reloaded));
        }
    }

    [Theory]
    [MemberData(nameof(GovernedClaimCorpus.ClaimIndices), MemberType = typeof(GovernedClaimCorpus))]
    public async Task Stored_line_items_are_field_identical_and_complete_when_reloaded(int claimIndex, int lineItemCount)
    {
        var original = GovernedClaimCorpus.Build(claimIndex, lineItemCount);
        using var store = new EphemeralClaimStore();

        await using (var writeContext = store.CreateContext())
        {
            writeContext.Claims.Add(original);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = store.CreateContext();
        var reloaded = await readContext.Claims
            .Include(c => c.LineItems)
            .SingleAsync(c => c.Id == original.Id);

        Assert.Equal(lineItemCount, reloaded.LineItems.Count);

        var originalLines = original.LineItems.OrderBy(l => l.LX01_AssignedLineNumber).ToList();
        var reloadedLines = reloaded.LineItems.OrderBy(l => l.LX01_AssignedLineNumber).ToList();

        for (var i = 0; i < originalLines.Count; i++)
        {
            foreach (var property in LineScalars)
            {
                Assert.Equal(property.GetValue(originalLines[i]), property.GetValue(reloadedLines[i]));
            }
        }
    }

    [Theory]
    [MemberData(nameof(MonetaryEdgeValues))]
    public async Task Claim_charge_amount_survives_the_store_without_drift(decimal amount)
    {
        var original = GovernedClaimCorpus.Build(1, lineItemCount: 1);
        original.CLM02_TotalClaimChargeAmount = amount;
        original.LineItems[0].SV102_LineItemChargeAmount = amount;
        using var store = new EphemeralClaimStore();

        await using (var writeContext = store.CreateContext())
        {
            writeContext.Claims.Add(original);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = store.CreateContext();
        var reloaded = await readContext.Claims
            .Include(c => c.LineItems)
            .SingleAsync(c => c.Id == original.Id);

        Assert.Equal(amount, reloaded.CLM02_TotalClaimChargeAmount);
        Assert.Equal(amount, reloaded.LineItems[0].SV102_LineItemChargeAmount);
    }

    /// <summary>
    /// Scale is not merely cosmetic: the 837 amount elements are rendered from these values,
    /// so a value that returns from the store with a different scale changes the emitted text.
    /// </summary>
    [Theory]
    [MemberData(nameof(MonetaryEdgeValues))]
    public async Task Claim_charge_amount_retains_its_scale_through_the_store(decimal amount)
    {
        var original = GovernedClaimCorpus.Build(2, lineItemCount: 1);
        original.CLM02_TotalClaimChargeAmount = amount;
        using var store = new EphemeralClaimStore();

        await using (var writeContext = store.CreateContext())
        {
            writeContext.Claims.Add(original);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = store.CreateContext();
        var reloaded = await readContext.Claims.SingleAsync(c => c.Id == original.Id);

        Assert.Equal(amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            reloaded.CLM02_TotalClaimChargeAmount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [MemberData(nameof(UnitCountEdgeValues))]
    public async Task Service_unit_count_survives_the_store_without_drift(decimal units)
    {
        var original = GovernedClaimCorpus.Build(3, lineItemCount: 1);
        original.LineItems[0].SV104_ServiceUnitCount = units;
        using var store = new EphemeralClaimStore();

        await using (var writeContext = store.CreateContext())
        {
            writeContext.Claims.Add(original);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = store.CreateContext();
        var reloaded = await readContext.Claims
            .Include(c => c.LineItems)
            .SingleAsync(c => c.Id == original.Id);

        Assert.Equal(units, reloaded.LineItems[0].SV104_ServiceUnitCount);
    }

    [Theory]
    [MemberData(nameof(GovernedClaimCorpus.ClaimIndices), MemberType = typeof(GovernedClaimCorpus))]
    public async Task Transaction_set_creation_date_survives_the_store_to_the_tick(int claimIndex, int lineItemCount)
    {
        var original = GovernedClaimCorpus.Build(claimIndex, lineItemCount);
        using var store = new EphemeralClaimStore();

        await using (var writeContext = store.CreateContext())
        {
            writeContext.Claims.Add(original);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = store.CreateContext();
        var reloaded = await readContext.Claims.SingleAsync(c => c.Id == original.Id);

        Assert.Equal(original.BHT04_TransactionSetCreationDate.Ticks, reloaded.BHT04_TransactionSetCreationDate.Ticks);
    }

    /// <summary>
    /// Referential integrity is the reason the governed schema is hosted on a real relational
    /// engine rather than a dictionary: an orphan service line must be impossible.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Line_item_without_a_parent_claim_is_rejected(int lineNumber)
    {
        using var store = new EphemeralClaimStore();
        await using var context = store.CreateContext();

        context.ClaimLineItems.Add(new ClaimLineItem
        {
            ClaimHeaderId = Guid.NewGuid(),
            LX01_AssignedLineNumber = lineNumber,
            SV101_2_ProcedureCode = "99213",
            SV102_LineItemChargeAmount = 10.00m,
            SV103_UnitOfMeasure = "UN",
            SV104_ServiceUnitCount = 1.0000m,
            DTP03_ServiceDate = "20260101",
        });

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Theory]
    [MemberData(nameof(GovernedClaimCorpus.ClaimIndices), MemberType = typeof(GovernedClaimCorpus))]
    public async Task Deleting_a_claim_removes_its_service_lines(int claimIndex, int lineItemCount)
    {
        var original = GovernedClaimCorpus.Build(claimIndex, lineItemCount);
        using var store = new EphemeralClaimStore();

        await using (var writeContext = store.CreateContext())
        {
            writeContext.Claims.Add(original);
            await writeContext.SaveChangesAsync();
        }

        await using (var deleteContext = store.CreateContext())
        {
            var claim = await deleteContext.Claims.Include(c => c.LineItems).SingleAsync(c => c.Id == original.Id);
            deleteContext.Claims.Remove(claim);
            await deleteContext.SaveChangesAsync();
        }

        await using var readContext = store.CreateContext();
        Assert.Empty(await readContext.ClaimLineItems.Where(l => l.ClaimHeaderId == original.Id).ToListAsync());
    }

    /// <summary>
    /// PROVENANCE: ADR-002 - the deployment carries no durable database by decision.
    /// Two stores must therefore share nothing: state lives and dies with its store.
    /// </summary>
    [Theory]
    [MemberData(nameof(GovernedClaimCorpus.ClaimIndices), MemberType = typeof(GovernedClaimCorpus))]
    public async Task Claims_written_to_one_store_are_invisible_to_another(int claimIndex, int lineItemCount)
    {
        var original = GovernedClaimCorpus.Build(claimIndex, lineItemCount);

        using (var firstStore = new EphemeralClaimStore())
        {
            await using var writeContext = firstStore.CreateContext();
            writeContext.Claims.Add(original);
            await writeContext.SaveChangesAsync();
        }

        using var secondStore = new EphemeralClaimStore();
        await using var readContext = secondStore.CreateContext();

        Assert.Empty(await readContext.Claims.ToListAsync());
    }

    private static PropertyInfo[] GovernedScalars(Type entityType) =>
        entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType != typeof(List<ClaimLineItem>) && p.PropertyType != typeof(ClaimHeader))
            .ToArray();
}
