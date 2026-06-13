namespace Tts.Core.Services.Output;

public sealed record OutputProviderContext(
	Guid SessionId,
	IReadOnlyDictionary<string, string> Settings);