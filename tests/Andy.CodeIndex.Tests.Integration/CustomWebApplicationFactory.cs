using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Andy.CodeIndex.Tests.Integration;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = "IntegrationTest_" + Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

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
