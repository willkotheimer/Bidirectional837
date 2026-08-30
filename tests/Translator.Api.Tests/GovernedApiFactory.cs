using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Translator.Api.Tests;

/// <summary>
/// PROVENANCE: ADR-012 - the test host runs with the live NPI registry switched off, so the suite
/// is deterministic and needs no network. The registry client itself is covered against a stubbed
/// transport in Translator.Generation.Tests, and the live path is exercised only by an opt-in test.
/// </summary>
public sealed class GovernedApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Generation:UseLiveNpiRegistry"] = "false",
            }));
    }
}
