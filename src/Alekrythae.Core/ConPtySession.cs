using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace AlekrythaeCore
{
    /// <summary>
    /// Lightweight Windows ConPTY-backed terminal session. PF3 correct HPCON attribute wiring.
    ///
    /// ConPTY uses UTF-8 streams and Unicode CreateProcessW, so paths such as
    /// "📜 Eïs Ųm Ałek’ryŧhæ" are not forced through the legacy OEM code page.
    /// No extra terminal runtime is bundled with Core.
    /// </summary>
    internal sealed class ConPtySession : IDisposable
    {
        private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
        private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        private const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
        private const uint HANDLE_FLAG_INHERIT = 0x00000001;

        private IntPtr _pseudoConsole;
        private IntPtr _attributeList;
        private SafeFileHandle? _inputWrite;
        private SafeFileHandle? _outputRead;
        private FileStream? _inputStream;
        private FileStream? _outputStream;
        private StreamWriter? _writer;
        private StreamReader? _reader;
        private Process? _process;
        private CancellationTokenSource? _readCts;
        private Task? _readTask;
        private bool _disposed;

        public event Action<string>? Output;
        public event Action<int>? Exited;

        public int ProcessId => _process?.Id ?? 0;

        public static ConPtySession Start(string workingDirectory, short columns = 140, short rows = 36)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
                throw new PlatformNotSupportedException("conpty_requires_windows_10_1809");

            var session = new ConPtySession();
            session.StartCore(workingDirectory, columns, rows);
            return session;
        }

        private void StartCore(string workingDirectory, short columns, short rows)
        {
            SafeFileHandle? ptyInputRead = null;
            SafeFileHandle? inputWrite = null;
            SafeFileHandle? outputRead = null;
            SafeFileHandle? ptyOutputWrite = null;

            try
            {
                if (!CreatePipe(out ptyInputRead, out inputWrite, IntPtr.Zero, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe input failed");

                if (!CreatePipe(out outputRead, out ptyOutputWrite, IntPtr.Zero, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe output failed");

                // Parent-owned ends must not leak into the child.
                if (!SetHandleInformation(inputWrite, HANDLE_FLAG_INHERIT, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetHandleInformation input failed");

                if (!SetHandleInformation(outputRead, HANDLE_FLAG_INHERIT, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetHandleInformation output failed");

                COORD size = new(columns, rows);
                int hr = CreatePseudoConsole(
                    size,
                    ptyInputRead,
                    ptyOutputWrite,
                    0,
                    out _pseudoConsole);

                if (hr != 0)
                    Marshal.ThrowExceptionForHR(hr);

                // PF2:
                // The handles passed to CreatePseudoConsole MUST remain alive until
                // the hosted child has been created with CreateProcessW.
                // Closing them before CreateProcessW can make cmd.exe fail startup
                // with 0xC0000142 (STATUS_DLL_INIT_FAILED).
                IntPtr attributeSize = IntPtr.Zero;
                InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeSize);

                _attributeList = Marshal.AllocHGlobal(attributeSize);

                if (!InitializeProcThreadAttributeList(
                        _attributeList,
                        1,
                        0,
                        ref attributeSize))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "InitializeProcThreadAttributeList failed");
                }

                // PF3:
                // PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE expects the HPCON VALUE itself
                // as lpValue, not a pointer to a memory cell containing that handle.
                //
                // Native Microsoft sample:
                // UpdateProcThreadAttribute(..., PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                //                           hPC, sizeof(HPCON), ...);
                //
                // _pseudoConsole is already an IntPtr/HPCON, so pass it directly.
                if (!UpdateProcThreadAttribute(
                        _attributeList,
                        0,
                        (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                        _pseudoConsole,
                        (IntPtr)IntPtr.Size,
                        IntPtr.Zero,
                        IntPtr.Zero))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "UpdateProcThreadAttribute failed");
                }

                STARTUPINFOEX si = new();
                si.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
                si.lpAttributeList = _attributeList;

                string comSpec =
                    Environment.GetEnvironmentVariable("ComSpec")
                    ?? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System),
                        "cmd.exe");

                string commandLine = "\"" + comSpec + "\" /Q /D";

                PROCESS_INFORMATION pi;

                bool created = CreateProcessW(
                    null,
                    new StringBuilder(commandLine),
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT,
                    IntPtr.Zero,
                    workingDirectory,
                    ref si,
                    out pi);

                if (!created)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessW cmd.exe failed");

                // Child is now attached to the pseudoconsole.
                // Close our temporary ends only AFTER CreateProcessW succeeds.
                ptyInputRead.Dispose();
                ptyInputRead = null;
                ptyOutputWrite.Dispose();
                ptyOutputWrite = null;

                try
                {
                    _process = Process.GetProcessById((int)pi.dwProcessId);
                    _process.EnableRaisingEvents = true;
                    _process.Exited += (_, _) =>
                    {
                        int code = -1;
                        try { code = _process.ExitCode; } catch { }
                        Exited?.Invoke(code);
                    };
                }
                finally
                {
                    if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
                    if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
                }

                _inputWrite = inputWrite;
                inputWrite = null;
                _outputRead = outputRead;
                outputRead = null;

                // CreatePipe returns synchronous (non-overlapped) handles.
                // Marking them async makes FileStream throw:
                // "Handle does not support asynchronous operations".
                _inputStream = new FileStream(
                    _inputWrite,
                    FileAccess.Write,
                    4096,
                    isAsync: false);

                _outputStream = new FileStream(
                    _outputRead,
                    FileAccess.Read,
                    4096,
                    isAsync: false);

                var utf8 = new UTF8Encoding(false, false);

                _writer = new StreamWriter(
                    _inputStream,
                    utf8,
                    4096,
                    leaveOpen: true)
                {
                    AutoFlush = true,
                    NewLine = "\r\n"
                };

                _reader = new StreamReader(
                    _outputStream,
                    utf8,
                    detectEncodingFromByteOrderMarks: false,
                    4096,
                    leaveOpen: true);

                _readCts = new CancellationTokenSource();

                // Blocking pipe read runs on one dedicated background thread.
                // This avoids fake async/overlapped assumptions while keeping UI free.
                _readTask = Task.Factory.StartNew(
                    () => ReadLoop(_readCts.Token),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }
            catch
            {
                ptyInputRead?.Dispose();
                inputWrite?.Dispose();
                outputRead?.Dispose();
                ptyOutputWrite?.Dispose();
                Dispose();
                throw;
            }
        }

        public void Write(string text)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ConPtySession));

            if (_writer == null)
                throw new InvalidOperationException("conpty_writer_not_ready");

            _writer.Write(text);
            _writer.Flush();
        }

        public void Resize(short columns, short rows)
        {
            if (_disposed || _pseudoConsole == IntPtr.Zero)
                return;

            if (columns < 20) columns = 20;
            if (rows < 5) rows = 5;

            int hr = ResizePseudoConsole(_pseudoConsole, new COORD(columns, rows));
            if (hr != 0)
                Marshal.ThrowExceptionForHR(hr);
        }

        private void ReadLoop(CancellationToken token)
        {
            char[] buffer = new char[4096];

            try
            {
                while (!token.IsCancellationRequested && _reader != null)
                {
                    int read = _reader.Read(buffer, 0, buffer.Length);

                    if (read <= 0)
                        break;

                    Output?.Invoke(new string(buffer, 0, read));
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (IOException)
            {
                // Pipe closes during normal shell shutdown/dispose.
            }
            catch (Exception ex)
            {
                Output?.Invoke("\r\n[ConPTY read error: " + ex.Message + "]\r\n");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try { _readCts?.Cancel(); } catch { }

            try
            {
                if (_process != null && !_process.HasExited)
                    _process.Kill(true);
            }
            catch { }

            try { _writer?.Dispose(); } catch { }
            try { _reader?.Dispose(); } catch { }
            try { _inputStream?.Dispose(); } catch { }
            try { _outputStream?.Dispose(); } catch { }
            try { _inputWrite?.Dispose(); } catch { }
            try { _outputRead?.Dispose(); } catch { }

            if (_attributeList != IntPtr.Zero)
            {
                try { DeleteProcThreadAttributeList(_attributeList); } catch { }
                try { Marshal.FreeHGlobal(_attributeList); } catch { }
                _attributeList = IntPtr.Zero;
            }

            if (_pseudoConsole != IntPtr.Zero)
            {
                try { ClosePseudoConsole(_pseudoConsole); } catch { }
                _pseudoConsole = IntPtr.Zero;
            }

            try { _process?.Dispose(); } catch { }
            try { _readCts?.Dispose(); } catch { }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct COORD
        {
            public short X;
            public short Y;

            public COORD(short x, short y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string? lpReserved;
            public string? lpDesktop;
            public string? lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STARTUPINFOEX
        {
            public STARTUPINFO StartupInfo;
            public IntPtr lpAttributeList;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CreatePipe(
            out SafeFileHandle hReadPipe,
            out SafeFileHandle hWritePipe,
            IntPtr lpPipeAttributes,
            int nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetHandleInformation(
            SafeFileHandle hObject,
            uint dwMask,
            uint dwFlags);

        [DllImport("kernel32.dll")]
        private static extern int CreatePseudoConsole(
            COORD size,
            SafeFileHandle hInput,
            SafeFileHandle hOutput,
            uint dwFlags,
            out IntPtr phPC);

        [DllImport("kernel32.dll")]
        private static extern int ResizePseudoConsole(
            IntPtr hPC,
            COORD size);

        [DllImport("kernel32.dll")]
        private static extern void ClosePseudoConsole(IntPtr hPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool InitializeProcThreadAttributeList(
            IntPtr lpAttributeList,
            int dwAttributeCount,
            int dwFlags,
            ref IntPtr lpSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UpdateProcThreadAttribute(
            IntPtr lpAttributeList,
            uint dwFlags,
            IntPtr attribute,
            IntPtr lpValue,
            IntPtr cbSize,
            IntPtr lpPreviousValue,
            IntPtr lpReturnSize);

        [DllImport("kernel32.dll")]
        private static extern void DeleteProcThreadAttributeList(
            IntPtr lpAttributeList);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessW(
            string? lpApplicationName,
            StringBuilder lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFOEX lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}
