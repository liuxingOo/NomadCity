using System.Collections.Generic;
using NomadCity.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.EditorTools
{
    /// <summary>
    /// 一键生成（或重排）右侧「玩家状态栏」四行：左边圆形头像框 + 压框底的昵称，右边两排「图标 + 数字」
    /// （第一排 3 组：得分 / 金券 / 标记；第二排 4 组：源岩 / 源石 / 异铁 / 至纯源石）。
    ///
    /// 这套不再依赖 StatusBar 底板美术（那组图已作废）：底板用 UGUI 内置圆角矩形、头像框用内置圆形，
    /// 只是占位，想换图时直接替换 Background / AvatarFrame 的 Source Image，尺寸不用动。
    ///
    /// 所有尺寸都以「基准行 400×120」为设计稿，实际按倍数摆：倍数 = Row1 高度 / 120。
    /// 所以把 Row1 拉高一倍（240）再跑一次，四行以及里面的头像框 / 昵称 / 图标 / 数字都会等比放大。
    ///
    /// 场景里四行即使被整块删掉也能直接跑：缺 PlayerRows 会自动建、缺 Row1 会按设计稿建。
    /// 可以反复执行：同名子物体已存在就复用，不会重复生成。
    ///
    /// 用法：打开 Game 场景 → 菜单 NomadCity / 界面：生成玩家状态栏（HUD Rows）。
    /// </summary>
    public static class GameHudRowBuilder
    {
        private const string RowsHolderName = "PlayerRows";
        private const string TemplateRowName = "Row1";

        /// <summary>最多 4 名玩家，所以固定 4 行槽位；空槽位由 GameHud 运行时整行关掉。</summary>
        private const int MaxRows = 4;

        /// <summary>每行的数值文本数量：0 得分 / 1 金券 / 2 玩家标记 / 3 源岩 / 4 源石 / 5 异铁 / 6 至纯源石。</summary>
        private const int ValueCount = 7;

        // —— 子物体命名（重复执行时按这些名字复用，所以别在场景里改名） ——
        private const string BackgroundName = "Background";
        private const string AvatarFrameName = "AvatarFrame";
        private const string AvatarName = "Avatar";
        private const string NameName = "Name";
        private const string StatsName = "Stats";
        private const string IconName = "Icon";
        private const string ValueName = "Value";

        /// <summary>行左侧那颗「查看这名玩家城市面板」的小按钮。</summary>
        private const string InspectName = "Inspect";

        // —— 设计稿尺寸（倍数 = 1 时的绝对值）：基准行 400×120，下面所有数字都乘以倍数 ——

        private const float BaseRowWidth = 400f;
        private const float BaseRowHeight = 120f;

        /// <summary>头像框直径。</summary>
        private const float BaseAvatarFrameSize = 96f;

        /// <summary>头像直径：比框小 8，露出的那一圈就是「头像框」。</summary>
        private const float BaseAvatarSize = 88f;

        /// <summary>头像框中心 x（离行左边缘）。</summary>
        private const float BaseAvatarCenterX = 60f;

        private const float BaseNameWidth = 120f;
        private const float BaseNameHeight = 28f;

        /// <summary>昵称底边距行底：负值就是压到行底以下，正好压在头像框的下沿（现场按 -6 调好）。</summary>
        private const float BaseNameBottom = -6f;

        private const float BaseNameFontSize = 20f;

        /// <summary>数值区左边界（头像框右边再留一点空隙）。</summary>
        private const float BaseStatsLeft = 132f;

        private const float BaseStatsWidth = 260f;

        /// <summary>数值区高 = 2 行 × 行高。</summary>
        private const float BaseStatsHeight = 60f;

        private const float BaseIconSize = 22f;
        private const float BaseValueFontSize = 18f;
        private const float BaseStatLineHeight = 30f;

        /// <summary>行距 = 行高 + 这个间隙（间隙也按倍数放大）。</summary>
        private const float BaseRowGapPadding = 10f;

        /// <summary>「查看」小按钮的直径（设计稿 120 行高时，跟头像框一个量级的「小按钮」）。</summary>
        private const float BaseInspectSize = 30f;

        /// <summary>小按钮右边缘距行左边缘的空隙。</summary>
        private const float BaseInspectGap = 8f;

        /// <summary>查看按钮的眼睛图标（缺图时退回「查看」两个字）。</summary>
        private const string EyeIconPath = "Assets/_Project/Art/Icons/UI/EyeIcon.png";

        private static readonly Color RowBackgroundColor = new Color(0.07f, 0.08f, 0.10f, 0.86f);
        private static readonly Color AvatarFrameColor = new Color(1f, 1f, 1f, 0.85f);

        /// <summary>缺图标的槽位（得分 / 金券 / 标记）用的暗色占位，避免一块刺眼的白方块。</summary>
        private static readonly Color MissingIconColor = new Color(1f, 1f, 1f, 0.22f);

        private const string BuiltinPanelSprite = "UI/Skin/Background.psd";
        private const string BuiltinCircleSprite = "UI/Skin/Knob.psd";

        /// <summary>企业家头像，顺序 = PlayerColor（绿 0 / 黄 1 / 红 2 / 蓝 3）。</summary>
        private static readonly string[] AvatarPaths =
        {
            "Assets/_Project/Art/Avatars/Tiag_Face.png",
            "Assets/_Project/Art/Avatars/Martin_Face.png",
            "Assets/_Project/Art/Avatars/Agnes_Face.png",
            "Assets/_Project/Art/Avatars/Paul_Face.png",
        };

        /// <summary>玩家标记素材，顺序 = PlayerColor（绿 / 黄 / 红 / 蓝）。</summary>
        private static readonly string[] PlayerMarkerPaths =
        {
            "Assets/_Project/Art/Tokens/token_green.jpg",
            "Assets/_Project/Art/Tokens/token_yellow.jpg",
            "Assets/_Project/Art/Tokens/token_red.jpg",
            "Assets/_Project/Art/Tokens/token_blue.jpg",
        };

        /// <summary>数值下标 → 图标素材路径（对不上号的槽位留空，生成时用暗色方块占位）。</summary>
        private static readonly Dictionary<int, string> KnownIcons = new Dictionary<int, string>
        {
            // 得分 / 金券两张是临时占位：得分用现画的五角星，金券先用资源目录里那张 5 元券。
            { 0, "Assets/_Project/Art/Icons/UI/ScoreStar.png" },              // 得分（临时五角星）
            { 1, "Assets/_Project/Art/Icons/Resource/Money.jpg" },            // 金券（5 元券）

            // 素材命名与规则里的资源名对不上，这里按「图长什么样」对号入座：
            // Originite.png 是一堆金色六棱石块 = 源岩；Originium.png 是深色尖晶 = 源石；
            // Iron.png = 异铁；Pure Originium.png 是带六边形标记的宝石 = 至纯源石。
            { 3, "Assets/_Project/Art/Icons/Resource/Originite.png" },       // 源岩（金色六棱石块）
            { 4, "Assets/_Project/Art/Icons/Resource/Originium.png" },       // 源石（深色尖晶）
            { 5, "Assets/_Project/Art/Icons/Resource/Iron.png" },            // 异铁
            { 6, "Assets/_Project/Art/Icons/Resource/Pure Originium.png" },  // 至纯源石（宝石）
        };

        /// <summary>数值下标 → 子物体名（名字带语义，方便之后对着拖图标）。</summary>
        private static readonly string[] SlotNames =
        {
            "Score", "Voucher", "Marker", "Orirock", "Originium", "Oriron", "Prime",
        };

        [MenuItem("NomadCity/界面：生成玩家状态栏（HUD Rows）")]
        private static void Build()
        {
            GameHud hud = Object.FindAnyObjectByType<GameHud>(FindObjectsInactive.Include);
            if (hud == null)
            {
                Debug.LogError("[状态栏] 场景里找不到 GameHud（挂在 HUD 物体上）。请先打开 Game 场景。");
                return;
            }

            // —— 1. 容器 PlayerRows：没有就在 HUD 右上角新建一个 ——
            Transform holder = FindDeep(hud.transform, RowsHolderName);
            if (holder == null)
            {
                var go = new GameObject(RowsHolderName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(go, "生成玩家状态栏");
                GameObjectUtility.SetParentAndAlign(go, hud.gameObject);

                RectTransform created = (RectTransform)go.transform;
                created.anchorMin = new Vector2(1f, 1f);
                created.anchorMax = new Vector2(1f, 1f);
                created.pivot = new Vector2(1f, 1f);
                created.sizeDelta = new Vector2(BaseRowWidth,
                    BaseRowHeight + (BaseRowHeight + BaseRowGapPadding) * (MaxRows - 1));
                created.anchoredPosition = new Vector2(-10f, -150f);

                holder = go.transform;
                Debug.Log("[状态栏] HUD 下没有 PlayerRows，已在右上角新建（想挪位置直接拖这个物体）。");
            }

            RectTransform holderRect = holder as RectTransform;
            if (holderRect == null)
            {
                Debug.LogError($"[状态栏]「{holder.name}」上没有 RectTransform，无法当容器用。");
                return;
            }

            // —— 2. 基准行 Row1：没有就按设计稿建 ——
            RectTransform template = FindChild(holder, TemplateRowName);
            if (template == null)
            {
                template = EnsureRect(holder, TemplateRowName);
                template.anchorMin = new Vector2(0f, 1f);
                template.anchorMax = new Vector2(0f, 1f);
                template.pivot = new Vector2(0.5f, 0.5f);
                template.sizeDelta = new Vector2(BaseRowWidth, BaseRowHeight);
                template.anchoredPosition = new Vector2(BaseRowWidth * 0.5f, -BaseRowHeight * 0.5f);
                Debug.Log("[状态栏] 没有基准行 Row1，已按设计稿 400×120 新建（想整体放大就改它的高度，再重跑本菜单）。");
            }

            // 先把整个状态栏里的自动布局组件一次性停掉。
            // LayoutGroup / ContentSizeFitter 会在每次布局重建时重写子物体的 anchoredPosition / sizeDelta，
            // 把下面算好的坐标全部盖掉 —— 这就是「脚本明明摆好了，游戏里却全挤在一起」的唯一原因。
            int stripped = StripAutoLayoutDeep(holderRect);
            if (stripped > 0)
            {
                Debug.Log($"[状态栏] 停用了 {stripped} 个自动布局组件（LayoutGroup / ContentSizeFitter）。" +
                          "它们会在布局重建时重写子物体坐标，必须停掉脚本才算数。");
            }

            // 倍数从 Row1 的高度反推（设计稿行高 120）；宽度只决定行有多宽，不参与算倍数。
            float templateWidth = template.sizeDelta.x > 1f ? template.sizeDelta.x : BaseRowWidth;
            float templateHeight = template.sizeDelta.y > 1f ? template.sizeDelta.y : BaseRowHeight;
            float scale = Mathf.Max(0.5f, templateHeight / BaseRowHeight);

            float rowWidth = Mathf.Max(160f, templateWidth);
            float rowHeight = Mathf.Max(60f, templateHeight);
            float rowGap = rowHeight + BaseRowGapPadding * scale;

            // 行一律在 PlayerRows 里居中：中心 x 取行宽的一半，这样行不会整体偏一边、左边像被裁掉。
            float centerX = rowWidth * 0.5f;

            Layout layout = Layout.Create(rowWidth, rowHeight, scale, centerX);

            // PlayerRows 的宽高都跟着行走：宽 = 行宽（否则行会伸到父物体外面），高 = 行高 + 3 个行距。
            float neededHeight = rowHeight + rowGap * (MaxRows - 1);
            holderRect.sizeDelta = new Vector2(rowWidth, neededHeight);

            GameHudRow[] rows = new GameHudRow[MaxRows];

            for (int i = 0; i < MaxRows; i++)
            {
                string rowName = "Row" + (i + 1);

                RectTransform row = i == 0 ? template : EnsureRect(holder, rowName);
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(0f, 1f);
                row.pivot = new Vector2(0.5f, 0.5f);
                row.sizeDelta = new Vector2(rowWidth, rowHeight);
                row.anchoredPosition = new Vector2(layout.RowCenterX, -(rowGap * i + rowHeight * 0.5f));

                rows[i] = FillRow(row, layout, rowName);

                Debug.Log($"[状态栏] {rowName}: 行 {rowWidth}×{rowHeight} @({row.anchoredPosition.x}, {row.anchoredPosition.y})｜" +
                          $"头像框 {layout.AvatarFrameSize}（头像 {layout.AvatarSize}）中心 ({layout.AvatarCenter.x}, {layout.AvatarCenter.y})｜" +
                          $"昵称 {layout.NameSize.x}×{layout.NameSize.y} 字号 {layout.NameFontSize}｜" +
                          $"数值区 {layout.StatsSize.x}×{layout.StatsSize.y}@{layout.StatsPosition}｜" +
                          $"每列 {layout.ColumnWidth}，图标 {layout.IconSize}，数字字号 {layout.ValueFontSize}");
            }

            // —— 3. 把四行 / 头像接进 GameHud ——
            int wired = 0;
            var so = new SerializedObject(hud);
            SerializedProperty rowsProp = so.FindProperty("rows");
            if (rowsProp != null && rowsProp.isArray)
            {
                rowsProp.arraySize = rows.Length;
                for (int i = 0; i < rows.Length; i++)
                {
                    SerializedProperty el = rowsProp.GetArrayElementAtIndex(i);
                    GameHudRow row = rows[i];
                    SetRef(el, "Root", row.Root);
                    SetRef(el, "Background", row.Background);
                    SetRef(el, "Avatar", row.Avatar);
                    SetRef(el, "Name", row.Name);
                    SetRef(el, "MarkerIcon", row.MarkerIcon);
                    SetRef(el, "Inspect", row.Inspect);

                    SerializedProperty values = el.FindPropertyRelative("Values");
                    if (values != null && values.isArray)
                    {
                        values.arraySize = ValueCount;
                        for (int v = 0; v < ValueCount; v++)
                            values.GetArrayElementAtIndex(v).objectReferenceValue = row.Values[v];
                    }
                }
                wired++;
            }

            wired += FillSpriteArray(so, "avatarSprites", AvatarPaths);
            wired += FillSpriteArray(so, "playerMarkerSprites", PlayerMarkerPaths);

            Undo.RecordObject(hud, "接线玩家状态栏");
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(hud);

            EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
            Selection.activeGameObject = hud.gameObject;

            Debug.Log($"[状态栏] 按 {scale:0.##} 倍生成 / 重排了 {MaxRows} 行（行 {rowWidth}×{rowHeight}，行距 {rowGap}），" +
                      $"并接好 GameHud（Rows / 头像 {wired} 处）。\n" +
                      "想再放大：改 Row1 的高度（240 = 2 倍、360 = 3 倍）后重跑本菜单即可，四行会一起跟着变。\n" +
                      "每行左侧多了一颗眼睛小按钮（Inspect，挂在行外面左侧，不占行内空间）：" +
                      "点了由 GameScreenController 弹这名玩家的城市面板；要挪位置就拖它自己的 RectTransform。\n" +
                      "还要手工补的：\n" +
                      "1) 得分 / 金券两张是临时占位（Art/Icons/UI/ScoreStar.png 五角星、Resource/Money.jpg 5 元券），" +
                      "正式美术出来后替换这两张图即可，尺寸别动。\n" +
                      "2) 底板 / 头像框用的是 UGUI 内置占位图（Background / AvatarFrame），想换美术就替换它们的 Source Image，" +
                      "尺寸别动，脚本重跑也不会覆盖 sprite。");
        }

        /// <summary>填一行的部件：底板 / 头像框（内含头像）/ 昵称 / 数值区 + 7 组「图标 + 数字」。</summary>
        private static GameHudRow FillRow(RectTransform row, Layout l, string rowName)
        {
            StripAutoLayout(row);

            // 行根上如果残留着旧版直接挂的 Image（旧生成器把底板放在行根上），停掉它，
            // 否则会和下面的 Background 叠成两层白底。
            Image stray = row.GetComponent<Image>();
            if (stray != null && stray.enabled)
            {
                stray.enabled = false;
                Debug.Log($"[状态栏] {rowName} 行根上还挂着旧的 Image 底板，已停用（底板改由子物体 Background 负责）。");
            }

            // —— 底板：铺满整行，内置圆角矩形，运行时不换图，只是让四行看得见 ——
            RectTransform backgroundRect = EnsureRect(row, BackgroundName);
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.pivot = new Vector2(0.5f, 0.5f);
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            Image backgroundImage = EnsureImage(backgroundRect, LoadBuiltinSprite(BuiltinPanelSprite));
            backgroundImage.type = Image.Type.Sliced;
            backgroundImage.preserveAspect = false;
            backgroundImage.raycastTarget = false;
            backgroundImage.color = RowBackgroundColor;

            // —— 行左侧的「查看城市面板」小按钮：挂在行外侧（行左边缘再往左留一点空隙），
            //    所以不挤占头像框与数值区，行有多宽、放大到几倍都跟着走。 ——
            RectTransform inspect = EnsureRect(row, InspectName);
            StripAutoLayout(inspect);
            inspect.anchorMin = new Vector2(0f, 0.5f);
            inspect.anchorMax = new Vector2(0f, 0.5f);
            inspect.pivot = new Vector2(1f, 0.5f);          // 右边缘钉在行左边缘，向右长就是往行里，往左长才是往外
            inspect.sizeDelta = new Vector2(l.InspectSize, l.InspectSize);
            inspect.anchoredPosition = new Vector2(-l.InspectGap, 0f);

            Sprite eye = LoadSprite(EyeIconPath);
            Image inspectImage = EnsureImage(inspect, eye);
            inspectImage.type = Image.Type.Simple;
            inspectImage.preserveAspect = true;
            inspectImage.raycastTarget = true;              // Button 靠这一层收点击
            inspectImage.color = eye != null ? Color.white : new Color(1f, 1f, 1f, 0.9f);

            Button inspectButton = inspect.GetComponent<Button>();
            if (inspectButton == null) inspectButton = Undo.AddComponent<Button>(inspect.gameObject);
            inspectButton.targetGraphic = inspectImage;

            // 没读到眼睛图就退回文字按钮「查看」，两种都读不到时至少还是个能点的方块。
            RectTransform inspectLabel = FindChild(inspect, "Label");
            if (eye == null)
            {
                if (inspectLabel == null) inspectLabel = EnsureRect(inspect, "Label");
                inspectLabel.anchorMin = Vector2.zero;
                inspectLabel.anchorMax = Vector2.one;
                inspectLabel.offsetMin = Vector2.zero;
                inspectLabel.offsetMax = Vector2.zero;
                TextMeshProUGUI lookText = EnsureText(inspectLabel, Mathf.Max(10f, 14f * l.Scale), TextAlignmentOptions.Center);
                lookText.text = "查看";
                lookText.overflowMode = TextOverflowModes.Overflow;
                lookText.raycastTarget = false;
            }
            else if (inspectLabel != null)
            {
                inspectLabel.gameObject.SetActive(false);
            }

            // —— 头像框：圆形，占位就是一圈白边 ——
            RectTransform frame = EnsureRect(row, AvatarFrameName);
            StripAutoLayout(frame);
            frame.anchorMin = new Vector2(0f, 0f);
            frame.anchorMax = new Vector2(0f, 0f);
            frame.pivot = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = new Vector2(l.AvatarFrameSize, l.AvatarFrameSize);
            frame.anchoredPosition = l.AvatarCenter;
            Image frameImage = EnsureImage(frame, LoadBuiltinSprite(BuiltinCircleSprite));
            frameImage.type = Image.Type.Simple;
            frameImage.preserveAspect = true;
            frameImage.raycastTarget = false;
            frameImage.color = AvatarFrameColor;

            // —— 头像：框内居中，跟着框走（框挪了头像自动跟） ——
            RectTransform avatar = EnsureRect(frame, AvatarName);
            avatar.anchorMin = new Vector2(0.5f, 0.5f);
            avatar.anchorMax = new Vector2(0.5f, 0.5f);
            avatar.pivot = new Vector2(0.5f, 0.5f);
            avatar.sizeDelta = new Vector2(l.AvatarSize, l.AvatarSize);
            avatar.anchoredPosition = Vector2.zero;
            Image avatarImage = EnsureImage(avatar, LoadSprite(AvatarPaths[0]));
            avatarImage.type = Image.Type.Simple;
            avatarImage.preserveAspect = true;
            avatarImage.raycastTarget = false;

            // —— 昵称：压在头像框下沿，画在框之后所以盖在圆环上 ——
            RectTransform name = EnsureRect(row, NameName);
            StripAutoLayout(name);
            name.anchorMin = new Vector2(0f, 0f);
            name.anchorMax = new Vector2(0f, 0f);
            name.pivot = new Vector2(0.5f, 0f);
            name.sizeDelta = l.NameSize;
            name.anchoredPosition = l.NamePosition;
            TextMeshProUGUI nameText = EnsureText(name, l.NameFontSize, TextAlignmentOptions.Center);
            nameText.overflowMode = TextOverflowModes.Overflow;

            // —— 数值区：两排，第一排 3 组（得分 / 金券 / 标记，居中），第二排 4 种资源 ——
            RectTransform stats = EnsureRect(row, StatsName);
            StripAutoLayout(stats);
            stats.anchorMin = new Vector2(0f, 0f);
            stats.anchorMax = new Vector2(0f, 0f);
            stats.pivot = new Vector2(0f, 0f);
            stats.sizeDelta = l.StatsSize;
            stats.anchoredPosition = l.StatsPosition;

            TMP_Text[] values = new TMP_Text[ValueCount];
            Image markerIcon = null;

            // 数值区里只认这 7 个名字，其余（旧版残留 / 手拖杂项）清掉，免得叠出第二套数字。
            var expected = new HashSet<string>();
            for (int i = 0; i < ValueCount; i++) expected.Add($"Stat{i}_{SlotNames[i]}");
            for (int c = stats.childCount - 1; c >= 0; c--)
            {
                Transform child = stats.GetChild(c);
                if (expected.Contains(child.name)) continue;
                Debug.Log($"[状态栏] {rowName}/{StatsName} 里清掉了多余物体：{child.name}");
                Undo.DestroyObjectImmediate(child.gameObject);
            }

            for (int v = 0; v < ValueCount; v++)
            {
                // 第一排（上面那排）3 组：得分 / 金券 / 标记，按 4 列的宽度居中；
                // 第二排（下面那排）4 组：源岩 / 源石 / 异铁 / 至纯源石，正好占满 4 列。
                int line = v < 3 ? 1 : 0;
                float x = v < 3
                    ? (v + (4 - 3) * 0.5f) * l.ColumnWidth    // 3 组居中
                    : (v - 3) * l.ColumnWidth;                // 4 组铺满
                float y = line * l.StatLineHeight + l.StatLineHeight * 0.5f;

                RectTransform slot = EnsureRect(stats, $"Stat{v}_{SlotNames[v]}");
                StripAutoLayout(slot);
                slot.anchorMin = new Vector2(0f, 0f);
                slot.anchorMax = new Vector2(0f, 0f);
                slot.pivot = new Vector2(0f, 0.5f);
                slot.sizeDelta = new Vector2(l.ColumnWidth, l.StatLineHeight);
                slot.anchoredPosition = new Vector2(x, y);

                RectTransform icon = EnsureRect(slot, IconName);
                StripAutoLayout(icon);
                icon.anchorMin = new Vector2(0f, 0.5f);
                icon.anchorMax = new Vector2(0f, 0.5f);
                icon.pivot = new Vector2(0f, 0.5f);
                icon.sizeDelta = new Vector2(l.IconSize, l.IconSize);
                icon.anchoredPosition = Vector2.zero;

                Sprite iconSprite = LoadKnownIcon(v);
                Image iconImage = EnsureImage(icon, iconSprite);
                iconImage.type = Image.Type.Simple;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
                iconImage.color = iconSprite != null ? Color.white : MissingIconColor;

                // 第 2 格是「玩家标记」：图标运行时按玩家颜色换成 token 素材，这里把引用接给 GameHud。
                if (v == 2) markerIcon = iconImage;

                RectTransform valueRect = EnsureRect(slot, ValueName);
                StripAutoLayout(valueRect);
                valueRect.anchorMin = new Vector2(0f, 0.5f);
                valueRect.anchorMax = new Vector2(0f, 0.5f);
                valueRect.pivot = new Vector2(0f, 0.5f);
                valueRect.sizeDelta = new Vector2(l.ColumnWidth - l.IconSize - 2f * l.Scale, l.StatLineHeight);
                valueRect.anchoredPosition = new Vector2(l.IconSize + 2f * l.Scale, 0f);
                TextMeshProUGUI valueText = EnsureText(valueRect, l.ValueFontSize, TextAlignmentOptions.Left);
                valueText.overflowMode = TextOverflowModes.Overflow;   // 三位数允许略微溢出，别缩成一团
                valueText.text = "0";

                values[v] = valueText;
            }

            // 自检：一行建完，把七组的实际坐标打出来。若看到「全在 (0,0)」或「首尾同 x」，
            // 说明又有自动布局在抢坐标。
            var sb = new System.Text.StringBuilder();
            sb.Append($"[状态栏] {rowName} 数值实测：");
            for (int v = 0; v < ValueCount; v++)
            {
                RectTransform slot = FindChild(stats, $"Stat{v}_{SlotNames[v]}");
                if (slot == null) continue;
                sb.Append($" {SlotNames[v]}=({slot.anchoredPosition.x:0.#},{slot.anchoredPosition.y:0.#})");
            }
            Debug.Log(sb.ToString());

            return new GameHudRow
            {
                Root = row.gameObject,
                Background = backgroundImage,
                Avatar = avatarImage,
                Name = nameText,
                MarkerIcon = markerIcon,
                Inspect = inspectButton,
                Values = values,
            };
        }

        // —— 布局 ——

        /// <summary>设计稿尺寸 × 倍数后的实际数值，四行共用一份。</summary>
        private sealed class Layout
        {
            public float Scale = 1f;
            public float RowWidth = BaseRowWidth;
            public float RowHeight = BaseRowHeight;
            public float RowCenterX = BaseRowWidth * 0.5f;

            public float AvatarFrameSize = BaseAvatarFrameSize;
            public float AvatarSize = BaseAvatarSize;
            public Vector2 AvatarCenter;

            public Vector2 NameSize;
            public Vector2 NamePosition;
            public float NameFontSize;

            public Vector2 StatsSize;
            public Vector2 StatsPosition;

            public float ColumnWidth;
            public float IconSize;
            public float ValueFontSize;
            public float StatLineHeight;

            public float InspectSize = BaseInspectSize;
            public float InspectGap = BaseInspectGap;

            /// <summary>
            /// 以设计稿为基准乘倍数。行宽被手动拉宽时，数值区最多占到行尾留一点边距。
            /// </summary>
            public static Layout Create(float rowWidth, float rowHeight, float scale, float rowCenterX)
            {
                var layout = new Layout
                {
                    Scale = scale,
                    RowWidth = rowWidth,
                    RowHeight = rowHeight,
                    RowCenterX = rowCenterX,

                    AvatarFrameSize = BaseAvatarFrameSize * scale,
                    AvatarSize = BaseAvatarSize * scale,
                    // 头像框在行内垂直居中，这样昵称压在框下沿时的重叠量是固定比例。
                    AvatarCenter = new Vector2(BaseAvatarCenterX * scale, rowHeight * 0.5f),

                    NameSize = new Vector2(BaseNameWidth * scale, BaseNameHeight * scale),
                    NamePosition = new Vector2(BaseAvatarCenterX * scale, BaseNameBottom * scale),
                    NameFontSize = Mathf.Max(10f, BaseNameFontSize * scale),

                    IconSize = BaseIconSize * scale,
                    ValueFontSize = Mathf.Max(9f, BaseValueFontSize * scale),
                    StatLineHeight = BaseStatLineHeight * scale,

                    InspectSize = BaseInspectSize * scale,
                    InspectGap = BaseInspectGap * scale,
                };

                float statsLeft = BaseStatsLeft * scale;
                // 四列至少要塞得下一个图标 + 三位数字，否则宁可挤也别缩成一条。
                float minWidth = 4f * (layout.IconSize + 8f);
                float maxWidth = rowWidth - statsLeft - 8f * scale;
                float width = Mathf.Max(minWidth, Mathf.Min(BaseStatsWidth * scale, maxWidth));

                // 两排数值在行内垂直居中。
                layout.StatsPosition = new Vector2(statsLeft, (rowHeight - BaseStatsHeight * scale) * 0.5f);
                layout.StatsSize = new Vector2(width, BaseStatsHeight * scale);
                layout.ColumnWidth = width / 4f;
                return layout;
            }
        }

        // —— 小工具 ——

        /// <summary>只读检查：把 PlayerRows 子树的实际坐标打到 Console，不改场景里的任何东西。</summary>
        [MenuItem("NomadCity/界面：打印玩家状态栏坐标（检查用）")]
        private static void DumpPositions()
        {
            GameHud hud = Object.FindAnyObjectByType<GameHud>(FindObjectsInactive.Include);
            if (hud == null)
            {
                Debug.LogError("[状态栏] 场景里找不到 GameHud。请先打开 Game 场景。");
                return;
            }

            Transform holder = FindDeep(hud.transform, RowsHolderName);
            if (holder == null)
            {
                Debug.LogError($"[状态栏] HUD 下没有「{RowsHolderName}」，先去跑一次生成菜单。");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[状态栏] 坐标实测（子物体坐标以父物体左下 / 中心为原点，单位与行一致）：");
            DumpRectTree((RectTransform)holder, 0, sb);

            int layouts = CountEnabledLayouts((RectTransform)holder);
            sb.Append(layouts == 0
                ? "没有启用的自动布局组件：坐标不会被系统改写。"
                : $"警告：还有 {layouts} 个启用的自动布局组件，坐标会被它们改写！");
            Debug.Log(sb.ToString());
        }

        private static void DumpRectTree(RectTransform rect, int depth, System.Text.StringBuilder sb)
        {
            if (rect == null || depth > 3) return;

            sb.Append(new string(' ', depth * 2));
            sb.Append($"{rect.name}  pos=({rect.anchoredPosition.x:0.#},{rect.anchoredPosition.y:0.#})");
            sb.Append($"  size=({rect.sizeDelta.x:0.#},{rect.sizeDelta.y:0.#})");
            sb.Append($"  anchor=({rect.anchorMin.x:0.#},{rect.anchorMin.y:0.#})-({rect.anchorMax.x:0.#},{rect.anchorMax.y:0.#})");
            sb.Append($"  pivot=({rect.pivot.x:0.#},{rect.pivot.y:0.#})");

            TMP_Text text = rect.GetComponent<TMP_Text>();
            if (text != null) sb.Append($"  文本=\"{text.text}\" 字号 {text.fontSize:0.#}");

            Image image = rect.GetComponent<Image>();
            if (image != null) sb.Append($"  sprite={(image.sprite != null ? image.sprite.name : "无")}");

            sb.AppendLine();

            // 数值槽更下一层，只展开 Icon / Value 这种叶子，避免整棵树刷屏。
            foreach (Transform child in rect) DumpRectTree(child as RectTransform, depth + 1, sb);
        }

        private static int CountEnabledLayouts(RectTransform root)
        {
            if (root == null) return 0;
            int count = 0;
            foreach (LayoutGroup group in root.GetComponentsInChildren<LayoutGroup>(true))
                if (group != null && group.enabled) count++;
            foreach (ContentSizeFitter fitter in root.GetComponentsInChildren<ContentSizeFitter>(true))
                if (fitter != null && fitter.enabled) count++;
            return count;
        }

        private static void SetRef(SerializedProperty element, string field, Object value)
        {
            SerializedProperty prop = element.FindPropertyRelative(field);
            if (prop != null) prop.objectReferenceValue = value;
        }

        /// <summary>把一组资源路径填进 GameHud 的 Sprite 数组字段，返回填了几项。</summary>
        private static int FillSpriteArray(SerializedObject so, string field, string[] paths)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null || !prop.isArray) return 0;

            prop.arraySize = paths.Length;
            int filled = 0;
            for (int i = 0; i < paths.Length; i++)
            {
                Sprite sprite = LoadSprite(paths[i]);
                prop.GetArrayElementAtIndex(i).objectReferenceValue = sprite;
                if (sprite != null) filled++;
            }
            return filled;
        }

        /// <summary>
        /// 按路径读一张精灵。token_* 那几张是「Sprite Mode = Multiple」的图集，
        /// 直接 LoadAssetAtPath&lt;Sprite&gt; 会拿到 null，所以再兜一层 LoadAllAssetsAtPath。
        /// </summary>
        private static Sprite LoadSprite(string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Sprite child)
                {
                    sprite = child;
                    break;
                }
            }

            if (sprite == null) Debug.LogWarning($"[状态栏] 读不到精灵：{path}");
            return sprite;
        }

        private static Sprite LoadKnownIcon(int index)
        {
            // 「玩家标记」那格没有专属图：运行时按玩家颜色换成 token 素材，这里先用绿色那张做静态预览。
            if (index == 2) return LoadSprite(PlayerMarkerPaths[0]);
            return KnownIcons.TryGetValue(index, out string path) ? LoadSprite(path) : null;
        }

        /// <summary>UGUI 自带的那几张通用图（圆角面板 / 圆形），用来做占位底板与头像框。</summary>
        private static Sprite LoadBuiltinSprite(string resourcePath)
        {
            Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(resourcePath);
            if (sprite == null) Debug.LogWarning($"[状态栏] 读不到内置精灵：{resourcePath}（这一层会退化成纯色方块）");
            return sprite;
        }

        /// <summary>
        /// 停用某个物体子树里所有 LayoutGroup / ContentSizeFitter。
        /// 它们每次布局重建都会把子物体的 anchoredPosition 重新算一遍写回 RectTransform，
        /// 脚本刚写好的坐标会被立刻覆盖 —— 表现就是「数值全挤在左下角 / 竖着排成一列」。
        /// 只停用不删除：随时可以在 Inspector 里勾回来，也不怕误删你手搭的组件。
        /// </summary>
        private static void StripAutoLayout(RectTransform rect) => StripAutoLayoutDeep(rect);

        /// <summary>递归停用子树里所有自动布局组件，返回停用的个数。</summary>
        private static int StripAutoLayoutDeep(RectTransform root)
        {
            if (root == null) return 0;

            int removed = 0;

            foreach (LayoutGroup group in root.GetComponentsInChildren<LayoutGroup>(true))
            {
                if (group == null || !group.enabled) continue;
                Undo.RecordObject(group, "停用自动布局");
                group.enabled = false;
                Debug.Log($"[状态栏] 停用了 {group.GetType().Name}：{group.gameObject.name}（它会把子物体坐标改回自动排布）");
                removed++;
            }

            foreach (ContentSizeFitter fitter in root.GetComponentsInChildren<ContentSizeFitter>(true))
            {
                if (fitter == null || !fitter.enabled) continue;
                Undo.RecordObject(fitter, "停用自动布局");
                fitter.enabled = false;
                Debug.Log($"[状态栏] 停用了 ContentSizeFitter：{fitter.gameObject.name}");
                removed++;
            }

            return removed;
        }

        private static RectTransform EnsureRect(Transform parent, string name)
        {
            RectTransform existing = FindChild(parent, name);
            if (existing != null) return existing;

            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "生成玩家状态栏");
            GameObjectUtility.SetParentAndAlign(go, parent.gameObject);
            return (RectTransform)go.transform;
        }

        private static Image EnsureImage(RectTransform rect, Sprite sprite)
        {
            Image image = rect.GetComponent<Image>();
            if (image == null) image = rect.gameObject.AddComponent<Image>();
            if (sprite != null) image.sprite = sprite;
            return image;
        }

        private static TextMeshProUGUI EnsureText(RectTransform rect, float fontSize, TextAlignmentOptions alignment)
        {
            TextMeshProUGUI text = rect.GetComponent<TextMeshProUGUI>();
            if (text == null) text = rect.gameObject.AddComponent<TextMeshProUGUI>();

            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            if (text.font == null && TMP_Settings.defaultFontAsset != null)
                text.font = TMP_Settings.defaultFontAsset;
            return text;
        }

        private static RectTransform FindChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
                if (child.name == name) return child as RectTransform;
            return null;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
