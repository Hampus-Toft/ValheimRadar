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

            if (!TryGetCachedOrPrepareRegistration(filePath, fallback, out Minimap.PinType cachedType))
            {
                return cachedType;
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

                    return RegisterSprite(filePath, sprite);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ValheimRadar] Failed to load custom pin PNG '{filePath}': {ex.Message}");
            }

            return fallback;
        }

        /// <summary>
        /// Registers an already-loaded Sprite (e.g. a vanilla item icon pulled from ObjectDB) as a
        /// pin, sharing the same Minimap.instance cache-invalidation as PNG-file registration.
        /// </summary>
        public static Minimap.PinType RegisterSpriteAsPin(string cacheKey, Sprite sprite, Minimap.PinType fallback)
        {
            if (sprite == null)
            {
                return fallback;
            }

            if (!TryGetCachedOrPrepareRegistration(cacheKey, fallback, out Minimap.PinType cachedType))
            {
                return cachedType;
            }

            return RegisterSprite(cacheKey, sprite);
        }

        /// <summary>
        /// Shared cache lookup/invalidation for both registration paths. Returns false (with the
        /// resolved PinType in cachedType) when a cached or fallback value should be used instead of
        /// registering; returns true when the caller still needs to create+register a new sprite.
        /// </summary>
        private static bool TryGetCachedOrPrepareRegistration(string cacheKey, Minimap.PinType fallback, out Minimap.PinType cachedType)
        {
            if (Minimap.instance != m_registeredAgainst)
            {
                // Cached PinType values are indices into the previous Minimap.instance.m_icons list.
                // A new instance (world reload/reconnect) invalidates them, so drop the stale cache.
                Clear();
                m_registeredAgainst = Minimap.instance;
            }

            if (RegisteredCustomPins.TryGetValue(cacheKey, out cachedType))
            {
                return false;
            }

            if (Minimap.instance == null || Minimap.instance.m_icons == null)
            {
                cachedType = fallback;
                return false;
            }

            return true;
        }

        private static Minimap.PinType RegisterSprite(string cacheKey, Sprite sprite)
        {
            Minimap.SpriteData newSpriteData = new Minimap.SpriteData
            {
                m_name = (Minimap.PinType)Minimap.instance.m_icons.Count,
                m_icon = sprite
            };

            Minimap.instance.m_icons.Add(newSpriteData);

            Minimap.PinType newPinType = newSpriteData.m_name;
            RegisteredCustomPins[cacheKey] = newPinType;

            return newPinType;
        }
    }
}
