using CommunityToolkit.Mvvm.ComponentModel;
using Tts.Core.Services;

namespace Tts.App.ViewModels;

public sealed partial class ProviderSettingViewModel : ObservableObject
{
    private readonly ProviderSettingDescriptor _descriptor;

    public ProviderSettingViewModel(ProviderSettingDescriptor descriptor, string value)
    {
        _descriptor = descriptor;
        _value = value;
    }

    [ObservableProperty]
    private string _value;

    public string Key => _descriptor.Key;

    public string DisplayName => _descriptor.DisplayName;

    public string Description => _descriptor.Description;

    public IReadOnlyList<ProviderSettingOption> Options => _descriptor.Options ?? Array.Empty<ProviderSettingOption>();

    public bool IsSelect => _descriptor.ControlKind == ProviderSettingControlKind.Select;

    public bool IsTextInput => _descriptor.ControlKind is ProviderSettingControlKind.Text or ProviderSettingControlKind.Integer;

    public bool IsReadOnlyText => _descriptor.ControlKind == ProviderSettingControlKind.ReadOnlyText;

    public bool IsCompact => _descriptor.Layout == ProviderSettingLayout.Compact;
}
