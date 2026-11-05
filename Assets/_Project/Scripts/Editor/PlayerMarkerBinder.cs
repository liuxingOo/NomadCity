using NomadCity.Game;
using NomadCity.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.EditorTools
{
    /// <summary>
    /// 一键把 <c>Art/Tokens</c> 里的四个玩家标记（绿 / 黄 / 红 / 蓝）接到所有会画标记的组件：
    /// - <see cref="MapStateRenderer"/>：地图上的影响力标记 + 外圈计分轨上的分数标记；
    /// - <see cref="CityPatternPanel"/>：城市样式牌轨道上的标记；
    /// - <see cref="GameHud"/>：玩家状态栏里「玩家标记」那一格的图标（顺带把引用补接上）。
    ///
    /// token_* 是「Sprite Mode = Multiple」的图集，直接 LoadAssetAtPath&lt;Sprite&gt; 会拿到 null，
    /// 所以这里统一先按单图取、再按图集兜底。
    ///
    /// 用法：打开 Game 场景 → 菜单 NomadCity / 美术：接上玩家标记素材（Token）。
    /// 换素材后重跑一次即可；重复执行不会出问题。
    /// </summary>
    public static class PlayerMarkerBinder
    {
        /// <summary>顺序 = PlayerColor（绿 0 / 黄 1 / 红 2 / 蓝 3）。</summary>
        private static readonly string[] MarkerPaths =
        {
            "Assets/_Project/Art/Tokens/token_green.jpg",
            "Assets/_Project/Art/Tokens/token_yellow.jpg",
            "Assets/_Project/Art/Tokens/token_red.jpg",
            "Assets/_Project/Art/Tokens/token_blue.jpg",
        };

        /// <summary>状态栏里「玩家标记」那格图标的相对路径（生成器建出来的结构）。</summary>
        private const string MarkerIconPath = "Stats/Stat2_Marker/Icon";

        [MenuItem("NomadCity/美术：接上玩家标记素材（Token）")]
        private static void Bind()
        {
            Sprite[] sprites = new Sprite[MarkerPaths.Length];
            int missing = 0;
            for (int i = 0; i < MarkerPaths.Length; i++)
            {
                sprites[i] = LoadSprite(MarkerPaths[i]);
                if (sprites[i] == null) missing++;
            }

            if (missing == MarkerPaths.Length)
            {
                Debug.LogError("[玩家标记] 一张素材都没读到，检查 Assets/_Project/Art/Tokens 下是否还有 token_*.jpg。");
                return;
            }

            int bound = 0;

            foreach (MapStateRenderer renderer in Object.FindObjectsByType<MapStateRenderer>(
                         FindObjectsInactive.Include))
            {
                if (SetArray(renderer, "playerMarkerSprites", sprites)) bound++;
            }

            foreach (CityPatternPanel panel in Object.FindObjectsByType<CityPatternPanel>(
                         FindObjectsInactive.Include))
            {
                if (SetArray(panel, "playerMarkerSprites", sprites)) bound++;
            }

            foreach (GameHud hud in Object.FindObjectsByType<GameHud>(
                         FindObjectsInactive.Include))
            {
                if (SetArray(hud, "playerMarkerSprites", sprites)) bound++;
                bound += WireMarkerIcons(hud);
            }

            if (missing > 0)
                Debug.LogWarning($"[玩家标记] 有 {missing} 张素材没读到，对应颜色在场景里会退回纯色块占位。");

            if (bound == 0)
            {
                Debug.LogWarning("[玩家标记] 场景里没找到 MapStateRenderer / CityPatternPanel / GameHud（" +
                                 "请先打开 Game 场景，并确认这几个组件都挂在场景物体上）。");
                return;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[玩家标记] 接好了 {bound} 处（影响力 / 计分轨 / 样式牌轨道 / 状态栏图标都用 token 素材）。" +
                      "\n别忘了 Ctrl+S 保存场景。");
        }

        private static bool SetArray(Object target, string field, Sprite[] sprites)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null || !prop.isArray)
            {
                Debug.LogWarning($"[玩家标记] {target.GetType().Name} 上没有 Sprite 数组字段 {field}。");
                return false;
            }

            prop.arraySize = sprites.Length;
            for (int i = 0; i < sprites.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);

            if (target is Component component) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
            return true;
        }

        /// <summary>
        /// 把每行的 MarkerIcon 补接到「Stats/Stat2_Marker/Icon」上：
        /// 之前用生成器建状态栏时已经接过，这里只是给手工搭的行兜底。
        /// </summary>
        private static int WireMarkerIcons(GameHud hud)
        {
            var so = new SerializedObject(hud);
            SerializedProperty rows = so.FindProperty("rows");
            if (rows == null || !rows.isArray) return 0;

            int wired = 0;

            for (int i = 0; i < rows.arraySize; i++)
            {
                SerializedProperty row = rows.GetArrayElementAtIndex(i);
                SerializedProperty iconProp = row.FindPropertyRelative("MarkerIcon");
                SerializedProperty rootProp = row.FindPropertyRelative("Root");
                if (iconProp == null || rootProp == null) continue;
                if (iconProp.objectReferenceValue != null) continue;

                GameObject root = rootProp.objectReferenceValue as GameObject;
                Transform icon = root != null ? root.transform.Find(MarkerIconPath) : null;
                Image image = icon != null ? icon.GetComponent<Image>() : null;
                if (image == null) continue;

                iconProp.objectReferenceValue = image;
                wired++;
            }

            if (wired > 0)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(hud);
                EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
            }

            return wired;
        }

        /// <summary>单图直接取；图集（Sprite Mode = Multiple）则从子资源里挑第一张 Sprite。</summary>
        private static Sprite LoadSprite(string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Sprite child) return child;
            }

            Debug.LogWarning($"[玩家标记] 读不到精灵：{path}");
            return null;
        }
    }
}
