using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ValheimRadar
{
    // Readers for the pipe-delimited .txt save files used before PinDatabase (v1.11.0 and older):
    //   <world>.txt            - resource points, "#v<N>" header, 8 fields per line
    //   <world>.locations.txt  - Locations, "#locv1" header, 7 fields per line
    //   <world>.dismissed.txt  - DismissedPinStore.Load
    //   <world>.respawn.txt    - RespawnTimerStore.Load
    // Only used to import them once into the world's database (see PinManager.OpenWorld), after
    // which they're deleted. Pure parsing, no file access, so it can be unit tested.
    internal static class LegacyPinFiles
    {
        internal const string PointsSuffix = ".txt";
        internal const string LocationsSuffix = ".locations.txt";
        internal const string DismissedSuffix = ".dismissed.txt";
        internal const string RespawnSuffix = ".respawn.txt";

        // No version header (or an unparseable one) = the format shipped before versioning existed.
        internal const int PreVersioningFormat = 0;

        // Parses resource points (Key left null - PinManager assigns it when storing) and reports
        // the file's format version, which decides whether category keys need migrating.
        internal static List<PointRecord> ParsePoints(IList<string> lines, out int formatVersion)
        {
            formatVersion = PreVersioningFormat;
            var result = new List<PointRecord>();
            if (lines == null) return result;

            int startIndex = 0;
            if (lines.Count > 0 && lines[0].StartsWith("#v", StringComparison.Ordinal) &&
                int.TryParse(lines[0].Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedVersion))
            {
                formatVersion = parsedVersion;
                startIndex = 1;
            }

            for (int i = startIndex; i < lines.Count; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;

                string[] parts = line.Split('|');
                if (parts.Length != 8) continue;

                if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long userId)) continue;
                if (!uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id)) continue;
                if (!TryParseFloat(parts[2], out float x) || !TryParseFloat(parts[3], out float y) || !TryParseFloat(parts[4], out float z)) continue;

                string displayName = Unescape(parts[5]);
                if (string.IsNullOrEmpty(displayName)) continue;

                result.Add(new PointRecord
                {
                    UserId = userId,
                    Id = id,
                    Position = new Vector3(x, y, z),
                    DisplayName = displayName,
                    RawName = Unescape(parts[6]),
                    CategoryKey = Unescape(parts[7])
                });
            }

            return result;
        }

        internal static List<TrackedLocation> ParseLocations(IList<string> lines)
        {
            var result = new List<TrackedLocation>();
            if (lines == null) return result;

            int startIndex = lines.Count > 0 && lines[0].StartsWith("#locv", StringComparison.Ordinal) ? 1 : 0;
            for (int i = startIndex; i < lines.Count; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;

                string[] parts = line.Split('|');
                if (parts.Length != 7) continue;

                string locationKey = Unescape(parts[0]);
                if (!TryParseFloat(parts[1], out float x) || !TryParseFloat(parts[2], out float y) || !TryParseFloat(parts[3], out float z)) continue;

                string displayName = Unescape(parts[4]);
                if (string.IsNullOrEmpty(locationKey) || string.IsNullOrEmpty(displayName)) continue;

                result.Add(new TrackedLocation
                {
                    LocationKey = locationKey,
                    Position = new Vector3(x, y, z),
                    DisplayName = displayName,
                    RawName = Unescape(parts[5]),
                    CategoryKey = Unescape(parts[6])
                });
            }

            return result;
        }

        private static bool TryParseFloat(string s, out float value) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        private static string Unescape(string value) =>
            string.IsNullOrEmpty(value) ? string.Empty : Uri.UnescapeDataString(value);
    }
}
