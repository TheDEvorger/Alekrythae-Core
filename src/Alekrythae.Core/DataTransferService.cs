using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace AlekrythaeCore
{
    /// <summary>
    /// Meggy kullanıcı verisini sürümden bağımsız bir .alekdata kasasına taşır.
    ///
    /// Güvenlik sözleşmesi:
    /// - Dışa aktarım canlı SQLite dosyasını kopyalamaz; tutarlı snapshot alır.
    /// - Arşivdeki her dosya SHA-256 ile doğrulanır.
    /// - İçe aktarım mevcut oyunun üstüne yazmaz; çakışan macerayı ayrı kopya yapar.
    /// - Her içe aktarımdan önce otomatik geri dönüş paketi oluşturulur.
    /// - Eski JSON, eski/yeni SQLite ve tam eski Meggy ZIP paketleri tanınır.
    /// </summary>
    internal sealed class DataTransferService
    {
        private const string PackageFormat = "alekrythae.meggy.data";
        private const int PackageFormatVersion = 2;
        private const int ArchitectureRevision = 126;
        private const long MaxArchiveBytes = 32L * 1024L * 1024L * 1024L;
        private const int MaxArchiveEntries = 50000;
        private const string ManifestName = "alekrythae-data-manifest.json";

        private static readonly Regex SafeDocumentKey =
            new Regex(@"^[a-zA-Z0-9_.-]{1,80}$", RegexOptions.Compiled);

        private static readonly Dictionary<string, string> LegacyDocumentKeys =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Story.json"] = "story",
                ["Journey.json"] = "story",
                ["Character.json"] = "characters",
                ["Location.json"] = "locations",
                ["Group.json"] = "groups",
                ["Mount.json"] = "mounts",
                ["Forgotten.json"] = "forgotten",
                ["AFMV.json"] = "special",
                ["Map.json"] = "map",
                ["TideOfAia.json"] = "tide",
                ["Lore.json"] = "lore",
                ["Reverie.json"] = "reverie",
                ["MapLibrary.json"] = "mapLibrary",
                ["Harmonizer.json"] = "harmonizer"
            };

        private readonly string _rootFolder;
        private readonly string _dataFolder;
        private readonly string _gamesFolder;
        private readonly string _registryPath;
        private readonly string _backupsFolder;
        private readonly Window _owner;

        public DataTransferService(string rootFolder, Window owner)
        {
            _rootFolder = Path.GetFullPath(rootFolder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            _dataFolder = Path.Combine(_rootFolder, "Data");
            _gamesFolder = Path.Combine(_rootFolder, "Games");
            _registryPath = Path.Combine(_dataFolder, "meggy.db");
            _backupsFolder = Path.Combine(_rootFolder, "Backups");
            _owner = owner;
        }

        public bool TryHandleApi(string operation, JsonElement payload, out object result)
        {
            _ = payload;
            switch (operation)
            {
                case "data.export":
                    result = ExportWithPicker();
                    return true;
                case "data.import":
                    result = ImportWithPicker();
                    return true;
                default:
                    result = new { ok = false, error = "unsupported_data_transfer_operation" };
                    return false;
            }
        }

        private object ExportWithPicker()
        {
            try
            {
                string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var dialog = new SaveFileDialog
                {
                    Title = "Ałek’ryŧhæ Verisini Dışarı Aktar",
                    FileName = $"Alekrythae-Meggy-Veri-{stamp}.alekdata",
                    DefaultExt = ".alekdata",
                    AddExtension = true,
                    OverwritePrompt = true,
                    Filter = "Ałek’ryŧhæ veri kasası (*.alekdata)|*.alekdata"
                };

                if (dialog.ShowDialog(_owner) != true)
                    return new { ok = true, cancelled = true };

                PackageBuildResult package = CreatePackage(
                    dialog.FileName,
                    reason: "user-export",
                    sourceRevision: ArchitectureRevision);

                return new
                {
                    ok = true,
                    cancelled = false,
                    path = package.Path,
                    fileName = Path.GetFileName(package.Path),
                    formatVersion = PackageFormatVersion,
                    files = package.FileCount,
                    games = package.GameCount,
                    bytes = package.TotalBytes
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, error = ex.Message };
            }
        }

        private object ImportWithPicker()
        {
            string workspace = Path.Combine(
                Path.GetTempPath(),
                "Alekrythae-DataImport-" + Guid.NewGuid().ToString("N"));

            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Ałek’ryŧhæ Verisini İçeri Aktar",
                    Multiselect = false,
                    CheckFileExists = true,
                    Filter =
                        "Desteklenen veri (*.alekdata;*.zip;*.db;*.json)|*.alekdata;*.zip;*.db;*.json|" +
                        "Ałek’ryŧhæ veri kasası (*.alekdata)|*.alekdata|" +
                        "Eski Meggy paketi (*.zip)|*.zip|" +
                        "SQLite verisi (*.db)|*.db|" +
                        "Eski JSON verisi (*.json)|*.json"
                };

                if (dialog.ShowDialog(_owner) != true)
                    return new { ok = true, cancelled = true };

                Directory.CreateDirectory(workspace);
                SourceDescriptor source = PrepareSource(dialog.FileName, workspace);
                List<SourceAdventure> adventures = DiscoverSourceAdventures(source.Root);
                if (adventures.Count == 0)
                    throw new InvalidDataException(
                        "Seçilen dosyada aktarılabilir Meggy macerası veya oyun verisi bulunamadı.");

                Directory.CreateDirectory(_backupsFolder);
                string backupPath = Path.Combine(
                    _backupsFolder,
                    $"Alekrythae-Oncesi-Yedek-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.alekdata");
                CreatePackage(backupPath, "pre-import-backup", ArchitectureRevision);

                ImportResult imported = ApplyImport(adventures, source);
                return new
                {
                    ok = true,
                    cancelled = false,
                    reloadRequired = true,
                    importedAdventures = imported.AdventureCount,
                    migratedDocuments = imported.DocumentCount,
                    copiedMediaFiles = imported.MediaFileCount,
                    sourceFormat = source.Format,
                    sourceRevision = source.Revision,
                    backupFile = Path.GetFileName(backupPath),
                    importedNames = imported.Names
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, error = ex.Message };
            }
            finally
            {
                TryDeleteDirectory(workspace);
            }
        }

        private PackageBuildResult CreatePackage(string outputPath, string reason, int sourceRevision)
        {
            string fullOutput = Path.GetFullPath(outputPath);
            string? outputDirectory = Path.GetDirectoryName(fullOutput);
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new InvalidOperationException("Dışa aktarma klasörü belirlenemedi.");

            Directory.CreateDirectory(outputDirectory);
            string partial = fullOutput + ".partial-" + Guid.NewGuid().ToString("N");
            string snapshots = Path.Combine(
                Path.GetTempPath(),
                "Alekrythae-DataSnapshot-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(snapshots);

            var files = new List<PackageFile>();
            long totalBytes = 0;
            int gameCount = Directory.Exists(_gamesFolder)
                ? Directory.EnumerateDirectories(_gamesFolder).Count()
                : 0;

            try
            {
                using (FileStream stream = new FileStream(
                    partial,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
                {
                    foreach (UserFile source in EnumerateUserFiles())
                    {
                        string sourcePath = source.FullPath;
                        string? snapshot = null;
                        if (source.IsDatabase)
                        {
                            snapshot = Path.Combine(
                                snapshots,
                                Guid.NewGuid().ToString("N") + ".db");
                            SnapshotDatabase(sourcePath, snapshot);
                            sourcePath = snapshot;
                        }

                        PackageFile added = AddFileToArchive(archive, sourcePath, source.ArchivePath);
                        files.Add(added);
                        totalBytes += added.Size;

                        if (snapshot != null)
                            TryDeleteFile(snapshot);
                    }

                    var manifest = new PackageManifest
                    {
                        Format = PackageFormat,
                        FormatVersion = PackageFormatVersion,
                        App = "Ałek’ryŧhæ Meggy The DM Master",
                        AppVersion = "0.0.0",
                        ArchitectureRevision = sourceRevision,
                        Storage = "portable-sqlite-with-legacy-json-migration",
                        Reason = reason,
                        CreatedUtc = DateTimeOffset.UtcNow.ToString("O"),
                        GameCount = gameCount,
                        Files = files
                    };

                    ZipArchiveEntry manifestEntry = archive.CreateEntry(
                        ManifestName,
                        CompressionLevel.Optimal);
                    using Stream manifestStream = manifestEntry.Open();
                    JsonSerializer.Serialize(
                        manifestStream,
                        manifest,
                        new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        });
                }

                File.Move(partial, fullOutput, overwrite: true);
                return new PackageBuildResult(
                    fullOutput,
                    files.Count,
                    gameCount,
                    totalBytes);
            }
            finally
            {
                TryDeleteFile(partial);
                TryDeleteDirectory(snapshots);
            }
        }

        private IEnumerable<UserFile> EnumerateUserFiles()
        {
            var roots = new[]
            {
                new { Full = _dataFolder, Archive = "Data" },
                new { Full = _gamesFolder, Archive = "Games" }
            };

            foreach (var root in roots)
            {
                if (!Directory.Exists(root.Full))
                    continue;

                foreach (string file in Directory.EnumerateFiles(
                    root.Full,
                    "*",
                    SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    FileInfo info = new FileInfo(file);
                    string extension = info.Extension;
                    string relative = Path.GetRelativePath(root.Full, file).Replace('\\', '/');
                    string archivePath = root.Archive + "/" + relative;

                    if (IsTransientDatabaseFile(file)
                        || extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase)
                        || archivePath.Contains("/.import-", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    yield return new UserFile(
                        file,
                        archivePath,
                        extension.Equals(".db", StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        private static PackageFile AddFileToArchive(
            ZipArchive archive,
            string sourcePath,
            string archivePath)
        {
            string safeArchivePath = archivePath.Replace('\\', '/').TrimStart('/');
            FileInfo info = new FileInfo(sourcePath);
            string hash = ComputeSha256(sourcePath);
            ZipArchiveEntry entry = archive.CreateEntry(safeArchivePath, CompressionLevel.Optimal);
            entry.LastWriteTime = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            using (FileStream input = File.Open(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (Stream output = entry.Open())
                input.CopyTo(output);

            return new PackageFile
            {
                Path = safeArchivePath,
                Size = info.Length,
                Sha256 = hash
            };
        }

        private SourceDescriptor PrepareSource(string selectedPath, string workspace)
        {
            string extension = Path.GetExtension(selectedPath);
            if (extension.Equals(".alekdata", StringComparison.OrdinalIgnoreCase)
                && !LooksLikeZipArchive(selectedPath))
            {
                return PreparePortableJsonSource(selectedPath, workspace);
            }
            if (extension.Equals(".alekdata", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                string extractRoot = Path.Combine(workspace, "archive");
                ExtractArchiveSafe(selectedPath, extractRoot);
                PackageManifest? manifest = VerifyPackageIfPresent(extractRoot, out string? manifestRoot);
                string root = manifestRoot ?? FindDataRoot(extractRoot);
                return new SourceDescriptor(
                    root,
                    manifest == null ? "legacy-zip" : $"alekdata-v{manifest.FormatVersion}",
                    manifest?.ArchitectureRevision ?? 0);
            }

            if (extension.Equals(".db", StringComparison.OrdinalIgnoreCase))
                return PrepareDatabaseSource(selectedPath, workspace);

            if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                string root = Path.Combine(workspace, "legacy-json");
                string data = Path.Combine(root, "Data");
                Directory.CreateDirectory(data);
                File.Copy(selectedPath, Path.Combine(data, Path.GetFileName(selectedPath)), true);
                return new SourceDescriptor(root, "legacy-json", 0);
            }

            throw new InvalidDataException("Bu dosya türü veri içe aktarma için desteklenmiyor.");
        }

        private static SourceDescriptor PreparePortableJsonSource(
            string selectedPath,
            string workspace)
        {
            PortableJsonPackage package = JsonSerializer.Deserialize<PortableJsonPackage>(
                File.ReadAllText(selectedPath, Encoding.UTF8),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("JSON veri kasası okunamadı.");
            if (!string.Equals(
                    package.Format,
                    "alekrythae.meggy.data-json",
                    StringComparison.Ordinal)
                || package.FormatVersion < 1
                || package.FormatVersion > 2
                || package.Files == null
                || package.Files.Count > MaxArchiveEntries)
            {
                throw new InvalidDataException("JSON veri kasasının biçim sürümü desteklenmiyor.");
            }

            string root = Path.Combine(workspace, "portable-json");
            Directory.CreateDirectory(root);
            string rootPrefix = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            long total = 0;
            foreach (PortableJsonFile file in package.Files)
            {
                string normalized = (file.Path ?? string.Empty).Replace('\\', '/');
                if (!(normalized.StartsWith("Data/", StringComparison.OrdinalIgnoreCase)
                    || normalized.StartsWith("Games/", StringComparison.OrdinalIgnoreCase))
                    || normalized.Contains(":", StringComparison.Ordinal)
                    || normalized.Split('/').Any(part => part == ".."))
                {
                    throw new InvalidDataException($"JSON veri kasasında güvenli olmayan yol: {normalized}");
                }

                byte[] bytes;
                try { bytes = Convert.FromBase64String(file.Base64 ?? string.Empty); }
                catch (FormatException)
                {
                    throw new InvalidDataException($"JSON veri kasasında bozuk Base64: {normalized}");
                }
                total += bytes.LongLength;
                if (total > MaxArchiveBytes
                    || bytes.LongLength != file.Size
                    || !string.Equals(
                        ComputeSha256(bytes),
                        file.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"JSON veri kasası bütünlük denetimi başarısız: {normalized}");
                }

                string target = Path.GetFullPath(Path.Combine(
                    root,
                    normalized.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("JSON veri kasası hedef kökün dışına çıkıyor.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
            }

            return new SourceDescriptor(
                root,
                "alekdata-json-v1",
                package.ArchitectureRevision);
        }

        private static bool LooksLikeZipArchive(string path)
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            int first = stream.ReadByte();
            int second = stream.ReadByte();
            return first == 0x50 && second == 0x4b;
        }

        private SourceDescriptor PrepareDatabaseSource(string selectedPath, string workspace)
        {
            string selected = Path.GetFullPath(selectedPath);
            if (DatabaseHasTable(selected, "adventures"))
            {
                DirectoryInfo? dataDirectory = new FileInfo(selected).Directory;
                DirectoryInfo? probableRoot = dataDirectory?.Name.Equals(
                    "Data",
                    StringComparison.OrdinalIgnoreCase) == true
                    ? dataDirectory.Parent
                    : dataDirectory;
                if (probableRoot != null
                    && (Directory.Exists(Path.Combine(probableRoot.FullName, "Games"))
                        || Directory.Exists(Path.Combine(probableRoot.FullName, "Data"))))
                {
                    return new SourceDescriptor(probableRoot.FullName, "portable-registry-db", 0);
                }
            }

            if (!DatabaseHasTable(selected, "documents"))
                throw new InvalidDataException("Seçilen SQLite dosyası Meggy veritabanı olarak tanınmadı.");

            string root = Path.Combine(workspace, "single-game-db");
            string gameFolder = Path.Combine(root, "Games", Path.GetFileNameWithoutExtension(selected));
            Directory.CreateDirectory(gameFolder);
            SnapshotDatabase(selected, Path.Combine(gameFolder, "game.db"));
            return new SourceDescriptor(root, "single-game-db", 0);
        }

        private static void ExtractArchiveSafe(string archivePath, string destination)
        {
            Directory.CreateDirectory(destination);
            string destinationRoot = Path.GetFullPath(destination)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            long extractedBytes = 0;
            int entryCount = 0;

            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                entryCount++;
                if (entryCount > MaxArchiveEntries)
                    throw new InvalidDataException("Arşiv güvenli dosya sayısı sınırını aşıyor.");

                extractedBytes += entry.Length;
                if (entry.Length < 0 || extractedBytes > MaxArchiveBytes)
                    throw new InvalidDataException("Arşiv güvenli veri boyutu sınırını aşıyor.");

                string normalized = entry.FullName.Replace('\\', '/');
                if (normalized.StartsWith("/", StringComparison.Ordinal)
                    || normalized.Contains(":", StringComparison.Ordinal)
                    || normalized.Split('/').Any(part => part == ".."))
                {
                    throw new InvalidDataException("Arşivde güvenli olmayan bir yol bulundu.");
                }

                string target = Path.GetFullPath(Path.Combine(
                    destination,
                    normalized.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Arşiv hedef klasör dışına çıkmaya çalışıyor.");

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using Stream input = entry.Open();
                using FileStream output = new FileStream(
                    target,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None);
                input.CopyTo(output);
            }
        }

        private static PackageManifest? VerifyPackageIfPresent(
            string extractRoot,
            out string? packageRoot)
        {
            packageRoot = null;
            string? manifestPath = Directory.EnumerateFiles(
                extractRoot,
                ManifestName,
                SearchOption.AllDirectories).OrderBy(path => path.Length).FirstOrDefault();
            if (manifestPath == null)
                return null;

            PackageManifest manifest = JsonSerializer.Deserialize<PackageManifest>(
                File.ReadAllText(manifestPath, Encoding.UTF8),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Veri paketi manifesti okunamadı.");

            if (!string.Equals(manifest.Format, PackageFormat, StringComparison.Ordinal)
                || manifest.FormatVersion < 1
                || manifest.FormatVersion > PackageFormatVersion)
            {
                throw new InvalidDataException("Veri paketinin biçim sürümü desteklenmiyor.");
            }

            packageRoot = Path.GetDirectoryName(manifestPath)!;
            string rootPrefix = Path.GetFullPath(packageRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            foreach (PackageFile expected in manifest.Files ?? new List<PackageFile>())
            {
                string path = Path.GetFullPath(Path.Combine(
                    packageRoot,
                    expected.Path.Replace('/', Path.DirectorySeparatorChar)));
                if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
                    || !File.Exists(path))
                {
                    throw new InvalidDataException($"Veri paketinde dosya eksik: {expected.Path}");
                }

                var info = new FileInfo(path);
                if (info.Length != expected.Size
                    || !string.Equals(
                        ComputeSha256(path),
                        expected.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Veri paketi bütünlük denetimi başarısız: {expected.Path}");
                }
            }

            return manifest;
        }

        private static string FindDataRoot(string extractRoot)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.GetFullPath(extractRoot)
            };

            foreach (string directory in Directory.EnumerateDirectories(
                extractRoot,
                "*",
                SearchOption.AllDirectories))
            {
                candidates.Add(directory);
                string name = Path.GetFileName(directory);
                if (name.Equals("Data", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Games", StringComparison.OrdinalIgnoreCase))
                {
                    string? parent = Path.GetDirectoryName(directory);
                    if (parent != null)
                        candidates.Add(parent);
                }
            }

            string? best = candidates
                .Select(path => new
                {
                    Path = path,
                    Score = (Directory.Exists(Path.Combine(path, "Data")) ? 5 : 0)
                        + (Directory.Exists(Path.Combine(path, "Games")) ? 5 : 0)
                        + (File.Exists(Path.Combine(path, "Data", "meggy.db")) ? 4 : 0)
                })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Path.Length)
                .Select(item => item.Path)
                .FirstOrDefault();

            return best ?? throw new InvalidDataException(
                "Arşiv içinde Data veya Games veri kökü bulunamadı.");
        }

        private List<SourceAdventure> DiscoverSourceAdventures(string sourceRoot)
        {
            string data = Path.Combine(sourceRoot, "Data");
            string games = Path.Combine(sourceRoot, "Games");
            string registry = Path.Combine(data, "meggy.db");
            var result = File.Exists(registry)
                ? ReadRegistryRows(registry).Select(row => new SourceAdventure(row)).ToList()
                : new List<SourceAdventure>();

            var byFolder = new Dictionary<string, SourceAdventure>(StringComparer.OrdinalIgnoreCase);
            foreach (SourceAdventure adventure in result)
            {
                adventure.SourceGameFolder = Directory.Exists(Path.Combine(games, adventure.Folder))
                    ? Path.Combine(games, adventure.Folder)
                    : null;
                byFolder[adventure.Folder] = adventure;
            }

            if (Directory.Exists(games))
            {
                foreach (string gameDirectory in Directory.EnumerateDirectories(games))
                {
                    string folder = Path.GetFileName(gameDirectory);
                    if (folder.StartsWith(".", StringComparison.Ordinal))
                        continue;
                    if (!byFolder.TryGetValue(folder, out SourceAdventure? adventure))
                    {
                        var row = new AdventureRecord
                        {
                            Id = "import_" + Guid.NewGuid().ToString("N"),
                            Name = folder,
                            Folder = folder,
                            Mode = ReadGameMode(Path.Combine(gameDirectory, "game.db")),
                            CoverPolicy = "none",
                            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                            UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
                        };
                        adventure = new SourceAdventure(row);
                        result.Add(adventure);
                        byFolder[folder] = adventure;
                    }
                    adventure.SourceGameFolder = gameDirectory;
                    adventure.LegacyDocuments.AddRange(DiscoverLegacyDocuments(gameDirectory));
                }
            }

            AddRootLegacyAdventures(data, result);
            return result
                .Where(item => item.SourceGameFolder != null || item.LegacyDocuments.Count > 0)
                .ToList();
        }

        private static void AddRootLegacyAdventures(
            string dataFolder,
            List<SourceAdventure> result)
        {
            if (!Directory.Exists(dataFolder))
                return;

            List<LegacyDocument> shared = DiscoverLegacyDocuments(dataFolder)
                .Where(item => item.Key != "story")
                .ToList();
            var journeys = new List<string>();
            string story = Path.Combine(dataFolder, "Story.json");
            if (File.Exists(story))
                journeys.Add(story);
            string journeyFolder = Path.Combine(dataFolder, "Journey");
            if (Directory.Exists(journeyFolder))
                journeys.AddRange(Directory.EnumerateFiles(journeyFolder, "*.json"));

            if (journeys.Count == 0 && shared.Count == 0)
                return;
            if (journeys.Count == 0)
                journeys.Add(string.Empty);

            foreach (string journey in journeys.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string name = string.IsNullOrEmpty(journey)
                    ? "Eski Meggy Dünyası"
                    : Path.GetFileNameWithoutExtension(journey);
                if (name.Equals("Story", StringComparison.OrdinalIgnoreCase))
                    name = "Eski Meggy Dünyası";

                var row = new AdventureRecord
                {
                    Id = "legacy_" + Guid.NewGuid().ToString("N"),
                    Name = name,
                    Folder = NormalizeFolderName(name),
                    Mode = "other_realm",
                    CoverPolicy = "none",
                    CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                    UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
                };
                var adventure = new SourceAdventure(row);
                if (!string.IsNullOrEmpty(journey))
                    adventure.LegacyDocuments.Add(new LegacyDocument("story", journey));
                adventure.LegacyDocuments.AddRange(shared);
                result.Add(adventure);
            }
        }

        private ImportResult ApplyImport(
            IReadOnlyList<SourceAdventure> sourceAdventures,
            SourceDescriptor source)
        {
            Directory.CreateDirectory(_dataFolder);
            Directory.CreateDirectory(_gamesFolder);
            List<AdventureRecord> existing = File.Exists(_registryPath)
                ? ReadRegistryRows(_registryPath)
                : new List<AdventureRecord>();
            var usedIds = new HashSet<string>(existing.Select(item => item.Id), StringComparer.Ordinal);
            var usedFolders = new HashSet<string>(
                existing.Select(item => item.Folder),
                StringComparer.OrdinalIgnoreCase);
            var usedNames = new HashSet<string>(
                existing.Select(item => item.Name),
                StringComparer.OrdinalIgnoreCase);
            foreach (string folder in Directory.EnumerateDirectories(_gamesFolder))
                usedFolders.Add(Path.GetFileName(folder));

            string importTag = DateTime.Now.ToString("yyyy-MM-dd HH.mm");
            string stagingRoot = Path.Combine(
                _rootFolder,
                ".alek-import-" + Guid.NewGuid().ToString("N"));
            string stagingGames = Path.Combine(stagingRoot, "Games");
            Directory.CreateDirectory(stagingGames);

            var prepared = new List<PreparedAdventure>();
            var moved = new List<string>();
            int documentCount = 0;
            int mediaCount = 0;

            try
            {
                foreach (SourceAdventure sourceAdventure in sourceAdventures)
                {
                    string name = MakeUniqueName(sourceAdventure.Name, usedNames, importTag);
                    string folder = MakeUniqueFolder(
                        sourceAdventure.Folder,
                        usedFolders,
                        importTag);
                    string id = string.IsNullOrWhiteSpace(sourceAdventure.Id)
                        || !usedIds.Add(sourceAdventure.Id)
                        ? "adventure_import_" + Guid.NewGuid().ToString("N")
                        : sourceAdventure.Id;
                    usedFolders.Add(folder);
                    usedNames.Add(name);

                    string stagedGame = Path.Combine(stagingGames, folder);
                    Directory.CreateDirectory(stagedGame);
                    if (sourceAdventure.SourceGameFolder != null)
                    {
                        mediaCount += CopyGameFolderPayload(
                            sourceAdventure.SourceGameFolder,
                            stagedGame);
                    }

                    string sourceDb = sourceAdventure.SourceGameFolder == null
                        ? string.Empty
                        : Path.Combine(sourceAdventure.SourceGameFolder, "game.db");
                    string targetDb = Path.Combine(stagedGame, "game.db");
                    if (File.Exists(sourceDb))
                        SnapshotDatabase(sourceDb, targetDb);

                    using (SqliteConnection connection = PortableGameStore.OpenDatabase(targetDb))
                    {
                        PortableGameStore.EnsureGameSchema(connection);
                        documentCount += NormalizeDatabaseDocumentKeys(connection);
                        foreach (LegacyDocument legacy in sourceAdventure.LegacyDocuments)
                            documentCount += ImportLegacyDocument(connection, legacy);
                        PortableGameStore.SetGameMeta(connection, "folder", folder);
                        PortableGameStore.SetGameMeta(
                            connection,
                            "mode",
                            NormalizeMode(sourceAdventure.Mode));
                        PortableGameStore.SetGameMeta(
                            connection,
                            "data_transfer_format",
                            PackageFormatVersion.ToString());
                        PortableGameStore.SetGameMeta(
                            connection,
                            "imported_from_revision",
                            source.Revision.ToString());
                        PortableGameStore.SetGameMeta(
                            connection,
                            "imported_utc",
                            DateTimeOffset.UtcNow.ToString("O"));
                    }
                    PortableGameStore.EnsureMediaFolders(stagedGame);

                    string cover = RewriteCoverPath(
                        sourceAdventure.Cover,
                        sourceAdventure.Folder,
                        folder,
                        stagedGame);
                    var targetRecord = new AdventureRecord
                    {
                        Id = id,
                        Name = name,
                        Folder = folder,
                        Cover = cover,
                        Mode = NormalizeMode(sourceAdventure.Mode),
                        CoverPolicy = string.IsNullOrWhiteSpace(sourceAdventure.CoverPolicy)
                            ? "none"
                            : sourceAdventure.CoverPolicy,
                        CreatedAt = string.IsNullOrWhiteSpace(sourceAdventure.CreatedAt)
                            ? DateTimeOffset.UtcNow.ToString("O")
                            : sourceAdventure.CreatedAt,
                        UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
                    };
                    prepared.Add(new PreparedAdventure(targetRecord, stagedGame));
                }

                foreach (PreparedAdventure adventure in prepared)
                {
                    string target = Path.Combine(_gamesFolder, adventure.Record.Folder);
                    if (Directory.Exists(target) || File.Exists(target))
                        throw new IOException($"Hedef macera klasörü zaten var: {adventure.Record.Folder}");
                    Directory.Move(adventure.StagedFolder, target);
                    moved.Add(target);
                }

                WriteImportedRegistryRows(
                    prepared.Select(item => item.Record).ToList(),
                    prepared[0].Record.Id);

                return new ImportResult(
                    prepared.Count,
                    documentCount,
                    mediaCount,
                    prepared.Select(item => item.Record.Name).ToArray());
            }
            catch
            {
                foreach (string folder in moved)
                    TryDeleteDirectory(folder);
                throw;
            }
            finally
            {
                TryDeleteDirectory(stagingRoot);
            }
        }

        private void WriteImportedRegistryRows(
            IReadOnlyList<AdventureRecord> rows,
            string activeId)
        {
            using SqliteConnection connection = PortableGameStore.OpenDatabase(_registryPath);
            PortableGameStore.EnsureRegistrySchema(connection);
            using SqliteTransaction transaction = connection.BeginTransaction();
            foreach (AdventureRecord row in rows)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT INTO adventures
                        (id, name, folder, cover, mode, cover_policy, created_at, updated_at)
                    VALUES
                        ($id, $name, $folder, $cover, $mode, $coverPolicy, $createdAt, $updatedAt);";
                command.Parameters.AddWithValue("$id", row.Id);
                command.Parameters.AddWithValue("$name", row.Name);
                command.Parameters.AddWithValue("$folder", row.Folder);
                command.Parameters.AddWithValue("$cover", row.Cover);
                command.Parameters.AddWithValue("$mode", row.Mode);
                command.Parameters.AddWithValue("$coverPolicy", row.CoverPolicy);
                command.Parameters.AddWithValue("$createdAt", row.CreatedAt);
                command.Parameters.AddWithValue("$updatedAt", row.UpdatedAt);
                command.ExecuteNonQuery();
            }

            using (SqliteCommand state = connection.CreateCommand())
            {
                state.Transaction = transaction;
                state.CommandText = @"
                    INSERT INTO app_state(key, value)
                    VALUES('active_adventure_id', $activeId)
                    ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
                state.Parameters.AddWithValue("$activeId", activeId);
                state.ExecuteNonQuery();
            }
            transaction.Commit();
        }

        private static List<AdventureRecord> ReadRegistryRows(string databasePath)
        {
            var rows = new List<AdventureRecord>();
            if (!DatabaseHasTable(databasePath, "adventures"))
                return rows;

            using SqliteConnection connection = OpenReadOnlyDatabase(databasePath);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM adventures;";
            using SqliteDataReader reader = command.ExecuteReader();
            var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < reader.FieldCount; index++)
                ordinals[reader.GetName(index)] = index;

            while (reader.Read())
            {
                string name = ReadColumn(reader, ordinals, "name", "İçe Aktarılan Macera");
                string folder = ReadColumn(reader, ordinals, "folder", name);
                rows.Add(new AdventureRecord
                {
                    Id = ReadColumn(reader, ordinals, "id", "import_" + Guid.NewGuid().ToString("N")),
                    Name = name,
                    Folder = NormalizeFolderName(folder),
                    Cover = ReadColumn(reader, ordinals, "cover", string.Empty),
                    Mode = NormalizeMode(ReadColumn(reader, ordinals, "mode", "other_realm")),
                    CoverPolicy = ReadColumn(reader, ordinals, "cover_policy", "none"),
                    CreatedAt = ReadColumn(reader, ordinals, "created_at", DateTimeOffset.UtcNow.ToString("O")),
                    UpdatedAt = ReadColumn(reader, ordinals, "updated_at", DateTimeOffset.UtcNow.ToString("O"))
                });
            }
            return rows;
        }

        private static string ReadColumn(
            SqliteDataReader reader,
            IReadOnlyDictionary<string, int> ordinals,
            string name,
            string fallback)
        {
            if (!ordinals.TryGetValue(name, out int ordinal) || reader.IsDBNull(ordinal))
                return fallback;
            return Convert.ToString(reader.GetValue(ordinal)) ?? fallback;
        }

        private static int NormalizeDatabaseDocumentKeys(SqliteConnection connection)
        {
            var documents = new List<(string Key, string Json, int Schema)>();
            using (SqliteCommand read = connection.CreateCommand())
            {
                read.CommandText = "SELECT document_key, json_text, schema_version FROM documents;";
                using SqliteDataReader reader = read.ExecuteReader();
                while (reader.Read())
                    documents.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
            }

            int migrated = 0;
            foreach ((string key, string json, int schema) in documents)
            {
                string canonical = CanonicalDocumentKey(key);
                if (canonical == key)
                    continue;
                using SqliteCommand write = connection.CreateCommand();
                write.CommandText = @"
                    INSERT INTO documents(document_key, json_text, schema_version, updated_utc)
                    VALUES($key, $json, $schema, $updated)
                    ON CONFLICT(document_key) DO NOTHING;";
                write.Parameters.AddWithValue("$key", canonical);
                write.Parameters.AddWithValue("$json", json);
                write.Parameters.AddWithValue("$schema", schema);
                write.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                migrated += write.ExecuteNonQuery();
            }
            return migrated;
        }

        private static int ImportLegacyDocument(
            SqliteConnection connection,
            LegacyDocument document)
        {
            string json = File.ReadAllText(document.Path, Encoding.UTF8);
            using JsonDocument parsed = JsonDocument.Parse(json);
            int schema = ReadJsonSchemaVersion(parsed.RootElement);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO documents(document_key, json_text, schema_version, updated_utc)
                VALUES($key, $json, $schema, $updated)
                ON CONFLICT(document_key) DO NOTHING;";
            command.Parameters.AddWithValue("$key", document.Key);
            command.Parameters.AddWithValue("$json", json);
            command.Parameters.AddWithValue("$schema", Math.Max(1, schema));
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            return command.ExecuteNonQuery();
        }

        private static int ReadJsonSchemaVersion(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("meta", out JsonElement meta)
                && meta.ValueKind == JsonValueKind.Object
                && meta.TryGetProperty("schemaVersion", out JsonElement schema))
            {
                if (schema.ValueKind == JsonValueKind.Number && schema.TryGetInt32(out int value))
                    return value;
                if (schema.ValueKind == JsonValueKind.String
                    && int.TryParse(schema.GetString(), out value))
                    return value;
            }
            return 1;
        }

        private static List<LegacyDocument> DiscoverLegacyDocuments(string directory)
        {
            var result = new List<LegacyDocument>();
            if (!Directory.Exists(directory))
                return result;
            foreach (string file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                if (LegacyDocumentKeys.TryGetValue(Path.GetFileName(file), out string? key))
                    result.Add(new LegacyDocument(key, file));
            }
            return result;
        }

        private static int CopyGameFolderPayload(string source, string destination)
        {
            int mediaFiles = 0;
            string sourceRoot = Path.GetFullPath(source)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(file);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0
                    || info.Name.Equals("game.db", StringComparison.OrdinalIgnoreCase)
                    || IsTransientDatabaseFile(file)
                    || LegacyDocumentKeys.ContainsKey(info.Name))
                {
                    continue;
                }

                string relative = Path.GetRelativePath(sourceRoot, file);
                string target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, true);
                if (relative.StartsWith("Media" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    mediaFiles++;
            }
            return mediaFiles;
        }

        private static string RewriteCoverPath(
            string sourceCover,
            string sourceFolder,
            string targetFolder,
            string stagedGame)
        {
            string cover = (sourceCover ?? string.Empty).Replace('\\', '/');
            string oldPrefix = "Games/" + sourceFolder + "/";
            if (cover.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                cover = "Games/" + targetFolder + "/" + cover.Substring(oldPrefix.Length);

            string stagedCover = Path.Combine(stagedGame, "Media", "Adventure", "cover.png");
            if (string.IsNullOrWhiteSpace(cover) && File.Exists(stagedCover))
                cover = "Games/" + targetFolder + "/Media/Adventure/cover.png";
            return cover;
        }

        private static string MakeUniqueName(
            string source,
            ISet<string> used,
            string importTag)
        {
            string baseName = string.IsNullOrWhiteSpace(source)
                ? "İçe Aktarılan Macera"
                : source.Trim();
            if (!used.Contains(baseName))
                return baseName;
            string candidate = $"{baseName} · İçe Aktarılan {importTag}";
            int index = 2;
            while (used.Contains(candidate))
                candidate = $"{baseName} · İçe Aktarılan {importTag} ({index++})";
            return candidate;
        }

        private static string MakeUniqueFolder(
            string source,
            ISet<string> used,
            string importTag)
        {
            string baseFolder = NormalizeFolderName(source);
            if (!used.Contains(baseFolder))
                return baseFolder;
            string suffix = NormalizeFolderName("Iceri Aktarilan " + importTag);
            string candidate = NormalizeFolderName(baseFolder + " - " + suffix);
            int index = 2;
            while (used.Contains(candidate))
                candidate = NormalizeFolderName(baseFolder + " - " + suffix + " " + index++);
            return candidate;
        }

        private static string NormalizeFolderName(string source)
        {
            string value = (source ?? string.Empty).Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '-');
            value = Regex.Replace(value, @"\s+", " ").Trim().TrimEnd('.', ' ');
            if (string.IsNullOrWhiteSpace(value))
                value = "Iceri Aktarilan Macera";
            if (value.Length > 110)
                value = value.Substring(0, 110).TrimEnd('.', ' ');
            return value;
        }

        private static string NormalizeMode(string? mode)
        {
            string value = mode ?? string.Empty;
            return value == "alekrytha" || value == "real_world" || value == "other_realm"
                ? value
                : "other_realm";
        }

        private static string ReadGameMode(string databasePath)
        {
            if (!File.Exists(databasePath) || !DatabaseHasTable(databasePath, "game_meta"))
                return "other_realm";
            try
            {
                using SqliteConnection connection = OpenReadOnlyDatabase(databasePath);
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "SELECT value FROM game_meta WHERE key = 'mode' LIMIT 1;";
                return NormalizeMode(command.ExecuteScalar() as string);
            }
            catch
            {
                return "other_realm";
            }
        }

        private static string CanonicalDocumentKey(string key)
        {
            string value = (key ?? string.Empty).Trim();
            if (LegacyDocumentKeys.TryGetValue(
                value.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    ? value
                    : value + ".json",
                out string? canonical))
            {
                return canonical;
            }

            string lower = value.ToLowerInvariant();
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["journey"] = "story",
                ["character"] = "characters",
                ["location"] = "locations",
                ["group"] = "groups",
                ["mount"] = "mounts",
                ["afmv"] = "special",
                ["tideofaia"] = "tide"
            };
            if (aliases.TryGetValue(lower, out canonical))
                return canonical;
            return SafeDocumentKey.IsMatch(value) ? value : "legacy_" + Guid.NewGuid().ToString("N");
        }

        private static bool DatabaseHasTable(string databasePath, string table)
        {
            try
            {
                using SqliteConnection connection = OpenReadOnlyDatabase(databasePath);
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT EXISTS(
                        SELECT 1 FROM sqlite_master
                        WHERE type = 'table' AND name = $table
                    );";
                command.Parameters.AddWithValue("$table", table);
                return Convert.ToInt64(command.ExecuteScalar() ?? 0) == 1;
            }
            catch
            {
                return false;
            }
        }

        private static SqliteConnection OpenReadOnlyDatabase(string path)
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            };
            var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            return connection;
        }

        private static void SnapshotDatabase(string sourcePath, string destinationPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            TryDeleteFile(destinationPath);
            using SqliteConnection source = OpenReadOnlyDatabase(sourcePath);
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = destinationPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            };
            using var destination = new SqliteConnection(builder.ToString());
            destination.Open();
            source.BackupDatabase(destination);
        }

        private static string ComputeSha256(string path)
        {
            using SHA256 sha = SHA256.Create();
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using SHA256 sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
        }

        private static bool IsTransientDatabaseFile(string path)
        {
            return path.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("-wal", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("-shm", StringComparison.OrdinalIgnoreCase);
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
        }

        private sealed class UserFile
        {
            public UserFile(string fullPath, string archivePath, bool isDatabase)
            {
                FullPath = fullPath;
                ArchivePath = archivePath;
                IsDatabase = isDatabase;
            }
            public string FullPath { get; }
            public string ArchivePath { get; }
            public bool IsDatabase { get; }
        }

        private sealed class PackageManifest
        {
            public string Format { get; set; } = PackageFormat;
            public int FormatVersion { get; set; }
            public string App { get; set; } = string.Empty;
            public string AppVersion { get; set; } = string.Empty;
            public int ArchitectureRevision { get; set; }
            public string Storage { get; set; } = string.Empty;
            public string Reason { get; set; } = string.Empty;
            public string CreatedUtc { get; set; } = string.Empty;
            public int GameCount { get; set; }
            public List<PackageFile> Files { get; set; } = new List<PackageFile>();
        }

        private sealed class PackageFile
        {
            public string Path { get; set; } = string.Empty;
            public long Size { get; set; }
            public string Sha256 { get; set; } = string.Empty;
        }

        private sealed class PortableJsonPackage
        {
            public string Format { get; set; } = string.Empty;
            public int FormatVersion { get; set; }
            public int ArchitectureRevision { get; set; }
            public List<PortableJsonFile> Files { get; set; } = new List<PortableJsonFile>();
        }

        private sealed class PortableJsonFile
        {
            public string Path { get; set; } = string.Empty;
            public long Size { get; set; }
            public string Sha256 { get; set; } = string.Empty;
            public string Base64 { get; set; } = string.Empty;
        }

        private sealed class AdventureRecord
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Folder { get; set; } = string.Empty;
            public string Cover { get; set; } = string.Empty;
            public string Mode { get; set; } = "other_realm";
            public string CoverPolicy { get; set; } = "none";
            public string CreatedAt { get; set; } = string.Empty;
            public string UpdatedAt { get; set; } = string.Empty;
        }

        private sealed class SourceAdventure
        {
            public SourceAdventure(AdventureRecord row)
            {
                Id = row.Id;
                Name = row.Name;
                Folder = row.Folder;
                Cover = row.Cover;
                Mode = row.Mode;
                CoverPolicy = row.CoverPolicy;
                CreatedAt = row.CreatedAt;
                UpdatedAt = row.UpdatedAt;
            }
            public string Id { get; }
            public string Name { get; }
            public string Folder { get; }
            public string Cover { get; }
            public string Mode { get; }
            public string CoverPolicy { get; }
            public string CreatedAt { get; }
            public string UpdatedAt { get; }
            public string? SourceGameFolder { get; set; }
            public List<LegacyDocument> LegacyDocuments { get; } = new List<LegacyDocument>();
        }

        private sealed class LegacyDocument
        {
            public LegacyDocument(string key, string path)
            {
                Key = key;
                Path = path;
            }
            public string Key { get; }
            public string Path { get; }
        }

        private sealed class PreparedAdventure
        {
            public PreparedAdventure(AdventureRecord record, string stagedFolder)
            {
                Record = record;
                StagedFolder = stagedFolder;
            }
            public AdventureRecord Record { get; }
            public string StagedFolder { get; }
        }

        private sealed class SourceDescriptor
        {
            public SourceDescriptor(string root, string format, int revision)
            {
                Root = Path.GetFullPath(root);
                Format = format;
                Revision = revision;
            }
            public string Root { get; }
            public string Format { get; }
            public int Revision { get; }
        }

        private sealed class PackageBuildResult
        {
            public PackageBuildResult(
                string path,
                int fileCount,
                int gameCount,
                long totalBytes)
            {
                Path = path;
                FileCount = fileCount;
                GameCount = gameCount;
                TotalBytes = totalBytes;
            }
            public string Path { get; }
            public int FileCount { get; }
            public int GameCount { get; }
            public long TotalBytes { get; }
        }

        private sealed class ImportResult
        {
            public ImportResult(
                int adventureCount,
                int documentCount,
                int mediaFileCount,
                string[] names)
            {
                AdventureCount = adventureCount;
                DocumentCount = documentCount;
                MediaFileCount = mediaFileCount;
                Names = names;
            }
            public int AdventureCount { get; }
            public int DocumentCount { get; }
            public int MediaFileCount { get; }
            public string[] Names { get; }
        }
    }
}
