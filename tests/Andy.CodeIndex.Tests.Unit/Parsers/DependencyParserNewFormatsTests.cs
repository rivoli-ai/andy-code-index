using Andy.CodeIndex.Infrastructure.Parsers;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit.Parsers;

/// <summary>
/// Coverage for the dependency-parsing fixes in epic #245: C# Central Package
/// Management (#255), Python setup.py/Pipfile/markers/Poetry (#256), and JS
/// peer/optional deps + package-lock resolution (#257).
/// </summary>
public class DependencyParserNewFormatsTests
{
    // ---------- #255 C# Central Package Management ----------

    [Fact]
    public void Csproj_ParsesPackageVersion_FromDirectoryPackagesProps()
    {
        var props = @"
<Project>
  <ItemGroup>
    <PackageVersion Include=""Serilog"" Version=""3.1.1"" />
    <PackageVersion Include=""xunit"" Version=""2.9.2"" />
  </ItemGroup>
</Project>";
        var deps = DependencyParserService.ParseCsproj(props, "Directory.Packages.props");

        deps.Should().HaveCount(2);
        deps.Single(d => d.Name == "Serilog").Version.Should().Be("3.1.1");
        deps.Should().OnlyContain(d => d.Source == "nuget");
    }

    [Fact]
    public void Csproj_ParsesChildElementVersion()
    {
        var csproj = @"
<Project>
  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"">
      <Version>13.0.3</Version>
    </PackageReference>
  </ItemGroup>
</Project>";
        var deps = DependencyParserService.ParseCsproj(csproj, "app.csproj");

        deps.Should().ContainSingle();
        deps[0].Name.Should().Be("Newtonsoft.Json");
        deps[0].Version.Should().Be("13.0.3");
    }

    [Fact]
    public void Csproj_CpmReference_WithoutInlineVersion_HasNullVersion()
    {
        // Under CPM the .csproj reference has no version; it comes from the props file.
        var csproj = @"<Project><ItemGroup><PackageReference Include=""Serilog"" /></ItemGroup></Project>";
        var deps = DependencyParserService.ParseCsproj(csproj, "app.csproj");

        deps.Should().ContainSingle(d => d.Name == "Serilog" && d.Version == null);
    }

    // ---------- #257 JavaScript ----------

    [Fact]
    public void PackageJson_ParsesPeerAndOptionalDependencies()
    {
        var json = @"{
            ""dependencies"": { ""express"": ""^4.18.0"" },
            ""peerDependencies"": { ""react"": "">=18"" },
            ""optionalDependencies"": { ""fsevents"": ""^2.3.0"" }
        }";
        var deps = DependencyParserService.ParsePackageJson(json, "package.json");

        deps.Single(d => d.Name == "react").Scope.Should().Be("peer");
        deps.Single(d => d.Name == "fsevents").Scope.Should().Be("optional");
        deps.Single(d => d.Name == "express").Scope.Should().Be("runtime");
    }

    [Fact]
    public void PackageLock_V3_ResolvesDirectVersions_SkipsTransitive()
    {
        var lockJson = @"{
            ""lockfileVersion"": 3,
            ""packages"": {
                """": { ""name"": ""root"" },
                ""node_modules/express"": { ""version"": ""4.18.2"" },
                ""node_modules/jest"": { ""version"": ""29.7.0"", ""dev"": true },
                ""node_modules/express/node_modules/cookie"": { ""version"": ""0.5.0"" }
            }
        }";
        var deps = DependencyParserService.ParsePackageLockJson(lockJson, "package-lock.json");

        deps.Select(d => d.Name).Should().BeEquivalentTo(new[] { "express", "jest" });
        deps.Single(d => d.Name == "express").Version.Should().Be("4.18.2");
        deps.Single(d => d.Name == "jest").Scope.Should().Be("dev");
    }

    // ---------- #256 Python ----------

    [Fact]
    public void Requirements_HandlesExtrasMarkersAndUrls()
    {
        var txt = @"
requests[security]>=2.28.0
django==4.2 ; python_version >= '3.8'
mypkg @ https://example.com/mypkg.whl
flask==2.3.0  # inline comment
";
        var deps = DependencyParserService.ParseRequirementsTxt(txt, "requirements.txt");

        deps.Single(d => d.Name == "requests").Version.Should().Be(">=2.28.0");
        deps.Single(d => d.Name == "django").Version.Should().Be("==4.2");
        deps.Single(d => d.Name == "mypkg").Version.Should().BeNull();
        deps.Single(d => d.Name == "flask").Version.Should().Be("==2.3.0");
    }

    [Fact]
    public void Pyproject_ParsesPoetryAndOptionalDependencies()
    {
        var toml = @"
[tool.poetry.dependencies]
python = ""^3.11""
requests = ""^2.28""
pydantic = { version = ""^2.0"", optional = true }

[tool.poetry.dev-dependencies]
pytest = ""^7.0""

[project.optional-dependencies]
extra = [""rich>=13.0""]
";
        var deps = DependencyParserService.ParsePyprojectToml(toml, "pyproject.toml");

        deps.Should().NotContain(d => d.Name == "python");
        deps.Single(d => d.Name == "requests").Version.Should().Be("^2.28");
        deps.Single(d => d.Name == "pydantic").Version.Should().Be("^2.0");
        deps.Single(d => d.Name == "pytest").Scope.Should().Be("dev");
        deps.Single(d => d.Name == "rich").Scope.Should().Be("optional");
    }

    [Fact]
    public void Pyproject_ParsesPep621Array()
    {
        var toml = @"
[project]
name = ""demo""
dependencies = [
    ""httpx>=0.24"",
    ""click>=8.0"",
]
";
        var deps = DependencyParserService.ParsePyprojectToml(toml, "pyproject.toml");

        deps.Select(d => d.Name).Should().BeEquivalentTo(new[] { "httpx", "click" });
        deps.Single(d => d.Name == "httpx").Version.Should().Be(">=0.24");
    }

    [Fact]
    public void SetupPy_ParsesInstallRequires()
    {
        var setup = @"
from setuptools import setup
setup(
    name='demo',
    install_requires=[
        'requests>=2.0',
        'click',
    ],
)
";
        var deps = DependencyParserService.ParseSetupPy(setup, "setup.py");

        deps.Select(d => d.Name).Should().BeEquivalentTo(new[] { "requests", "click" });
        deps.Single(d => d.Name == "requests").Version.Should().Be(">=2.0");
    }

    [Fact]
    public void Pipfile_ParsesPackagesAndDevPackages()
    {
        var pipfile = @"
[packages]
requests = ""*""
flask = "">=2.0""

[dev-packages]
pytest = ""*""
";
        var deps = DependencyParserService.ParsePipfile(pipfile, "Pipfile");

        deps.Single(d => d.Name == "requests").Version.Should().BeNull(); // "*" -> null
        deps.Single(d => d.Name == "flask").Version.Should().Be(">=2.0");
        deps.Single(d => d.Name == "pytest").Scope.Should().Be("dev");
    }

    [Fact]
    public void Parse_DispatchesSetupPyAndPipfileAndPackageLock()
    {
        var svc = new DependencyParserService();

        svc.CanParse("setup.py").Should().BeTrue();
        svc.CanParse("Pipfile").Should().BeTrue();
        svc.CanParse("package-lock.json").Should().BeTrue();
        svc.Parse("setup.py", "install_requires=['requests']").Should().ContainSingle(d => d.Name == "requests");
        svc.Parse("Pipfile", "[packages]\nflask = \"*\"").Should().ContainSingle(d => d.Name == "flask");
    }
}
