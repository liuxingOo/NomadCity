using NomadCity.Core.Model;
using UnityEngine;

namespace NomadCity.UI
{
    /// <summary>
    /// 干员角色牌（企业扩展·罗德岛）的卡面素材。
    ///
    /// 素材是一张 4 列 × 2 行的拼版图 <c>rhodes_operators_exp.png</c>，按行优先顺序依次是
    /// 银灰 / 泥岩 / 森蚺 / 可露希尔 / 凯尔希 / W / 多萝西 / 异客 —— 与
    /// <see cref="CardCatalog"/> 里从 <c>OperatorCharacterBase</c> 起的顺序一致，所以不用手动切片命名。
    ///
    /// 不切 Sprite 资源、不勾 Read/Write：整张纹理按矩形区域 <c>Sprite.Create</c>，
    /// 编辑器里用 AssetDatabase、打包后自己解码一份副本，保证两边都能显示。
    /// </summary>
    public static class OperatorCardArt
    {
        public const string SheetPath = "Assets/_Project/Art/Cards/Character/rhodes_operators_exp.png";

        /// <summary>干员牌卡背（目前只用于素材完整性，界面还没用到）。</summary>
        public const string BackPath = "Assets/_Project/Art/Cards/Character/rhodes_op_back_exp.png";

        private const int Columns = 4;
        private const int Rows = 2;

        private static Texture2D sheet;
        private static bool sheetLoaded;
        private static readonly Sprite[] sprites = new Sprite[Columns * Rows];

        /// <summary>关掉 Domain Reload 时静态缓存会残留销毁过的纹理，进播放先清空。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            sheet = null; sheetLoaded = false;
            for (int i = 0; i < sprites.Length; i++) sprites[i] = null;
        }

        public static Texture2D Sheet
        {
            get
            {
                if (!sheetLoaded)
                {
                    sheetLoaded = true;
                    sheet = LoadTexture(SheetPath);
                    if (sheet == null)
                        Debug.LogWarning($"[OperatorCardArt] 读不到干员牌卡面：{SheetPath}");
                }
                return sheet;
            }
        }

        /// <summary>卡面下标（0–7）；不是干员牌时返回 -1。</summary>
        public static int IndexOf(int cardId)
        {
            int offset = cardId - CardIds.OperatorCharacterBase;
            return offset >= 0 && offset < CardCounts.OperatorCards ? offset : -1;
        }

        /// <summary>某张干员牌的卡面 Sprite；不是干员牌或素材缺失时返回 null。</summary>
        public static Sprite SpriteFor(int cardId)
        {
            int index = IndexOf(cardId);
            if (index < 0) return null;
            if (sprites[index] == null)
            {
                Texture2D texture = Sheet;
                if (texture == null) return null;

                int cellWidth = texture.width / Columns;
                int cellHeight = texture.height / Rows;
                int column = index % Columns;
                int row = index / Columns;
                // Sprite 的 rect 原点在左下：拼版图最上面那排是 row=0
                sprites[index] = Sprite.Create(texture,
                    new Rect(column * cellWidth, (Rows - 1 - row) * cellHeight, cellWidth, cellHeight),
                    new Vector2(0.5f, 0.5f), 100f);
            }
            return sprites[index];
        }

        private static Texture2D LoadTexture(string path)
        {
#if UNITY_EDITOR
            Texture2D asset = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (asset != null) return asset;
            Object[] assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (Object found in assets)
                if (found is Texture2D texture) return texture;
#endif
            try
            {
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                if (bytes == null || bytes.Length < 16) return null;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes, false);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                return tex;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[OperatorCardArt] 自己解码纹理失败 {path}：{e.Message}");
                return null;
            }
        }
    }
}
