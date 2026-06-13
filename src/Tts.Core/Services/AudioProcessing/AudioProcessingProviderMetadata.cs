namespace Tts.Core.Services.AudioProcessing;

public sealed record AudioProcessingProviderMetadata(
	string Id,
	string DisplayName,
	string Description = "");
