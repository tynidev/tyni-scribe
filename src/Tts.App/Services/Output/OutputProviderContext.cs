namespace Tts.App.Services.Output;

public sealed record OutputProviderContext(
	Guid SessionId,
	IReadOnlyDictionary<string, string> Settings);