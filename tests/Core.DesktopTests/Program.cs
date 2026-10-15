using System.IO;
using System.Text.Json;
using System.Windows;
using AlekrythaeCore;

namespace CoreDesktopTests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string? fixture = null;
        try
        {
            bool child = args.Length == 3 && args[0] == "--child";
            if (!child && !(args.Length == 2 && args[0] == "--run"))
                throw new ArgumentException("Use --run <isolated native-smoke.alek fixture>.");
            fixture = ValidateFixture(args[^1]);
            if (child) return TestApplication.Run(args[1], fixture);
            string inputBefore = NativeDesktop.InputName();
            using var desktop = new NativeDesktop();
            if (desktop.Name == inputBefore) throw new InvalidOperationException("Test desktop must be separate.");
            int result = IsolatedProcess.Run(desktop.Name, fixture, FixtureOptions.TimeoutSeconds(fixture));
            string inputAfter = NativeDesktop.InputName();
            Write(fixture, "host-isolation.json", new
            {
                protocol = "joa-private-desktop-v1", testDesktop = desktop.Name,
                inputBefore, inputAfter, inputUnchanged = inputBefore == inputAfter,
                productionBootstrap = false, desktopSwitch = false, exitCode = result
            });
            if (inputBefore != inputAfter) throw new InvalidOperationException("Input desktop changed during the test.");
            return result;
        }
        catch (Exception ex)
        {
            if (fixture != null) Write(fixture, "host-error.json", new { error = ex.ToString() });
            return 1; // No message boxes and no fallback onto the user's desktop.
        }
    }

    private static string ValidateFixture(string path)
    {
        string full = Path.GetFullPath(path);
        var directory = new DirectoryInfo(Path.GetDirectoryName(full)!);
        if (!File.Exists(full) || Path.GetFileName(full) != "native-smoke.alek" ||
            directory.Parent?.Name != "test-output" || !directory.Name.StartsWith("native-", StringComparison.Ordinal) ||
            directory.Name.Length != 15 || !directory.Name[7..].All(Uri.IsHexDigit))
            throw new ArgumentException("Only synthetic test-output/native-<8 hex>/native-smoke.alek fixtures are allowed.");
        return full;
    }

    internal static void Write(string fixture, string name, object value) =>
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixture)!, name), JsonSerializer.Serialize(value));
}
