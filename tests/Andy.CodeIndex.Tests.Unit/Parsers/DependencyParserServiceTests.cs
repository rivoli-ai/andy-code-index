using Andy.CodeIndex.Infrastructure.Parsers;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit.Parsers;

public class DependencyParserServiceTests
{
    private readonly DependencyParserService _parser = new();

    // --- CanParse ---

    [Theory]
    [InlineData("project.csproj", true)]
    [InlineData("Directory.Build.props", true)]
    [InlineData("package.json", true)]
    [InlineData("requirements.txt", true)]
    [InlineData("go.mod", true)]
    [InlineData("pom.xml", true)]
    [InlineData("build.gradle", true)]
    [InlineData("build.gradle.kts", true)]
    [InlineData("Cargo.toml", true)]
    [InlineData("Gemfile", true)]
    [InlineData("composer.json", true)]
    [InlineData("pyproject.toml", true)]
    [InlineData("README.md", false)]
    [InlineData("Program.cs", false)]
    public void CanParse_DetectsCorrectFiles(string fileName, bool expected)
    {
        _parser.CanParse(fileName).Should().Be(expected);
    }

    // --- .NET / NuGet ---

    [Fact]
    public void ParseCsproj_ExtractsPackageReferences()
    {
        var csproj = @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"" Version=""13.0.1"" />
    <PackageReference Include=""Moq"" Version=""4.20.72"" />
  </ItemGroup>
</Project>";
        var deps = DependencyParserService.ParseCsproj(csproj, "test.csproj");
        deps.Should().HaveCount(2);
        deps[0].Name.Should().Be("Newtonsoft.Json");
        deps[0].Version.Should().Be("13.0.1");
        deps[0].Source.Should().Be("nuget");
    }

    [Fact]
    public void ParseCsproj_HandlesNoVersion()
    {
        var csproj = @"<PackageReference Include=""SomePackage"" />";
        var deps = DependencyParserService.ParseCsproj(csproj, "test.csproj");
        deps.Should().HaveCount(1);
        deps[0].Version.Should().BeNull();
    }

    [Fact]
    public void ParseCsproj_EmptyFile_ReturnsEmpty()
    {
        DependencyParserService.ParseCsproj("<Project></Project>", "test.csproj").Should().BeEmpty();
    }

    // --- Node.js / npm ---

    [Fact]
    public void ParsePackageJson_ExtractsBothDependencyTypes()
    {
        var json = @"{
            ""dependencies"": { ""express"": ""^4.18.0"", ""cors"": ""~2.8.5"" },
            ""devDependencies"": { ""jest"": ""^29.0.0"" }
        }";
        var deps = DependencyParserService.ParsePackageJson(json, "package.json");
        deps.Should().HaveCount(3);
        deps.Where(d => d.Scope == "runtime").Should().HaveCount(2);
        deps.Where(d => d.Scope == "dev").Should().HaveCount(1);
        deps.First(d => d.Name == "express").Version.Should().Be("^4.18.0");
        deps.All(d => d.Source == "npm").Should().BeTrue();
    }

    [Fact]
    public void ParsePackageJson_NoDeps_ReturnsEmpty()
    {
        DependencyParserService.ParsePackageJson(@"{""name"":""app""}", "package.json").Should().BeEmpty();
    }

    // --- Python ---

    [Fact]
    public void ParseRequirementsTxt_ExtractsPackages()
    {
        var txt = @"
flask==2.3.0
requests>=2.28.0
# comment
numpy
-r other.txt
";
        var deps = DependencyParserService.ParseRequirementsTxt(txt, "requirements.txt");
        deps.Should().HaveCount(3);
        deps[0].Name.Should().Be("flask");
        deps[0].Version.Should().Be("==2.3.0");
        deps[1].Name.Should().Be("requests");
        deps[2].Name.Should().Be("numpy");
        deps[2].Version.Should().BeNull();
    }

    // --- Go ---

    [Fact]
    public void ParseGoMod_ExtractsRequirements()
    {
        var gomod = @"
module github.com/my/project

go 1.21

require (
    github.com/gin-gonic/gin v1.9.1
    github.com/stretchr/testify v1.8.4
)
";
        var deps = DependencyParserService.ParseGoMod(gomod, "go.mod");
        deps.Should().HaveCount(2);
        deps[0].Name.Should().Be("github.com/gin-gonic/gin");
        deps[0].Version.Should().Be("v1.9.1");
        deps[0].Source.Should().Be("go");
    }

    // --- Java / Maven ---

    [Fact]
    public void ParsePomXml_ExtractsDependencies()
    {
        var pom = @"
<project>
  <dependencies>
    <dependency>
      <groupId>org.springframework</groupId>
      <artifactId>spring-core</artifactId>
      <version>5.3.0</version>
    </dependency>
    <dependency>
      <groupId>junit</groupId>
      <artifactId>junit</artifactId>
      <version>4.13</version>
      <scope>test</scope>
    </dependency>
  </dependencies>
</project>";
        var deps = DependencyParserService.ParsePomXml(pom, "pom.xml");
        deps.Should().HaveCount(2);
        deps[0].Name.Should().Be("org.springframework:spring-core");
        deps[1].Scope.Should().Be("test");
    }

    // --- Gradle ---

    [Fact]
    public void ParseBuildGradle_ExtractsImplementations()
    {
        var gradle = @"
dependencies {
    implementation 'org.springframework.boot:spring-boot-starter:3.0.0'
    testImplementation 'org.junit:junit:5.9.0'
    api ""com.google.guava:guava:31.1""
}";
        var deps = DependencyParserService.ParseBuildGradle(gradle, "build.gradle");
        deps.Should().HaveCountGreaterThanOrEqualTo(2);
        deps.Should().Contain(d => d.Scope == "test");
    }

    // --- Rust ---

    [Fact]
    public void ParseCargoToml_ExtractsDependencies()
    {
        var toml = @"
[package]
name = ""myapp""

[dependencies]
serde = ""1.0""
tokio = { version = ""1.0"", features = [""full""] }

[dev-dependencies]
criterion = ""0.5""
";
        var deps = DependencyParserService.ParseCargoToml(toml, "Cargo.toml");
        deps.Should().HaveCountGreaterThanOrEqualTo(2);
        deps.Should().Contain(d => d.Name == "serde");
        deps.All(d => d.Source == "cargo").Should().BeTrue();
    }

    // --- Ruby ---

    [Fact]
    public void ParseGemfile_ExtractsGems()
    {
        var gemfile = @"
source 'https://rubygems.org'
gem 'rails', '~> 7.0'
gem 'puma'
gem 'pg', '>= 1.4'
";
        var deps = DependencyParserService.ParseGemfile(gemfile, "Gemfile");
        deps.Should().HaveCount(3);
        deps[0].Name.Should().Be("rails");
        deps[0].Version.Should().Be("~> 7.0");
        deps[1].Name.Should().Be("puma");
        deps[1].Version.Should().BeNull();
    }

    // --- PHP ---

    [Fact]
    public void ParseComposerJson_ExtractsBothTypes()
    {
        var json = @"{
            ""require"": { ""php"": ""^8.1"", ""laravel/framework"": ""^10.0"" },
            ""require-dev"": { ""phpunit/phpunit"": ""^10.0"" }
        }";
        var deps = DependencyParserService.ParseComposerJson(json, "composer.json");
        deps.Should().HaveCount(2); // php excluded
        deps.Should().Contain(d => d.Name == "laravel/framework" && d.Scope == "runtime");
        deps.Should().Contain(d => d.Name == "phpunit/phpunit" && d.Scope == "dev");
    }

    // --- Unknown ---

    [Fact]
    public void Parse_UnknownFile_ReturnsEmpty()
    {
        _parser.Parse("README.md", "some content").Should().BeEmpty();
    }
}
