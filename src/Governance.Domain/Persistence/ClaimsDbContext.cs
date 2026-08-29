using Governance.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Governance.Domain.Persistence;

/// <summary>
/// EF Core Code-First context for the governed 837P schema (governance.txt Section 2).
/// </summary>
public class ClaimsDbContext : DbContext
{
    public ClaimsDbContext(DbContextOptions<ClaimsDbContext> options) : base(options) { }

    public DbSet<ClaimHeader> Claims => Set<ClaimHeader>();
    public DbSet<ClaimLineItem> ClaimLineItems => Set<ClaimLineItem>();
}
