using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace AlekrythaeCore.Media
{
    /// <summary>
    /// External-media bridge.
    ///
    /// Seçilen dosyanın mutlak Windows yolu yalnız içe aktarma süresince onaylanır.
    /// Meggy dosyayı aktif maceranın Media klasörüne kopyalar; mutlak dış yol oyun
    /// veritabanında veya bir yan JSON dosyasında kalıcı tutulmaz.
    /// </summary>
    public static class ExternalMediaBridge
    {
        public const string ExternalHost = "alek-external.local";

        private static readonly string[] SupportedExtensions =
        {
            "png", "jpg", "jpeg", "webp", "gif", "bmp", "svg", "avif",
            "mp4", "webm", "mov", "ogg",
            "mp3", "m4a", "m3a", "aac", "wav", "opus", "flac", "wma"
        };

        private static readonly HashSet<string> SupportedExtensionSet =
            new HashSet<string>(SupportedExtensions, StringComparer.OrdinalIgnoreCase);

        private sealed class BridgeState
        {
            public readonly object Sync = new object();
            public readonly HashSet<string> ApprovedPaths =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public bool StreamingRegistered;
        }

        private static readonly ConditionalWeakTable<CoreWebView2, BridgeState> States =
            new ConditionalWeakTable<CoreWebView2, BridgeState>();

        public static void RegisterStreaming(CoreWebView2 core, string? memoryPath)
        {
            if (core == null) throw new ArgumentNullException(nameof(core));

            BridgeState state = States.GetValue(core, _ => new BridgeState());

            lock (state.Sync)
            {
                if (state.StreamingRegistered) return;
                state.StreamingRegistered = true;
            }

            core.AddWebResourceRequestedFilter(
                $"https://{ExternalHost}/*",
                CoreWebView2WebResourceContext.All);

            core.WebResourceRequested += (sender, args) =>
                HandleRequest(core, state, args);
        }

        public static void RefreshApprovedPaths(CoreWebView2 core, string? memoryPath)
        {
            // oyun kayıtlarından veya eski yan JSON dosyalarından mutlak
            // yolları yeniden onaylama. Dış dosya erişimi yalnız seçici ile başlayan
            // tek içe aktarma işlemi boyunca geçerlidir; kalıcı kayıt Media/ kopyasıdır.
        }

        public static bool TryHandleApi(
            CoreWebView2 core,
            Window owner,
            string? operation,
            JsonElement payload,
            out object result)
        {
            BridgeState state = States.GetValue(core, _ => new BridgeState());

            switch (operation)
            {
                case "pickExternalMedia":
                    result = Pick(owner, state, payload);
                    return true;

                case "probeExternalMedia":
                    result = Probe(owner, state, ReadString(payload, "path"));
                    return true;

                default:
                    result = new { ok = false, error = "unsupported_external_media_operation" };
                    return false;
            }
        }

        private static object Pick(Window owner, BridgeState state, JsonElement payload)
        {
            bool multiple = ReadBool(payload, "multiple");
            string[] requestedExtensions = ReadExtensions(payload);
            string[] allowed = requestedExtensions.Length == 0
                ? SupportedExtensions
                : requestedExtensions
                    .Where(ext => SupportedExtensionSet.Contains(ext))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            var dialog = new OpenFileDialog
            {
                Title = "Ałek’ryŧhæ Medya Bağla",
                Multiselect = multiple,
                CheckFileExists = true,
                CheckPathExists = true,
                DereferenceLinks = true,
                RestoreDirectory = true,
                ValidateNames = true,
                Filter = BuildFilter(allowed)
            };

            bool? accepted = dialog.ShowDialog(owner);
            if (accepted != true)
            {
                return new
                {
                    ok = true,
                    cancelled = true,
                    files = Array.Empty<object>()
                };
            }

            var files = new List<object>();
            foreach (string selected in dialog.FileNames)
            {
                string path = NormalizeFullPath(selected);
                if (!IsAllowedExistingMedia(path)) continue;

                string ext = Path.GetExtension(path).TrimStart('.');
                if (allowed.Length > 0 && !allowed.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    continue;

                Approve(state, path);
                files.Add(ToDescriptor(path));
            }

            if (files.Count == 0)
            {
                return new
                {
                    ok = false,
                    cancelled = false,
                    error = "Seçilen dosyanın uzantısı desteklenmiyor veya dosyaya erişilemiyor.",
                    files = Array.Empty<object>()
                };
            }

            return new
            {
                ok = true,
                cancelled = false,
                files = files.ToArray()
            };
        }

        private static object Probe(Window owner, BridgeState state, string? candidatePath)
        {
            string path = NormalizeFullPath(candidatePath);
            if (!IsAllowedMediaPath(path))
            {
                return new
                {
                    ok = false,
                    exists = false,
                    error = "invalid_external_media_path"
                };
            }

            if (!File.Exists(path))
            {
                return new
                {
                    ok = true,
                    exists = false,
                    path,
                    error = "not_found"
                };
            }

            // A legacy absolute path must never silently become an approved native-file
            // capability. The picker already provides explicit consent; probe therefore
            // asks once per Core session before granting access to a previously unknown path.
            if (!IsApproved(state, path))
            {
                MessageBoxResult consent = MessageBox.Show(
                    owner,
                    "Bu .alek uygulaması eski bir kayıt içindeki harici medya dosyasına " +
                    "erişmek istiyor:\n\n" + path +
                    "\n\nBu dosyaya yalnızca bu Core oturumu için erişim verilsin mi?",
                    "Ałek’ryŧhæ Harici Medya İzni",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (consent != MessageBoxResult.Yes)
                {
                    return new
                    {
                        ok = false,
                        exists = true,
                        path,
                        error = "external_media_permission_denied"
                    };
                }

                Approve(state, path);
            }

            return new
            {
                ok = true,
                exists = true,
                path,
                file = ToDescriptor(path)
            };
        }

        public static string CreateUrl(string path)
        {
            string fullPath = NormalizeFullPath(path);
            if (!IsAllowedMediaPath(fullPath)) return string.Empty;
            return $"https://{ExternalHost}/media?path={Uri.EscapeDataString(fullPath)}";
        }

        private static void HandleRequest(
            CoreWebView2 core,
            BridgeState state,
            CoreWebView2WebResourceRequestedEventArgs args)
        {
            try
            {
                string method = (args.Request.Method ?? "GET").ToUpperInvariant();
                if (method == "OPTIONS")
                {
                    args.Response = core.Environment.CreateWebResourceResponse(
                        Stream.Null,
                        204,
                        "No Content",
                        CommonHeaders() + "\r\nContent-Length: 0");
                    return;
                }

                if (method != "GET" && method != "HEAD")
                {
                    args.Response = CreateTextResponse(
                        core,
                        405,
                        "Method Not Allowed",
                        "Only GET and HEAD are supported.");
                    return;
                }

                var uri = new Uri(args.Request.Uri);
                string path = NormalizeFullPath(GetQueryValue(uri, "path"));
                if (!IsAllowedMediaPath(path))
                {
                    args.Response = CreateTextResponse(core, 400, "Bad Request", "Invalid media path.");
                    return;
                }

                if (!IsApproved(state, path))
                {
                    args.Response = CreateTextResponse(
                        core,
                        403,
                        "Forbidden",
                        "The media path has not been approved by the picker or probe operation.");
                    return;
                }

                if (!File.Exists(path))
                {
                    args.Response = CreateTextResponse(core, 404, "Not Found", "Media file not found.");
                    return;
                }

                var info = new FileInfo(path);
                long totalLength = info.Length;
                string? rangeHeader = null;
                try { rangeHeader = args.Request.Headers.GetHeader("Range"); } catch { }

                RangeResult range = ParseRange(rangeHeader, totalLength);
                if (range.Requested && !range.Valid)
                {
                    args.Response = core.Environment.CreateWebResourceResponse(
                        Stream.Null,
                        416,
                        "Range Not Satisfiable",
                        CommonHeaders()
                        + $"\r\nContent-Range: bytes */{totalLength}"
                        + "\r\nContent-Length: 0");
                    return;
                }

                bool partial = range.Requested && range.Valid;
                long start = partial ? range.Start : 0;
                long end = partial ? range.End : Math.Max(0, totalLength - 1);
                long responseLength = totalLength == 0 ? 0 : end - start + 1;

                Stream body = Stream.Null;
                if (method != "HEAD" && responseLength > 0)
                {
                    var file = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete,
                        128 * 1024,
                        FileOptions.SequentialScan);

                    body = partial
                        ? new BoundedReadStream(file, start, responseLength)
                        : file;
                }

                int status = partial ? 206 : 200;
                string reason = partial ? "Partial Content" : "OK";
                string headers = CommonHeaders()
                    + $"\r\nContent-Type: {GetMimeType(path)}"
                    + $"\r\nContent-Length: {responseLength}"
                    + $"\r\nLast-Modified: {info.LastWriteTimeUtc:R}"
                    + $"\r\nContent-Disposition: inline; filename*=UTF-8''{Uri.EscapeDataString(info.Name)}";

                if (partial)
                    headers += $"\r\nContent-Range: bytes {start}-{end}/{totalLength}";

                args.Response = core.Environment.CreateWebResourceResponse(body, status, reason, headers);
            }
            catch (Exception ex)
            {
                args.Response = CreateTextResponse(core, 500, "Internal Server Error", ex.Message);
            }
        }

        private static IEnumerable<string> ExtractAbsoluteWindowsPaths(string json)
        {
            // JSON-escaped backslashes and ordinary slash paths are both accepted.
            string decoded = json.Replace("\\\\", "\\").Replace("\\/", "/");
            var matches = Regex.Matches(
                decoded,
                @"(?ix)(?:[a-z]:[\\/][^""\r\n<>|?*]+|\\\\[^\\""\r\n]+\\[^""\r\n<>|?*]+)");

            foreach (Match match in matches)
            {
                string value = match.Value.Trim().TrimEnd(',', '}', ']', ' ');
                if (!string.IsNullOrWhiteSpace(value)) yield return value;
            }
        }

        private static bool IsAllowedExistingMedia(string path) =>
            IsAllowedMediaPath(path) && File.Exists(path);

        private static bool IsAllowedMediaPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
            string ext = Path.GetExtension(path).TrimStart('.');
            return SupportedExtensionSet.Contains(ext);
        }

        private static void Approve(BridgeState state, string path)
        {
            lock (state.Sync) state.ApprovedPaths.Add(path);
        }

        private static bool IsApproved(BridgeState state, string path)
        {
            lock (state.Sync) return state.ApprovedPaths.Contains(path);
        }

        private static string NormalizeFullPath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            string raw = value.Trim().Trim('"');
            try
            {
                if (Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri) && uri.IsFile)
                    raw = uri.LocalPath;
                return Path.GetFullPath(raw);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ReadString(JsonElement payload, string property)
        {
            if (payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty(property, out JsonElement node)
                && node.ValueKind == JsonValueKind.String)
                return node.GetString() ?? string.Empty;

            return string.Empty;
        }

        private static bool ReadBool(JsonElement payload, string property)
        {
            return payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty(property, out JsonElement node)
                && node.ValueKind == JsonValueKind.True;
        }

        private static string[] ReadExtensions(JsonElement payload)
        {
            if (payload.ValueKind != JsonValueKind.Object
                || !payload.TryGetProperty("extensions", out JsonElement node)
                || node.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            return node.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => (item.GetString() ?? string.Empty)
                    .Trim()
                    .TrimStart('.')
                    .ToLowerInvariant())
                .Where(item => item.Length > 0 && item.All(char.IsLetterOrDigit))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string BuildFilter(IReadOnlyCollection<string> extensions)
        {
            if (extensions.Count == 0) return "Tüm dosyalar (*.*)|*.*";
            string mask = string.Join(";", extensions.Select(ext => $"*.{ext}"));
            return $"Desteklenen medya ({mask})|{mask}|Tüm dosyalar (*.*)|*.*";
        }

        private static object ToDescriptor(string path)
        {
            var info = new FileInfo(path);
            return new
            {
                path,
                name = info.Name,
                extension = info.Extension.TrimStart('.').ToLowerInvariant(),
                size = info.Length,
                lastModifiedUtc = info.LastWriteTimeUtc,
                mimeType = GetMimeType(path),
                url = CreateUrl(path)
            };
        }

        private static string CommonHeaders() =>
            "Accept-Ranges: bytes\r\n"
            + "Access-Control-Allow-Origin: *\r\n"
            + "Access-Control-Allow-Methods: GET, HEAD, OPTIONS\r\n"
            + "Access-Control-Allow-Headers: Range\r\n"
            + "Cross-Origin-Resource-Policy: cross-origin\r\n"
            + "X-Content-Type-Options: nosniff\r\n"
            + "Cache-Control: no-cache";

        private static CoreWebView2WebResourceResponse CreateTextResponse(
            CoreWebView2 core,
            int status,
            string reason,
            string message)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message ?? string.Empty);
            return core.Environment.CreateWebResourceResponse(
                new MemoryStream(bytes, false),
                status,
                reason,
                CommonHeaders()
                + "\r\nContent-Type: text/plain; charset=utf-8"
                + $"\r\nContent-Length: {bytes.Length}");
        }

        private static string GetQueryValue(Uri uri, string key)
        {
            string query = uri.Query.TrimStart('?');
            foreach (string item in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair = item.Split('=', 2);
                if (!string.Equals(Uri.UnescapeDataString(pair[0]), key, StringComparison.OrdinalIgnoreCase))
                    continue;
                return pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
            }
            return string.Empty;
        }

        private readonly struct RangeResult
        {
            public RangeResult(bool requested, bool valid, long start, long end)
            {
                Requested = requested;
                Valid = valid;
                Start = start;
                End = end;
            }

            public bool Requested { get; }
            public bool Valid { get; }
            public long Start { get; }
            public long End { get; }
        }

        private static RangeResult ParseRange(string? value, long totalLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new RangeResult(false, true, 0, Math.Max(0, totalLength - 1));

            if (totalLength <= 0 || !value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
                return new RangeResult(true, false, 0, 0);

            string first = value.Substring(6).Split(',')[0].Trim();
            string[] parts = first.Split('-', 2);
            if (parts.Length != 2)
                return new RangeResult(true, false, 0, 0);

            if (parts[0].Length == 0)
            {
                if (!long.TryParse(parts[1], out long suffix) || suffix <= 0)
                    return new RangeResult(true, false, 0, 0);
                suffix = Math.Min(suffix, totalLength);
                return new RangeResult(true, true, totalLength - suffix, totalLength - 1);
            }

            if (!long.TryParse(parts[0], out long start) || start < 0 || start >= totalLength)
                return new RangeResult(true, false, 0, 0);

            long end = totalLength - 1;
            if (parts[1].Length > 0)
            {
                if (!long.TryParse(parts[1], out end) || end < start)
                    return new RangeResult(true, false, 0, 0);
                end = Math.Min(end, totalLength - 1);
            }

            return new RangeResult(true, true, start, end);
        }

        private static string GetMimeType(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".mp3": return "audio/mpeg";
                case ".m4a":
                case ".m3a": return "audio/mp4";
                case ".aac": return "audio/aac";
                case ".wav": return "audio/wav";
                case ".ogg": return "audio/ogg";
                case ".opus": return "audio/ogg";
                case ".flac": return "audio/flac";
                case ".wma": return "audio/x-ms-wma";
                case ".mp4": return "video/mp4";
                case ".webm": return "video/webm";
                case ".mov": return "video/quicktime";
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".webp": return "image/webp";
                case ".gif": return "image/gif";
                case ".bmp": return "image/bmp";
                case ".svg": return "image/svg+xml";
                case ".avif": return "image/avif";
                default: return "application/octet-stream";
            }
        }

        private sealed class BoundedReadStream : Stream
        {
            private readonly FileStream _inner;
            private readonly long _start;
            private readonly long _length;
            private long _position;

            public BoundedReadStream(FileStream inner, long start, long length)
            {
                _inner = inner;
                _start = start;
                _length = length;
                _inner.Position = start;
            }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => _length;

            public override long Position
            {
                get => _position;
                set => Seek(value, SeekOrigin.Begin);
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_position >= _length) return 0;
                int allowed = (int)Math.Min(count, _length - _position);
                int read = _inner.Read(buffer, offset, allowed);
                _position += read;
                return read;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                long target;
                switch (origin)
                {
                    case SeekOrigin.Begin: target = offset; break;
                    case SeekOrigin.Current: target = _position + offset; break;
                    case SeekOrigin.End: target = _length + offset; break;
                    default: throw new ArgumentOutOfRangeException(nameof(origin));
                }

                if (target < 0 || target > _length)
                    throw new IOException("Seek is outside the media range.");

                _position = target;
                _inner.Position = _start + target;
                return _position;
            }

            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing) _inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
