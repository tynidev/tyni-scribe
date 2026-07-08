using Microsoft.Extensions.DependencyInjection;
using Tts.Core;
using YtScribe.Cli;
using YtScribe.Cli.Export;
using YtScribe.Core;

if (args.Length == 0 || IsHelp(args[0]))
{
    CommandLineHelp.Write(Console.Out);
    return 0;
}

var command = args[0];
var commandArgs = args.Skip(1).ToArray();

if (command.Equals("export", StringComparison.OrdinalIgnoreCase))
{
    await using var serviceProvider = BuildServiceProvider();
    return await ExportCommand.RunAsync(commandArgs, serviceProvider);
}

Console.Error.WriteLine($"Unknown command '{command}'.");
CommandLineHelp.Write(Console.Error);
return 2;

static ServiceProvider BuildServiceProvider()
{
    var services = new ServiceCollection();
    services.AddTtsCoreServices();
    services.AddYtScribeCoreServices();
    return services.BuildServiceProvider(validateScopes: true);
}

static bool IsHelp(string value)
{
    return value.Equals("--help", StringComparison.OrdinalIgnoreCase)
           || value.Equals("-h", StringComparison.OrdinalIgnoreCase)
           || value.Equals("help", StringComparison.OrdinalIgnoreCase);
}