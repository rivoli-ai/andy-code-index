using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit.Services;

/// <summary>
/// Covers the C# constructs the previous regex engine got wrong (epic #243 /
/// story #247): records, structs, nested/multiple classes, expression-bodied
/// members, generics, parameter defaults, interface members, and XML docs.
/// </summary>
public class CSharpRoslynAnalysisTests
{
    private readonly CodeAnalysisService _service = new();

    [Fact]
    public void Records_AreExtracted_WithPositionalParametersAsProperties()
    {
        var code = "public record Money(decimal Amount, string Currency);";

        var result = _service.Analyze(code, "Money.cs", "csharp");

        result.Classes.Should().ContainSingle(c => c.Name == "Money");
        var money = result.Classes.Single();
        money.Properties.Select(p => p.Name).Should().Contain(new[] { "Amount", "Currency" });
        money.Properties.Should().OnlyContain(p => p.HasGetter && !p.HasSetter);
    }

    [Fact]
    public void RecordStruct_IsExtracted()
    {
        var code = "public record struct Coordinate(double Lat, double Lng);";

        var result = _service.Analyze(code, "Coordinate.cs", "csharp");

        result.Classes.Should().ContainSingle(c => c.Name == "Coordinate");
        result.Classes.Single().Properties.Select(p => p.Name).Should().Equal("Lat", "Lng");
    }

    [Fact]
    public void Struct_IsExtracted_WithMembers()
    {
        var code = @"
public struct Point
{
    public int X { get; set; }
    public int Y { get; set; }
    public double Distance() => 0.0;
}";
        var result = _service.Analyze(code, "Point.cs", "csharp");

        var point = result.Classes.Should().ContainSingle(c => c.Name == "Point").Subject;
        point.Properties.Select(p => p.Name).Should().Equal("X", "Y");
        point.Methods.Should().ContainSingle(m => m.Name == "Distance");
    }

    [Fact]
    public void NestedClass_MethodsAttributedToDeclaringType_NotMostRecentClass()
    {
        // The regex engine appended every method to result.Classes[^1], so the
        // outer method leaked onto the nested class. Roslyn attributes correctly.
        var code = @"
public class Outer
{
    public void OuterMethod() { }

    public class Inner
    {
        public void InnerMethod() { }
    }
}";
        var result = _service.Analyze(code, "Outer.cs", "csharp");

        var outer = result.Classes.Should().ContainSingle(c => c.Name == "Outer").Subject;
        var inner = result.Classes.Should().ContainSingle(c => c.Name == "Inner").Subject;

        outer.Methods.Select(m => m.Name).Should().Equal("OuterMethod");
        inner.Methods.Select(m => m.Name).Should().Equal("InnerMethod");
    }

    [Fact]
    public void MultipleTopLevelClasses_EachOwnTheirMethods()
    {
        var code = @"
public class A { public void Ay() { } }
public class B { public void Bee() { } }";
        var result = _service.Analyze(code, "AB.cs", "csharp");

        result.Classes.Single(c => c.Name == "A").Methods.Select(m => m.Name).Should().Equal("Ay");
        result.Classes.Single(c => c.Name == "B").Methods.Select(m => m.Name).Should().Equal("Bee");
    }

    [Fact]
    public void ExpressionBodiedMembers_AreExtracted_GetterOnlyProperty()
    {
        var code = @"
public class Calc
{
    public int Total => 42;
    public string Describe(int n) => n.ToString();
}";
        var result = _service.Analyze(code, "Calc.cs", "csharp");

        var calc = result.Classes.Single();
        var total = calc.Properties.Should().ContainSingle(p => p.Name == "Total").Subject;
        total.HasGetter.Should().BeTrue();
        total.HasSetter.Should().BeFalse();
        calc.Methods.Should().ContainSingle(m => m.Name == "Describe");
    }

    [Fact]
    public void InitOnlyProperty_HasSetterTrue_PrivateMembersExcluded()
    {
        var code = @"
public class Account
{
    public string Id { get; init; }
    public decimal Balance { get; set; }
    private string Secret { get; set; }
    private void Internal() { }
}";
        var result = _service.Analyze(code, "Account.cs", "csharp");

        var acc = result.Classes.Single();
        acc.Properties.Should().ContainSingle(p => p.Name == "Id").Which.HasSetter.Should().BeTrue();
        acc.Properties.Select(p => p.Name).Should().NotContain("Secret");
        acc.Methods.Select(m => m.Name).Should().NotContain("Internal");
    }

    [Fact]
    public void GenericMethod_RendersParameterTypes_AndDefaults()
    {
        var code = @"
public class Repo
{
    public System.Threading.Tasks.Task<System.Collections.Generic.List<T>> GetAsync<T>(System.Collections.Generic.IEnumerable<T> items, int limit = 10)
    {
        return null;
    }
}";
        var result = _service.Analyze(code, "Repo.cs", "csharp");

        var method = result.Classes.Single().Methods.Single(m => m.Name == "GetAsync");
        method.Parameters.Should().HaveCount(2);
        method.Parameters[0].Name.Should().Be("items");
        method.Parameters[0].Type.Should().Contain("IEnumerable<T>");
        method.Parameters[1].Name.Should().Be("limit");
        method.Parameters[1].DefaultValue.Should().Be("10");
    }

    [Fact]
    public void InterfaceMembers_ArePopulated()
    {
        var code = @"
public interface IRepository
{
    string Name { get; }
    System.Threading.Tasks.Task SaveAsync(int id);
}";
        var result = _service.Analyze(code, "IRepository.cs", "csharp");

        var iface = result.Interfaces.Should().ContainSingle(i => i.Name == "IRepository").Subject;
        iface.Methods.Should().ContainSingle(m => m.Name == "SaveAsync");
        iface.Properties.Should().ContainSingle(p => p.Name == "Name");
    }

    [Fact]
    public void XmlDocSummaries_AreCaptured_ForClassAndMethod()
    {
        var code = @"
/// <summary>
/// Provides access to widgets.
/// </summary>
public class WidgetService
{
    /// <summary>Gets a widget by id.</summary>
    public string Get(int id) => """";
}";
        var result = _service.Analyze(code, "WidgetService.cs", "csharp");

        var cls = result.Classes.Single();
        cls.Summary.Should().Be("Provides access to widgets.");
        cls.Methods.Single(m => m.Name == "Get").Summary.Should().Be("Gets a widget by id.");
    }

    [Fact]
    public void GenerateApiDocs_IncludesXmlSummary()
    {
        var code = @"
/// <summary>A typed cache.</summary>
public class Cache { }";
        var result = _service.Analyze(code, "Cache.cs", "csharp");

        var docs = _service.GenerateApiDocs(result);

        docs.Should().Contain("A typed cache.");
    }

    [Fact]
    public void MalformedCode_DoesNotThrow_BestEffortExtraction()
    {
        // Roslyn parses with error recovery; the analyzer must not throw.
        var code = "public class Broken { public void Oops( { ";

        var act = () => _service.Analyze(code, "Broken.cs", "csharp");

        act.Should().NotThrow();
        var result = act();
        result.Classes.Should().ContainSingle(c => c.Name == "Broken");
    }
}
