using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace ValheimRadar
{
    public static class CustomPinLoader
    {
        private static readonly Dictionary<string, Minimap.PinType> RegisteredCustomPins = new Dictionary<string, Minimap.PinType>();
        private static Minimap m_registeredAgainst;

        public static void Clear()
        {
            RegisteredCustomPins.Clear();
            m_registeredAgainst = null;
        }

        public static Minimap.PinType RegisterPngAsPin(string filePath, Minimap.PinType fallback)
        {
            if (!File.Exists(filePath))
            {
                return fallback;
            }

            if (Minimap.instance != m_registeredAgainst)
            {
                // Cached PinType values are indices into the previous Minimap.instance.m_icons list.
                // A new instance (world reload/reconnect) invalidates them, so drop the stale cache.
                Clear();
                m_registeredAgainst = Minimap.instance;
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
                    Sprite sprite = Sprite.Create(
                        texture,
                        new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f),
                        100f
                    );
                    sprite.name = Path.GetFileNameWithoutExtension(filePath);

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