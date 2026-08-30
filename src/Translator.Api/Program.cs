using Translator.Domain.Persistence;
using Translator.Edi;
using Translator.Generation;

var builder = WebApplication.CreateBuilder(args);

// PROVENANCE: FIND-020 - serialise with the names the DTOs declare, not a naming policy's idea of
// them. The default camelCase policy lowercases the leading character of a name, and on a governed
// column beginning with an acronym that produces clM02_, bhT03_, hI01_2_. Governance Section 1
// requires ASC X12 nomenclature to reach the DTOs and the React forms; those manglings are not it,
// and they disagreed with docs/api/swagger.json, which publishes the governed names.
//
// Null is the policy: leave the name exactly as written. Query strings and route values are
// unaffected; this governs the JSON body only.
builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.PropertyNamingPolicy = null);

// PROVENANCE: ADR-028 - the browser client is served from its own origin in development, so it
// cannot call this API without an explicit grant. The grant is named, scoped to the Vite dev server
// and enabled only outside Production, because a wildcard origin is how a development convenience
// becomes a deployed one.
builder.Services.AddCors(options => options.AddPolicy(DevelopmentClientCors, policy => policy
    .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders("Content-Disposition")));

// The OpenAPI document is part of the governed deliverable (governance Section 4), not a
// development convenience, so it is served in every environment rather than only in Development.
builder.Services.AddOpenApi();

// PROVENANCE: ADR-015 - the store is a singleton because it *is* the database. An in-memory SQLite
// database lives only while a connection to it is open, so a scoped store would discard every claim
// at the end of the request that created it, and the dashboard and export would find nothing.
builder.Services.AddSingleton<EphemeralClaimStore>();

// PROVENANCE: ADR-023 - governance User Story 1.1, in three tiers, most real first.
//
// The distilled NPPES snapshot leads. It is the same data the registry API serves, from the same
// authority, and it answers without a network call - which matters because the registry answers one
// provider per request, so a governed batch of 500 was 500 round trips against the 3.0 second
// budget User Story 1.3 sets.
//
// The live registry sits behind it for jurisdictions the snapshot does not carry, and remains on by
// default because governance names it explicitly. The test host switches it off, so the suite is
// deterministic and no governed timing budget is measured against someone else's service.
//
// The synthetic set stands behind both, which is the graceful fallback User Story 1.1 requires.
builder.Services.AddSingleton<SyntheticProviderDirectory>();
builder.Services.AddSingleton<SeedProviderDirectory>();

if (builder.Configuration.GetValue("Generation:UseLiveNpiRegistry", defaultValue: true))
{
    builder.Services.AddHttpClient<NpiRegistryProviderDirectory>(client =>
    {
        client.BaseAddress = new Uri(NpiRegistryProviderDirectory.DefaultBaseAddress);
        client.Timeout = TimeSpan.FromSeconds(5);
    });

    // PROVENANCE: FIND-018 - the snapshot is layered, not made resilient. A jurisdiction it does not
    // carry must not set it aside for every jurisdiction it does. The registry keeps the latching
    // wrapper, because that is the remote dependency ADR-012 reasoned about.
    builder.Services.AddSingleton<IProviderDirectory>(services => new LayeredProviderDirectory(
        services.GetRequiredService<SeedProviderDirectory>(),
        new ResilientProviderDirectory(
            services.GetRequiredService<NpiRegistryProviderDirectory>(),
            services.GetRequiredService<SyntheticProviderDirectory>())));
}
else
{
    builder.Services.AddSingleton<IProviderDirectory>(services => new LayeredProviderDirectory(
        services.GetRequiredService<SeedProviderDirectory>(),
        services.GetRequiredService<SyntheticProviderDirectory>()));
}

builder.Services.AddSingleton<IMedicalCodeCatalog, SeedMedicalCodeCatalog>();
builder.Services.AddSingleton<IChargeSchedule, SeedChargeSchedule>();
builder.Services.AddSingleton<SyntheticClaimGenerator>();

// PROVENANCE: GOVERNANCE-5 - governance Feature 2. The serializer holds no per-request state
// and reads nothing outside the claim it is given (ADR-016), so one instance serves every
// export.
builder.Services.AddSingleton<Edi837Serializer>();
builder.Services.AddSingleton<ClaimArchive>();

// PROVENANCE: GOVERNANCE-5 - governance Feature 3. The reader holds no state between calls and
// reads the delimiters out of each interchange it is given, so one instance serves every import.
builder.Services.AddSingleton<Edi837Parser>();
builder.Services.AddSingleton<ReversibilityVerifier>();

var app = builder.Build();

app.MapOpenApi();

// PROVENANCE: ADR-028 - development only. A deployed instance serves the client from its own origin
// or is fronted by an ingress that does, so it needs no grant and is given none.
if (!app.Environment.IsProduction())
{
    app.UseCors(DevelopmentClientCors);
}

// No UseHttpsRedirection: TLS terminates at the Azure ingress in the deployed topology, and an
// in-process redirect would make the served OpenAPI document unreachable to the test host.

app.UseAuthorization();

app.MapControllers();

app.Run();

/// <summary>Exposed so the test host can boot the real application rather than a stand-in.</summary>
public partial class Program
{
    /// <summary>The named CORS policy for the development client (ADR-028).</summary>
    public const string DevelopmentClientCors = "DevelopmentClient";
}
