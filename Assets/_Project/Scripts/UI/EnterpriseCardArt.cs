using System.Collections.Generic;
using NomadCity.Core.Model;
using UnityEngine;

namespace NomadCity.UI
{
    /// <summary>
    /// 企业扩展用到的卡面素材。
    ///
    /// 素材：
    /// - 企业板拼版图 <c>card_enterprise.jpg</c>（2600×3700，4 列 × 2 行，8 家企业板）。
    /// - 科室板 <c>laiyin1/2/3.png</c>（各 516×1000）。
    ///
    /// 不切 Sprite、不勾 Read/Write：
    /// 整张纹理交给 <c>RawImage</c> + <c>uvRect</c> 裁切，像素扫描用
    /// <c>Texture2D.LoadImage(File.ReadAllBytes(path))</c> 自己解码一份可读副本。
    /// </summary>
    public static class EnterpriseCardArt
    {
        public const string SheetPath = "Assets/_Project/Art/Cards/Enterprise/card_enterprise.jpg";

        private const int Columns = 4;
        private const int Rows = 2;
        private const int BoardCount = Columns * Rows;

        /// <summary>拿不到卡面时的宽高比（650×1850 的板面）。</summary>
        public const float FallbackAspect = 650f / 1850f;

        private static readonly string[] TokenPaths =
        {
            "Assets/_Project/Art/Tokens/token_green.jpg",
            "Assets/_Project/Art/Tokens/token_yellow.jpg",
            "Assets/_Project/Art/Tokens/token_red.jpg",
            "Assets/_Project/Art/Tokens/token_blue.jpg",
        };

        /// <summary>三张莱茵生命科室板的路径（下标 0/1/2）。</summary>
        private static readonly string[] DepartmentPaths =
        {
            "Assets/_Project/Art/Cards/Enterprise/laiyin1.png",
            "Assets/_Project/Art/Cards/Enterprise/laiyin2.png",
            "Assets/_Project/Art/Cards/Enterprise/laiyin3.png",
        };

        private static Texture2D sheet;
        private static bool sheetLoaded;

        /// <summary>8 块企业板的 Sprite 缓存（图鉴缩略图用）。</summary>
        private static readonly Sprite[] boardSprites = new Sprite[BoardCount];

        private static readonly Texture2D[] departments = new Texture2D[DepartmentPaths.Length];
        private static readonly bool[] departmentLoaded = new bool[DepartmentPaths.Length];

        private static readonly Sprite[] tokens = new Sprite[TokenPaths.Length];
        private static readonly bool[] tokenLoaded = new bool[TokenPaths.Length];

        /// <summary>
        /// 关掉 Domain Reload 时静态缓存会跨会话残留（里面可能是销毁的纹理）。
        /// 每次进入播放先清空。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            sheet = null; sheetLoaded = false;
            for (int i = 0; i < boardSprites.Length; i++) boardSprites[i] = null;
            for (int i = 0; i < departments.Length; i++) { departments[i] = null; departmentLoaded[i] = false; }
            for (int i = 0; i < tokens.Length; i++) { tokens[i] = null; tokenLoaded[i] = false; }
        }

        // —— 企业板拼版图 ——

        public static Texture2D Sheet
        {
            get
            {
                if (!sheetLoaded)
                {
                    sheetLoaded = true;
                    sheet = LoadTexture(SheetPath);
                    if (sheet == null) Debug.LogWarning($"[EnterpriseCardArt] 读不到企业板卡面：{SheetPath}");
                }
                return sheet;
            }
        }

        /// <summary>某块企业板在拼版图上的 uvRect（给 RawImage.uvRect 用）。</summary>
        public static Rect BoardUv(int boardId)
        {
            if (boardId < 0 || boardId >= BoardCount) return new Rect(0f, 0f, 1f, 1f);
            int column = boardId % Columns;
            int row = boardId / Columns;
            Texture2D texture = Sheet;
            float insetU = texture != null && texture.width > 0 ? 1f / texture.width : 0f;
            float insetV = texture != null && texture.height > 0 ? 1f / texture.height : 0f;
            // uv 原点左下：拼版图第 0 行（最上面那排）= row=0 → 从下往上数 Rows-1-row
            return new Rect(column * 1f / Columns + insetU, (Rows - 1 - row) * 1f / Rows + insetV,
                1f / Columns - insetU * 2f, 1f / Rows - insetV * 2f);
        }

        /// <summary>企业板宽高比（宽 ÷ 高）。</summary>
        public static float AspectOf(int boardId)
        {
            Texture2D texture = Sheet;
            if (texture == null || texture.height <= 0) return FallbackAspect;
            float cellWidth = texture.width / (float)Columns;
            float cellHeight = texture.height / (float)Rows;
            return cellHeight > 0f ? cellWidth / cellHeight : FallbackAspect;
        }

        /// <summary>
        /// 某块企业板的 Sprite（图鉴缩略图用）：直接引用拼版图上的矩形区域，不复制像素。
        /// 纹理不可读也没问题 —— Sprite.Create 只登记区域，读取像素才需要 Read/Write。
        /// </summary>
        public static Sprite BoardSprite(int boardId)
        {
            if (boardId < 0 || boardId >= BoardCount) return null;
            if (boardSprites[boardId] == null)
            {
                Texture2D texture = Sheet;
                if (texture == null) return null;

                int cellWidth = texture.width / Columns;
                int cellHeight = texture.height / Rows;
                int column = boardId % Columns;
                int row = boardId / Columns;
                // Sprite 的 rect 原点在左下：拼版图最上面那排是 row=0
                boardSprites[boardId] = Sprite.Create(texture,
                    new Rect(column * cellWidth, (Rows - 1 - row) * cellHeight, cellWidth, cellHeight),
                    new Vector2(0.5f, 0.5f), 100f);
            }
            return boardSprites[boardId];
        }

        public const int BoardsPerRow = Columns;
        public const int BoardRows = Rows;

        // —— 科室板 ——

        public static Texture2D Department(int departmentId)
        {
            if (departmentId < 0 || departmentId >= departments.Length) return null;
            if (!departmentLoaded[departmentId])
            {
                departmentLoaded[departmentId] = true;
                departments[departmentId] = LoadTexture(DepartmentPaths[departmentId]);
                if (departments[departmentId] == null)
                    Debug.LogWarning($"[EnterpriseCardArt] 读不到科室板：{DepartmentPaths[departmentId]}");
            }
            return departments[departmentId];
        }

        public static string DepartmentPath(int departmentId) =>
            departmentId >= 0 && departmentId < DepartmentPaths.Length ? DepartmentPaths[departmentId] : null;

        public const int DepartmentCount = 3;

        // —— 玩家标记 ——

        public static Sprite Token(PlayerColor color)
        {
            int index = (int)color;
            if (index < 0 || index >= TokenPaths.Length) return null;
            if (!tokenLoaded[index])
            {
                tokenLoaded[index] = true;
                tokens[index] = LoadSprite(TokenPaths[index]);
                if (tokens[index] == null)
                    Debug.LogWarning($"[EnterpriseCardArt] 读不到玩家标记素材：{TokenPaths[index]}");
            }
            return tokens[index];
        }

        // —— 内部加载 ——

        private static Sprite LoadSprite(string path)
        {
#if UNITY_EDITOR
            Sprite sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;
            Object[] assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (Object asset in assets)
                if (asset is Sprite found) return found;
#endif
            Texture2D texture = LoadTexture(path);
            if (texture != null)
                return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f);
            return null;
        }

        /// <summary>
        /// 通用纹理加载：编辑器里优先用 AssetDatabase（共享纹理，省内存）；
        /// 拿不到时自己解码一份副本，保证 Runtime 也能显示。
        /// </summary>
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
                Debug.LogWarning($"[EnterpriseCardArt] 自己解码纹理失败 {path}：{e.Message}");
                return null;
            }
        }
    }
}
