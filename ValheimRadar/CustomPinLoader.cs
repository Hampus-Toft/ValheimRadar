using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace ValheimRadar
{
    public static class CustomPinLoader
    {
        // Cache generated pin types to avoid duplicate registrations
        private static readonly Dictionary<string, Minimap.PinType> RegisteredCustomPins = new Dictionary<string, Minimap.PinType>();

        /// <summary>
        /// Registers a PNG image directly into Valheim's Minimap sprite array.
        /// </summary>
        /// <param name="filePath">Full path to the PNG image file.</param>
        /// <param name="fallback">Fallback PinType if the file is missing.</param>
        /// <returns>The newly registered Minimap.PinType enum value.</returns>
        public static Minimap.PinType RegisterPngAsPin(string filePath, Minimap.PinType fallback)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"[ValheimRadar] Custom pin file not found: {filePath}. Using fallback pin.");
                return fallback;
            }

            if (RegisteredCustomPins.TryGetValue(filePath, out Minimap.PinType cachedType))
            {
                return cachedType;
            }

            if (Minimap.instance == null || Minimap.instance.m_icons == null)
            {
                return fallback;
            }

            try
            {
                byte[] fileData = File.ReadAllBytes(filePath);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

                if (ImageConversion.LoadImage(texture, fileData))
                {
                    // Create Unity Sprite with centered pivot
                    Sprite sprite = Sprite.Create(
                        texture,
                        new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f),
                        100f
                    );
                    sprite.name = Path.GetFileNameWithoutExtension(filePath);

                    // Build SpriteData struct and append to Minimap.m_icons
                    Minimap.SpriteData newSpriteData = new Minimap.SpriteData
                    {
                        m_name = (Minimap.PinType)Minimap.instance.m_icons.Count,
                        m_icon = sprite
                    };

                    Minimap.instance.m_icons.Add(newSpriteData);

                    Minimap.PinType newPinType = newSpriteData.m_name;
                    RegisteredCustomPins[filePath] = newPinType;

                    return newPinType;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ValheimRadar] Failed to load custom pin PNG '{filePath}': {ex.Message}");
            }

            return fallback;
        }
    }
}