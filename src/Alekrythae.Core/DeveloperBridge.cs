using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Forms = System.Windows.Forms;

namespace AlekrythaeCore
{
    /// <summary>
    /// Ałek’ryŧhæ Code v0.1.0 R1 developer bridge.
    /// Only dev.* operations are handled here. Existing Core APIs remain untouched.
    /// </summary>
    internal sealed class DeveloperBridge : IDisposable
    {
        private const string ApiVersion = "0.1.0-R1-PF3-v011";

        private readonly Dictionary<string, string> _workspaces =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, ConPtySession> _shells =
            new(StringComparer.OrdinalIgnoreCase);

        private CoreWebView2? _webView;
        private Dispatcher? _dispatcher;
        private bool _disposed;

        public void Attach(CoreWebView2 webView, Dispatcher dispatcher)
        {
            _webView = webView;
            _dispatcher = dispatcher;
        }

        public bool TryHandleApi(string op, JsonElement payload, out object result)
        {
            result = new { ok = false };

            if (!op.StartsWith("dev.", StringComparison.OrdinalIgnoreCase))
                return false;

            if (_disposed)
            {
                result = new { ok = false, error = "developer_bridge_disposed" };
                return true;
            }

            try
            {
                switch (op)
                {
                    case "dev.status":
                        result = new
                        {
                            ok = true,
                            apiVersion = ApiVersion,
                            workspace = true,
                            shell = true,
                            shellBackend = "Windows ConPTY",
                            unicode = true,
                            elevatedShellMode = "external-uac-r1"
                        };
                        return true;

                    case "dev.workspace.pick":
                        result = PickWorkspace(payload);
                        return true;

                    case "dev.workspace.release":
                        result = ReleaseWorkspace(payload);
                        return true;

                    case "dev.workspace.list":
                        result = ListWorkspace(payload);
                        return true;

                    case "dev.workspace.readText":
                        result = ReadText(payload);
                        return true;

                    case "dev.workspace.writeText":
                        result = WriteText(payload);
                        return true;

                    case "dev.workspace.exists":
                        result = Exists(payload);
                        return true;

                    case "dev.workspace.mkdir":
                        result = Mkdir(payload);
                        return true;

                    case "dev.shell.start":
                        result = StartShell(payload);
                        return true;

                    case "dev.shell.write":
                        result = WriteShell(payload);
                        return true;

                    case "dev.shell.resize":
                        result = ResizeShell(payload);
                        return true;

                    case "dev.shell.stop":
                        result = StopShell(payload);
                        return true;

                    default:
                        result = new { ok = false, error = "unknown_dev_op" };
                        return true;
                }
            }
            catch (Exception ex)
            {
                result = new { ok = false, error = ex.Message };
                return true;
            }
        }

        private object PickWorkspace(JsonElement payload)
        {
            string description = GetString(payload, "description");

            using var dialog = new Forms.FolderBrowserDialog
            {
                Description = string.IsNullOrWhiteSpace(description)
                    ? "Kod/proje klasörünü seç"
                    : description,
                ShowNewFolderButton = true,
                UseDescriptionForTitle = true
            };

            Forms.DialogResult selected = dialog.ShowDialog();
            if (selected != Forms.DialogResult.OK ||
                string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                return new { ok = false, error = "cancelled" };
            }

            string root = NormalizeRoot(dialog.SelectedPath);

            if (HasReparsePoint(root.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)))
            {
                return new { ok = false, error = "workspace_reparse_point_denied" };
            }

            string token = Guid.NewGuid().ToString("N");
            _workspaces[token] = root;

            string displayRoot = root.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

            return new
            {
                ok = true,
                workspaceToken = token,
                name = Path.GetFileName(displayRoot)
            };
        }

        private object ReleaseWorkspace(JsonElement payload)
        {
            string token = GetString(payload, "workspaceToken");
            if (string.IsNullOrWhiteSpace(token))
                return new { ok = false, error = "workspace_token_required" };

            bool removed = _workspaces.Remove(token);
            return new { ok = true, released = removed };
        }

        private object ListWorkspace(JsonElement payload)
        {
            if (!TryResolve(
                    payload,
                    out string root,
                    out string full,
                    out string relative,
                    out string error))
            {
                return new { ok = false, error };
            }

            if (!Directory.Exists(full))
                return new { ok = false, error = "directory_not_found" };

            var items = new List<object>();

            foreach (string directory in Directory.GetDirectories(full))
            {
                FileAttributes attrs;
                try { attrs = File.GetAttributes(directory); }
                catch { continue; }

                if ((attrs & FileAttributes.ReparsePoint) != 0)
                    continue;

                items.Add(new
                {
                    name = Path.GetFileName(directory),
                    type = "directory",
                    path = ToForwardSlashes(Path.GetRelativePath(root, directory))
                });
            }

            foreach (string file in Directory.GetFiles(full))
            {
                FileAttributes attrs;
                try { attrs = File.GetAttributes(file); }
                catch { continue; }

                if ((attrs & FileAttributes.ReparsePoint) != 0)
                    continue;

                FileInfo info = new(file);
                items.Add(new
                {
                    name = info.Name,
                    type = "file",
                    size = info.Length,
                    path = ToForwardSlashes(Path.GetRelativePath(root, file))
                });
            }

            return new { ok = true, path = relative, items };
        }

        private object ReadText(JsonElement payload)
        {
            if (!TryResolve(payload, out _, out string full, out _, out string error))
                return new { ok = false, error };

            if (!File.Exists(full))
                return new { ok = false, error = "file_not_found" };

            const long maxBytes = 8L * 1024L * 1024L;
            FileInfo info = new(full);

            if (info.Length > maxBytes)
                return new { ok = false, error = "file_too_large_r1", maxBytes };

            if (IsLikelyBinaryFile(full))
                return new { ok = false, error = "binary_file" };

            string content = File.ReadAllText(full, Encoding.UTF8);
            return new { ok = true, content, size = info.Length };
        }

        private static bool IsLikelyBinaryFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();

            switch (ext)
            {
                case ".mp3": case ".m4a": case ".aac": case ".wav":
                case ".ogg": case ".flac": case ".mp4": case ".mkv":
                case ".avi": case ".mov": case ".webm":
                case ".png": case ".jpg": case ".jpeg": case ".gif":
                case ".webp": case ".bmp": case ".ico":
                case ".exe": case ".dll": case ".pdb":
                case ".zip": case ".rar": case ".7z": case ".gz":
                case ".pdf": case ".db": case ".sqlite": case ".sqlite3":
                case ".woff": case ".woff2": case ".ttf": case ".otf":
                    return true;
            }

            try
            {
                using FileStream stream = new(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                byte[] probe = new byte[8192];
                int read = stream.Read(probe, 0, probe.Length);

                if (read >= 2)
                {
                    // UTF-16 BOM is text even though its payload commonly contains NUL bytes.
                    if ((probe[0] == 0xFF && probe[1] == 0xFE) ||
                        (probe[0] == 0xFE && probe[1] == 0xFF))
                        return false;
                }

                int nul = 0;
                int controls = 0;

                for (int i = 0; i < read; i++)
                {
                    byte b = probe[i];

                    if (b == 0)
                        nul++;

                    if (b < 0x09 || (b > 0x0D && b < 0x20))
                        controls++;
                }

                return nul > 0 || (read > 64 && controls > read / 12);
            }
            catch
            {
                return false;
            }
        }

        private object WriteText(JsonElement payload)
        {
            if (!TryResolve(payload, out _, out string full, out _, out string error))
                return new { ok = false, error };

            string content = GetString(payload, "content");
            string? parent = Path.GetDirectoryName(full);

            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);

            string temp = full + ".alek-code-r1.tmp";

            File.WriteAllText(temp, content, new UTF8Encoding(false));
            File.Move(temp, full, true);

            return new { ok = true };
        }

        private object Exists(JsonElement payload)
        {
            if (!TryResolve(payload, out _, out string full, out _, out string error))
                return new { ok = false, error };

            if (File.Exists(full))
                return new { ok = true, exists = true, type = "file" };

            if (Directory.Exists(full))
                return new { ok = true, exists = true, type = "directory" };

            return new { ok = true, exists = false };
        }

        private object Mkdir(JsonElement payload)
        {
            if (!TryResolve(payload, out _, out string full, out _, out string error))
                return new { ok = false, error };

            Directory.CreateDirectory(full);
            return new { ok = true };
        }

        private object StartShell(JsonElement payload)
        {
            string token = GetString(payload, "workspaceToken");

            if (!_workspaces.TryGetValue(token, out string? root) ||
                string.IsNullOrWhiteSpace(root))
            {
                return new { ok = false, error = "invalid_workspace_token" };
            }

            bool elevated = GetBool(payload, "elevated");
            string shell = GetString(payload, "shell");

            if (!string.IsNullOrWhiteSpace(shell) &&
                !string.Equals(shell, "cmd", StringComparison.OrdinalIgnoreCase))
            {
                return new { ok = false, error = "r1_only_cmd_supported" };
            }

            string workingDirectory = root.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

            if (elevated)
            {
                try
                {
                    string comSpec =
                        Environment.GetEnvironmentVariable("ComSpec")
                        ?? Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.System),
                            "cmd.exe");

                    var elevatedInfo = new ProcessStartInfo
                    {
                        FileName = comSpec,
                        Arguments = "/K",
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = true,
                        Verb = "runas"
                    };

                    Process? elevatedProcess = Process.Start(elevatedInfo);

                    if (elevatedProcess == null)
                        return new { ok = false, error = "elevated_shell_failed" };

                    return new
                    {
                        ok = true,
                        elevated = true,
                        external = true,
                        processId = elevatedProcess.Id
                    };
                }
                catch (System.ComponentModel.Win32Exception ex)
                    when (ex.NativeErrorCode == 1223)
                {
                    return new { ok = false, error = "uac_cancelled" };
                }
            }

            string shellId = Guid.NewGuid().ToString("N");
            ConPtySession session = ConPtySession.Start(workingDirectory);

            session.Output += text =>
                PushShellEvent(shellId, "stdout", text, null);

            session.Exited += code =>
            {
                PushShellEvent(shellId, "exit", "", code);

                if (_shells.TryRemove(shellId, out ConPtySession? removed))
                {
                    try { removed.Dispose(); } catch { }
                }
            };

            if (!_shells.TryAdd(shellId, session))
            {
                session.Dispose();
                return new { ok = false, error = "shell_registry_failed" };
            }

            return new
            {
                ok = true,
                shellId,
                elevated = false,
                external = false,
                backend = "conpty",
                unicode = true
            };
        }

        private object WriteShell(JsonElement payload)
        {
            string shellId = GetString(payload, "shellId");
            string text = GetString(payload, "text");

            if (!_shells.TryGetValue(shellId, out ConPtySession? session))
                return new { ok = false, error = "shell_not_found" };

            session.Write(text);
            return new { ok = true };
        }

        private object ResizeShell(JsonElement payload)
        {
            string shellId = GetString(payload, "shellId");

            if (!_shells.TryGetValue(shellId, out ConPtySession? session))
                return new { ok = false, error = "shell_not_found" };

            int columns = GetInt(payload, "columns", 140);
            int rows = GetInt(payload, "rows", 36);

            session.Resize(
                (short)Math.Clamp(columns, 20, short.MaxValue),
                (short)Math.Clamp(rows, 5, short.MaxValue));

            return new { ok = true };
        }

        private object StopShell(JsonElement payload)
        {
            string shellId = GetString(payload, "shellId");

            if (!_shells.TryRemove(shellId, out ConPtySession? session))
                return new { ok = true, stopped = false };

            try { session.Dispose(); } catch { }

            return new { ok = true, stopped = true };
        }

        private void PushShellEvent(
            string shellId,
            string kind,
            string text,
            int? exitCode)
        {
            CoreWebView2? webView = _webView;
            Dispatcher? dispatcher = _dispatcher;

            if (webView == null || dispatcher == null)
                return;

            string json = JsonSerializer.Serialize(new
            {
                shellId,
                kind,
                text,
                exitCode
            });

            try
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        _ = webView.ExecuteScriptAsync(
                            "window.__alekDevShellEvent && " +
                            "window.__alekDevShellEvent(" + json + ");");
                    }
                    catch
                    {
                    }
                }));
            }
            catch
            {
            }
        }

        private bool TryResolve(
            JsonElement payload,
            out string root,
            out string full,
            out string relative,
            out string error)
        {
            root = "";
            full = "";
            relative = GetString(payload, "path");
            error = "";

            string token = GetString(payload, "workspaceToken");

            if (!_workspaces.TryGetValue(token, out string? approvedRoot) ||
                string.IsNullOrWhiteSpace(approvedRoot))
            {
                error = "invalid_workspace_token";
                return false;
            }

            root = approvedRoot;

            if (Path.IsPathRooted(relative))
            {
                error = "absolute_path_denied";
                return false;
            }

            try
            {
                string rootNoSlash = root.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

                full = Path.GetFullPath(Path.Combine(rootNoSlash, relative ?? ""));

                bool atRoot = string.Equals(
                    full.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                    rootNoSlash,
                    StringComparison.OrdinalIgnoreCase);

                bool belowRoot = full.StartsWith(
                    root,
                    StringComparison.OrdinalIgnoreCase);

                if (!atRoot && !belowRoot)
                {
                    error = "workspace_escape_denied";
                    return false;
                }

                if (PathContainsReparsePoint(rootNoSlash, full))
                {
                    error = "workspace_reparse_point_denied";
                    return false;
                }

                relative = ToForwardSlashes(
                    Path.GetRelativePath(rootNoSlash, full));

                if (relative == ".")
                    relative = "";

                return true;
            }
            catch
            {
                error = "invalid_path";
                return false;
            }
        }

        private static bool PathContainsReparsePoint(
            string root,
            string target)
        {
            if (HasReparsePoint(root))
                return true;

            string relative = Path.GetRelativePath(root, target);

            if (relative == ".")
                return false;

            string current = root;

            foreach (string part in relative.Split(
                new[]
                {
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                },
                StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);

                if ((Directory.Exists(current) || File.Exists(current)) &&
                    HasReparsePoint(current))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) &
                    FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeRoot(string path)
        {
            string full = Path.GetFullPath(path)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            return full + Path.DirectorySeparatorChar;
        }

        private static string ToForwardSlashes(string path)
        {
            return path
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
        }

        private static string GetString(JsonElement payload, string name)
        {
            if (payload.ValueKind == JsonValueKind.Object &&
                payload.TryGetProperty(name, out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.String)
                    return value.GetString() ?? "";

                return value.ToString();
            }

            return "";
        }

        private static bool GetBool(JsonElement payload, string name)
        {
            if (payload.ValueKind == JsonValueKind.Object &&
                payload.TryGetProperty(name, out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.True)
                    return true;

                if (value.ValueKind == JsonValueKind.False)
                    return false;

                if (bool.TryParse(value.ToString(), out bool parsed))
                    return parsed;
            }

            return false;
        }

        private static int GetInt(JsonElement payload, string name, int fallback)
        {
            if (payload.ValueKind == JsonValueKind.Object &&
                payload.TryGetProperty(name, out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.Number &&
                    value.TryGetInt32(out int n))
                    return n;

                if (int.TryParse(value.ToString(), out int parsed))
                    return parsed;
            }

            return fallback;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            foreach (KeyValuePair<string, ConPtySession> pair in _shells)
            {
                try { pair.Value.Dispose(); } catch { }
            }

            _shells.Clear();
            _workspaces.Clear();
            _webView = null;
            _dispatcher = null;
        }
    }
}
