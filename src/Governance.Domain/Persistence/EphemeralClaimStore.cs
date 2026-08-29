using Microsoft.EntityFrameworkCore;

namespace Governance.Domain.Persistence;

/// <summary>
/// Process-lifetime-only relational store backing the governed schema.
/// NOT YET IMPLEMENTED - see tests/Governance.Domain.Tests.
/// </summary>
public sealed class EphemeralClaimStore : IDisposable
{
    public ClaimsDbContext CreateContext()
        => throw new NotImplementedException("EphemeralClaimStore.CreateContext");

    public void Dispose() { }
}
