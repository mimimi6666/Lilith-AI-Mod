using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using Microsoft.Win32;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;
using UI.Common;
using UI.TraySetting;

namespace LilithTextInjector;

internal static partial class DialogueManagerUpdatePatch
{
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attachThreadId, uint attachToThreadId, bool attach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [DllImport("PowrProf.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    private static bool IsVirtualKeyDown(int virtualKey) =>
        OperatingSystem.IsWindows() && (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static bool IsKeyCurrentlyDown(KeyCode key)
    {
        if (TryGetWindowsVirtualKey(key, out var virtualKey))
            return IsVirtualKeyDown(virtualKey);
        try
        {
            return Input.GetKey(key);
        }
        catch
        {
            return false;
        }
    }

    private static void CaptureHeldRebindingKeys()
    {
        RebindingHeldVirtualKeys.Clear();
        for (var code = (int)KeyCode.Backspace; code < (int)KeyCode.Mouse0; code++)
        {
            if (TryGetWindowsVirtualKey((KeyCode)code, out var virtualKey) && IsVirtualKeyDown(virtualKey))
                RebindingHeldVirtualKeys.Add(virtualKey);
        }
    }

    private static bool TryGetWindowsVirtualKey(KeyCode key, out int virtualKey)
    {
        virtualKey = 0;
        if (!OperatingSystem.IsWindows())
            return false;

        var code = (int)key;
        if (code >= (int)KeyCode.Alpha0 && code <= (int)KeyCode.Alpha9)
        {
            virtualKey = 0x30 + code - (int)KeyCode.Alpha0;
            return true;
        }
        if (code >= (int)KeyCode.A && code <= (int)KeyCode.Z)
        {
            virtualKey = 0x41 + code - (int)KeyCode.A;
            return true;
        }
        if (code >= (int)KeyCode.Keypad0 && code <= (int)KeyCode.Keypad9)
        {
            virtualKey = 0x60 + code - (int)KeyCode.Keypad0;
            return true;
        }
        if (code >= (int)KeyCode.F1 && code <= (int)KeyCode.F15)
        {
            virtualKey = 0x70 + code - (int)KeyCode.F1;
            return true;
        }

        virtualKey = key switch
        {
            KeyCode.Backspace => 0x08,
            KeyCode.Tab => 0x09,
            KeyCode.Clear => 0x0C,
            KeyCode.Return => 0x0D,
            KeyCode.Pause => 0x13,
            KeyCode.Escape => 0x1B,
            KeyCode.Space => 0x20,
            KeyCode.PageUp => 0x21,
            KeyCode.PageDown => 0x22,
            KeyCode.End => 0x23,
            KeyCode.Home => 0x24,
            KeyCode.LeftArrow => 0x25,
            KeyCode.UpArrow => 0x26,
            KeyCode.RightArrow => 0x27,
            KeyCode.DownArrow => 0x28,
            KeyCode.Insert => 0x2D,
            KeyCode.Delete => 0x2E,
            KeyCode.KeypadMultiply => 0x6A,
            KeyCode.KeypadPlus => 0x6B,
            KeyCode.KeypadMinus => 0x6D,
            KeyCode.KeypadPeriod => 0x6E,
            KeyCode.KeypadDivide => 0x6F,
            KeyCode.Numlock => 0x90,
            KeyCode.ScrollLock => 0x91,
            KeyCode.LeftShift => 0xA0,
            KeyCode.RightShift => 0xA1,
            KeyCode.LeftControl => 0xA2,
            KeyCode.RightControl => 0xA3,
            KeyCode.LeftAlt => 0xA4,
            KeyCode.RightAlt => 0xA5,
            KeyCode.Semicolon => 0xBA,
            KeyCode.Equals => 0xBB,
            KeyCode.Comma => 0xBC,
            KeyCode.Minus => 0xBD,
            KeyCode.Period => 0xBE,
            KeyCode.Slash => 0xBF,
            KeyCode.BackQuote => 0xC0,
            KeyCode.LeftBracket => 0xDB,
            KeyCode.Backslash => 0xDC,
            KeyCode.RightBracket => 0xDD,
            KeyCode.Quote => 0xDE,
            _ => 0
        };
        return virtualKey != 0;
    }

    private static void SendVirtualKey(byte virtualKey)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows media keys are only available on Windows.");
        keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        keybd_event(virtualKey, 0, 0x0002, UIntPtr.Zero);
    }

    private static void SendShortcut(params byte[] virtualKeys)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows shortcuts are only available on Windows.");
        foreach (var key in virtualKeys)
            keybd_event(key, 0, 0, UIntPtr.Zero);
        for (var index = virtualKeys.Length - 1; index >= 0; index--)
            keybd_event(virtualKeys[index], 0, 0x0002, UIntPtr.Zero);
    }

    private static void ObserveForegroundWindow()
    {
        if (!OperatingSystem.IsWindows() || Time.unscaledTime < _nextForegroundWindowScanAt)
            return;
        _nextForegroundWindowScanAt = Time.unscaledTime + 0.25f;
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || !IsWindowVisible(window))
            return;
        GetWindowThreadProcessId(window, out var processId);
        if (processId != (uint)Environment.ProcessId)
            _lastExternalForegroundWindow = window;
    }

    private static IntPtr GetControllableWindow()
    {
        var foreground = GetForegroundWindow();
        if (foreground != IntPtr.Zero && IsWindowVisible(foreground))
        {
            GetWindowThreadProcessId(foreground, out var processId);
            if (processId != (uint)Environment.ProcessId)
                return foreground;
        }
        if (_lastExternalForegroundWindow != IntPtr.Zero && IsWindowVisible(_lastExternalForegroundWindow))
            return _lastExternalForegroundWindow;
        return IntPtr.Zero;
    }

}
