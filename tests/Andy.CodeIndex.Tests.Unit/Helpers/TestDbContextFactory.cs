using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Andy.CodeIndex.Tests.Unit.Helpers;

public static class TestDbContextFactory
{
    public static CodeIndexDbContext Create()
    {
        var options = new DbContextOptionsBuilder<CodeIndexDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new CodeIndexDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
