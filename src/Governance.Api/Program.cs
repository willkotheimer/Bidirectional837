var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// The OpenAPI document is part of the governed deliverable (governance Section 4), not a
// development convenience, so it is served in every environment rather than only in Development.
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();

// No UseHttpsRedirection: TLS terminates at the Azure ingress in the deployed topology, and an
// in-process redirect would make the served OpenAPI document unreachable to the test host.

app.UseAuthorization();

app.MapControllers();

app.Run();

/// <summary>Exposed so the test host can boot the real application rather than a stand-in.</summary>
public partial class Program;
