using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AlekrythaeCore
{
    internal static class TaxonomyEnergyIndexWriter
    {
        private static readonly Regex LayerRegex =
            new(
                @"^(?<tag>[MTPS])-(?<visibility>\d{1,3})-(?<seconds>\d+(?:\.\d+)?)(?:-(?<variant>[A-Za-z0-9_]+))?\.png$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly IReadOnlyDictionary<string, int> TagOrder =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["P"] = 0,
                ["S"] = 1,
                ["M"] = 2,
                ["T"] = 3
            };

        public static void Write(
            string taxonomyAssetDirectory,
            string frameFileName = "taxonomy_frame.png",
            string maskFileName = "taxonomy_energy_channel.png")
        {
            if (!Directory.Exists(taxonomyAssetDirectory))
                return;

            var layers = Directory
                .EnumerateFiles(taxonomyAssetDirectory, "*.png", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Select(ParseLayer)
                .Where(static x => x is not null)
                .Select(static x => x!)
                .OrderBy(static x => TagOrder.TryGetValue(x.ColorTag, out int order) ? order : int.MaxValue)
                .ThenByDescending(static x => x.Visibility)
                .ThenByDescending(static x => x.DurationSeconds)
                .ThenBy(static x => x.Variant, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static x => x.FileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var model = new TaxonomyEnergyLayerIndex(
                frameFileName,
                maskFileName,
                layers.Select(static x => new TaxonomyEnergyLayerItem(
                    FileName: x.FileName,
                    ColorTag: x.ColorTag,
                    Visibility: x.Visibility,
                    Opacity: Math.Clamp(x.Visibility / 100d, 0d, 1d),
                    DurationSeconds: x.DurationSeconds,
                    Variant: x.Variant)).ToArray());

            string outputPath = Path.Combine(taxonomyAssetDirectory, "taxonomy-energy-index.json");
            var json = JsonSerializer.Serialize(
                model,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            File.WriteAllText(outputPath, json);
        }

        private static ParsedLayer? ParseLayer(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return null;

            var match = LayerRegex.Match(fileName);
            if (!match.Success)
                return null;

            string colorTag = match.Groups["tag"].Value.ToUpperInvariant();
            string variant = match.Groups["variant"].Success
                ? match.Groups["variant"].Value
                : string.Empty;

            if (!int.TryParse(
                    match.Groups["visibility"].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int visibility))
                return null;

            if (!double.TryParse(
                    match.Groups["seconds"].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double seconds))
                return null;

            visibility = Math.Clamp(visibility, 0, 100);
            seconds = Math.Max(0.05d, seconds);

            return new ParsedLayer(fileName, colorTag, visibility, seconds, variant);
        }

        private sealed record ParsedLayer(
            string FileName,
            string ColorTag,
            int Visibility,
            double DurationSeconds,
            string Variant);

        internal sealed record TaxonomyEnergyLayerIndex(
            string FrameFile,
            string MaskFile,
            TaxonomyEnergyLayerItem[] Layers);

        internal sealed record TaxonomyEnergyLayerItem(
            string FileName,
            string ColorTag,
            int Visibility,
            double Opacity,
            double DurationSeconds,
            string Variant);
    }
}
