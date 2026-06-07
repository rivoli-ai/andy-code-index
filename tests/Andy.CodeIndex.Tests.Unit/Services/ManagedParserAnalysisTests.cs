using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit.Services;

/// <summary>
/// Covers the managed JS/Python/TS analyzers added in story #248: JavaScript via
/// the Acornima AST, Python via an indentation-aware scanner, and TypeScript
/// heuristic improvements (arrow exports, export default).
/// </summary>
public class ManagedParserAnalysisTests
{
    private readonly CodeAnalysisService _service = new();

    // ---------- JavaScript (Acornima AST) ----------

    [Fact]
    public void JavaScript_ExtractsClassWithMethods_BaseClass_StaticAndAsync()
    {
        var code = @"
export class Widget extends Base {
  constructor(id) { this.id = id; }
  async load(url) { return fetch(url); }
  static create() { return new Widget(); }
}
";
        var result = _service.Analyze(code, "widget.js", "javascript");

        var widget = result.Classes.Should().ContainSingle(c => c.Name == "Widget").Subject;
        widget.BaseClass.Should().Be("Base");
        widget.Methods.Should().ContainSingle(m => m.Name == "load").Which.IsAsync.Should().BeTrue();
        widget.Methods.Should().ContainSingle(m => m.Name == "create").Which.IsStatic.Should().BeTrue();
    }

    [Fact]
    public void JavaScript_ExtractsArrowFunctionExports()
    {
        // The previous regex path missed these entirely.
        var code = @"
export const make = (x) => x * 2;
export const fetchAsync = async (u) => await fetch(u);
export function build(a, b) { return a + b; }
";
        var result = _service.Analyze(code, "fns.js", "javascript");

        result.Functions.Select(f => f.Name).Should().Contain(new[] { "make", "fetchAsync", "build" });
        result.Functions.Should().OnlyContain(f => f.IsExported);
        result.Functions.Single(f => f.Name == "build").Parameters.Select(p => p.Name).Should().Equal("a", "b");
    }

    [Fact]
    public void JavaScript_ExtractsExportDefaultClassAndFunction()
    {
        _service.Analyze("export default class App {}", "app.js", "javascript")
            .Classes.Should().ContainSingle(c => c.Name == "App");

        _service.Analyze("export default function setup() {}", "setup.js", "javascript")
            .Functions.Should().ContainSingle(f => f.Name == "setup");
    }

    [Fact]
    public void JavaScript_NonExportedTopLevel_IsCaptured_NotMarkedExported()
    {
        var result = _service.Analyze("function helper(a) { return a; }", "h.js", "javascript");

        var fn = result.Functions.Should().ContainSingle(f => f.Name == "helper").Subject;
        fn.IsExported.Should().BeFalse();
    }

    // ---------- Python (indentation-aware) ----------

    [Fact]
    public void Python_ExtractsClassMethods_AsyncDef_AndModuleAsyncFunctions()
    {
        var code = @"
class Animal:
    def __init__(self, name):
        self.name = name

    async def speak(self, volume=5) -> str:
        return 'hi'

    def _private(self):
        pass

def top_level(a, b) -> int:
    return a + b

async def fetch_data(url):
    return None

def _hidden():
    pass
";
        var result = _service.Analyze(code, "animal.py", "python");

        var animal = result.Classes.Should().ContainSingle(c => c.Name == "Animal").Subject;
        animal.Methods.Select(m => m.Name).Should().Contain(new[] { "__init__", "speak" });
        animal.Methods.Select(m => m.Name).Should().NotContain("_private");
        animal.Methods.Single(m => m.Name == "speak").IsAsync.Should().BeTrue();
        // self is dropped; the annotated return type is captured.
        animal.Methods.Single(m => m.Name == "speak").Parameters.Select(p => p.Name).Should().Equal("volume");
        animal.Methods.Single(m => m.Name == "speak").ReturnType.Should().Be("str");

        result.Functions.Select(f => f.Name).Should().Contain(new[] { "top_level", "fetch_data" });
        result.Functions.Select(f => f.Name).Should().NotContain("_hidden");
        result.Functions.Single(f => f.Name == "fetch_data").IsAsync.Should().BeTrue();
        result.Functions.Single(f => f.Name == "top_level").Parameters.Select(p => p.Name).Should().Equal("a", "b");
    }

    [Fact]
    public void Python_Decorators_PropertyBecomesProperty_StaticmethodMarksStatic()
    {
        var code = @"
class Config:
    @property
    def name(self) -> str:
        return self._name

    @staticmethod
    def default() -> int:
        return 0
";
        var result = _service.Analyze(code, "config.py", "python");

        var config = result.Classes.Single();
        config.Properties.Should().ContainSingle(p => p.Name == "name");
        config.Methods.Should().ContainSingle(m => m.Name == "default").Which.IsStatic.Should().BeTrue();
        config.Methods.Should().NotContain(m => m.Name == "name");
    }

    [Fact]
    public void Python_NestedClassBase_IsCaptured()
    {
        var code = @"
class Base:
    pass

class Derived(Base):
    def run(self):
        pass
";
        var result = _service.Analyze(code, "d.py", "python");

        result.Classes.Should().HaveCount(2);
        result.Classes.Single(c => c.Name == "Derived").BaseClass.Should().Be("Base");
        result.Classes.Single(c => c.Name == "Derived").Methods.Select(m => m.Name).Should().Equal("run");
    }

    // ---------- TypeScript (heuristic improvements) ----------

    [Fact]
    public void TypeScript_ExtractsArrowFunctionExports()
    {
        var code = @"
export const handler = (req: Request): Response => doThing(req);
export const compute = async (n: number): Promise<number> => n;
";
        var result = _service.Analyze(code, "handlers.ts", "typescript");

        result.Functions.Select(f => f.Name).Should().Contain(new[] { "handler", "compute" });
    }

    [Fact]
    public void TypeScript_ExtractsExportDefaultClassAndFunction()
    {
        _service.Analyze("export default class Page extends Component {}", "page.ts", "typescript")
            .Classes.Should().ContainSingle(c => c.Name == "Page" && c.BaseClass == "Component");

        _service.Analyze("export default function setup() {}", "s.ts", "typescript")
            .Functions.Should().ContainSingle(f => f.Name == "setup");
    }
}
