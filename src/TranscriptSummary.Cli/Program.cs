using Microsoft.Extensions.DependencyInjection;
using TranscriptSummary.Cli;
using TranscriptSummary.Cli.Summarize;
using TranscriptSummary.Core;

if (args.Length == 0 || IsHelp(args[0]))
{
    CommandLineHelp.Write(Console.Out);
    return 0;
}

var command = args[0];
var commandArgs = args.Skip(1).ToArray();

if (command.Equals("summarize", StringComparison.OrdinalIgnoreCase))
{
    await using var serviceProvider = BuildServiceProvider();
    return await SummarizeCommand.RunAsync(commandArgs, serviceProvider);
}

Console.Error.WriteLine($"Unknown command '{command}'.");
CommandLineHelp.Write(Console.Error);
return 2;

static ServiceProvider BuildServiceProvider()
{
    var services = new ServiceCollection();
    services.AddTranscriptSummaryCoreServices();
    return services.BuildServiceProvider(validateScopes: true);
}

static bool IsHelp(string value)
{
    return value.Equals("--help", StringComparison.OrdinalIgnoreCase)
           || value.Equals("-h", StringComparison.OrdinalIgnoreCase)
           || value.Equals("help", StringComparison.OrdinalIgnoreCase);
}