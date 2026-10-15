using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace CoreDesktopTests;

internal static class WindowNative
{
    internal static string DesktopName(uint thread)
    {
        var name = new StringBuilder(256);
        if (!GetUserObjectInformationW(GetThreadDesktop(thread), 2, name, name.Capacity * 2, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return name.ToString();
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")]
    private static extern nint GetThreadDesktop(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformationW(nint handle, int index, StringBuilder value, int length, out int needed);
}
