namespace Tts.Core.Services;

public sealed record ProviderSettingDescriptor(
    string Key,
    string DisplayName,
    ProviderSettingControlKind ControlKind,
    IReadOnlyList<ProviderSettingOption>? Options = null,
    string Description = "",
    ProviderSettingLayout Layout = ProviderSettingLayout.FullWidth);