using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Translator.Domain.Persistence;

/// <summary>
/// Hosts the governed 837P schema (governance.txt Section 2) on a real relational engine whose
/// lifetime is bounded by this object. Nothing is written to disk and nothing outlives the
/// process.
/// </summary>
/// <remarks>
/// <para>
/// PROVENANCE: ADR-002 - governance Section 2 mandates an EF
/// Core Code-First model but does not name a database engine. The deployment carries no durable
/// store, so the engine is SQLite in shared-cache in-memory mode. The entity model itself is
/// unchanged from governance Section 2.
/// </para>
/// <para>
/// SQLite is chosen over the EF Core in-memory provider because the in-memory provider enforces
/// no relational constraints at all. Referential integrity is load-bearing for the Section 1
/// Reversibility Guarantee: an orphaned service line would round-trip into a claim that silently
/// lost a charge. A real engine makes that failure impossible rather than merely unlikely.
/// </para>
/// <para>
/// The connection is opened here and held for the lifetime of the store. An in-memory SQLite
/// database exists only while at least one connection to it is open, so releasing this
/// connection would drop the schema and every row with it.
/// </para>
/// </remarks>
public sealed class EphemeralClaimStore : IDisposable, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ClaimsDbContext> _options;
    private bool _disposed;

    public EphemeralClaimStore()
    {
        // A unique database name per store instance. Two stores must share nothing, otherwise a
        // test asserting the absence of persistence could pass against another store's rows.
        _connection = new SqliteConnection($"DataSource=claims-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        _options = new DbContextOptionsBuilder<ClaimsDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var schemaContext = new ClaimsDbContext(_options);
        schemaContext.Database.EnsureCreated();
    }

    /// <summary>
    /// Creates a context over this store. Each call returns a context with its own change
    /// tracker, so a claim read back through a new context is genuinely reloaded from the
    /// engine rather than served from an identity map.
    /// </summary>
    public ClaimsDbContext CreateContext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new ClaimsDbContext(_options);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _connection.DisposeAsync();
    }
}
