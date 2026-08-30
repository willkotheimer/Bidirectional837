using System.Text.RegularExpressions;

namespace Governance.Traceability.Tests;

/// <summary>
/// PROVENANCE: ADR-031 - the deployment is declared in Bicep, and the declaration is measured against
/// the code it deploys.
///
/// Infrastructure fails differently from code: it fails quietly, in a subscription nobody is looking
/// at, and the first symptom is usually a page that loads and does nothing. So the properties
/// asserted here are the ones whose absence is invisible until a user hits them - a runtime that no
/// longer matches what the project targets, a CORS origin that does not match the site allowed to
/// call it, a secret committed by accident.
/// </summary>
public partial class DeploymentTheories
{
    private static readonly string Template = ReadTemplate();

    [GeneratedRegex(@"<TargetFramework>net([0-9]+\.[0-9]+)</TargetFramework>")]
    private static partial Regex TargetFramework();

    private static string ReadTemplate() =>
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra", "main.bicep"));

    /// <summary>
    /// The runtime the plan deploys must be the one the project targets.
    ///
    /// This is the drift that costs an afternoon: the csproj moves to a new framework, the Bicep does
    /// not, and the app fails to start with a message about a missing runtime that names neither
    /// file. Reading the version out of the csproj rather than repeating it here means the assertion
    /// cannot go stale.
    /// </summary>
    [Fact]
    public void Declared_runtime_matches_the_framework_the_api_targets()
    {
        var project = File.ReadAllText(
            Path.Combine(RepositoryRoot.Path, "src", "Translator.Api", "Translator.Api.csproj"));

        var targeted = TargetFramework().Match(project).Groups[1].Value;

        Assert.False(string.IsNullOrEmpty(targeted), "Translator.Api.csproj declares no TargetFramework.");

        // The template takes the runtime as a parameter, so it is the parameter's default that has
        // to track the project - asserting the literal would force the template to hard-code it.
        Assert.Contains($"param dotnetVersion string = '{targeted}'", Template, StringComparison.Ordinal);

        // And the parameter must actually reach the runtime setting rather than sitting unused.
        Assert.Contains("linuxFxVersion: 'DOTNETCORE|${dotnetVersion}'", Template, StringComparison.Ordinal);
    }

    /// <summary>
    /// PROVENANCE: ADR-032 - one host serves the client and the API, so there is no cross-origin
    /// call to grant and the template declares no grant at all.
    ///
    /// Asserted as an absence, which is the only way to assert it. A CORS block that crept back in
    /// would work - the client would keep functioning - while quietly opening the API to an origin
    /// nobody chose. The absence is the security property.
    /// </summary>
    [Fact]
    public void Deployment_grants_no_cross_origin_access_because_it_serves_one_origin()
    {
        Assert.DoesNotContain("allowedOrigins", Template, StringComparison.Ordinal);
        Assert.DoesNotContain("'*'", Template, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_is_reachable_only_over_https()
    {
        Assert.Contains("httpsOnly: true", Template, StringComparison.Ordinal);
        Assert.Contains("minTlsVersion:", Template, StringComparison.Ordinal);
    }

    /// <summary>
    /// PROVENANCE: ADR-015 - the claim store is an in-memory SQLite database held by a singleton, so
    /// it exists only while the process does. Without Always On the platform idles the app out and
    /// every generated claim goes with it, which a user would experience as a batch that vanished
    /// between two clicks.
    ///
    /// This does not make the store durable. It makes the window in which it is lost a restart rather
    /// than twenty minutes of inactivity, and that limit is recorded rather than hidden.
    /// </summary>
    [Fact]
    public void Api_is_kept_warm_because_its_store_lives_in_the_process()
    {
        Assert.Contains("alwaysOn: true", Template, StringComparison.Ordinal);
    }

    /// <summary>
    /// PROVENANCE: ADR-028 - the application grants CORS only outside Production, because in
    /// Production the grant belongs to the platform. That is only true if the deployment actually
    /// sets the environment, so the template must say so.
    /// </summary>
    [Fact]
    public void Deployment_declares_the_production_environment()
    {
        Assert.Contains("ASPNETCORE_ENVIRONMENT", Template, StringComparison.Ordinal);
        Assert.Contains("'Production'", Template, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing in the template may be a secret. There are none to hold today - the store is
    /// ephemeral and the API is unauthenticated - so the assertion is cheap now and is worth having
    /// in place before the first one exists.
    /// </summary>
    [Theory]
    [InlineData("password")]
    [InlineData("connectionString")]
    [InlineData("apiKey")]
    [InlineData("clientSecret")]
    [InlineData("AccountKey=")]
    public void Template_carries_no_secret(string forbidden)
    {
        Assert.DoesNotContain(forbidden, Template, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One site, named for the project, so the address is the one a person would guess. A Static Web
    /// App was the alternative and was rejected: its hostname is generated rather than chosen, and on
    /// the free plan it cannot proxy to a bring-your-own backend, which would have forced the
    /// cross-origin grant this topology does not need.
    /// </summary>
    [Fact]
    public void Deployment_declares_exactly_one_site()
    {
        Assert.Single(Regex.Matches(Template, @"resource\s+\w+\s+'Microsoft\.Web/sites@"));
        Assert.DoesNotContain("Microsoft.Web/staticSites@", Template, StringComparison.Ordinal);
    }

    /// <summary>
    /// The API shares an App Service plan that already exists rather than declaring a new one, so
    /// deploying this project adds no recurring cost. The plan is referenced, never created.
    /// </summary>
    [Fact]
    public void Deployment_reuses_an_existing_plan_rather_than_creating_one()
    {
        Assert.Contains("existing", Template, StringComparison.Ordinal);
        Assert.DoesNotContain("resource plan 'Microsoft.Web/serverfarms@", Template, StringComparison.Ordinal);
    }
}
