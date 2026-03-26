using System.Security.Claims;
using System.Text.Encodings.Web;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Andy.CodeIndex.Tests.Integration;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = "IntegrationTest_" + Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Provide dummy auth config so Andy.Auth doesn't throw during DI setup
        builder.UseSetting("AndyAuth:Authority", "https://test-auth.example.com");
        builder.UseSetting("AndyAuth:Audience", "andy-code-index");

        builder.ConfigureServices(services =>
        {
            // Remove real DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<CodeIndexDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            // Remove hosted services to avoid background processing during tests
            RemoveAll<IHostedService>(services);

            // Remove real IGitService and IEmbeddingProvider
            RemoveAll<IGitService>(services);
            RemoveAll<IEmbeddingProvider>(services);

            // Override authentication with test scheme as default
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
                options.DefaultScheme = "Test";
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

            // Override authorization to allow all requests (bypass RBAC in tests)
            services.AddAuthorization(options =>
            {
                options.DefaultPolicy = new AuthorizationPolicyBuilder("Test")
                    .RequireAssertion(_ => true)
                    .Build();
                options.FallbackPolicy = new AuthorizationPolicyBuilder("Test")
                    .RequireAssertion(_ => true)
                    .Build();
            });

            // Replace the policy provider so RequirePermission dynamic policies also pass
            services.AddSingleton<IAuthorizationPolicyProvider, AllowAllPolicyProvider>();

            // Add InMemory database with stable name per factory instance
            services.AddDbContext<CodeIndexDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            // Mock external services
            var gitServiceMock = new Mock<IGitService>();
            gitServiceMock.Setup(g => g.GetCloneDir(It.IsAny<string>(), It.IsAny<Guid>()))
                .Returns("/tmp/test/repos/mock");
            services.AddSingleton(gitServiceMock.Object);

            var embeddingProviderMock = new Mock<IEmbeddingProvider>();
            embeddingProviderMock.Setup(p => p.Dimensions).Returns(1536);
            embeddingProviderMock.Setup(p => p.GenerateEmbeddingsAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string[] texts, CancellationToken _) =>
                    texts.Select(_ => new float[1536]).ToArray());
            services.AddSingleton(embeddingProviderMock.Object);
        });
    }

    private static void RemoveAll<T>(IServiceCollection services)
    {
        var descriptors = services.Where(d =>
            d.ServiceType == typeof(T) ||
            d.ImplementationType == typeof(T) ||
            (d.ServiceType.IsAssignableTo(typeof(T)))).ToList();
        foreach (var d in descriptors)
            services.Remove(d);
    }
}

/// <summary>
/// Test authentication handler that auto-authenticates all requests.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim(ClaimTypes.Email, "test@example.com"),
            new Claim("sub", "test-user-id")
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// Policy provider that allows all requests regardless of policy name.
/// Used in integration tests to bypass RBAC permission checks.
/// </summary>
public class AllowAllPolicyProvider : IAuthorizationPolicyProvider
{
    private static readonly AuthorizationPolicy AllowAll = new AuthorizationPolicyBuilder("Test")
        .RequireAssertion(_ => true)
        .Build();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) => Task.FromResult<AuthorizationPolicy?>(AllowAll);
    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => Task.FromResult(AllowAll);
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => Task.FromResult<AuthorizationPolicy?>(AllowAll);
}
