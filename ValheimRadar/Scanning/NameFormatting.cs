using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ValheimRadar
{
    // Name/display-text helpers shared by every type-specific evaluator (CreatureEvaluator,
    // ResourceEvaluator, PoiEvaluator) and by PinManager's icon-cache key derivation. Extracted from
    // the original monolithic ObjectEvaluator so none of the three evaluators need to depend on each
    // other just to format a name.
    internal static class NameFormatting
    {
        // Exact match against the raw, lowercased, (Clone)-stripped GameObject name - deliberately
        // NOT run through StripKnownPrefixes first. Stripping prefixes like "piece_" before matching
        // would let a player-built object collide with an unrelated natural one that happens to
        // share the same base name (e.g. "piece_beehive" stripped to "beehive" would collide with
        // the natural "Beehive" prefab). StripKnownPrefixes is still used, as before, for
        // display-name formatting and the icon-cache/PNG-override key - just not for match decisions.
        internal static bool IsExactAlias(string nameLower, params string[] aliases)
        {
            foreach (var alias in aliases)
            {
                if (nameLower == alias) return true;
            }
            return false;
        }

        internal static bool ContainsAny(string value, string[] markers)
        {
            foreach (var marker in markers)
            {
                if (value.Contains(marker)) return true;
            }

            return false;
        }

        internal static string StripKnownPrefixes(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return string.Empty;

            string clean = Regex.Replace(rawName, @"(?i)^(pickable_|item_|piece_|vfx_|sfx_)", "");
            return clean.Replace("(Clone)", "").Trim();
        }

        internal static string FormatHumanFriendlyName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return string.Empty;

            string clean = StripKnownPrefixes(rawName);
            clean = clean.Replace('_', ' ');
            clean = Regex.Replace(clean, @"(?<=[a-z])(?=[A-Z])", " ");
            clean = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(clean.ToLower());

            return clean;
        }

        // Shared "what should this object's label be before any rule-specific override" derivation -
        // HoverText first (what the player would actually see when looking at it), falling back to
        // Character.GetHoverName() for creatures without their own HoverText, then the raw
        // GameObject name as a last resort.
        internal static string DeriveDisplayName(GameObject go, Character character)
        {
            string displayName = string.Empty;

            HoverText hover = go.GetComponent<HoverText>();
            if (hover != null && !string.IsNullOrEmpty(hover.m_text))
            {
                displayName = hover.m_text;
            }
            else if (character != null)
            {
                displayName = character.GetHoverName();
            }

            if (string.IsNullOrEmpty(displayName))
            {
                displayName = go.name;
            }

            return FormatHumanFriendlyName(displayName);
        }
    }
}
