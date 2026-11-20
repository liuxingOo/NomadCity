using System.Collections.Generic;
using NomadCity.Core.Model;
using UnityEngine;

namespace NomadCity.UI
{
    /// <summary>
    /// 角色牌卡背：四名玩家各一种颜色，素材在 <c>Assets/_Project/Art/Cards/Character/</c>。
    ///
    /// 盖放的牌是暗牌，界面上直接画对应玩家颜色的卡背 —— 与实体桌游一致，
    /// 别人一眼能看出「谁盖了一张」，但看不到牌面。
    /// 加载方式沿用 <see cref="OperatorCardArt"/>：编辑器里走 AssetDatabase，
    /// 打包后直接读文件解码，不要求素材勾 Read/Write。
    /// </summary>
    public static class CharacterCardBackArt
    {
        private const string Folder = "Assets/_Project/Art/Cards/Character/";

        private static readonly Dictionary<PlayerColor, Sprite> Cache = new Dictionary<PlayerColor, Sprite>();

        /// <summary>关掉 Domain Reload 时静态缓存会残留已销毁的纹理，进播放先清空。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => Cache.Clear();

        /// <summary>某位玩家颜色的卡背；素材缺失时返回 null（调用方退回纯色块）。</summary>
        public static Sprite BackFor(PlayerColor color)
        {
            if (Cache.TryGetValue(color, out Sprite cached) && cached != null) return cached;

            Texture2D texture = OperatorCardArt.LoadTexture(Folder + FileName(color));
            if (texture == null) return null;

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f);
            Cache[color] = sprite;
            return sprite;
        }

        private static string FileName(PlayerColor color)
        {
            switch (color)
            {
                case PlayerColor.Green: return "op_back_green.png";
                case PlayerColor.Yellow: return "op_back_yellow.png";
                case PlayerColor.Red: return "op_back_red.png";
                default: return "op_back_blue.png";
            }
        }
    }
}
