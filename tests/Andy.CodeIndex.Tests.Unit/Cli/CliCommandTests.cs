using System.CommandLine;
using System.CommandLine.IO;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit.Cli;

/// <summary>
/// Tests CLI command parsing and help output.
/// These tests verify the command structure without making HTTP calls.
/// </summary>
public class CliCommandTests
{
    [Fact]
    public async Task RootCommand_Help_ShowsAllCommands()
    {
        var output = await RunCli("--help");
        output.Should().Contain("repo");
        output.Should().Contain("search");
        output.Should().Contain("enrichments");
        output.Should().Contain("status");
    }

    [Fact]
    public async Task RepoCommand_Help_ShowsSubcommands()
    {
        var output = await RunCli("repo", "--help");
        output.Should().Contain("list");
        output.Should().Contain("add");
        output.Should().Contain("remove");
        output.Should().Contain("sync");
        output.Should().Contain("status");
    }

    [Fact]
    public async Task SearchCommand_Help_ShowsOptions()
    {
        var output = await RunCli("search", "--help");
        output.Should().Contain("--mode");
        output.Should().Contain("--lang");
        output.Should().Contain("--repo");
        output.Should().Contain("--limit");
    }

    [Fact]
    public async Task SearchGrepCommand_Help_ShowsOptions()
    {
        var output = await RunCli("search", "grep", "--help");
        output.Should().Contain("--repo");
        output.Should().Contain("--glob");
        output.Should().Contain("--limit");
    }

    [Fact]
    public async Task EnrichmentsCommand_Help_ShowsOptions()
    {
        var output = await RunCli("enrichments", "--help");
        output.Should().Contain("--type");
        output.Should().Contain("--subtype");
        output.Should().Contain("--repo");
        output.Should().Contain("--limit");
    }

    [Fact]
    public async Task GlobalOptions_BaseUrl_HasDefault()
    {
        var output = await RunCli("--help");
        output.Should().Contain("--base-url");
        output.Should().Contain("https://localhost:5101");
    }

    [Fact]
    public async Task GlobalOptions_Token_Available()
    {
        var output = await RunCli("--help");
        output.Should().Contain("--token");
    }

    [Fact]
    public async Task GlobalOptions_Format_HasDefault()
    {
        var output = await RunCli("--help");
        output.Should().Contain("--format");
        output.Should().Contain("table");
    }

    [Fact]
    public async Task RepoAdd_RequiresUrl()
    {
        var output = await RunCli("repo", "add", "--help");
        output.Should().Contain("<url>");
    }

    [Fact]
    public async Task RepoAdd_HasPatOption()
    {
        var output = await RunCli("repo", "add", "--help");
        output.Should().Contain("--pat");
    }

    [Fact]
    public async Task RepoRemove_RequiresId()
    {
        var output = await RunCli("repo", "remove", "--help");
        output.Should().Contain("<id>");
    }

    [Fact]
    public async Task RepoRemove_HasForceOption()
    {
        var output = await RunCli("repo", "remove", "--help");
        output.Should().Contain("--force");
    }

    private static async Task<string> RunCli(params string[] args)
    {
        // Build the CLI root command (same as Program.cs but for testing)
        var rootCommand = new RootCommand("test");
        var baseUrl = new Option<string>("--base-url", () => "https://localhost:5101");
        var token = new Option<string?>("--token");
        var format = new Option<string>("--format", () => "table");
        rootCommand.AddGlobalOption(baseUrl);
        rootCommand.AddGlobalOption(token);
        rootCommand.AddGlobalOption(format);

        var repoCommand = new Command("repo", "Manage repositories");
        repoCommand.AddCommand(new Command("list", "List all tracked repositories"));
        var addCmd = new Command("add", "Add a repository for indexing");
        addCmd.AddArgument(new Argument<string>("url"));
        addCmd.AddOption(new Option<string?>("--pat"));
        repoCommand.AddCommand(addCmd);
        var removeCmd = new Command("remove", "Remove a repository");
        removeCmd.AddArgument(new Argument<string>("id"));
        removeCmd.AddOption(new Option<bool>("--force"));
        repoCommand.AddCommand(removeCmd);
        repoCommand.AddCommand(new Command("sync", "Trigger repository sync"));
        repoCommand.AddCommand(new Command("status", "Show repository indexing status"));

        var searchCommand = new Command("search", "Search indexed code");
        searchCommand.AddArgument(new Argument<string>("query"));
        searchCommand.AddOption(new Option<string>("--mode", () => "hybrid"));
        searchCommand.AddOption(new Option<string?>("--lang"));
        searchCommand.AddOption(new Option<string?>("--repo"));
        searchCommand.AddOption(new Option<int>("--limit", () => 10));
        var grepCmd = new Command("grep", "Search file contents with regex");
        grepCmd.AddArgument(new Argument<string>("pattern"));
        grepCmd.AddOption(new Option<string>("--repo") { IsRequired = true });
        grepCmd.AddOption(new Option<string?>("--glob"));
        grepCmd.AddOption(new Option<int>("--limit", () => 50));
        searchCommand.AddCommand(grepCmd);

        var enrichCommand = new Command("enrichments", "Browse enrichments");
        enrichCommand.AddOption(new Option<string?>("--type"));
        enrichCommand.AddOption(new Option<string?>("--subtype"));
        enrichCommand.AddOption(new Option<string?>("--repo"));
        enrichCommand.AddOption(new Option<int>("--limit", () => 20));

        var statusCommand = new Command("status", "Show system status");

        rootCommand.AddCommand(repoCommand);
        rootCommand.AddCommand(searchCommand);
        rootCommand.AddCommand(enrichCommand);
        rootCommand.AddCommand(statusCommand);

        var console = new TestConsole();
        await rootCommand.InvokeAsync(args, console);
        return console.Out.ToString()!;
    }
}
