using System.Collections.Generic;
using UnityEngine;

namespace NomadCity.Game
{
    /// <summary>
    /// 运行时生成圆形占位 Sprite。真素材（影响力标记 / 移动城市 / 资源点指示物）尚未就绪，
    /// 先用程序生成的白色圆盘与圆环着色顶上；日后换成美术素材时，只需替换本类产出的 Sprite。
    /// 生成的纹理与 Sprite 都带 DontSave，不会污染资源库。
    /// </summary>
    public static class MapSpriteFactory
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        /// <summary>实心圆盘。</summary>
        public static Sprite Disc(int size = 64) => Get($"disc_{size}", size, 0);

        /// <summary>空心圆环，thickness 为环宽（像素）。</summary>
        public static Sprite Ring(int size = 64, int thickness = 10) => Get($"ring_{size}_{thickness}", size, thickness);

        private static Sprite Get(string key, int size, int ringThickness)
        {
            if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = key,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };

            float radius = size * 0.5f;
            float inner = ringThickness > 0 ? Mathf.Max(0f, radius - ringThickness) : 0f;
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - radius;
                    float dy = y + 0.5f - radius;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);

                    // 外缘留 1 像素做抗锯齿；圆环再叠一个内缘渐隐。
                    float alpha = Mathf.Clamp01(radius - 0.5f - distance);
                    if (ringThickness > 0) alpha = Mathf.Min(alpha, Mathf.Clamp01(distance - inner + 0.5f));

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            // pixelsPerUnit = size，使一张 Sprite 恰好是 1 个世界单位；实际大小由 Transform 缩放控制。
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            sprite.name = key;
            sprite.hideFlags = HideFlags.DontSave;

            Cache[key] = sprite;
            return sprite;
        }
    }
}
