using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit.Services;

/// <summary>
/// Golden-file coverage (story #250): runs the analyzer over real source files
/// for every supported language and asserts the extracted API. This is the only
/// parsing coverage for Go and Java, and exercises the C#/Python/JS/TS analyzers
/// against whole files rather than inline snippets.
/// </summary>
public class CodeAnalysisGoldenFixtureTests
{
    private readonly CodeAnalysisService _service = new();

    private static string Load(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "CodeSamples", fileName);
        return File.ReadAllText(path);
    }

    private CodeAnalysisResult Analyze(string fileName, string language) =>
        _service.Analyze(Load(fileName), fileName, language);

    [Fact]
    public void CSharp_Fixture_ExtractsRecordsInterfacesClassesEnumsAndDocs()
    {
        var r = Analyze("Sample.cs", "csharp");

        r.Classes.Select(c => c.Name).Should().Contain(new[] { "Money", "InvoiceService", "LineItem" });
        r.Interfaces.Select(i => i.Name).Should().Contain("IInvoiceService");
        r.Enums.Should().ContainSingle(e => e.Name == "InvoiceStatus")
            .Which.Values.Should().Equal("Draft", "Issued", "Paid");

        var svc = r.Classes.Single(c => c.Name == "InvoiceService");
        svc.Summary.Should().Be("Issues and totals invoices.");
        svc.ImplementedInterfaces.Should().Contain("IInvoiceService");
        svc.BaseClass.Should().Be("ServiceBase");
        svc.Methods.Should().Contain(m => m.Name == "GetTotalAsync" && m.IsAsync);
        svc.Methods.Should().Contain(m => m.Name == "CreateDefault" && m.IsStatic);
        svc.Methods.Should().NotContain(m => m.Name == "Touch"); // private excluded

        // Nested type's members attributed to it, not the outer class.
        r.Classes.Single(c => c.Name == "LineItem").Properties.Select(p => p.Name)
            .Should().Contain(new[] { "Sku", "Price" });

        // Interface members populated, and the record's positional params surface.
        r.Interfaces.Single().Methods.Should().Contain(m => m.Name == "GetTotalAsync");
        r.Classes.Single(c => c.Name == "Money").Properties.Select(p => p.Name)
            .Should().Equal("Amount", "Currency");
    }

    [Fact]
    public void Python_Fixture_ExtractsMethodsAsyncDecoratorsAndModuleFunctions()
    {
        var r = Analyze("sample.py", "python");

        var repo = r.Classes.Should().ContainSingle(c => c.Name == "Repository").Subject;
        repo.Methods.Select(m => m.Name).Should().Contain(new[] { "__init__", "fetch", "default" });
        repo.Methods.Should().NotContain(m => m.Name == "_internal");
        repo.Methods.Single(m => m.Name == "fetch").IsAsync.Should().BeTrue();
        repo.Methods.Single(m => m.Name == "default").IsStatic.Should().BeTrue();
        repo.Properties.Should().ContainSingle(p => p.Name == "label");

        r.Functions.Select(f => f.Name).Should().Contain(new[] { "build_repository", "load_all" });
        r.Functions.Select(f => f.Name).Should().NotContain("_private_helper");
        r.Functions.Single(f => f.Name == "load_all").IsAsync.Should().BeTrue();
    }

    [Fact]
    public void JavaScript_Fixture_ExtractsClassMethodsArrowsAndDefaults()
    {
        var r = Analyze("sample.js", "javascript");

        var widget = r.Classes.Should().ContainSingle(c => c.Name == "Widget").Subject;
        widget.BaseClass.Should().Be("Base");
        widget.Methods.Single(m => m.Name == "render").IsAsync.Should().BeTrue();
        widget.Methods.Single(m => m.Name == "create").IsStatic.Should().BeTrue();

        r.Classes.Select(c => c.Name).Should().Contain("App"); // export default class
        r.Functions.Select(f => f.Name).Should().Contain(new[] { "build", "scale", "fetchData" });
        r.Functions.Single(f => f.Name == "fetchData").IsAsync.Should().BeTrue();
    }

    [Fact]
    public void TypeScript_Fixture_ExtractsExportedItemsIncludingArrowsAndDefault()
    {
        var r = Analyze("sample.ts", "typescript");

        r.Classes.Select(c => c.Name).Should().Contain("HttpUserService");
        r.Interfaces.Select(i => i.Name).Should().Contain("UserService");
        r.Enums.Should().ContainSingle(e => e.Name == "Role")
            .Which.Values.Should().Equal("Admin", "Editor", "Viewer");
        r.Functions.Select(f => f.Name).Should().Contain(
            new[] { "createService", "makeHandler", "loadAsync", "bootstrap" });
    }

    [Fact]
    public void Go_Fixture_ExtractsStructsInterfacesAndExportedFunctions()
    {
        var r = Analyze("sample.go", "go");

        r.Classes.Select(c => c.Name).Should().Contain("Invoice");
        r.Interfaces.Select(i => i.Name).Should().Contain("Repository");
        r.Functions.Select(f => f.Name).Should().Contain(new[] { "NewInvoice", "Total" });
        r.Functions.Select(f => f.Name).Should().NotContain("unexportedHelper");
    }

    [Fact]
    public void Java_Fixture_ExtractsClassesInterfacesAndEnums()
    {
        var r = Analyze("Sample.java", "java");

        r.Classes.Select(c => c.Name).Should().Contain("HttpInvoiceService");
        r.Interfaces.Select(i => i.Name).Should().Contain("InvoiceService");
        r.Enums.Should().ContainSingle(e => e.Name == "InvoiceStatus")
            .Which.Values.Should().Equal("DRAFT", "ISSUED", "PAID");
    }
}
