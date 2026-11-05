using NomadCity.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.EditorTools
{
    /// <summary>
    /// 只给现有四行状态栏「补图标」，位置 / 尺寸一概不动。
    ///
    /// 为什么单独一个菜单：生成器（<see cref="GameHudRowBuilder"/>）会把四行的坐标整套重摆一遍，
    /// 手调过行的位置后不想再被推一遍，就用这里 —— 它只摸每行 <c>Stats/Stat{n}_{Slot}/Icon</c> 的 Source Image。
    ///
    /// 七格的图：
    /// 0 得分 —— 现画的临时五角星（<c>Art/Icons/UI/ScoreStar.png</c>，正式美术出来后直接换这张图）；
    /// 1 金券 —— 资源目录里那张 5 元券（<c>Art/Icons/Resource/Money.jpg</c>）；
    /// 2 玩家标记 —— 这里先塞绿色那张 token 做静态预览，运行期 <see cref="GameHud"/> 会按玩家颜色换；
    /// 3~6 源岩 / 源石 / 异铁 / 至纯源石 —— 与生成器同一套路径。
    ///
    /// 用法：打开 Game 场景 → 菜单 NomadCity / 美术：补齐状态栏缺失图标（得分 / 金券 / 标记）→ Ctrl+S。
    /// </summary>
    public static class GameHudIconFiller
    {
        /// <summary>七格的图标路径，顺序 = GameHudRow.Values（0 得分 … 6 至纯源石）。</summary>
        private static readonly string[] IconPaths =
        {
            "Assets/_Project/Art/Icons/UI/ScoreStar.png",                 // 0 得分（临时五角星）
            "Assets/_Project/Art/Icons/Resource/Money.jpg",               // 1 金券（5 元券）
            "Assets/_Project/Art/Tokens/token_green.jpg",                 // 2 玩家标记（运行期换色）
            "Assets/_Project/Art/Icons/Resource/Originite.png",           // 3 源岩
            "Assets/_Project/Art/Icons/Resource/Originium.png",           // 4 源石
            "Assets/_Project/Art/Icons/Resource/Iron.png",                // 5 异铁
            "Assets/_Project/Art/Icons/Resource/Pure Originium.png",      // 6 至纯源石
        };

        /// <summary>与生成器一致的子物体名，用来定位 <c>Stats/Stat{n}_{Slot}/Icon</c>。</summary>
        private static readonly string[] SlotNames =
        {
            "Score", "Voucher", "Marker", "Orirock", "Originium", "Oriron", "Prime",
        };

        [MenuItem("NomadCity/美术：补齐状态栏缺失图标（得分 / 金券 / 标记）")]
        private static void Fill()
        {
            GameHud hud = Object.FindAnyObjectByType<GameHud>(FindObjectsInactive.Include);
            if (hud == null)
            {
                Debug.LogError("[状态栏图标] 场景里找不到 GameHud。请先打开 Game 场景。");
                return;
            }

            Sprite[] sprites = new Sprite[IconPaths.Length];
            int missing = 0;
            for (int i = 0; i < IconPaths.Length; i++)
            {
                sprites[i] = LoadSprite(IconPaths[i]);
                if (sprites[i] == null) missing++;
            }

            var so = new SerializedObject(hud);
            SerializedProperty rows = so.FindProperty("rows");
            if (rows == null || !rows.isArray)
            {
                Debug.LogError("[状态栏图标] GameHud 上没有 rows 数组，先跑一次生成菜单。");
                return;
            }

            int changed = 0;

            for (int i = 0; i < rows.arraySize; i++)
            {
                SerializedProperty row = rows.GetArrayElementAtIndex(i);
                SerializedProperty rootProp = row.FindPropertyRelative("Root");
                GameObject root = rootProp != null ? rootProp.objectReferenceValue as GameObject : null;
                if (root == null) continue;   // 空槽位（人不够时整行是关的）

                for (int v = 0; v < sprites.Length; v++)
                {
                    string path = $"Stats/Stat{v}_{SlotNames[v]}/Icon";
                    Transform iconTransform = root.transform.Find(path);
                    Image icon = iconTransform != null ? iconTransform.GetComponent<Image>() : null;
                    if (icon == null)
                    {
                        Debug.LogWarning($"[状态栏图标] Row{i + 1} 找不到 {path}，这一格跳过。");
                        continue;
                    }

                    if (icon.sprite == sprites[v] && icon.color == Color.white) continue;

                    Undo.RecordObject(icon, "补齐状态栏图标");
                    icon.sprite = sprites[v];
                    if (sprites[v] != null)
                    {
                        icon.color = Color.white;      // 清掉生成器留的暗色占位
                        icon.preserveAspect = true;    // 五角星 / 券面都别拉变形
                    }
                    icon.raycastTarget = false;
                    EditorUtility.SetDirty(icon);
                    changed++;
                }

                // 顺手把「玩家标记」那格的引用接给 GameHud（没有它，运行期不会按玩家颜色换 token）。
                SerializedProperty markerIcon = row.FindPropertyRelative("MarkerIcon");
                if (markerIcon != null && markerIcon.objectReferenceValue == null)
                {
                    Transform iconTransform = root.transform.Find("Stats/Stat2_Marker/Icon");
                    Image icon = iconTransform != null ? iconTransform.GetComponent<Image>() : null;
                    if (icon != null)
                    {
                        markerIcon.objectReferenceValue = icon;
                        changed++;
                    }
                }
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(hud);
            EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);

            if (missing > 0)
                Debug.LogWarning($"[状态栏图标] 有 {missing} 张图没读到，对应格子仍是暗色占位。");

            Debug.Log($"[状态栏图标] 补好 {changed} 处图标（得分五角星 / 金券 5 元券 / 玩家标记按颜色换）。" +
                      "\n位置尺寸一点没动；别忘了 Ctrl+S 保存场景。");
        }

        /// <summary>单图直接取；Sprite Mode = Multiple 的图集（token / 部分旧图）从子资源里挑第一张。</summary>
        private static Sprite LoadSprite(string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Sprite child) return child;
            }

            Debug.LogWarning($"[状态栏图标] 读不到精灵：{path}");
            return null;
        }
    }
}
