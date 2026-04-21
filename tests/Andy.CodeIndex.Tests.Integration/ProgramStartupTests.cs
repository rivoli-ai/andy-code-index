using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Andy.CodeIndex.Tests.Integration;

/// <summary>
/// Pins the fail-loud contract for andy-settings configuration.
///
/// andy-code-index must refuse to start when <c>AndySettings:ApiBaseUrl</c>
/// is missing. Without this guard, indexer/embedding credentials fall
/// back to stale local config and enrichment silently stops working —
/// see epic rivoli-ai/conductor#771.
/// </summary>
public class ProgramStartupTests
{
    [Fact]
    public void Host_refuses_to_start_when_AndySettings_ApiBaseUrl_is_empty()
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AndySettings:ApiBaseUrl"] = "",
                    });
                });
            });

        Action act = () => _ = factory.Services;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AndySettings:ApiBaseUrl must be configured*");
    }
}
