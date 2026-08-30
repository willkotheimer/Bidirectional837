using Governance.Domain.Persistence;
using Governance.Edi;
using Governance.Generation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// The OpenAPI document is part of the governed deliverable (governance Section 4), not a
// development convenience, so it is served in every environment rather than only in Development.
builder.Services.AddOpenApi();

// PROVENANCE: ADR-015 - the store is a singleton because it *is* the database. An in-memory SQLite
// database lives only while a connection to it is open, so a scoped store would discard every claim
// at the end of the request that created it, and the dashboard and export would find nothing.
builder.Services.AddSingleton<EphemeralClaimStore>();

// PROVENANCE: ADR-012 - governance User Story 1.1. The registry is tried first and the synthetic
// set stands behind it, so an unreachable registry degrades a batch rather than failing it.
//
// The live registry is on by default, because User Story 1.1 asks for real provider data. It is
// switched off by configuration in the test host, so that the suite is deterministic and needs no
// network, and so a governed timing budget is never measured against someone else's service.
builder.Services.AddSingleton<SyntheticProviderDirectory>();

if (builder.Configuration.GetValue("Generation:UseLiveNpiRegistry", defaultValue: true))
{
    builder.Services.AddHttpClient<NpiRegistryProviderDirectory>(client =>
    {
        client.BaseAddress = new Uri(NpiRegistryProviderDirectory.DefaultBaseAddress);
        client.Timeout = TimeSpan.FromSeconds(5);
    });
    builder.Services.AddSingleton<IProviderDirectory>(services => new ResilientProviderDirectory(
        services.GetRequiredService<NpiRegistryProviderDirectory>(),
        services.GetRequiredService<SyntheticProviderDirectory>()));
}
else
{
    builder.Services.AddSingleton<IProviderDirectory>(
        services => services.GetRequiredService<SyntheticProviderDirectory>());
}

builder.Services.AddSingleton<IMedicalCodeCatalog, SeedMedicalCodeCatalog>();
builder.Services.AddSingleton<IChargeSchedule, SeedChargeSchedule>();
builder.Services.AddSingleton<SyntheticClaimGenerator>();

// PROVENANCE: GOVERNANCE-5 - governance Feature 2. The serializer holds no per-request state
// and reads nothing outside the claim it is given (ADR-016), so one instance serves every
// export.
builder.Services.AddSingleton<Edi837Serializer>();
builder.Services.AddSingleton<ClaimArchive>();

var app = builder.Build();

app.MapOpenApi();

// No UseHttpsRedirection: TLS terminates at the Azure ingress in the deployed topology, and an
// in-process redirect would make the served OpenAPI document unreachable to the test host.

app.UseAuthorization();

app.MapControllers();

app.Run();

/// <summary>Exposed so the test host can boot the real application rather than a stand-in.</summary>
public partial class Program;
