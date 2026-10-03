using System;
using System.Collections.Generic;
using System.IO;
using Jotunn.Utils;
using UnityEngine;

namespace ValheimRadar
{
    /// <summary>
    /// Loads a user-supplied PNG (from the BepInEx config folder) into a Sprite via Jotunn's
    /// AssetUtils, for use as a Minimap.PinData.m_icon override. Sprites are plain Unity objects
    /// applied directly per-pin, so unlike the old approach there's no shared Minimap.m_icons list
    /// to register into or invalidate across world reloads.
    /// </summary>
    public static class IconLoader
    {
        private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

        public static Sprite LoadPng(string filePath)
        {
            if (spriteCache.TryGetValue(filePath, out Sprite cached))
            {
                return cached;
            }

            if (!File.Exists(filePath))
            {
                // Misses are cached too: most pins have no custom PNG, and loading a world resolves an
                // icon per saved point (tens of thousands), each probing up to two paths on disk.
                spriteCache[filePath] = null;
                return null;
            }

            Sprite sprite = null;
            try
            {
                Texture2D texture = AssetUtils.LoadTexture(filePath, relativePath: false);
                if (texture != null)
                {
                    sprite = Sprite.Create(
                        texture,
                        new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f),
                        100f
                    );
                    sprite.name = Path.GetFileNameWithoutExtension(filePath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ValheimRadar] Failed to load custom pin PNG '{filePath}': {ex.Message}");
            }

            spriteCache[filePath] = sprite;
            return sprite;
        }
    }
}
