using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LilithRuntime;

internal sealed class WindowsJobObject : IDisposable
{
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int JobObjectBasicAccountingInformationClass = 1;
    private const int JobObjectExtendedLimitInformationClass = 9;
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint StartfUseStdHandles = 0x00000100;
    private const uint HandleFlagInherit = 0x00000001;
    private const uint ResumeFailed = uint.MaxValue;

    private readonly SafeFileHandle _handle;

    private WindowsJobObject(SafeFileHandle handle)
    {
        _handle = handle;
    }

    internal static WindowsJobProcess StartProcess(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Hermes Runtime process containment requires Windows.");
        if (startInfo.UseShellExecute
            || !startInfo.RedirectStandardOutput
            || !startInfo.RedirectStandardError
            || string.IsNullOrWhiteSpace(startInfo.FileName)
            || !Path.IsPathFullyQualified(startInfo.FileName)
            || string.IsNullOrWhiteSpace(startInfo.WorkingDirectory)
            || !Path.IsPathFullyQualified(startInfo.WorkingDirectory)
            || !string.IsNullOrEmpty(startInfo.Arguments))
        {
            throw new ArgumentException(
                "A direct absolute process with redirected output, an absolute working directory, and ArgumentList is required.",
                nameof(startInfo));
        }

        var jobHandle = CreateJobObject(IntPtr.Zero, null);
        if (jobHandle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            jobHandle.Dispose();
            throw new Win32Exception(error);
        }

        var job = new WindowsJobObject(jobHandle);
        SafeFileHandle? standardOutputRead = null;
        SafeFileHandle? standardOutputWrite = null;
        SafeFileHandle? standardErrorRead = null;
        SafeFileHandle? standardErrorWrite = null;
        SafeFileHandle? standardInputRead = null;
        SafeFileHandle? standardInputWrite = null;
        IntPtr environment = IntPtr.Zero;
        var processInformation = default(ProcessInformation);
        Process? process = null;
        try
        {
            job.ConfigureKillOnClose();
            CreateRedirectPipe(out standardOutputRead, out standardOutputWrite);
            CreateRedirectPipe(out standardErrorRead, out standardErrorWrite);
            CreateInputPipe(out standardInputRead, out standardInputWrite);

            var startupInfo = new StartupInfo
            {
                Size = Marshal.SizeOf<StartupInfo>(),
                Flags = StartfUseStdHandles,
                StandardInput = standardInputRead.DangerousGetHandle(),
                StandardOutput = standardOutputWrite.DangerousGetHandle(),
                StandardError = standardErrorWrite.DangerousGetHandle()
            };
            environment = BuildEnvironmentBlock(startInfo.Environment);
            var commandLine = new StringBuilder(BuildCommandLine(startInfo));
            var creationFlags = CreateSuspended | CreateUnicodeEnvironment;
            if (startInfo.CreateNoWindow)
                creationFlags |= CreateNoWindow;

            if (!CreateProcess(
                    startInfo.FileName,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    inheritHandles: true,
                    creationFlags,
                    environment,
                    startInfo.WorkingDirectory,
                    ref startupInfo,
                    out processInformation))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            // The primary thread is still suspended here. Neither Hermes nor
            // any descendant can run until containment and KILL_ON_JOB_CLOSE
            // have both succeeded.
            if (!AssignProcessToJobObject(jobHandle, processInformation.Process))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            process = Process.GetProcessById(checked((int)processInformation.ProcessId));
            process.EnableRaisingEvents = true;
            if (ResumeThread(processInformation.Thread) == ResumeFailed)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            standardOutputWrite.Dispose();
            standardOutputWrite = null;
            standardErrorWrite.Dispose();
            standardErrorWrite = null;
            standardInputRead.Dispose();
            standardInputRead = null;
            standardInputWrite.Dispose();
            standardInputWrite = null;

            var output = new StreamReader(
                new FileStream(
                    standardOutputRead,
                    FileAccess.Read,
                    bufferSize: 4096,
                    isAsync: false),
                startInfo.StandardOutputEncoding ?? Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            standardOutputRead = null;
            var error = new StreamReader(
                new FileStream(
                    standardErrorRead,
                    FileAccess.Read,
                    bufferSize: 4096,
                    isAsync: false),
                startInfo.StandardErrorEncoding ?? Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            standardErrorRead = null;
            return new WindowsJobProcess(job, process, output, error);
        }
        catch
        {
            if (processInformation.Process != IntPtr.Zero)
            {
                _ = TerminateProcess(processInformation.Process, 1);
                _ = WaitForSingleObject(processInformation.Process, 5_000);
            }
            try
            {
                if (job.ActiveProcessCount != 0)
                {
                    job.TerminateAndWaitAsync(
                            exitCode: 1,
                            timeout: TimeSpan.FromSeconds(5),
                            CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }
            }
            catch
            {
                // Preserve the launch failure. Closing KILL_ON_JOB_CLOSE below
                // is the final fail-closed containment action.
            }
            process?.Dispose();
            job.Dispose();
            throw;
        }
        finally
        {
            if (environment != IntPtr.Zero)
                Marshal.ZeroFreeGlobalAllocUnicode(environment);
            if (processInformation.Thread != IntPtr.Zero)
                _ = CloseHandle(processInformation.Thread);
            if (processInformation.Process != IntPtr.Zero)
                _ = CloseHandle(processInformation.Process);
            standardOutputRead?.Dispose();
            standardOutputWrite?.Dispose();
            standardErrorRead?.Dispose();
            standardErrorWrite?.Dispose();
            standardInputRead?.Dispose();
            standardInputWrite?.Dispose();
        }
    }

    internal uint ActiveProcessCount
    {
        get
        {
            ThrowIfClosed();
            var information = new JobObjectBasicAccountingInformation();
            var length = Marshal.SizeOf<JobObjectBasicAccountingInformation>();
            if (!QueryInformationJobObject(
                    _handle,
                    JobObjectBasicAccountingInformationClass,
                    ref information,
                    (uint)length,
                    out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            return information.ActiveProcesses;
        }
    }

    internal async Task TerminateAndWaitAsync(
        uint exitCode,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ThrowIfClosed();
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        if (!TerminateJobObject(_handle, exitCode))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (ActiveProcessCount != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    "The Hermes Windows job did not reach ACTIVE_PROCESS_ZERO.");
            }
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose() => _handle.Dispose();

    private void ConfigureKillOnClose()
    {
        var information = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };
        var length = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var pointer = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(information, pointer, false);
            if (!SetInformationJobObject(
                    _handle,
                    JobObjectExtendedLimitInformationClass,
                    pointer,
                    (uint)length))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static void CreateRedirectPipe(
        out SafeFileHandle parentHandle,
        out SafeFileHandle childHandle)
    {
        var attributes = new SecurityAttributes
        {
            Length = Marshal.SizeOf<SecurityAttributes>(),
            InheritHandle = true
        };
        if (!CreatePipe(
                out parentHandle,
                out childHandle,
                ref attributes,
                size: 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        if (!SetHandleInformation(
                parentHandle,
                HandleFlagInherit,
                flags: 0))
        {
            var error = Marshal.GetLastWin32Error();
            parentHandle.Dispose();
            childHandle.Dispose();
            throw new Win32Exception(error);
        }
    }

    private static void CreateInputPipe(
        out SafeFileHandle childHandle,
        out SafeFileHandle parentHandle)
    {
        var attributes = new SecurityAttributes
        {
            Length = Marshal.SizeOf<SecurityAttributes>(),
            InheritHandle = true
        };
        if (!CreatePipe(
                out childHandle,
                out parentHandle,
                ref attributes,
                size: 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        if (!SetHandleInformation(
                parentHandle,
                HandleFlagInherit,
                flags: 0))
        {
            var error = Marshal.GetLastWin32Error();
            childHandle.Dispose();
            parentHandle.Dispose();
            throw new Win32Exception(error);
        }
    }

    private static string BuildCommandLine(ProcessStartInfo startInfo)
    {
        var builder = new StringBuilder();
        AppendQuotedArgument(builder, startInfo.FileName);
        foreach (var argument in startInfo.ArgumentList)
        {
            builder.Append(' ');
            AppendQuotedArgument(builder, argument);
        }
        return builder.ToString();
    }

    private static void AppendQuotedArgument(StringBuilder builder, string value)
    {
        if (value.IndexOf('\0') >= 0)
            throw new ArgumentException("Process arguments must not contain NUL.");
        builder.Append('"');
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }
            if (character == '"')
            {
                builder.Append('\\', checked(backslashes * 2 + 1));
                builder.Append('"');
                backslashes = 0;
                continue;
            }
            builder.Append('\\', backslashes);
            backslashes = 0;
            builder.Append(character);
        }
        builder.Append('\\', checked(backslashes * 2));
        builder.Append('"');
    }

    private static IntPtr BuildEnvironmentBlock(
        IDictionary<string, string?> environment)
    {
        var builder = new StringBuilder();
        foreach (var entry in environment.OrderBy(
                     static item => item.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (entry.Key.IndexOf('\0') >= 0
                || entry.Value?.IndexOf('\0') >= 0)
            {
                throw new ArgumentException(
                    "Process environment entries must not contain NUL.");
            }
            builder.Append(entry.Key);
            builder.Append('=');
            builder.Append(entry.Value ?? string.Empty);
            builder.Append('\0');
        }
        if (builder.Length == 0)
            builder.Append('\0');
        builder.Append('\0');
        return Marshal.StringToHGlobalUni(builder.ToString());
    }

    private void ThrowIfClosed()
    {
        if (_handle.IsInvalid || _handle.IsClosed)
            throw new ObjectDisposedException(nameof(WindowsJobObject));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public uint Flags;
        public short ShowWindow;
        public short Reserved2Size;
        public IntPtr Reserved2;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicAccountingInformation
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(
        IntPtr jobAttributes,
        string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle job,
        int informationClass,
        IntPtr information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(
        SafeFileHandle job,
        int informationClass,
        ref JobObjectBasicAccountingInformation information,
        uint informationLength,
        out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(
        SafeFileHandle job,
        IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(
        SafeFileHandle job,
        uint exitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(
        out SafeFileHandle readPipe,
        out SafeFileHandle writePipe,
        ref SecurityAttributes pipeAttributes,
        uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(
        SafeFileHandle handle,
        uint mask,
        uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}

internal sealed class WindowsJobProcess : IDisposable
{
    internal WindowsJobProcess(
        WindowsJobObject job,
        Process process,
        StreamReader standardOutput,
        StreamReader standardError)
    {
        Job = job;
        Process = process;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    internal WindowsJobObject Job { get; }
    internal Process Process { get; }
    internal StreamReader StandardOutput { get; }
    internal StreamReader StandardError { get; }

    public void Dispose()
    {
        StandardOutput.Dispose();
        StandardError.Dispose();
        Process.Dispose();
        Job.Dispose();
    }
}
