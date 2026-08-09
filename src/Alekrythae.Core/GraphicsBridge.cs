using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace AlekrythaeCore
{
    internal static class GraphicsBridge
    {
        private const string RegistryPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
        private static readonly string ConfigFolder = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "KozmikData",
            "Graphics");
        private static readonly string ConfigPath = Path.Combine(ConfigFolder, "graphics.json");

        private sealed class GraphicsConfig
        {
            public int SchemaVersion { get; set; } = 5;
            public string Preference { get; set; } = "system";
            public string SelectedAdapterId { get; set; } = string.Empty;
            public bool UserExplicitlySelected { get; set; }
        }

        private sealed class AdapterInfo
        {
            public string id { get; set; } = string.Empty;
            public string name { get; set; } = string.Empty;
            public string description { get; set; } = string.Empty;
            public string kind { get; set; } = string.Empty;
            public bool active { get; set; }
            public bool primary { get; set; }
            public string deviceName { get; set; } = string.Empty;
            public string deviceId { get; set; } = string.Empty;
            public string driverVersion { get; set; } = string.Empty;
            public string source { get; set; } = string.Empty;
            public long memoryMb { get; set; }
        }

        [Flags]
        private enum DisplayDeviceStateFlags : int
        {
            AttachedToDesktop = 0x00000001,
            MultiDriver = 0x00000002,
            PrimaryDevice = 0x00000004,
            MirroringDriver = 0x00000008,
            VgaCompatible = 0x00000010,
            Removable = 0x00000020,
            ModesPruned = 0x08000000,
            Remote = 0x04000000,
            Disconnect = 0x02000000
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public DisplayDeviceStateFlags StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplayDevices(
            string? lpDevice,
            uint iDevNum,
            ref DISPLAY_DEVICE lpDisplayDevice,
            uint dwFlags);

        public static bool TryHandleApi(string op, JsonElement payload, out object result)
        {
            switch (op)
            {
                case "listGraphicsAdapters":
                {
                    List<AdapterInfo> adapters = EnumerateAdapters();
                    result = new
                    {
                        ok = true,
                        adapters,
                        config = LoadConfig(),
                        exactAdapterSelection = false,
                        selectionMode = "windows-preference-class",
                        detectionSources = adapters.Select(x => x.source).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray()
                    };
                    return true;
                }

                case "setGraphicsPreference":
                    result = ApplyPreference(payload);
                    return true;

                case "openWindowsGraphicsSettings":
                    result = OpenWindowsGraphicsSettings();
                    return true;

                case "getGraphicsPreference":
                    result = new { ok = true, config = LoadConfig() };
                    return true;

                default:
                    result = new { ok = false, error = "unknown_graphics_op" };
                    return false;
            }
        }

        public static string GetAdditionalBrowserArguments()
        {
            GraphicsConfig config = LoadConfig();
            return NormalizePreference(config.Preference) switch
            {
                "software" => "--use-angle=swiftshader --enable-webgl --ignore-gpu-blocklist",
                // Donanım seçimi tek kaynak olarak Windows'un uygulama başına
                // GPU tercihinde tutulur. Aynı seçimi Chromium bayrağıyla ikinci
                // kez zorlamak hibrit sistemlerde gereksiz adaptör geçişi yaratır.
                "high-performance" => string.Empty,
                "power-saving" => string.Empty,
                _ => string.Empty
            };
        }

        public static void ApplyPersistedWindowsPreference()
        {
            try
            {
                GraphicsConfig config = LoadConfig();
                WriteWindowsPreference(NormalizePreference(config.Preference));
            }
            catch
            {
                // GPU tercihi Core'un açılmasını hiçbir zaman engellememeli.
            }
        }

        private static object ApplyPreference(JsonElement payload)
        {
            string preference = "system";
            string adapterId = string.Empty;
            if (payload.ValueKind == JsonValueKind.Object)
            {
                if (payload.TryGetProperty("preference", out JsonElement p))
                    preference = p.GetString() ?? "system";
                if (payload.TryGetProperty("adapterId", out JsonElement a))
                    adapterId = a.GetString() ?? string.Empty;
            }

            List<AdapterInfo> adapters = EnumerateAdapters();
            AdapterInfo? selected = adapters.FirstOrDefault(item =>
                string.Equals(item.id, adapterId, StringComparison.OrdinalIgnoreCase));

            // Windows, uygulama başına tam GPU kimliği yerine güç sınıfı uygular.
            // Kullanıcı listeden NVIDIA/AMD ayrık kartı seçerse yüksek performans,
            // Intel/entegre kartı seçerse güç tasarrufu sınıfını otomatik eşleştir.
            if (selected != null && !string.Equals(preference, "software", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(selected.kind, "discrete", StringComparison.OrdinalIgnoreCase))
                    preference = "high-performance";
                else if (string.Equals(selected.kind, "integrated", StringComparison.OrdinalIgnoreCase))
                    preference = "power-saving";
            }

            preference = NormalizePreference(preference);
            GraphicsConfig config = new()
            {
                SchemaVersion = 5,
                Preference = preference,
                SelectedAdapterId = adapterId,
                UserExplicitlySelected = true
            };
            SaveConfig(config);
            WriteWindowsPreference(preference);

            string selectedName = selected?.name ?? string.Empty;
            string message = preference switch
            {
                "power-saving" => string.IsNullOrWhiteSpace(selectedName)
                    ? "Güç tasarrufu GPU tercihi kaydedildi. Core'u kapatıp yeniden aç."
                    : $"{selectedName} için güç tasarrufu sınıfı kaydedildi. Core'u kapatıp yeniden aç.",
                "high-performance" => string.IsNullOrWhiteSpace(selectedName)
                    ? "Yüksek performans GPU tercihi kaydedildi. Core'u kapatıp yeniden aç."
                    : $"{selectedName} için yüksek performans sınıfı kaydedildi. Core'u kapatıp yeniden aç.",
                "software" => "Yazılım tabanlı güvenli WebGL çizimi kaydedildi. Core'u kapatıp yeniden aç.",
                _ => "GPU tercihi Windows yönetimine bırakıldı. Core'u kapatıp yeniden aç."
            };

            return new
            {
                ok = true,
                message,
                restartRequired = true,
                preference,
                selectedAdapterId = adapterId,
                selectedAdapterName = selectedName,
                exactAdapterSelection = false,
                selectionMode = "windows-preference-class"
            };
        }

        private static object OpenWindowsGraphicsSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:display-advancedgraphics")
                {
                    UseShellExecute = true
                });
                return new { ok = true };
            }
            catch (Exception first)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("ms-settings:display")
                    {
                        UseShellExecute = true
                    });
                    return new { ok = true };
                }
                catch (Exception second)
                {
                    return new { ok = false, error = second.Message, detail = first.Message };
                }
            }
        }

        private static List<AdapterInfo> EnumerateAdapters()
        {
            var adapters = new List<AdapterInfo>();

            MergeAdapters(adapters, EnumerateAdaptersViaPowerShell());
            MergeAdapters(adapters, EnumerateAdaptersViaRegistry());
            MergeAdapters(adapters, EnumerateAdaptersViaDisplayDevices());

            return adapters
                .Where(x => !string.IsNullOrWhiteSpace(x.name))
                .OrderByDescending(x => x.primary)
                .ThenByDescending(x => x.active)
                .ThenByDescending(x => string.Equals(x.kind, "discrete", StringComparison.OrdinalIgnoreCase))
                .ThenBy(x => x.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<AdapterInfo> EnumerateAdaptersViaPowerShell()
        {
            var result = new List<AdapterInfo>();
            try
            {
                const string script = "$ErrorActionPreference='SilentlyContinue';" +
                    "$items=@(Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue | Select-Object Name,PNPDeviceID,AdapterRAM,Status,Availability,VideoProcessor,DriverVersion);" +
                    "if($items.Count -eq 0){$items=@(Get-WmiObject Win32_VideoController -ErrorAction SilentlyContinue | Select-Object Name,PNPDeviceID,AdapterRAM,Status,Availability,VideoProcessor,DriverVersion)};" +
                    "ConvertTo-Json -InputObject $items -Compress -Depth 3";

                string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                psi.ArgumentList.Add("-NoLogo");
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-NonInteractive");
                psi.ArgumentList.Add("-EncodedCommand");
                psi.ArgumentList.Add(encoded);

                using Process? process = Process.Start(psi);
                if (process == null) return result;
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(7000))
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    return result;
                }

                string output = outputTask.GetAwaiter().GetResult().Trim();
                _ = errorTask.GetAwaiter().GetResult();
                if (string.IsNullOrWhiteSpace(output) || output == "null") return result;

                using JsonDocument doc = JsonDocument.Parse(output);
                IEnumerable<JsonElement> entries = doc.RootElement.ValueKind switch
                {
                    JsonValueKind.Array => doc.RootElement.EnumerateArray(),
                    JsonValueKind.Object => new[] { doc.RootElement },
                    _ => Array.Empty<JsonElement>()
                };

                foreach (JsonElement entry in entries)
                {
                    string name = ReadJsonString(entry, "Name");
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    string pnpId = ReadJsonString(entry, "PNPDeviceID");
                    string status = ReadJsonString(entry, "Status");
                    long availability = ReadJsonInt64(entry, "Availability");
                    long memoryBytes = ReadJsonInt64(entry, "AdapterRAM");
                    string processor = ReadJsonString(entry, "VideoProcessor");
                    string driver = ReadJsonString(entry, "DriverVersion");

                    result.Add(new AdapterInfo
                    {
                        id = !string.IsNullOrWhiteSpace(pnpId) ? pnpId : $"WMI|{name}",
                        name = name,
                        description = !string.IsNullOrWhiteSpace(processor) ? processor : name,
                        kind = GuessAdapterKind(name + " " + processor, 0),
                        active = string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase) || availability == 3,
                        primary = false,
                        deviceId = pnpId,
                        driverVersion = driver,
                        memoryMb = memoryBytes > 0 ? memoryBytes / (1024L * 1024L) : 0,
                        source = "WMI/CIM"
                    });
                }
            }
            catch
            {
                // Bazı kurumsal Windows kurulumlarında PowerShell/WMI kapalı olabilir.
            }
            return result;
        }

        private static List<AdapterInfo> EnumerateAdaptersViaRegistry()
        {
            var result = new List<AdapterInfo>();
            try
            {
                using RegistryKey? videoRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video");
                if (videoRoot == null) return result;

                foreach (string adapterGuid in videoRoot.GetSubKeyNames())
                {
                    using RegistryKey? adapterRoot = videoRoot.OpenSubKey(adapterGuid);
                    if (adapterRoot == null) continue;

                    foreach (string instanceName in adapterRoot.GetSubKeyNames().Where(x => x.All(char.IsDigit)))
                    {
                        using RegistryKey? instance = adapterRoot.OpenSubKey(instanceName);
                        if (instance == null) continue;
                        string name = ReadRegistryString(instance, "DriverDesc");
                        if (string.IsNullOrWhiteSpace(name))
                            name = ReadRegistryString(instance, "HardwareInformation.AdapterString");
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        string deviceId = ReadRegistryString(instance, "MatchingDeviceId");
                        string driver = ReadRegistryString(instance, "DriverVersion");
                        long memoryBytes = ReadRegistryInteger(instance, "HardwareInformation.MemorySize");

                        result.Add(new AdapterInfo
                        {
                            id = !string.IsNullOrWhiteSpace(deviceId) ? deviceId : $"REG|{adapterGuid}|{instanceName}|{name}",
                            name = name,
                            description = name,
                            kind = GuessAdapterKind(name, 0),
                            active = true,
                            primary = false,
                            deviceName = adapterGuid + "\\" + instanceName,
                            deviceId = deviceId,
                            driverVersion = driver,
                            memoryMb = memoryBytes > 0 ? memoryBytes / (1024L * 1024L) : 0,
                            source = "Registry"
                        });
                    }
                }
            }
            catch
            {
                // Registry erişimi kısıtlıysa diğer kaynaklar kullanılmaya devam eder.
            }
            return result;
        }

        private static List<AdapterInfo> EnumerateAdaptersViaDisplayDevices()
        {
            var result = new List<AdapterInfo>();
            try
            {
                for (uint index = 0; index < 64; index++)
                {
                    DISPLAY_DEVICE device = new() { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
                    if (!EnumDisplayDevices(null, index, ref device, 0)) break;

                    string name = (device.DeviceString ?? string.Empty).Trim();
                    string id = (device.DeviceID ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (device.StateFlags.HasFlag(DisplayDeviceStateFlags.MirroringDriver)) continue;

                    result.Add(new AdapterInfo
                    {
                        id = string.IsNullOrWhiteSpace(id) ? $"DISPLAY|{device.DeviceName}|{name}" : id,
                        name = name,
                        description = name,
                        kind = GuessAdapterKind(name, device.StateFlags),
                        active = device.StateFlags.HasFlag(DisplayDeviceStateFlags.AttachedToDesktop),
                        primary = device.StateFlags.HasFlag(DisplayDeviceStateFlags.PrimaryDevice),
                        deviceName = device.DeviceName ?? string.Empty,
                        deviceId = id,
                        memoryMb = 0,
                        source = "EnumDisplayDevices"
                    });
                }
            }
            catch
            {
                // Eski/özel Windows görüntü sürücülerinde bu API eksik sonuç verebilir.
            }
            return result;
        }

        private static void MergeAdapters(List<AdapterInfo> target, IEnumerable<AdapterInfo> incoming)
        {
            foreach (AdapterInfo item in incoming)
            {
                AdapterInfo? existing = target.FirstOrDefault(x => IsSameAdapter(x, item));
                if (existing == null)
                {
                    target.Add(item);
                    continue;
                }

                existing.active |= item.active;
                existing.primary |= item.primary;
                if (existing.memoryMb <= 0 && item.memoryMb > 0) existing.memoryMb = item.memoryMb;
                if (string.IsNullOrWhiteSpace(existing.driverVersion)) existing.driverVersion = item.driverVersion;
                if (string.IsNullOrWhiteSpace(existing.deviceId)) existing.deviceId = item.deviceId;
                if (string.IsNullOrWhiteSpace(existing.deviceName)) existing.deviceName = item.deviceName;
                if (string.IsNullOrWhiteSpace(existing.description)) existing.description = item.description;
                if (string.IsNullOrWhiteSpace(existing.source)) existing.source = item.source;
                else if (!existing.source.Contains(item.source, StringComparison.OrdinalIgnoreCase)) existing.source += "+" + item.source;
                if (string.Equals(existing.kind, "graphics-adapter", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(item.kind, "graphics-adapter", StringComparison.OrdinalIgnoreCase))
                    existing.kind = item.kind;
            }
        }

        private static bool IsSameAdapter(AdapterInfo left, AdapterInfo right)
        {
            string leftId = NormalizeHardwareId(left.deviceId);
            string rightId = NormalizeHardwareId(right.deviceId);
            if (!string.IsNullOrWhiteSpace(leftId) && !string.IsNullOrWhiteSpace(rightId) &&
                string.Equals(leftId, rightId, StringComparison.OrdinalIgnoreCase)) return true;

            string leftName = NormalizeAdapterName(left.name);
            string rightName = NormalizeAdapterName(right.name);
            return !string.IsNullOrWhiteSpace(leftName) &&
                   string.Equals(leftName, rightName, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeHardwareId(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string upper = value.Trim().ToUpperInvariant();
            int ven = upper.IndexOf("VEN_", StringComparison.Ordinal);
            int dev = upper.IndexOf("DEV_", StringComparison.Ordinal);
            if (ven >= 0 && dev >= 0 && upper.Length >= dev + 8)
                return upper.Substring(ven, 8) + "&" + upper.Substring(dev, 8);
            return upper;
        }

        private static string NormalizeAdapterName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var builder = new StringBuilder();
            foreach (char c in value.ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) builder.Append(c);
            return builder.ToString()
                .Replace("microsoftcorporation", string.Empty, StringComparison.Ordinal)
                .Replace("compatible", string.Empty, StringComparison.Ordinal)
                .Replace("graphicsadapter", string.Empty, StringComparison.Ordinal);
        }

        private static string GuessAdapterKind(string name, DisplayDeviceStateFlags flags)
        {
            string lower = name.ToLowerInvariant();
            if (lower.Contains("basic render") || lower.Contains("remote") || lower.Contains("virtual"))
                return "software/virtual";
            if (lower.Contains("nvidia") || lower.Contains("geforce") || lower.Contains("quadro") || lower.Contains("tesla") ||
                lower.Contains("radeon rx") || lower.Contains("radeon pro") || lower.Contains("firepro") ||
                lower.Contains("intel(r) arc") || lower.Contains("intel arc"))
                return "discrete";
            if (lower.Contains("intel") || lower.Contains("iris") || lower.Contains("uhd") ||
                lower.Contains("hd graphics") || lower.Contains("radeon(tm) graphics") || lower.Contains("vega"))
                return "integrated";
            if (flags.HasFlag(DisplayDeviceStateFlags.Remote))
                return "remote";
            return "graphics-adapter";
        }

        private static string ReadJsonString(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out JsonElement value)) return string.Empty;
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.GetRawText(),
                _ => string.Empty
            };
        }

        private static long ReadJsonInt64(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out JsonElement value)) return 0;
            if (value.ValueKind == JsonValueKind.Number)
            {
                if (value.TryGetInt64(out long signed)) return signed;
                if (value.TryGetUInt64(out ulong unsigned)) return unsigned > long.MaxValue ? long.MaxValue : (long)unsigned;
            }
            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out long parsed)) return parsed;
            return 0;
        }

        private static string ReadRegistryString(RegistryKey key, string name)
        {
            object? value = key.GetValue(name);
            return value switch
            {
                string text => text.Trim(),
                string[] values => string.Join(" ", values).Trim(),
                _ => string.Empty
            };
        }

        private static long ReadRegistryInteger(RegistryKey key, string name)
        {
            object? value = key.GetValue(name);
            return value switch
            {
                int i => unchecked((uint)i),
                long l => l,
                byte[] bytes when bytes.Length >= 8 => BitConverter.ToInt64(bytes, 0),
                byte[] bytes when bytes.Length >= 4 => BitConverter.ToUInt32(bytes, 0),
                _ => 0
            };
        }

        private static GraphicsConfig LoadConfig()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return new GraphicsConfig();
                string raw = File.ReadAllText(ConfigPath);
                GraphicsConfig? config = JsonSerializer.Deserialize<GraphicsConfig>(raw);
                if (config == null) return new GraphicsConfig();
                config.Preference = NormalizePreference(config.Preference);
                config.SelectedAdapterId ??= string.Empty;
                // Önceki paketlerin otomatik ayrık GPU zorlamasını kaldır.
                // Kullanıcının sonradan açıkça yaptığı seçimler korunur.
                if (config.SchemaVersion < 5)
                {
                    if (!config.UserExplicitlySelected)
                    {
                        config.Preference = "system";
                        config.SelectedAdapterId = string.Empty;
                    }
                    config.SchemaVersion = 5;
                    SaveConfig(config);
                }
                return config;
            }
            catch
            {
                return new GraphicsConfig();
            }
        }

        private static void SaveConfig(GraphicsConfig config)
        {
            Directory.CreateDirectory(ConfigFolder);
            string temp = ConfigPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
            File.Move(temp, ConfigPath);
        }

        private static string NormalizePreference(string? preference)
        {
            return preference switch
            {
                "system" => "system",
                "power-saving" => "power-saving",
                "high-performance" => "high-performance",
                "software" => "software",
                _ => "system"
            };
        }

        private static void WriteWindowsPreference(string preference)
        {
            string? executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath)) return;

            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);
            if (key == null) return;
            if (preference == "power-saving")
                key.SetValue(executablePath, "GpuPreference=1;", RegistryValueKind.String);
            else if (preference == "high-performance")
                key.SetValue(executablePath, "GpuPreference=2;", RegistryValueKind.String);
            else
                key.DeleteValue(executablePath, throwOnMissingValue: false);
        }
    }
}
