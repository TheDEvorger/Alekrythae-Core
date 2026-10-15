using System.IO;
using System.Text.Json;

namespace CoreDesktopTests;

internal static class FixtureOptions
{
    internal static int TimeoutSeconds(string fixture)
    {
        string path = Path.Combine(Path.GetDirectoryName(fixture)!, "test-host-options.json");
        if (!File.Exists(path)) return 75;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("timeoutSeconds", out var value) ||
            !value.TryGetInt32(out int seconds) || seconds < 30 || seconds > 900)
            throw new InvalidDataException("Test timeout must be between 30 and 900 seconds.");
        return seconds;
    }
}
