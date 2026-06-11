using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Extensions.Hosting;
using Tts.App.Configuration;
using WpfApplication = System.Windows.Application;

namespace Tts.App.Services;

public sealed class GlobalHotkeyService : IHostedService, IGlobalHotkeyService, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int ModNoRepeat = 0x4000;
    private const int StartStopHotkeyId = 1001;
    private const int CancelHotkeyId = 1002;

    private readonly IAppSettingsStore _settingsStore;
    private readonly ISessionOrchestrator _sessionOrchestrator;
    private readonly Dictionary<int, RegisteredHotkey> _registeredHotkeys = new();
    private bool _isMessageFilterAttached;

    public GlobalHotkeyService(IAppSettingsStore settingsStore, ISessionOrchestrator sessionOrchestrator)
    {
        _settingsStore = settingsStore;
        _sessionOrchestrator = sessionOrchestrator;
    }

    public event EventHandler<HotkeyRegistrationStatusChangedEventArgs>? RegistrationStatusChanged;

    public string StatusMessage { get; private set; } = "Hotkeys are not registered.";

    public bool IsRegistered => _registeredHotkeys.Count > 0;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        await ApplySettingsAsync(settings, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await WpfApplication.Current.Dispatcher.InvokeAsync(() => UnregisterAllOnUiThread());
    }

    public async Task<HotkeyRegistrationResult> ApplySettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await WpfApplication.Current.Dispatcher.InvokeAsync(() => ApplySettingsOnUiThread(settings));
    }

    public void Dispose()
    {
        if (WpfApplication.Current is null)
        {
            return;
        }

        WpfApplication.Current.Dispatcher.Invoke(() => UnregisterAllOnUiThread());
    }

    private HotkeyRegistrationResult ApplySettingsOnUiThread(AppSettings settings)
    {
        if (!TryBuildHotkey(settings.StartStopHotkey.Gesture, HotkeyCommand.StartStop, StartStopHotkeyId, "Start/Stop", out var startStopHotkey, out var errorMessage))
        {
            return Fail(errorMessage);
        }

        if (!TryBuildHotkey(settings.CancelHotkey.Gesture, HotkeyCommand.Cancel, CancelHotkeyId, "Cancel", out var cancelHotkey, out errorMessage))
        {
            return Fail(errorMessage);
        }

        if (startStopHotkey.Gesture == cancelHotkey.Gesture)
        {
            return Fail("Start/Stop and Cancel hotkeys must be different. Previous hotkeys kept.");
        }

        var previousHotkeys = _registeredHotkeys.Values.ToArray();
        UnregisterAllOnUiThread(detachMessageFilter: false);

        if (!TryRegister(startStopHotkey, out errorMessage) || !TryRegister(cancelHotkey, out errorMessage))
        {
            UnregisterAllOnUiThread(detachMessageFilter: false);
            RestorePreviousHotkeys(previousHotkeys);
            return Fail($"Could not register hotkeys: {errorMessage}. Previous hotkeys kept.");
        }

        AttachMessageFilter();
        return Succeed($"Hotkeys registered: Start/Stop {startStopHotkey.Gesture}, Cancel {cancelHotkey.Gesture}.");
    }

    private static bool TryBuildHotkey(
        string gestureText,
        HotkeyCommand command,
        int id,
        string displayName,
        out RegisteredHotkey registeredHotkey,
        out string errorMessage)
    {
        registeredHotkey = default;

        if (!HotkeyGestureParser.TryParse(gestureText, out var gesture, out errorMessage))
        {
            errorMessage = $"{displayName} hotkey is invalid: {errorMessage} Previous hotkeys kept.";
            return false;
        }

        registeredHotkey = new RegisteredHotkey(id, command, gesture);
        return true;
    }

    private bool TryRegister(RegisteredHotkey registeredHotkey, out string errorMessage)
    {
        var modifiers = ToNativeModifiers(registeredHotkey.Gesture.Modifiers) | ModNoRepeat;
        var virtualKey = KeyInterop.VirtualKeyFromKey(registeredHotkey.Gesture.Key);

        if (NativeMethods.RegisterHotKey(IntPtr.Zero, registeredHotkey.Id, modifiers, virtualKey))
        {
            _registeredHotkeys[registeredHotkey.Id] = registeredHotkey;
            errorMessage = string.Empty;
            return true;
        }

        errorMessage = new Win32Exception(Marshal.GetLastWin32Error()).Message;
        return false;
    }

    private void RestorePreviousHotkeys(IReadOnlyList<RegisteredHotkey> previousHotkeys)
    {
        foreach (var previousHotkey in previousHotkeys)
        {
            TryRegister(previousHotkey, out _);
        }

        if (_registeredHotkeys.Count > 0)
        {
            AttachMessageFilter();
        }
    }

    private void UnregisterAllOnUiThread(bool detachMessageFilter = true)
    {
        foreach (var registeredHotkey in _registeredHotkeys.Values)
        {
            NativeMethods.UnregisterHotKey(IntPtr.Zero, registeredHotkey.Id);
        }

        _registeredHotkeys.Clear();

        if (detachMessageFilter && _isMessageFilterAttached)
        {
            ComponentDispatcher.ThreadFilterMessage -= OnThreadFilterMessage;
            _isMessageFilterAttached = false;
        }
    }

    private void AttachMessageFilter()
    {
        if (_isMessageFilterAttached)
        {
            return;
        }

        ComponentDispatcher.ThreadFilterMessage += OnThreadFilterMessage;
        _isMessageFilterAttached = true;
    }

    private void OnThreadFilterMessage(ref MSG message, ref bool handled)
    {
        if (message.message != WmHotkey)
        {
            return;
        }

        var hotkeyId = message.wParam.ToInt32();

        if (!_registeredHotkeys.TryGetValue(hotkeyId, out var registeredHotkey))
        {
            return;
        }

        handled = true;
        _ = HandleCommandAsync(registeredHotkey.Command);
    }

    private async Task HandleCommandAsync(HotkeyCommand command)
    {
        if (command == HotkeyCommand.StartStop)
        {
            await _sessionOrchestrator.HandleStartStopAsync();
            return;
        }

        await _sessionOrchestrator.CancelAsync();
    }

    private HotkeyRegistrationResult Succeed(string message)
    {
        StatusMessage = message;
        RegistrationStatusChanged?.Invoke(this, new HotkeyRegistrationStatusChangedEventArgs(StatusMessage, true));
        return new HotkeyRegistrationResult(true, StatusMessage);
    }

    private HotkeyRegistrationResult Fail(string message)
    {
        StatusMessage = message;
        RegistrationStatusChanged?.Invoke(this, new HotkeyRegistrationStatusChangedEventArgs(StatusMessage, IsRegistered));
        return new HotkeyRegistrationResult(false, StatusMessage);
    }

    private static int ToNativeModifiers(ModifierKeys modifiers)
    {
        var nativeModifiers = 0;

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            nativeModifiers |= 0x0001;
        }

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            nativeModifiers |= 0x0002;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            nativeModifiers |= 0x0004;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            nativeModifiers |= 0x0008;
        }

        return nativeModifiers;
    }

    private readonly record struct RegisteredHotkey(int Id, HotkeyCommand Command, HotkeyGesture Gesture);

    private static partial class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr windowHandle, int id, int modifiers, int virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
    }
}