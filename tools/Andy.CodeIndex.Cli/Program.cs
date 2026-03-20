using System.CommandLine;

var rootCommand = new RootCommand("Andy CodeIndex CLI - semantic code indexing for the Andy ecosystem");

rootCommand.SetHandler(() =>
{
    Console.WriteLine("Andy CodeIndex CLI v1.0.0");
    Console.WriteLine("Use --help for available commands.");
});

return await rootCommand.InvokeAsync(args);
