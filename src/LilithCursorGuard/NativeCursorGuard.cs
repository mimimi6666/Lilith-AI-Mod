using System.Diagnostics;
using System.Runtime.InteropServices;
using BepInEx.Logging;

namespace LilithCursorGuard;

internal static class NativeCursorGuard
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ClipCursorCall(nint rectangle);

    private static readonly object InstallGate = new();
    private static readonly ClipCursorCall Callback = Hook;
    private static ClipCursorCall? _original;
    private static ManualLogSource? _log;
    private static uint _processId;
    private static long _blocked;
    private static long _lastReport;

    internal static CursorHookInstallResult Install(ManualLogSource log)
    {
        lock (InstallGate)
        {
            if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
                throw new PlatformNotSupportedException();
            nint module = GetModuleHandleW("UnityPlayer.dll");
            if (module == 0 || !K32GetModuleInformation(GetCurrentProcess(), module, out ModuleInfo info,
                    (uint)Marshal.SizeOf<ModuleInfo>()) || info.BaseAddress != module)
                throw new InvalidOperationException();
            var image = new PeImportReader(info.SizeOfImage, (rva, length) => ReadImage(module, rva, length));
            uint slotRva = image.FindClipCursorSlot() ?? throw new MissingMethodException();
            nint address = module + checked((int)slotRva);
            nint original = GetProcAddress(GetModuleHandleW("user32.dll"), "ClipCursor");
            if (original == 0 || (long)address % IntPtr.Size != 0) throw new InvalidOperationException();
            // Resolve and root everything before publishing the native pointer.
            _original ??= Marshal.GetDelegateForFunctionPointer<ClipCursorCall>(original);
            _log = log;
            _processId = GetCurrentProcessId();
            return CursorHookTransaction.Install(new NativeSlot(address), original,
                Marshal.GetFunctionPointerForDelegate(Callback));
        }
    }

    private static byte[] ReadImage(nint module, uint rva, int length)
    {
        var bytes = new byte[length];
        // Read our own process only. Unreadable mapped gaps return failure, not an unsafe dereference.
        if (!ReadProcessMemory(GetCurrentProcess(), module + checked((int)rva), bytes,
                (nuint)length, out nuint read) || read != (nuint)length)
            throw new InvalidDataException("Loaded image is unreadable.");
        return bytes;
    }

    private static int Hook(nint rectangle)
    {
        try
        {
            nint foreground = GetForegroundWindow();
            uint owner = 0;
            if (foreground != 0) GetWindowThreadProcessId(foreground, out owner);
            if (CursorPolicy.ShouldPassThrough(owner, _processId))
                return _original!(rectangle); // Original pointer and rectangle, including NULL.

            long count = Interlocked.Increment(ref _blocked);
            long now = Stopwatch.GetTimestamp();
            long previous = Interlocked.Read(ref _lastReport);
            if (count <= 5 || (now - previous >= Stopwatch.Frequency * 60 &&
                    Interlocked.CompareExchange(ref _lastReport, now, previous) == previous))
            {
                Interlocked.Exchange(ref _lastReport, now);
                // Never inspect RECT, foreground titles/process names, coordinates or user input.
                _log?.LogInfo($"cursor_guard.blocked: total={count}");
            }
            return 1;
        }
        catch
        {
            return 1; // Do not let a managed exception unwind across Unity's native callback.
        }
    }

    private sealed class NativeSlot : ICursorImportSlot
    {
        private readonly nint _address;
        internal NativeSlot(nint address) => _address = address;
        public nint Read()
        {
            var bytes = new byte[IntPtr.Size];
            if (!ReadProcessMemory(GetCurrentProcess(), _address, bytes, (nuint)bytes.Length, out nuint read) ||
                read != (nuint)bytes.Length) throw new InvalidDataException("Import slot is unreadable.");
            return (nint)BitConverter.ToInt64(bytes);
        }
        public bool MakeWritable(out uint previousProtection)
        {
            previousProtection = 0;
            if (VirtualQuery(_address, out MemoryInfo memory, (nuint)Marshal.SizeOf<MemoryInfo>()) == 0 ||
                memory.State != 0x1000 || memory.Type != 0x1000000 ||
                memory.Protect is not (0x02 or 0x04 or 0x08)) return false;
            return VirtualProtect(_address, (nuint)IntPtr.Size, 0x04, out previousProtection);
        }
        public unsafe nint CompareExchange(nint value, nint expected) =>
            Interlocked.CompareExchange(ref *(nint*)_address, value, expected);
        public bool RestoreProtection(uint protection) =>
            VirtualProtect(_address, (nuint)IntPtr.Size, protection, out _);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ModuleInfo
    {
        public nint BaseAddress;
        public uint SizeOfImage;
        public nint EntryPoint;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryInfo
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string name);
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Ansi)]
    private static extern nint GetProcAddress(nint module, string name);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool K32GetModuleInformation(nint process, nint module, out ModuleInfo info, uint size);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(nint process, nint address, [Out] byte[] bytes, nuint size, out nuint read);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualProtect(nint address, nuint size, uint protection, out uint oldProtection);
    [DllImport("kernel32.dll")]
    private static extern nuint VirtualQuery(nint address, out MemoryInfo info, nuint length);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
