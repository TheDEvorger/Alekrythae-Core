using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace CoreDesktopTests;

internal static class IsolatedProcess
{
    internal static int Run(string desktop, string fixture, int timeoutSeconds)
    {
        nint job = ProcessNative.CreateJobObjectW(0, null);
        if (job == 0) throw LastError();
        ProcessNative.ProcessInfo child = default;
        try
        {
            var limits = ProcessNative.ExtendedLimit.KillOnClose();
            if (!ProcessNative.SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ProcessNative.ExtendedLimit>()))
                throw LastError();
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing test executable.");
            // Paths are canonical Windows filenames (no quotes/newlines); desktop is a generated GUID name.
            var command = new StringBuilder($"\"{executable}\" --child {desktop} \"{fixture}\"");
            var startup = new ProcessNative.StartupInfo
            {
                Size = Marshal.SizeOf<ProcessNative.StartupInfo>(), Desktop = desktop
            };
            if (!ProcessNative.CreateProcessW(executable, command, 0, 0, false, 4, 0,
                AppContext.BaseDirectory, ref startup, out child)) throw LastError();
            if (!ProcessNative.AssignProcessToJobObject(job, child.Process))
            {
                ProcessNative.TerminateProcess(child.Process, 1);
                throw LastError();
            }
            if (ProcessNative.ResumeThread(child.Thread) == uint.MaxValue) throw LastError();
            uint wait = ProcessNative.WaitForSingleObject(child.Process, checked((uint)(timeoutSeconds + 15) * 1000));
            if (wait == 0x102)
            {
                ProcessNative.TerminateJobObject(job, 124);
                throw new TimeoutException("Isolated WebView2 test timed out; its job was terminated.");
            }
            if (wait != 0 || !ProcessNative.GetExitCodeProcess(child.Process, out uint exitCode)) throw LastError();
            return checked((int)exitCode);
        }
        finally
        {
            // This also terminates any WebView2 child left after application shutdown.
            ProcessNative.CloseHandle(job);
            if (child.Thread != 0) ProcessNative.CloseHandle(child.Thread);
            if (child.Process != 0) ProcessNative.CloseHandle(child.Process);
        }
    }

    private static Win32Exception LastError() => new(Marshal.GetLastWin32Error());
}
