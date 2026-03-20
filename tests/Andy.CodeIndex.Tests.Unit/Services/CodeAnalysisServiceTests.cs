using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit.Services;

public class CodeAnalysisServiceTests
{
    private readonly CodeAnalysisService _service = new();

    [Theory]
    [InlineData("csharp", true)]
    [InlineData("typescript", true)]
    [InlineData("python", true)]
    [InlineData("go", true)]
    [InlineData("java", true)]
    [InlineData("javascript", true)]
    [InlineData("rust", false)]
    [InlineData("ruby", false)]
    public void SupportsLanguage_ReturnsCorrectly(string language, bool expected)
    {
        _service.SupportsLanguage(language).Should().Be(expected);
    }

    [Fact]
    public void AnalyzeCSharp_ExtractsClassesAndInterfaces()
    {
        var code = @"
public interface IMyService
{
    Task<string> GetDataAsync(int id);
}

public class MyService : BaseService, IMyService, IDisposable
{
    public required string Name { get; set; }
    public int Count { get; set; }

    public async Task<string> GetDataAsync(int id)
    {
        return ""data"";
    }

    public static void DoWork(string input, int count)
    {
    }
}

public enum Status
{
    Active,
    Inactive,
    Pending
}
";
        var result = _service.Analyze(code, "MyService.cs", "csharp");

        result.Classes.Should().HaveCount(1);
        result.Classes[0].Name.Should().Be("MyService");
        result.Classes[0].BaseClass.Should().Be("BaseService");
        result.Classes[0].ImplementedInterfaces.Should().Contain("IMyService");
        result.Classes[0].ImplementedInterfaces.Should().Contain("IDisposable");

        result.Interfaces.Should().HaveCount(1);
        result.Interfaces[0].Name.Should().Be("IMyService");

        result.Enums.Should().HaveCount(1);
        result.Enums[0].Name.Should().Be("Status");
        result.Enums[0].Values.Should().Contain("Active");
        result.Enums[0].Values.Should().HaveCount(3);

        // Methods attached to class
        result.Classes[0].Methods.Should().Contain(m => m.Name == "GetDataAsync" && m.IsAsync);
        result.Classes[0].Methods.Should().Contain(m => m.Name == "DoWork" && m.IsStatic);
    }

    [Fact]
    public void AnalyzeTypeScript_ExtractsExportedItems()
    {
        var code = @"
export interface UserService {
    getUser(id: number): Promise<User>;
}

export class UserRepository extends BaseRepository implements UserService {
    async getUser(id: number): Promise<User> {
        return new User();
    }
}

export function createApp(config: AppConfig): Application {
    return new Application(config);
}

export enum UserRole {
    Admin,
    Editor,
    Viewer
}
";
        var result = _service.Analyze(code, "user.ts", "typescript");

        result.Interfaces.Should().HaveCount(1);
        result.Interfaces[0].Name.Should().Be("UserService");

        result.Classes.Should().HaveCount(1);
        result.Classes[0].Name.Should().Be("UserRepository");
        result.Classes[0].BaseClass.Should().Be("BaseRepository");

        result.Functions.Should().HaveCount(1);
        result.Functions[0].Name.Should().Be("createApp");
        result.Functions[0].IsExported.Should().BeTrue();

        result.Enums.Should().HaveCount(1);
        result.Enums[0].Name.Should().Be("UserRole");
    }

    [Fact]
    public void AnalyzePython_ExtractsClassesAndFunctions()
    {
        var code = @"
class UserRepository(BaseRepository):
    def get_user(self, user_id: int) -> User:
        pass

def create_connection(host: str, port: int) -> Connection:
    pass

def _private_helper():
    pass
";
        var result = _service.Analyze(code, "repo.py", "python");

        result.Classes.Should().HaveCount(1);
        result.Classes[0].Name.Should().Be("UserRepository");
        result.Classes[0].BaseClass.Should().Be("BaseRepository");

        result.Functions.Should().HaveCount(1); // _private_helper skipped
        result.Functions[0].Name.Should().Be("create_connection");
    }

    [Fact]
    public void AnalyzeGo_ExtractsExportedTypes()
    {
        var code = @"
type UserService interface {
    GetUser(id int) (*User, error)
}

type UserRepository struct {
    db *sql.DB
}

func NewUserRepository(db *sql.DB) *UserRepository {
    return &UserRepository{db: db}
}

func (r *UserRepository) GetUser(id int) (*User, error) {
    return nil, nil
}

func privateHelper() {}
";
        var result = _service.Analyze(code, "user.go", "go");

        result.Interfaces.Should().HaveCount(1);
        result.Interfaces[0].Name.Should().Be("UserService");

        result.Classes.Should().HaveCount(1);
        result.Classes[0].Name.Should().Be("UserRepository");

        result.Functions.Should().HaveCount(2); // NewUserRepository + GetUser (exported)
        result.Functions.Should().Contain(f => f.Name == "NewUserRepository");
        result.Functions.Should().Contain(f => f.Name == "GetUser");
    }

    [Fact]
    public void GenerateApiDocs_ProducesMarkdown()
    {
        var code = @"
public class Calculator
{
    public int Add(int a, int b) { return a + b; }
}
";
        var result = _service.Analyze(code, "Calculator.cs", "csharp");
        var docs = _service.GenerateApiDocs(result);

        docs.Should().Contain("# API Documentation: Calculator.cs");
        docs.Should().Contain("**Language:** csharp");
        docs.Should().Contain("## Class: `Calculator`");
    }

    [Fact]
    public void Analyze_EmptyFile_ReturnsEmptyResult()
    {
        var result = _service.Analyze("", "empty.cs", "csharp");

        result.Classes.Should().BeEmpty();
        result.Interfaces.Should().BeEmpty();
        result.Functions.Should().BeEmpty();
        result.Enums.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_UnsupportedLanguage_ReturnsEmptyResult()
    {
        var result = _service.Analyze("fn main() {}", "main.rs", "rust");

        result.Classes.Should().BeEmpty();
        result.Functions.Should().BeEmpty();
    }
}
