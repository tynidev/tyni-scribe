using System.Globalization;
using System.Windows.Input;

namespace Tts.App.Services;

public static class HotkeyGestureParser
{
    public static bool TryParse(string? gestureText, out HotkeyGesture gesture, out string errorMessage)
    {
        gesture = default;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(gestureText))
        {
            errorMessage = "Hotkey cannot be empty.";
            return false;
        }

        var modifiers = ModifierKeys.None;
        Key? parsedKey = null;

        foreach (var rawPart in gestureText.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = rawPart.ToLower(CultureInfo.InvariantCulture);

            switch (part)
            {
                case "ctrl":
                case "control":
                    modifiers |= ModifierKeys.Control;
                    continue;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    continue;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    continue;
                case "win":
                case "windows":
                    modifiers |= ModifierKeys.Windows;
                    continue;
            }

            if (parsedKey is not null)
            {
                errorMessage = "Hotkey can contain only one non-modifier key.";
                return false;
            }

            if (!TryParseKey(rawPart, out var key))
            {
                errorMessage = $"'{rawPart}' is not a supported key.";
                return false;
            }

            parsedKey = key;
        }

        if (parsedKey is null)
        {
            errorMessage = "Hotkey must include a non-modifier key.";
            return false;
        }

        if (modifiers == ModifierKeys.None)
        {
            errorMessage = "Hotkey must include at least one modifier.";
            return false;
        }

        gesture = new HotkeyGesture(modifiers, parsedKey.Value);
        return true;
    }

    private static bool TryParseKey(string keyText, out Key key)
    {
        key = keyText.Trim().ToLower(CultureInfo.InvariantCulture) switch
        {
            "esc" => Key.Escape,
            "escape" => Key.Escape,
            "return" => Key.Return,
            "enter" => Key.Return,
            "space" => Key.Space,
            "spacebar" => Key.Space,
            _ => Key.None
        };

        if (key != Key.None)
        {
            return true;
        }

        try
        {
            var converter = new KeyConverter();
            var converted = converter.ConvertFromInvariantString(keyText.Trim());

            if (converted is not Key convertedKey || convertedKey == Key.None)
            {
                return false;
            }

            key = convertedKey;
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}