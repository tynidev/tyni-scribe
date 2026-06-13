using Tts.Cli;
using Tts.Cli.Transcription;

if (args.Length == 0 || IsHelp(args[0]))
{
	CommandLineHelp.Write(Console.Out);
	return 0;
}

var command = args[0];
var commandArgs = args.Skip(1).ToArray();

if (command.Equals("transcribe", StringComparison.OrdinalIgnoreCase))
{
	return await TranscribeCommand.RunAsync(commandArgs);
}

if (command.Equals("transcribe-batch", StringComparison.OrdinalIgnoreCase))
{
	return await TranscribeBatchCommand.RunAsync(commandArgs);
}

Console.Error.WriteLine($"Unknown command '{command}'.");
CommandLineHelp.Write(Console.Error);
return 2;

static bool IsHelp(string value)
{
	return value.Equals("--help", StringComparison.OrdinalIgnoreCase)
		   || value.Equals("-h", StringComparison.OrdinalIgnoreCase)
		   || value.Equals("help", StringComparison.OrdinalIgnoreCase);
}
