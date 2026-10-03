using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ValheimRadar
{
    // Pure (no Minimap/game state) record of regrowing pickables (berries, mushrooms, flowers, crops)
    // the local player picked, each hidden from the map until its own respawn timer runs out - see
    // Pinning/DepletionPatches.cs -> PinManager.MarkPickedAt. Keyed by categoryKey + position for the
    // same reasons as DismissedPinStore.
    //
    // Times are Valheim world time in seconds (ZNet.GetTimeSeconds), the same clock Pickable compares
    // its saved pick time against (Pickable.ShouldRespawn: regrows once m_respawnTimeMinutes of world
    // time have passed). World time stops while nobody is in the world, exactly like the regrowth
    // itself, so a timer saved to disk stays correct across relogs.
    public sealed class RespawnTimerStore
    {
        public const float MatchRadius = 0.5f;

        private const string HeaderLine = "#respawnv1";

        public sealed class Entry
        {
            public string CategoryKey;
            public Vector3 Position;
            public double PickedAt;
            public double RespawnAt;
        }

        private readonly List<Entry> entries = new List<Entry>();

        public int Count => entries.Count;

        public static double GetRespawnAt(double pickedAt, float respawnMinutes) => pickedAt + respawnMinutes * 60.0;

        // Records (or restarts) the timer for the point at position.
        public void Add(string categoryKey, Vector3 position, double pickedAt, double respawnAt)
        {
            if (categoryKey == null) categoryKey = string.Empty;
            Remove(categoryKey, position);
            entries.Add(new Entry { CategoryKey = categoryKey, Position = position, PickedAt = pickedAt, RespawnAt = respawnAt });
        }

        public bool Contains(string categoryKey, Vector3 position) => IndexOf(categoryKey, position) >= 0;

        public bool Remove(string categoryKey, Vector3 position)
        {
            int index = IndexOf(categoryKey, position);
            if (index < 0) return false;

            entries.RemoveAt(index);
            return true;
        }

        // Removes and returns every entry whose pickable has grown back by now. Also expires entries
        // picked "in the future": world time only goes backwards when the world was replaced or reset
        // under the same name, and those timers mean nothing anymore.
        public List<Entry> TakeExpired(double now)
        {
            List<Entry> expired = null;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry entry = entries[i];
                if (now < entry.RespawnAt && now >= entry.PickedAt) continue;

                (expired ?? (expired = new List<Entry>())).Add(entry);
                entries.RemoveAt(i);
            }

            return expired;
        }

        public void Clear() => entries.Clear();

        public IEnumerable<Entry> Entries => entries;

        // One line per timer (categoryKey|x|y|z|pickedAt|respawnAt), same style as DismissedPinStore.
        public List<string> Serialize()
        {
            var lines = new List<string> { HeaderLine };
            foreach (Entry e in entries)
            {
                lines.Add(string.Join("|",
                    Uri.EscapeDataString(e.CategoryKey),
                    e.Position.x.ToString(CultureInfo.InvariantCulture),
                    e.Position.y.ToString(CultureInfo.InvariantCulture),
                    e.Position.z.ToString(CultureInfo.InvariantCulture),
                    e.PickedAt.ToString("R", CultureInfo.InvariantCulture),
                    e.RespawnAt.ToString("R", CultureInfo.InvariantCulture)));
            }

            return lines;
        }

        // Replaces the store's contents with the parsed lines; malformed lines and the header are skipped.
        public void Load(IEnumerable<string> lines)
        {
            Clear();
            if (lines == null) return;

            foreach (string line in lines)
            {
                if (string.IsNullOrEmpty(line) || line[0] == '#') continue;

                string[] parts = line.Split('|');
                if (parts.Length != 6) continue;
                if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) continue;
                if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) continue;
                if (!float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) continue;
                if (!double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double pickedAt)) continue;
                if (!double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double respawnAt)) continue;

                Add(Uri.UnescapeDataString(parts[0]), new Vector3(x, y, z), pickedAt, respawnAt);
            }
        }

        private int IndexOf(string categoryKey, Vector3 position)
        {
            if (entries.Count == 0) return -1;
            if (categoryKey == null) categoryKey = string.Empty;

            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                if (e.CategoryKey == categoryKey && DepletionRules.DistanceXZ(e.Position, position) <= MatchRadius) return i;
            }

            return -1;
        }
    }
}
