using System.Collections.Generic;
using NomadCity.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NomadCity.EditorTools
{
    /// <summary>
    /// 一键把卡面接线补齐：设施供应区 / 城市面板 / 城市样式牌 / 角色牌手牌 / 卡面放大弹窗的卡面库数组，
    /// 以及设施供应区 / 城市面板 / 角色牌手牌的 Card Images（Slot Images）数组（直接复用牌位自身已有的 Image，不必新建子物体）。
    /// 卡面放大弹窗有两份库：Card Faces（设施）与 Character Faces（角色）。
    ///
    /// 素材是拼版图（Multiple 切片），切片顺序和卡牌 id 顺序**不一致**，所以这里靠「切片名 → 卡 id」映射
    /// 来定位，而不是按拖拽顺序。切片名带语义（英文卡名 + 颜色 + 序号，角色牌只用英文角色名），改素材只要名字不变就不用动代码。
    ///
    /// 用法：打开 Game 场景 → 菜单 NomadCity / 卡面：填充卡面库（Card Faces）。
    /// </summary>
    public static class CardFaceFiller
    {
        private const string FacilitySheet = "Assets/_Project/Art/Cards/Facility/card_facility.jpg";
        private const string PatternLevel1Sheet = "Assets/_Project/Art/Cards/CityPattern/card_cp_lv1.jpg";
        private const string PatternLevel2Sheet = "Assets/_Project/Art/Cards/CityPattern/card_cp_lv2.jpg";
        private const string CharacterSheet = "Assets/_Project/Art/Cards/Character/bas_operators.png";

        /// <summary>面板都摆在这个场景里；从别的场景点菜单会先切过来。</summary>
        private const string GameScenePath = "Assets/_Project/Scenes/Game.unity";

        /// <summary>设施牌卡面库长度：41 张普通设施 + 4 张核心指挥塔 + 3 张延伸枢纽 + 3 张企业办事处（企业扩展）。</summary>
        private const int FacilityLibrarySize = 51;

        /// <summary>角色牌卡面库长度：5 个基础角色 + 企业扩展的山 / 玛恩纳，共 7。</summary>
        private const int CharacterLibrarySize = 7;

        /// <summary>
        /// 设施切片名主干 → 该组第一张牌的 id（同组后续切片依次 +1）。
        /// 已对照 CardCatalog.BuildStandardFacilities / card_facility.jpg.meta 逐条核实。
        /// 带「_exp」后缀的是企业办事处扩展牌（600–602），由 CardCatalog.BuildEnterpriseFacilities 定义。
        /// </summary>
        private static readonly Dictionary<string, int> FacilityStemToBaseId = new Dictionary<string, int>
        {
            // —— 彩色 100–110 ——
            { "City-Administrative-District_rainbow", 100 },
            { "Auxiliary-Power-Facility_rainbow", 103 },
            { "Federation-Office_rainbow", 106 },
            { "Makeshift-Engineering-Camp_rainbow", 109 },
            // —— 蓝色 111–120 ——
            { "Logistics-Hub_blue", 111 },
            { "Originium-Refinery_blue", 113 },
            { "Urbanized-District_blue", 114 },
            { "Iron-Smelter_blue", 117 },
            { "Trade-District_blue", 118 },
            // —— 黄色 121–130 ——
            { "City-Industrial-Zone_yellow", 121 },
            { "High-Performance-Power-Facility_yellow", 124 },
            { "Orirock-Refinery_yellow", 126 },
            { "Originium-Steelworks_yellow", 127 },
            { "Mining-Excavator_yellow", 128 },
            // —— 红色 131–140 ——
            { "Iron-Smelter_red", 131 },
            { "Orirock-Refinery_red", 132 },
            { "Mercenary-Command-Post_red", 133 },
            { "Escort-Dispatch-Center_red", 136 },
            { "Vehicle-Depot_red", 138 },
            // —— 延伸枢纽 300–302 ——
            { "Extension-Hub_blue", 300 },
            { "Extension-Hub_yellow", 301 },
            { "Extension-Hub_red", 302 },
            // —— 核心指挥塔 200–203 ——
            { "Core-Command-Tower_rainbow", 200 },
            // —— 企业办事处（企业扩展）600–602 ——
            { "Enterprise-Office_red_exp", 600 },
            { "Enterprise-Office_blue_exp", 601 },
            { "Enterprise-Office_yellow_exp", 602 },
        };

        /// <summary>
        /// 城市样式牌卡面库：下标 0–11 对应的切片名（顺序 = CityPatternPanel.SpriteIndexOf 的下标）。
        /// 0–5 是基础牌 500–505，6–11 是企业扩展牌 550–555。
        /// lv1 表里的 card_cp_lv1_unserd 是多出来的一格样张（基础 I 级只有 4 张，没有它），直接丢弃。
        /// </summary>
        private static readonly string[] PatternSliceOrder =
        {
            "card_cp_lv1_military-industry",     // 0 → 500 军工业化区域
            "card_cp_lv1_mobilization-support",  // 1 → 501 动员配套体系
            "card_cp_lv1_composite-power",       // 2 → 502 复合动力系统
            "card_cp_lv1_supply-relay",          // 3 → 503 物资中继站
            "card_cp_lv2_originium-industry",    // 4 → 504 源石工业中枢
            "card_cp_lv2_mobility-management",   // 5 → 505 高效移动管理体系
            "card_cp_lv1_urban-development_exp", // 6 → 550 城建开拓区ᴱ
            "card_cp_lv1_pioneer-outpost_exp",   // 7 → 551 拓荒者据点ᴱ
            "card_cp_lv1_core-business_exp",     // 8 → 552 核心商业区ᴱ
            "card_cp_lv1_enterprise-park_exp",   // 9 → 553 大型企业园ᴱ
            "card_cp_lv2_garrison-district_exp", // 10 → 554 警备队辖区ᴱ
            "card_cp_lv2_pioneer-command_exp",   // 11 → 555 拓荒指挥中枢ᴱ
        };

        /// <summary>
        /// 角色牌切片名（英文角色名）→ 角色下标（即 <see cref="CharacterHandPanel.SpriteIndexOf"/> 的返回下标）。
        /// 四色共用一张正面，所以一张角色图只对应一个下标，与牌 id 的 4 个颜色副本无关。
        /// 5 / 6 是企业扩展（EnterpriseExpansion.md §3.2）的山 / 玛恩纳，切片名带「_exp」后缀。
        /// </summary>
        private static readonly Dictionary<string, int> CharacterStemToRoleIndex = new Dictionary<string, int>
        {
            { "Liskarm", 0 },       // 雷蛇
            { "Elysium", 1 },       // 极境
            { "Texas",   2 },       // 德克萨斯
            { "Tin Man", 3 },       // 锡人
            { "Canot",   4 },       // 坎诺特
            { "Mountain_exp", 5 },  // 山
            { "Młynar_exp",   6 },  // 玛恩纳
        };

        [MenuItem("NomadCity/卡面：填充卡面库（Card Faces）")]
        private static void FillAll()
        {
            if (!EnsureGameSceneOpen()) return;

            Sprite[] facilityLibrary = BuildFacilityLibrary();
            if (facilityLibrary == null) return;

            Sprite[] patternLibrary = BuildPatternLibrary();
            if (patternLibrary == null) return;

            // 角色牌允许暂时没切片：这一项缺席只影响角色牌手牌，设施 / 样式照填。
            Sprite[] characterLibrary = BuildCharacterLibrary();

            int filled = 0;
            filled += FillPanel<FacilitySupplyPanel>("设施供应区", facilityLibrary);
            filled += FillPanel<CityPanelView>("城市面板", facilityLibrary);
            filled += FillPanel<CityPatternPanel>("城市样式牌", patternLibrary);
            if (characterLibrary != null)
                filled += FillPanel<CharacterHandPanel>("角色牌手牌", characterLibrary);

            // 卡面放大弹窗自己也要一份库：城市面板点设施、设施供应区只读点牌靠设施库，
            // 手牌点角色牌靠角色库。以前这份是生成弹窗时顺手填的，这里一并补上，省得手动拖 51 + 7 个 Sprite。
            filled += FillPanel<CardPreviewPopup>("卡面放大", facilityLibrary);
            if (characterLibrary != null)
                filled += FillPanel<CardPreviewPopup>("卡面放大", characterLibrary, "characterFaces");
            filled += FillPanel<CardPreviewPopup>("卡面放大", patternLibrary, "patternFaces");

            int images = 0;
            images += FillImagesFromButtons<FacilitySupplyPanel>("设施供应区", "cardImages", "cardButtons");
            images += FillImagesFromButtons<CityPanelView>("城市面板", "slotImages", "slotButtons");
            images += FillImagesFromButtons<CharacterHandPanel>("角色牌手牌", "cardImages", "cardButtons");

            Debug.Log(filled > 0
                ? $"[卡面填充] 已填 {filled} 个面板的卡面库（设施库 {facilityLibrary.Length} 项，样式库 {patternLibrary.Length} 项，" +
                  $"角色库 {(characterLibrary != null ? characterLibrary.Length : 0)} 项），并接好 {images} 个卡面 Image 位。"
                : "[卡面填充] 当前场景里没找到这几个面板，什么都没改。请先打开 Game 场景再执行。");
        }

        /// <summary>
        /// 菜单只对 Game 场景有效。当前打开的是别的场景（比如 MainMenu）时先切过去，
        /// 免得一路「找不到面板」刷屏。场景有改动会先问是否保存，取消就什么都不做。
        /// </summary>
        private static bool EnsureGameSceneOpen()
        {
            if (SceneManager.GetActiveScene().path == GameScenePath) return true;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;

            EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            if (SceneManager.GetActiveScene().path == GameScenePath)
            {
                Debug.Log($"[卡面填充] 当前打开的不是 Game 场景，已切到 {GameScenePath} 再填充。");
                return true;
            }

            Debug.LogError($"[卡面填充] 打不开 {GameScenePath}，请先手动打开 Game 场景再跑本菜单。");
            return false;
        }

        /// <summary>点菜单时顺便报告素材本身的问题，帮用户提前发现漏切 / 命名变化。</summary>
        internal static Sprite[] BuildFacilityLibrary()
        {
            Dictionary<string, Sprite> byName = LoadSprites(FacilitySheet);
            if (byName.Count == 0)
            {
                Debug.LogError($"[卡面填充] 读不到 {FacilitySheet} 里的子 Sprite —— 请确认这张图已设为 Sprite Mode = Multiple（多图）并切好片。");
                return null;
            }

            // 先按「主干」把切片名分组，组内再按名字排序，序号即同名牌的第几张。
            var groups = new Dictionary<string, List<string>>();
            foreach (string name in byName.Keys)
            {
                if (!name.StartsWith("card_facility_")) continue;
                string body = name.Substring("card_facility_".Length);
                if (body.StartsWith("None")) continue;                   // 切图残留的空格
                string stem = StemOf(body);
                if (!FacilityStemToBaseId.ContainsKey(stem))
                {
                    Debug.LogWarning($"[卡面填充] 切片「{name}」对不上任何已知设施牌主干（{stem}），已跳过。");
                    continue;
                }
                if (!groups.TryGetValue(stem, out List<string> list))
                {
                    list = new List<string>();
                    groups[stem] = list;
                }
                list.Add(name);
            }

            var library = new Sprite[FacilityLibrarySize];
            int missing = 0;
            foreach (KeyValuePair<string, List<string>> group in groups)
            {
                group.Value.Sort(System.StringComparer.Ordinal);
                int baseId = FacilityStemToBaseId[group.Key];
                for (int n = 0; n < group.Value.Count; n++)
                {
                    int cardId = baseId + n;
                    int index = FacilitySupplyPanel.SpriteIndexOf(cardId);
                    if (index < 0 || index >= library.Length)
                    {
                        Debug.LogWarning($"[卡面填充] 牌 id {cardId}（切片「{group.Value[n]}」）没有对应的卡面下标，已跳过。");
                        continue;
                    }
                    library[index] = byName[group.Value[n]];
                }
            }

            for (int i = 0; i < library.Length; i++)
                if (library[i] == null) missing++;
            if (missing > 0)
                Debug.LogWarning($"[卡面填充] 设施卡面库有 {missing} 格是空的 —— 多半是拼版图缺了对应的切片。");

            return library;
        }

        private static Sprite[] BuildPatternLibrary()
        {
            Dictionary<string, Sprite> byName = LoadSprites(PatternLevel1Sheet);
            foreach (KeyValuePair<string, Sprite> entry in LoadSprites(PatternLevel2Sheet))
                byName[entry.Key] = entry.Value;

            if (byName.Count == 0)
            {
                Debug.LogError($"[卡面填充] 读不到样式牌拼版图 —— 请确认 {PatternLevel1Sheet} / {PatternLevel2Sheet} 已设 Multiple 并切片。");
                return null;
            }

            var library = new Sprite[PatternSliceOrder.Length];
            for (int i = 0; i < PatternSliceOrder.Length; i++)
            {
                if (byName.TryGetValue(PatternSliceOrder[i], out Sprite sprite))
                {
                    library[i] = sprite;
                    continue;
                }
                Debug.LogWarning($"[卡面填充] 样式牌下标 {i} 找不到切片「{PatternSliceOrder[i]}」。");
            }
            return library;
        }

        /// <summary>
        /// 角色牌卡面库：按切片名（英文角色名）定位角色下标。四色共用一张正面，所以这里只有 7 格。
        /// 允许这张图还没切片 —— 返回 null 表示跳过，调用方不该因此中断其它面板。
        /// </summary>
        internal static Sprite[] BuildCharacterLibrary()
        {
            Dictionary<string, Sprite> byName = LoadSprites(CharacterSheet);
            if (byName.Count == 0)
            {
                Debug.LogWarning($"[卡面填充] 读不到 {CharacterSheet} 里的子 Sprite —— 这张图还没设 Sprite Mode = Multiple（多图）" +
                                 "并按 2×4 切片，本次跳过角色牌。");
                return null;
            }

            var library = new Sprite[CharacterLibrarySize];
            // 切片名可能带拼版图前缀（bas_operators_Liskarm），也可能被 Unity 自动附上序号（Liskarm_0），都先归一化再查。
            string prefix = System.IO.Path.GetFileNameWithoutExtension(CharacterSheet) + "_";
            foreach (KeyValuePair<string, Sprite> entry in byName)
            {
                string body = entry.Key.StartsWith(prefix) ? entry.Key.Substring(prefix.Length) : entry.Key;
                string stem = StemOf(body);
                if (!CharacterStemToRoleIndex.TryGetValue(stem, out int roleIndex) &&
                    !CharacterStemToRoleIndex.TryGetValue(body, out roleIndex) &&
                    !CharacterStemToRoleIndex.TryGetValue(entry.Key, out roleIndex))
                    continue;   // 占位符 / 名字对不上：跳过

                library[roleIndex] = entry.Value;
            }

            for (int i = 0; i < library.Length; i++)
                if (library[i] == null)
                    Debug.LogWarning($"[卡面填充] 角色卡面库第 {i} 格是空的 —— 切片名要对得上 " +
                                     $"{string.Join(" / ", CharacterStemToRoleIndex.Keys)}（可带 {prefix} 前缀）。");

            return library;
        }

        /// <summary>把一张 Multiple 切片的图里所有子 Sprite 按名字收成字典（切片名 → Sprite）。</summary>
        private static Dictionary<string, Sprite> LoadSprites(string assetPath)
        {
            var map = new Dictionary<string, Sprite>();
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            foreach (Object asset in assets)
                if (asset is Sprite sprite) map[sprite.name] = sprite;
            return map;
        }

        /// <summary>去掉名字结尾的「_序号」，得到同名牌共用的主干。例：「Logistics-Hub_blue_02」→「Logistics-Hub_blue」。</summary>
        private static string StemOf(string body)
        {
            int end = body.Length;
            while (end > 0 && char.IsDigit(body[end - 1])) end--;
            if (end < body.Length && end > 0 && body[end - 1] == '_') end--;
            return body.Substring(0, end);
        }

        /// <summary>
        /// 把卡面库写进某个面板的 Sprite 数组字段（默认 Card Faces；卡面放大弹窗的角色库字段叫 characterFaces）。
        /// </summary>
        private static int FillPanel<T>(string label, Sprite[] library, string propertyName = "cardFaces")
            where T : MonoBehaviour
        {
            T[] panels = Object.FindObjectsByType<T>(FindObjectsInactive.Include);
            if (panels.Length == 0)
            {
                Debug.LogWarning($"[卡面填充] 场景里找不到「{label}」面板（{typeof(T).Name}）——这一项没填。" +
                                 "先在 Game 场景里把该面板物体搭出来，再跑本菜单。");
                return 0;
            }

            foreach (T panel in panels)
            {
                var so = new SerializedObject(panel);
                SerializedProperty prop = so.FindProperty(propertyName);
                if (prop == null || !prop.isArray)
                {
                    Debug.LogWarning($"[卡面填充]「{panel.name}」上找不到 {propertyName} 数组字段，已跳过。");
                    continue;
                }

                Undo.RecordObject(panel, $"填充卡面库（{label}）");
                prop.arraySize = library.Length;
                for (int i = 0; i < library.Length; i++)
                    prop.GetArrayElementAtIndex(i).objectReferenceValue = library[i];
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(panel);
                if (panel.gameObject.scene.IsValid())
                    EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            }
            return panels.Length;
        }

        /// <summary>
        /// 把牌位自带的 Image 逐个接到卡面 Image 数组上：第 i 个牌位的卡面就是它自己那个 Image
        /// （也就是 Button 的 Target Graphic），不用再给每个牌位新建装饰用的子物体。
        /// </summary>
        private static int FillImagesFromButtons<T>(string label, string imagesField, string buttonsField) where T : MonoBehaviour
        {
            T[] panels = Object.FindObjectsByType<T>(FindObjectsInactive.Include);
            int assigned = 0;

            foreach (T panel in panels)
            {
                var so = new SerializedObject(panel);
                SerializedProperty buttons = so.FindProperty(buttonsField);
                SerializedProperty images = so.FindProperty(imagesField);
                if (buttons == null || images == null || !buttons.isArray || !images.isArray)
                {
                    Debug.LogWarning($"[卡面填充]「{panel.name}」上找不到 {buttonsField} / {imagesField} 字段，已跳过。");
                    continue;
                }

                Undo.RecordObject(panel, $"接线卡面 Image（{label}）");
                images.arraySize = buttons.arraySize;
                for (int i = 0; i < buttons.arraySize; i++)
                {
                    var button = buttons.GetArrayElementAtIndex(i).objectReferenceValue as Button;
                    // 优先取按钮的目标图形，取不到再退一步取同一物体上的 Image。
                    Image image = button != null ? button.targetGraphic as Image : null;
                    if (image == null && button != null) image = button.GetComponent<Image>();

                    images.GetArrayElementAtIndex(i).objectReferenceValue = image;
                    if (image != null) assigned++;
                    else Debug.LogWarning($"[卡面填充]「{panel.name}」第 {i} 个牌位没有可用的 Image，那一格会不显示卡面。");
                }
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(panel);
                if (panel.gameObject.scene.IsValid())
                    EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            }
            return assigned;
        }
    }
}
