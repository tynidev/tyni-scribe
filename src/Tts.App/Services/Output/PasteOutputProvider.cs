using System.Runtime.InteropServices;
using System.Windows.Threading;
using Tts.App.Services;
using WpfApplication = System.Windows.Application;
using WpfClipboard = System.Windows.Clipboard;

namespace Tts.App.Services.Output;

public sealed class PasteOutputProvider : IOutputProvider
{
    public const string ProviderId = "paste";

    private const int InputKeyboard = 1;
    private const ushort VirtualKeyControl = 0x11;
    private const ushort VirtualKeyV = 0x56;
    private const uint KeyEventKeyUp = 0x0002;

    public string Id => ProviderId;

    public string DisplayName => "Paste";

    public string Description => "Copies final transcript text to the clipboard and sends Ctrl+V to the active window.";

    public IReadOnlyList<ProviderSettingDescriptor> SettingDescriptors { get; } = Array.Empty<ProviderSettingDescriptor>();

    public async Task WriteAsync(string text, OutputProviderContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();

        await SetClipboardTextAsync(text, cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        SendPasteKeystroke();
    }

    private static async Task SetClipboardTextAsync(string text, CancellationToken cancellationToken)
    {
        var application = WpfApplication.Current
            ?? throw new InvalidOperationException("The WPF application is not available.");

        if (application.Dispatcher.CheckAccess())
        {
            WpfClipboard.SetText(text);
            return;
        }

        await application.Dispatcher
            .InvokeAsync(() => WpfClipboard.SetText(text), DispatcherPriority.Normal, cancellationToken)
            .Task;
    }

    private static void SendPasteKeystroke()
    {
        var inputs = new[]
        {
            CreateKeyboardInput(VirtualKeyControl, 0),
            CreateKeyboardInput(VirtualKeyV, 0),
            CreateKeyboardInput(VirtualKeyV, KeyEventKeyUp),
            CreateKeyboardInput(VirtualKeyControl, KeyEventKeyUp)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException($"Could not send paste keystroke. Win32 error {Marshal.GetLastWin32Error()}.");
        }
    }

    private static INPUT CreateKeyboardInput(ushort virtualKey, uint flags)
    {
        return new INPUT
        {
            type = InputKeyboard,
            union = new INPUTUNION
            {
                keyboardInput = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = flags
                }
            }
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public INPUTUNION union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)]
        public MOUSEINPUT mouseInput;

        [FieldOffset(0)]
        public KEYBDINPUT keyboardInput;

        [FieldOffset(0)]
        public HARDWAREINPUT hardwareInput;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}