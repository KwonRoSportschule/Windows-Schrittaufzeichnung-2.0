using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace StepRecorder.Core;

/// <summary>A global hotkey such as "Ctrl+Shift+R".</summary>
public readonly record struct Hotkey(bool Ctrl, bool Shift, bool Alt, bool Win, int VirtualKey)
{
    public bool IsValid => VirtualKey != 0 && (Ctrl || Alt || Win);

    public uint Modifiers =>
        (Ctrl ? Native.MOD_CONTROL : 0) | (Shift ? Native.MOD_SHIFT : 0) |
        (Alt ? Native.MOD_ALT : 0) | (Win ? Native.MOD_WIN : 0);

    public static Hotkey FromKey(Key key, ModifierKeys modifiers) => new(
        modifiers.HasFlag(ModifierKeys.Control), modifiers.HasFlag(ModifierKeys.Shift),
        modifiers.HasFlag(ModifierKeys.Alt), modifiers.HasFlag(ModifierKeys.Windows),
        KeyInterop.VirtualKeyFromKey(key));

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        bool ctrl = false, shift = false, alt = false, win = false;
        int vk = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "strg": case "control": ctrl = true; break;
                case "shift": case "umschalt": shift = true; break;
                case "alt": alt = true; break;
                case "win": case "windows": win = true; break;
                default:
                    var name = raw.Length == 1 && char.IsDigit(raw[0]) ? "D" + raw : raw;
                    if (!Enum.TryParse(name, true, out Key key)) return false;
                    vk = KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        hotkey = new Hotkey(ctrl, shift, alt, win, vk);
        return hotkey.IsValid;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Shift) parts.Add("Shift");
        if (Alt) parts.Add("Alt");
        if (Win) parts.Add("Win");
        var key = KeyInterop.KeyFromVirtualKey(VirtualKey);
        var name = key.ToString();
        if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])) name = name[1..];
        parts.Add(name);
        return string.Join("+", parts);
    }
}
