using Tts.Core.Configuration;

namespace Tts.App.Services;

public interface IGlobalHotkeyService
{
    event EventHandler<HotkeyRegistrationStatusChangedEventArgs>? RegistrationStatusChanged;

    string StatusMessage { get; }

    bool IsRegistered { get; }

    Task<HotkeyRegistrationResult> ApplySettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);
}