using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace CoreDesktopTests;

internal sealed class NativeDesktop : IDisposable
{
    internal const string Prefix = "JOA-Test-";
    private readonly nint _handle;
    internal string Name { get; } = Prefix + Guid.NewGuid().ToString("N");

    internal NativeDesktop()
    {
        // Read/write/create/enumerate rights only; never request SWITCHDESKTOP.
        _handle = CreateDesktopW(Name, null, 0, 0, 0xC3, 0);
        if (_handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    internal static string CurrentThreadName() => NameOf(GetThreadDesktop(GetCurrentThreadId()));

    internal static string InputName()
    {
        nint input = OpenInputDesktop(0, false, 1);
        if (input == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try { return NameOf(input); }
        finally { CloseDesktop(input); }
    }

    private static string NameOf(nint handle)
    {
        var name = new StringBuilder(256);
        if (!GetUserObjectInformationW(handle, 2, name, name.Capacity * 2, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return name.ToString();
    }

    public void Dispose() => CloseDesktop(_handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateDesktopW(string name, string? device, nint mode, uint flags, uint access, nint security);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformationW(nint handle, int index, StringBuilder value, int length, out int needed);
    [DllImport("user32.dll")]
    private static extern nint GetThreadDesktop(uint threadId);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
