using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace StepRecorder.Core;

public enum MouseButton { Left, Right, Middle }

public readonly record struct MouseInput(int X, int Y, MouseButton Button, DateTime Time);

public readonly record struct KeyInput(int VirtualKey, int ScanCode, bool Extended, string? Text,
    bool Ctrl, bool Shift, bool Alt, bool Win, DateTime Time);

/// <summary>
/// Owns a dedicated message-loop thread for global hotkeys and low-level mouse/keyboard hooks.
/// Hook callbacks only copy the event and return, so system input is never delayed.
/// Hooks are installed only while recording; hotkeys are always active.
/// </summary>
public sealed class InputHookService : IDisposable
{
    private readonly Thread _thread;
    private readonly ConcurrentQueue<Action> _pending = new();
    private readonly ManualResetEventSlim _ready = new();
    private readonly HashSet<int> _registeredHotkeys = new();
    private uint _threadId;
    private IntPtr _mouseHook, _keyboardHook;
    private bool _disposed;

    // Delegates must be kept alive while hooks are installed.
    private readonly Native.HookProc _mouseProc;
    private readonly Native.HookProc _keyboardProc;

    public event Action<MouseInput>? MouseDown;
    public event Action<KeyInput>? KeyDown;
    public event Action<int>? HotkeyPressed;

    /// <summary>When false, typed characters are not translated (privacy + less work).</summary>
    public volatile bool TranslateCharacters;

    public InputHookService()
    {
        _mouseProc = MouseProc;
        _keyboardProc = KeyboardProc;
        _thread = new Thread(Run) { IsBackground = true, Name = "InputHooks" };
        _thread.Start();
        _ready.Wait();
    }

    public bool HooksInstalled => _mouseHook != IntPtr.Zero;

    public void InstallHooks() => Invoke(() =>
    {
        var module = Native.GetModuleHandle(null);
        if (_mouseHook == IntPtr.Zero)
            _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, module, 0);
        if (_keyboardHook == IntPtr.Zero)
            _keyboardHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _keyboardProc, module, 0);
    });

    public void UninstallHooks() => Invoke(() =>
    {
        if (_mouseHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        if (_keyboardHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_keyboardHook); _keyboardHook = IntPtr.Zero; }
    });

    /// <summary>Registers hotkeys (replacing previous ones). Returns the ids that failed.</summary>
    public IReadOnlyList<int> SetHotkeys(IReadOnlyDictionary<int, Hotkey> hotkeys)
    {
        var failed = new List<int>();
        using var done = new ManualResetEventSlim();
        Invoke(() =>
        {
            foreach (var id in _registeredHotkeys) Native.UnregisterHotKey(IntPtr.Zero, id);
            _registeredHotkeys.Clear();
            foreach (var (id, hk) in hotkeys)
            {
                if (hk.IsValid && Native.RegisterHotKey(IntPtr.Zero, id, hk.Modifiers | Native.MOD_NOREPEAT, (uint)hk.VirtualKey))
                    _registeredHotkeys.Add(id);
                else
                    failed.Add(id);
            }
            done.Set();
        });
        done.Wait(2000);
        return failed;
    }

    private void Invoke(Action action)
    {
        if (_disposed) return;
        _pending.Enqueue(action);
        Native.PostThreadMessage(_threadId, Native.WM_APP + 1, IntPtr.Zero, IntPtr.Zero);
    }

    private void Run()
    {
        _threadId = Native.GetCurrentThreadId();
        // Make sure the thread has a message queue before anybody posts to it.
        Native.PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        _ready.Set();

        while (Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.hwnd == IntPtr.Zero && msg.message == Native.WM_APP + 1)
            {
                while (_pending.TryDequeue(out var action))
                {
                    try { action(); } catch { /* never kill the hook thread */ }
                }
                continue;
            }
            if (msg.message == Native.WM_HOTKEY)
            {
                var id = (int)msg.wParam;
                ThreadPool.QueueUserWorkItem(_ => HotkeyPressed?.Invoke(id));
                continue;
            }
            Native.TranslateMessage(ref msg);
            Native.DispatchMessage(ref msg);
        }

        foreach (var id in _registeredHotkeys) Native.UnregisterHotKey(IntPtr.Zero, id);
        if (_mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_mouseHook);
        if (_keyboardHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_keyboardHook);
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = (int)wParam;
            MouseButton? button = msg switch
            {
                Native.WM_LBUTTONDOWN => MouseButton.Left,
                Native.WM_RBUTTONDOWN => MouseButton.Right,
                Native.WM_MBUTTONDOWN => MouseButton.Middle,
                _ => null
            };
            if (button is { } b)
            {
                var data = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
                try { MouseDown?.Invoke(new MouseInput(data.pt.X, data.pt.Y, b, DateTime.Now)); } catch { }
            }
        }
        return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && ((int)wParam == Native.WM_KEYDOWN || (int)wParam == Native.WM_SYSKEYDOWN))
        {
            var data = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            var vk = (int)data.vkCode;
            if (!IsModifier(vk))
            {
                bool ctrl = Native.IsKeyDown(Native.VK_CONTROL);
                bool shift = Native.IsKeyDown(Native.VK_SHIFT);
                bool alt = Native.IsKeyDown(Native.VK_MENU);
                bool win = Native.IsKeyDown(Native.VK_LWIN) || Native.IsKeyDown(Native.VK_RWIN);
                string? text = null;
                // AltGr arrives as Ctrl+Alt and still produces characters (e.g. @ on German layouts).
                if (TranslateCharacters && !win && (ctrl == alt))
                    text = Translate(data, shift, ctrl && alt);
                try
                {
                    KeyDown?.Invoke(new KeyInput(vk, (int)data.scanCode, (data.flags & 1) != 0, text,
                        ctrl, shift, alt, win, DateTime.Now));
                }
                catch { }
            }
        }
        return Native.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private static bool IsModifier(int vk) => vk is Native.VK_SHIFT or Native.VK_CONTROL or Native.VK_MENU
        or Native.VK_LSHIFT or Native.VK_RSHIFT or Native.VK_LCONTROL or Native.VK_RCONTROL
        or Native.VK_LMENU or Native.VK_RMENU or Native.VK_LWIN or Native.VK_RWIN or Native.VK_CAPITAL
        or 0x90 /* NumLock */ or 0x91 /* ScrollLock */;

    private static readonly byte[] KeyState = new byte[256];
    private static readonly StringBuilder CharBuffer = new(8);

    private static string? Translate(Native.KBDLLHOOKSTRUCT data, bool shift, bool altGr)
    {
        Array.Clear(KeyState);
        if (shift) KeyState[Native.VK_SHIFT] = 0x80;
        if (altGr) { KeyState[Native.VK_CONTROL] = 0x80; KeyState[Native.VK_MENU] = 0x80; }
        if ((Native.GetKeyState(Native.VK_CAPITAL) & 1) != 0) KeyState[Native.VK_CAPITAL] = 0x01;

        var fg = Native.GetForegroundWindow();
        var layout = Native.GetKeyboardLayout(Native.GetWindowThreadProcessId(fg, out _));
        CharBuffer.Clear();
        // Flag 0x4: do not change the keyboard state (keeps dead keys working in the target app).
        int n = Native.ToUnicodeEx(data.vkCode, data.scanCode, KeyState, CharBuffer, 8, 0x4, layout);
        if (n <= 0) return null;
        var s = CharBuffer.ToString(0, Math.Min(n, CharBuffer.Length));
        foreach (var c in s) if (char.IsControl(c)) return null;
        return s;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Native.PostThreadMessage(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
    }
}
