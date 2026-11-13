using System;
using System.Collections.Generic;
using System.Text;
using NomadCity.Core.Model;
using NomadCity.Core.Model.Enterprise;
using NomadCity.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 合作企业面板：平铺查看 / 开局抽选 / 提升等级 / 莱茵生命科室板选择，共用一套图片卡面交互。
    ///
    /// —— 玩家标记摆法（用户明确要求）——
    ///   初始放在 L0 右边的 4 个方框里；升级就竖直向上平移到对应等级正中间。
    ///   水平位置固定 4 个 X 中心（L0 方框中心：0.40 / 0.50 / 0.60 / 0.70 卡面宽），大小不变。
    ///   Y 坐标由运行时像素扫描每级效果条的 Y 中心精确给出。
    ///
    /// —— 效果条热区 ——
    ///   每张板 L1-L5 的效果区域（含左侧 LEVEL 数字圈 + 右侧效果文字/图标）做精确边界框检测，
    ///   铺一层透明按钮：悬停时半透明白色遮罩变亮，点击弹出该等级的放大裁图（不含玩家标记）。
    ///
    /// —— 莱茵生命科室板 ——
    ///   选莱茵生命后弹 3 张科室板挑 1 张，面板上 L2-L5 的效果区切换为科室板对应等级的效果段；
    ///   放大查看时，把企业板上左侧 LEVEL 数字圈区域和科室板上对应等级的效果区域水平拼接显示。
    /// </summary>
    public sealed class EnterpriseBoardPanel : MonoBehaviour
    {
        public enum Mode { View = 0, Draft = 1, Advance = 2, RhineDepartment = 3, Codex = 4 }

        private static readonly Color WindowColor = new Color(0.247f, 0.208f, 0.153f, 0.97f);
        private static readonly Color BoardColor = new Color(0.165f, 0.142f, 0.110f, 0.98f);
        private static readonly Color TextColor = new Color(0.95f, 0.92f, 0.86f, 1f);
        private static readonly Color MutedColor = new Color(0.76f, 0.72f, 0.63f, 1f);

        private const float BaseWidth = 1020f;
        private const float BaseHeight = 620f;
        private const float ZoomWidth = 1040f;
        private const float ZoomHeight = 980f;
        private const float PaddingX = 18f;
        private const float PaddingTop = 88f;
        private const float PaddingBottom = 64f;
        private const float BoardGap = 16f;

        /// <summary>4 个玩家标记固定水平中心（L0 行右侧 4 个浅色方框的实测中心，比例 0-1 卡面宽）。</summary>
        private static readonly float[] MarkerCentersXRatio = { 0.398f, 0.532f, 0.667f, 0.798f };

        /// <summary>标记相对卡面宽度的尺寸比例（方框宽 58/650）。</summary>
        private const float MarkerSizeRatio = 0.092f;

        // —— 企业板 × 等级 缓存 ——

        /// <summary>每块企业板 × 每级（0-5）效果条的完整 uvRect（整张拼版图坐标）。</summary>
        private static readonly Rect[,] EffectStripUvCache = new Rect[EnterpriseCatalog.BoardCount, 6];

        /// <summary>每块企业板 × 每级（0-5）效果条的 Y 中心（卡面内从上到下的像素行号）。</summary>
        private static readonly int[,] LevelYCenterCache = new int[EnterpriseCatalog.BoardCount, 6];

        /// <summary>每块企业板 × 每级的左侧凸起左段（黑色 LEVEL 方块这一列，含上下人字纹）。莱茵放大拼接用。</summary>
        private static readonly Rect[,] LevelBadgeUvCache = new Rect[EnterpriseCatalog.BoardCount, 6];

        private static readonly bool[] EffectStripCached = new bool[EnterpriseCatalog.BoardCount];

        // —— 科室板 × 等级 缓存 ——

        /// <summary>每张科室板 × 每级（2-5）效果区域 uvRect（整张科室板纹理坐标）。</summary>
        private static readonly Rect[,] DepartmentEffectUvCache = new Rect[EnterpriseCardArt.DepartmentCount, 6];

        /// <summary>每张科室板 × 每级（2-5）效果区域 Y 中心（科室板内从上到下的像素行号）。</summary>
        private static readonly int[,] DepartmentLevelYCenter = new int[EnterpriseCardArt.DepartmentCount, 6];

        private static readonly bool[] DepartmentCached = new bool[EnterpriseCardArt.DepartmentCount];

        // —— 事件 ——

        public Action<int> BoardPicked;
        public Action<int> AdvancePicked;
        public Action<int> RhineDepartmentPicked;

        public bool IsOpen => root != null && root.activeSelf;
        public Mode CurrentMode => mode;
        public int Seat => seat;

        private TMP_FontAsset font;
        private GameObject root;
        private RectTransform window;
        private RectTransform content;
        private TMP_Text title;
        private TMP_Text hint;
        private Button backButton;
        private Button closeButton;

        private GameState state;
        private Mode mode = Mode.View;
        private int seat = -1;
        private int zoomBoard = -1;       // 放大的整块企业板
        private int zoomedStripBoard = -1; // 放大的某级效果条（-1=没开）
        private int zoomedStripLevel = -1;
        private string titleText = "合作企业";
        private string hintText = string.Empty;
        private string signature = string.Empty;

        private float windowWidth = BaseWidth;
        private float windowHeight = BaseHeight;
        private float ContentWidth => windowWidth - PaddingX * 2f;
        private float ContentHeight => windowHeight - PaddingTop - PaddingBottom;

        /// <summary>开局选企业 / 选科室板是必要流程，必须不可关闭、不可绕过。</summary>
        private bool IsMandatory => mode == Mode.Draft || mode == Mode.RhineDepartment;

        // —— 创建 ——

        public static EnterpriseBoardPanel Create(Transform canvas, TMP_FontAsset font)
        {
            GameObject host = new GameObject("EnterpriseBoardPanel", typeof(RectTransform));
            host.transform.SetParent(canvas, false);
            var panel = host.AddComponent<EnterpriseBoardPanel>();
            panel.font = font;
            panel.Build();
            return panel;
        }

        // —— 打开 ——

        public void OpenView(GameState gameState)
        {
            mode = Mode.View; seat = -1;
            state = gameState;
            titleText = "合作企业";
            hintText = "黑色效果条是各等级的效果，鼠标悬停变亮、点一下精确放大。标记按合作等级停在对应那一行。";

            // 平铺视图也标一下莱茵生命当前用的科室板，不用放大就能确认。
            int depId = DepartmentOfBoard((int)EnterpriseId.RhineLab);
            if (depId >= 0)
            {
                string depName = EnterpriseCatalog.Department(depId)?.Name ?? $"科室板 {depId + 1}";
                hintText += $" 莱茵生命当前科室板：{depName}（点开卡面查看整张科室板）。";
            }

            OpenInternal(gameState);
        }

        public void OpenDraft(GameState gameState, int draftSeat, string t, string h)
        {
            mode = Mode.Draft; seat = draftSeat;
            titleText = t; hintText = h;
            OpenInternal(gameState);
        }

        public void OpenAdvance(GameState gameState, int advanceSeat, string t, string h)
        {
            mode = Mode.Advance; seat = advanceSeat;
            titleText = t; hintText = h;
            OpenInternal(gameState);
        }

        /// <summary>莱茵生命：三张科室板挑一张。</summary>
        public void OpenRhineDepartment(GameState gameState, int draftSeat, string t, string h)
        {
            mode = Mode.RhineDepartment; seat = draftSeat;
            titleText = t; hintText = h;
            OpenInternal(gameState);
        }

        /// <summary>
        /// 图鉴专用：只打开一块企业板的单卡视图（没有玩家标记、没有确认按钮），
        /// 点等级热区可继续放大该级效果；返回键先回到单卡，再退出。
        /// 图鉴在标题界面也能打开，所以 <paramref name="gameState"/> 允许为空。
        /// </summary>
        public void OpenSingle(GameState gameState, int boardId)
        {
            if (boardId < 0 || boardId >= EnterpriseCatalog.BoardCount) return;

            mode = Mode.Codex; seat = -1;
            EnterpriseBoardDef def = EnterpriseCatalog.Board(boardId);
            titleText = def != null ? def.Name : $"企业板 {boardId}";
            hintText = "点左侧黑色等级方块可放大查看该级效果。";

            OpenInternal(gameState, boardId);
        }

        private void OpenInternal(GameState gameState, int openBoard = -1)
        {
            if (root == null) return;
            root.SetActive(true);
            root.transform.SetAsLastSibling();
            state = gameState;
            zoomBoard = openBoard; zoomedStripBoard = -1; zoomedStripLevel = -1;
            signature = string.Empty;
            Rebuild();
        }

        public void Close()
        {
            if (root != null) root.SetActive(false);
        }

        public void Refresh(GameState gameState)
        {
            if (!IsOpen || gameState == null) return;
            state = gameState;
            List<int> cards = Cards();
            if (zoomBoard >= 0 && !cards.Contains(zoomBoard)) zoomBoard = -1;
            string next = BuildSignature(cards);
            if (next == signature) return;
            Rebuild();
        }

        public static List<int> AdvanceCandidates(GameState gameState, int advanceSeat)
        {
            var r = new List<int>();
            if (gameState == null) return r;
            EnterpriseState en = gameState.Enterprises;
            foreach (int b in en.ActiveBoards)
            {
                if (b == en.PendingAdvanceExcludeBoard) continue;
                int l = en.LevelOf(b, advanceSeat);
                if (l >= 0 && l < 5) r.Add(b);
            }
            return r;
        }

        private List<int> Cards()
        {
            var r = new List<int>();
            if (mode == Mode.Codex)
            {
                if (zoomBoard >= 0) r.Add(zoomBoard);
                return r;
            }
            if (state == null) return r;
            if (mode == Mode.RhineDepartment)
            {
                // 3 张科室板
                for (int i = 0; i < EnterpriseCardArt.DepartmentCount; i++) r.Add(i);
                return r;
            }
            if (mode == Mode.View) r.AddRange(state.Enterprises.ActiveBoards);
            else if (mode == Mode.Draft)
            {
                if (seat >= 0 && seat < state.Enterprises.Offers.Count)
                    r.AddRange(state.Enterprises.Offers[seat]);
            }
            else if (mode == Mode.Advance)
            {
                r.AddRange(AdvanceCandidates(state, seat));
            }
            return r;
        }

        // —— 运行时搭 UI ——

        private void Build()
        {
            root = gameObject;
            RectTransform rootRect = (RectTransform)root.transform;
            Stretch(rootRect);

            Image dim = CreateImage("Dim", rootRect, new Color(0f, 0f, 0f, 0.55f));
            dim.raycastTarget = true;   // 盖住底下的所有按钮：面板打开时只允许点面板里的东西
            Stretch(dim.rectTransform);
            dim.gameObject.AddComponent<DimClickHole>();   // 只给「图鉴」按钮留可点洞口

            Image windowImage = CreateImage("Window", rootRect, WindowColor);
            window = windowImage.rectTransform;
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.pivot = new Vector2(0.5f, 0.5f);
            window.anchoredPosition = Vector2.zero;
            window.sizeDelta = new Vector2(BaseWidth, BaseHeight);

            // 注意：锚点收成一个点后 sizeDelta 就是实际宽高，不能写负数（会变成单字竖排）
            title = CreateText("Title", window, "合作企业", 26f, TextAlignmentOptions.Center);
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -12f);
            title.rectTransform.sizeDelta = new Vector2(BaseWidth - 220f, 38f);

            hint = CreateText("Hint", window, string.Empty, 14f, TextAlignmentOptions.Center);
            hint.color = MutedColor;
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            hint.rectTransform.pivot = new Vector2(0.5f, 1f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, -52f);
            hint.rectTransform.sizeDelta = new Vector2(BaseWidth - 80f, 24f);

            backButton = CreateButton("Back", window, "← 返回", 18f);
            var backRect = backButton.GetComponent<RectTransform>();
            backRect.anchorMin = backRect.anchorMax = new Vector2(0f, 1f);
            backRect.pivot = new Vector2(0f, 1f);
            backRect.anchoredPosition = new Vector2(16f, -14f);
            backRect.sizeDelta = new Vector2(84f, 32f);
            backButton.onClick.AddListener(() =>
            {
                if (zoomedStripBoard >= 0) { zoomedStripBoard = -1; zoomedStripLevel = -1; Rebuild(); }
                else if (zoomBoard >= 0)
                {
                    // 图鉴的单卡视图没有「一排板」可回，直接退出回图鉴。
                    if (mode == Mode.Codex) { Close(); return; }
                    zoomBoard = -1; Rebuild();
                }
            });
            backButton.gameObject.SetActive(false);

            Button close = CreateButton("Close", window, "关闭", 20f);
            var closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-16f, -14f);
            closeRect.sizeDelta = new Vector2(72f, 32f);
            close.onClick.AddListener(Close);
            closeButton = close;

            content = CreateRect("Content", window);
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(1f, 1f);
            content.offsetMin = new Vector2(PaddingX, PaddingBottom);
            content.offsetMax = new Vector2(-PaddingX, -PaddingTop);

            root.SetActive(false);
        }

        private void Rebuild()
        {
            if (content == null) return;

            ComputeWindowSize();
            window.sizeDelta = new Vector2(windowWidth, windowHeight);
            if (title != null)
            {
                title.text = titleText;
                title.rectTransform.sizeDelta = new Vector2(windowWidth - 220f, 38f);
            }
            if (hint != null)
            {
                hint.text = hintText;
                hint.rectTransform.sizeDelta = new Vector2(windowWidth - 80f, 24f);
            }
            if (backButton != null)
                backButton.gameObject.SetActive(zoomBoard >= 0 || zoomedStripBoard >= 0);
            // 开局选企业 / 选科室板是必要流程：不给关闭按钮，必须选完才能继续
            if (closeButton != null) closeButton.gameObject.SetActive(!IsMandatory);

            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);

            signature = BuildSignature(Cards());
            // 图鉴的单卡视图不需要对局状态；其余模式没有状态就没法画标记与等级。
            if (state == null && zoomBoard < 0 && zoomedStripBoard < 0) return;

            if (mode == Mode.RhineDepartment)
            {
                BuildDepartmentRow();
                return;
            }

            if (zoomedStripBoard >= 0) BuildZoomedEffectStrip(zoomedStripBoard, zoomedStripLevel);
            else if (zoomBoard >= 0) BuildZoomedBoard(zoomBoard);
            else BuildBoardRow(Cards());
        }

        /// <summary>
        /// 按当前要显示的卡牌数量算窗口大小：
        /// 卡面高度固定，宽度按卡面宽高比推；卡很多时窗口封顶、卡面等比缩小。
        /// </summary>
        private void ComputeWindowSize()
        {
            if (zoomBoard >= 0 || zoomedStripBoard >= 0)
            {
                windowWidth = ZoomWidth;
                windowHeight = ZoomHeight;
                return;
            }

            List<int> cards = Cards();
            bool dept = mode == Mode.RhineDepartment;
            int count = dept ? EnterpriseCardArt.DepartmentCount : cards.Count;
            count = Mathf.Max(1, count);
            float aspect = dept
                ? 516f / 1000f
                : EnterpriseCardArt.AspectOf(cards.Count > 0 ? cards[0] : 0);

            const float cardHeight = 560f;
            const float maxWindowWidth = 1500f;
            float cardWidth = cardHeight * aspect;
            float needed = count * cardWidth + (count - 1) * BoardGap + PaddingX * 2f;

            windowWidth = Mathf.Clamp(needed, 620f, maxWindowWidth);
            windowHeight = cardHeight + PaddingTop + PaddingBottom;
        }

        // —— 平铺 ——

        private void BuildBoardRow(List<int> boards)
        {
            if (boards.Count == 0)
            {
                TMP_Text empty = CreateText("Empty", content, EmptyText(), 20f, TextAlignmentOptions.Center);
                empty.color = MutedColor;
                Stretch(empty.rectTransform);
                return;
            }

            float aspect = EnterpriseCardArt.AspectOf(boards[0]);
            float width = ContentHeight * aspect;
            if (width * boards.Count + BoardGap * (boards.Count - 1) > ContentWidth)
                width = (ContentWidth - BoardGap * (boards.Count - 1)) / boards.Count;
            float height = width / aspect;
            float startX = -(width * boards.Count + BoardGap * (boards.Count - 1)) * 0.5f + width * 0.5f;

            for (int i = 0; i < boards.Count; i++)
                BuildBoardCard(boards[i], startX + i * (width + BoardGap), width, height, false);
        }

        private void BuildZoomedBoard(int boardId)
        {
            float height = ContentHeight;
            float width = height * EnterpriseCardArt.AspectOf(boardId);
            float maxWidth = ContentWidth * 0.46f;
            if (width > maxWidth) { width = maxWidth; height = width / EnterpriseCardArt.AspectOf(boardId); }

            float leftX = -ContentWidth * 0.5f + width * 0.5f;
            BuildBoardCard(boardId, leftX, width, height, true);

            float sideWidth = ContentWidth - width - 30f;

            // 莱茵生命：右侧直接摆出玩家选定的科室板（L2-L5 的效果都在那张板上）。
            // 只在查看模式这样做；开局选企业 / 提升等级还需要右侧的确认按钮。
            int depId = DepartmentOfBoard(boardId);
            if (mode == Mode.View && depId >= 0)
                BuildDepartmentSideCard(depId, ContentWidth * 0.5f - sideWidth * 0.5f, sideWidth, ContentHeight);
            else
                BuildSideCard(boardId, ContentWidth * 0.5f - sideWidth * 0.5f, sideWidth, ContentHeight);
        }

        /// <summary>
        /// 这张企业板采用的科室板 id；不是莱茵生命 = -1。
        /// 先看本面板的座位，再找场上选了莱茵生命的那位玩家（科室板只有他会选）。
        /// </summary>
        private int DepartmentOfBoard(int boardId)
        {
            if (state == null || boardId != (int)EnterpriseId.RhineLab) return -1;

            List<int> departments = state.Enterprises.Departments;
            if (seat >= 0 && seat < departments.Count && departments[seat] >= 0) return departments[seat];
            for (int s = 0; s < departments.Count; s++)
                if (departments[s] >= 0) return departments[s];
            return -1;
        }

        /// <summary>画一块企业板：卡面 + 玩家标记 + 效果条热区按钮。</summary>
        private void BuildBoardCard(int boardId, float centerX, float width, float height, bool zoom)
        {
            EnterpriseBoardDef def = EnterpriseCatalog.Board(boardId);

            GameObject cardObject = new GameObject($"Board_{boardId}", typeof(RectTransform));
            cardObject.transform.SetParent(content, false);
            RectTransform cardRect = (RectTransform)cardObject.transform;
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.anchoredPosition = new Vector2(centerX, 0f);
            cardRect.sizeDelta = new Vector2(width, height);

            Graphic graphic;
            Texture2D sheet = EnterpriseCardArt.Sheet;
            if (sheet != null)
            {
                RawImage face = cardObject.AddComponent<RawImage>();
                face.texture = sheet;
                face.uvRect = EnterpriseCardArt.BoardUv(boardId);
                face.color = Color.white;
                graphic = face;
            }
            else
            {
                Image bg = cardObject.AddComponent<Image>();
                bg.color = BoardColor;
                graphic = bg;
                BuildMissingFaceHint(cardRect, def, boardId, width, zoom);
            }

            if (!zoom)
            {
                Button open = cardObject.AddComponent<Button>();
                open.targetGraphic = graphic;
                open.onClick.AddListener(() => { zoomBoard = boardId; Rebuild(); });
            }

            // 玩家标记只在 View 模式下画；Draft/Advance/RhineDepartment 不画
            if (mode == Mode.View) BuildInfluenceMarkers(cardRect, boardId, width, height);

            // 等级热区只在「单独点开某一块板」的窗口里铺：
            // 平铺查看 / 开局二选一 时点等级不响应，避免和选牌、翻看整排板抢点击。
            if (zoom) BuildEffectStripHotZones(cardRect, boardId, width, height);
        }

        // —— 玩家标记 ——

        private void BuildInfluenceMarkers(RectTransform cardRect, int boardId, float width, float height)
        {
            EnterpriseState en = state.Enterprises;
            int n = state.Players.Count;
            EnsureEffectStripUv(boardId);

            for (int seatIndex = 0; seatIndex < n; seatIndex++)
            {
                int level = en.LevelOf(boardId, seatIndex);
                if (level < 0) continue;

                int cellIndex = seatIndex % 4;
                float xRatio = MarkerCentersXRatio[cellIndex];

                // Y 中心：像素扫描得到的 LevelYCenterCache（像素原点左下，从卡面底部往上量）
                Texture2D sheet = EnterpriseCardArt.Sheet;
                int cellH = sheet != null ? sheet.height / EnterpriseCardArt.BoardRows : 1850;
                int yCenterPx = LevelYCenterCache[boardId, level];
                if (yCenterPx <= 0)
                    yCenterPx = Mathf.RoundToInt(cellH *
                        (1f - (LevelBandTop[level] + LevelBandBottom[level]) * 0.5f));
                float yRatioFromBottom = (float)yCenterPx / cellH;

                float localX = width * xRatio - width * 0.5f;
                float localY = height * (yRatioFromBottom - 0.5f);

                PlayerState player = state.Players[seatIndex];
                Image token = CreateImage($"Mk_{seatIndex}_L{level}", cardRect, Color.white);
                token.raycastTarget = false;
                token.sprite = EnterpriseCardArt.Token(player.Color);
                if (token.sprite == null)
                {
                    token.sprite = MapSpriteFactory.Disc();
                    token.color = MapPalette.Player(player.Color);
                }

                float cellSize = width * MarkerSizeRatio;
                RectTransform tr = token.rectTransform;
                tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f);
                tr.pivot = new Vector2(0.5f, 0.5f);
                tr.sizeDelta = new Vector2(cellSize, cellSize);
                tr.anchoredPosition = new Vector2(localX, localY);
            }
        }

        // —— 效果条热区 ——

        /// <summary>
        /// 给 L1-L5 每级铺一层热区按钮：悬停时变亮（半透明白色遮罩），点击弹出该级的精确裁图。
        /// 热区位置由运行时像素扫描企业板卡面得到（精确到像素）。
        ///
        /// 莱茵生命特殊处理：L2-L5 的右侧效果区域在科室板上，需要水平拼接企业板的 LEVEL 圈 + 科室板的效果段。
        /// </summary>
        private void BuildEffectStripHotZones(RectTransform cardRect, int boardId, float width, float height)
        {
            EnsureEffectStripUv(boardId);
            for (int dep = 0; dep < EnterpriseCardArt.DepartmentCount; dep++)
                EnsureDepartmentEffectUv(dep);

            for (int level = 1; level <= 5; level++)
            {
                // 必须存成局部变量：for 的循环变量在循环结束后会变成 6，
                // 直接捕获它会让点击用越界下标取缓存（曾导致点任意等级都抛异常、面板空白）。
                int pickedLevel = level;

                // 热区严格盖住左侧那块黑色 LEVEL 方块（用户要求：隐形按键贴着黑色正方形）
                float xMin = BadgeLeft;
                float xMax = BadgeRight;
                float yMin = 1f - LevelBandBottom[level];
                float yMax = 1f - LevelBandTop[level];
                if (xMax - xMin < 0.01f || yMax - yMin < 0.01f) continue;

                Image hoverMask = CreateImage($"HotL{level}", cardRect, new Color(1f, 1f, 1f, 0f));
                RectTransform hr = hoverMask.rectTransform;
                hr.anchorMin = new Vector2(xMin, yMin);
                hr.anchorMax = new Vector2(xMax, yMax);
                hr.pivot = new Vector2(0.5f, 0.5f);
                hr.anchoredPosition = Vector2.zero;
                hr.sizeDelta = Vector2.zero;

                Button btn = hoverMask.gameObject.AddComponent<Button>();
                btn.targetGraphic = hoverMask;
                btn.onClick.AddListener(() =>
                {
                    // 保留 zoomBoard：放大等级窗口的「返回」要回到这块板的单卡窗口。
                    zoomedStripBoard = boardId;
                    zoomedStripLevel = pickedLevel;
                    Rebuild();
                });

                var colors = btn.colors;
                colors.highlightedColor = new Color(1f, 1f, 1f, 0.30f);
                colors.normalColor = new Color(1f, 1f, 1f, 0f);
                colors.selectedColor = new Color(1f, 1f, 1f, 0.30f);
                btn.colors = colors;
            }
        }

        // —— 效果条几何（实测常量：卡面是固定模板，不需要运行时猜）——

        /// <summary>
        /// 各等级整块「凸起区域」的竖直范围（从凸起条顶部圆角到下方阴影线），比例从卡面顶部量。
        /// 实测自 card_enterprise.jpg 单张 650×1850，8 块板模板一致。下标 = 等级 0..5。
        /// 像素：L5 400..658、L4 665..916、L3 921..1172、L2 1173..1420、L1 1421..1660、L0 1672..1850。
        /// </summary>
        private static readonly float[] LevelBandTop =
        {
            0.9038f, 0.7681f, 0.6341f, 0.4978f, 0.3595f, 0.2162f
        };

        private static readonly float[] LevelBandBottom =
        {
            1.0000f, 0.8973f, 0.7676f, 0.6335f, 0.4951f, 0.3557f
        };

        /// <summary>左侧黑色 LEVEL 方块的水平范围（实测 x=26..140 / 650）：热区要刚好盖住这块黑色正方形。</summary>
        private const float BadgeLeft = 0.040f;
        private const float BadgeRight = 0.2155f;

        /// <summary>整个等级效果行（LEVEL 方块 + 右侧效果文字）的水平范围，放大裁图用。</summary>
        private const float RowLeft = 0.040f;
        private const float RowRight = 0.972f;

        /// <summary>科室板（516×1000）里 L2-L5 四个分区的竖直范围，比例从顶部量。下标 = 等级。</summary>
        private static readonly float[] DeptBandTop =
        {
            0f, 0f, 0.765f, 0.518f, 0.262f, 0f
        };

        private static readonly float[] DeptBandBottom =
        {
            0f, 0f, 1f, 0.746f, 0.499f, 0.243f
        };

        /// <summary>科室板右侧那条 RHINE LAB 竖标不裁进去。</summary>
        private const float DeptContentLeft = 0.02f;
        private const float DeptContentRight = 0.829f;

        private static void EnsureEffectStripUv(int boardId)
        {
            if (EffectStripCached[boardId]) return;
            EffectStripCached[boardId] = true;

            Texture2D sheet = EnterpriseCardArt.Sheet;
            if (sheet == null) return;

            int cols = EnterpriseCardArt.BoardsPerRow, rows = EnterpriseCardArt.BoardRows;
            int cellW = sheet.width / cols;
            int cellH = sheet.height / rows;
            int cellX = (boardId % cols) * cellW;
            // uv 原点左下：拼版图最上面那排是 row=0
            int cellY = (rows - 1 - boardId / cols) * cellH;

            for (int level = 0; level <= 5; level++)
            {
                float top = LevelBandTop[level];
                float bottom = LevelBandBottom[level];

                float uvY = (cellY + cellH * (1f - bottom)) / sheet.height;
                float uvH = cellH * (bottom - top) / sheet.height;

                // 放大裁图：整行（黑色 LEVEL 方块 + 右侧效果文字）
                EffectStripUvCache[boardId, level] = new Rect(
                    (cellX + cellW * RowLeft) / sheet.width, uvY,
                    cellW * (RowRight - RowLeft) / sheet.width, uvH);

                // 左侧黑色 LEVEL 方块（莱茵拼接放大时和科室板效果拼在一起）
                LevelBadgeUvCache[boardId, level] = new Rect(
                    (cellX + cellW * BadgeLeft) / sheet.width, uvY,
                    cellW * (BadgeRight - BadgeLeft) / sheet.width, uvH);

                // 玩家标记停靠的竖直中心（像素，原点左下）
                LevelYCenterCache[boardId, level] =
                    Mathf.RoundToInt(cellH * (1f - (top + bottom) * 0.5f));
            }
        }

        private static void EnsureDepartmentEffectUv(int depId)
        {
            if (DepartmentCached[depId]) return;
            DepartmentCached[depId] = true;

            for (int level = 2; level <= 5; level++)
            {
                float top = DeptBandTop[level];
                float bottom = DeptBandBottom[level];
                DepartmentEffectUvCache[depId, level] = new Rect(
                    DeptContentLeft, 1f - bottom,
                    DeptContentRight - DeptContentLeft, bottom - top);
                DepartmentLevelYCenter[depId, level] =
                    Mathf.RoundToInt((1f - (top + bottom) * 0.5f) * 1000f);
            }
        }


        private void BuildZoomedEffectStrip(int boardId, int level)
        {
            EnsureEffectStripUv(boardId);
            Rect uv = EffectStripUvCache[boardId, level];
            Texture2D sheet = EnterpriseCardArt.Sheet;

            int depId = DepartmentOfBoard(boardId);
            bool hasDept = depId >= 0 && level >= 2;

            if (hasDept && sheet != null)
            {
                EnsureDepartmentEffectUv(depId);
                Rect deptUv = DepartmentEffectUvCache[depId, level];
                Texture2D deptTex = EnterpriseCardArt.Department(depId);

                if (deptTex != null && deptUv.width > 0f && deptUv.height > 0f)
                {
                    // 莱茵 L2-L5：左 = 企业板这一级的完整凸起区域，右 = 科室板该等级的效果段。
                    // 两边按“同一显示高度”拼接，整体尺寸与普通企业整条裁图接近，避免左侧 LEVEL 块被撑得过大。
                    Vector2 box = new Vector2(ContentWidth - 120f, ContentHeight - 120f);
                    Vector2 simpleSize = FitSize(sheet, uv, box);
                    Rect badgeUv = LevelBadgeUvCache[boardId, level];

                    float targetHeight = simpleSize.y;
                    float gap = 36f;
                    float leftW = targetHeight * PixelAspect(sheet, badgeUv);
                    float rightW = targetHeight * PixelAspect(deptTex, deptUv);

                    // 左右加起来超宽就整体等比缩小（高、宽、间距一起缩）
                    float maxWidth = ContentWidth - 80f;
                    float totalWidth = leftW + gap + rightW;
                    if (totalWidth > maxWidth)
                    {
                        float k = maxWidth / totalWidth;
                        leftW *= k; rightW *= k; gap *= k; targetHeight *= k;
                    }

                    float leftX = -(leftW + gap + rightW) * 0.5f + leftW * 0.5f;
                    float rightX = leftX + leftW * 0.5f + gap + rightW * 0.5f;

                    CreateRawImageScaled("StripLeft", content, sheet, badgeUv, targetHeight,
                        new Vector2(leftX, 0f));
                    CreateRawImageScaled("StripRight", content, deptTex, deptUv, targetHeight,
                        new Vector2(rightX, 0f));

                    TMP_Text plus = CreateText("Plus", content, "+", 40f, TextAlignmentOptions.Center);
                    plus.color = MutedColor;
                    plus.rectTransform.anchorMin = plus.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    plus.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    plus.rectTransform.anchoredPosition = new Vector2(leftX + leftW * 0.5f + gap * 0.5f, 0f);
                    plus.rectTransform.sizeDelta = new Vector2(48f, 56f);
                    return;
                }
            }

            BuildZoomedEffectStripSimple(sheet, uv);
        }

        private void BuildZoomedEffectStripSimple(Texture2D sheet, Rect uv)
        {
            if (sheet == null || uv.width <= 0f || uv.height <= 0f)
            {
                TMP_Text t = CreateText("Empty", content, "找不到该等级效果条的精确裁图。", 18f, TextAlignmentOptions.Center);
                t.color = MutedColor;
                Stretch(t.rectTransform);
                return;
            }

            RawImage img = CreateRawImage("StripZoom", content, sheet, uv, Vector2.zero,
                new Vector2(ContentWidth - 120f, ContentHeight - 120f));
        }

        /// <summary>新建一个只放 RawImage 的子节点（不能往 content 本身上加 Graphic）。size 是可用框，按 uv 实际像素比例等比内缩。</summary>
        private static RawImage CreateRawImage(string name, RectTransform parent, Texture2D texture, Rect uv,
            Vector2 position, Vector2 box)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RawImage img = go.AddComponent<RawImage>();
            img.texture = texture;
            img.uvRect = uv;
            img.color = Color.white;
            img.raycastTarget = false;
            RectTransform r = img.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = position;
            r.sizeDelta = FitSize(texture, uv, box);
            return img;
        }

        private static Vector2 FitSize(Texture2D texture, Rect uv, Vector2 box)
        {
            if (texture == null) return box;
            float pixelWidth = Mathf.Abs(uv.width) * texture.width;
            float pixelHeight = Mathf.Abs(uv.height) * texture.height;
            if (pixelWidth <= 0f || pixelHeight <= 0f || box.x <= 0f || box.y <= 0f) return box;
            float aspect = pixelWidth / pixelHeight;
            float boxAspect = box.x / box.y;
            return aspect > boxAspect ? new Vector2(box.x, box.x / aspect) : new Vector2(box.y * aspect, box.y);
        }

        /// <summary>uv 段在纹理上的实际宽高比（宽 ÷ 高）。</summary>
        private static float PixelAspect(Texture2D texture, Rect uv)
        {
            if (texture == null) return 1f;
            float w = Mathf.Abs(uv.width) * texture.width;
            float h = Mathf.Abs(uv.height) * texture.height;
            return h > 0f ? w / h : 1f;
        }

        /// <summary>按给定显示高度等比铺一张 RawImage（莱茵左右拼接用，保证两边高度一致）。</summary>
        private static void CreateRawImageScaled(string name, RectTransform parent, Texture2D texture, Rect uv,
            float height, Vector2 position)
        {
            RawImage img = CreateRawImage(name, parent, texture, uv, position, Vector2.zero);
            img.rectTransform.sizeDelta = new Vector2(
                Mathf.Max(1f, height * PixelAspect(texture, uv)), Mathf.Max(1f, height));
        }

        // —— 科室板选择 ——

        private void BuildDepartmentRow()
        {
            var boards = new List<int> { 0, 1, 2 }; // 三张科室板
            if (boards.Count == 0) return;

            float aspect = 516f / 1000f; // 科室板 516×1000
            float width = ContentHeight * aspect;
            if (width * boards.Count + BoardGap * (boards.Count - 1) > ContentWidth)
                width = (ContentWidth - BoardGap * (boards.Count - 1)) / boards.Count;
            float height = width / aspect;
            float startX = -(width * boards.Count + BoardGap * (boards.Count - 1)) * 0.5f + width * 0.5f;

            for (int i = 0; i < boards.Count; i++)
                BuildDepartmentCard(boards[i], startX + i * (width + BoardGap), width, height);
        }

        private void BuildDepartmentCard(int depId, float centerX, float width, float height)
        {
            GameObject cardObject = new GameObject($"Dept_{depId}", typeof(RectTransform));
            cardObject.transform.SetParent(content, false);
            RectTransform cardRect = (RectTransform)cardObject.transform;
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.anchoredPosition = new Vector2(centerX, 0f);
            cardRect.sizeDelta = new Vector2(width, height);

            Texture2D tex = EnterpriseCardArt.Department(depId);
            Graphic graphic;
            if (tex != null)
            {
                RawImage face = cardObject.AddComponent<RawImage>();
                face.texture = tex;
                face.uvRect = new Rect(0f, 0f, 1f, 1f);
                face.color = Color.white;
                graphic = face;
            }
            else
            {
                Image bg = cardObject.AddComponent<Image>();
                bg.color = BoardColor;
                graphic = bg;
                TMP_Text name = CreateText("Name", cardRect,
                    EnterpriseCatalog.Department(depId)?.Name ?? $"科室板 {depId + 1}", 18f, TextAlignmentOptions.Top);
                name.color = TextColor;
                name.rectTransform.anchorMin = name.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                name.rectTransform.pivot = new Vector2(0.5f, 1f);
                name.rectTransform.anchoredPosition = new Vector2(0f, -12f);
                name.rectTransform.sizeDelta = new Vector2(width - 16f, 30f);
            }

            Button open = cardObject.AddComponent<Button>();
            open.targetGraphic = graphic;
            open.onClick.AddListener(() =>
            {
                // 只抛事件：面板由 GameScreenController 决定是关掉还是接着摆下一家。
                // 这里不要再 Close()——否则会把控制器刚重开的面板又关掉，看起来像直接跳过了选企业阶段。
                int pickedId = depId;
                RhineDepartmentPicked?.Invoke(pickedId);
            });

            // 科室板名字标签
            string nameStr = EnterpriseCatalog.Department(depId)?.Name ?? $"科室板 {depId + 1}";
            TMP_Text nameLabel = CreateText("NameLabel", cardRect, nameStr, 14f, TextAlignmentOptions.Center);
            nameLabel.color = TextColor;
            nameLabel.rectTransform.anchorMin = nameLabel.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            nameLabel.rectTransform.pivot = new Vector2(0.5f, 0f);
            nameLabel.rectTransform.anchoredPosition = new Vector2(0f, -22f);
            nameLabel.rectTransform.sizeDelta = new Vector2(width + 20f, 18f);
        }

        // —— 放大侧栏 ——

        private void BuildSideCard(int boardId, float centerX, float width, float height)
        {
            EnterpriseBoardDef def = EnterpriseCatalog.Board(boardId);

            Image card = CreateImage("Side", content, BoardColor);
            RectTransform cr = card.rectTransform;
            cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f);
            cr.pivot = new Vector2(0.5f, 0.5f);
            cr.anchoredPosition = new Vector2(centerX, 0f);
            cr.sizeDelta = new Vector2(width, height);

            TMP_Text name = CreateText("Name", card.rectTransform, def != null ? def.Name : $"企业板 {boardId}", 26f, TextAlignmentOptions.Left);
            name.rectTransform.anchorMin = name.rectTransform.anchorMax = new Vector2(0f, 1f);
            name.rectTransform.pivot = new Vector2(0f, 1f);
            name.rectTransform.anchoredPosition = new Vector2(16f, -16f);
            name.rectTransform.sizeDelta = new Vector2(width - 32f, 34f);

            if (def != null && !string.IsNullOrEmpty(def.Subtitle))
            {
                TMP_Text sub = CreateText("Subtitle", card.rectTransform, def.Subtitle, 14f, TextAlignmentOptions.TopLeft);
                sub.color = MutedColor;
                sub.textWrappingMode = TextWrappingModes.Normal;
                sub.rectTransform.anchorMin = sub.rectTransform.anchorMax = new Vector2(0f, 1f);
                sub.rectTransform.pivot = new Vector2(0f, 1f);
                sub.rectTransform.anchoredPosition = new Vector2(16f, -52f);
                sub.rectTransform.sizeDelta = new Vector2(width - 32f, 40f);
            }

            TMP_Text body = CreateText("Body", card.rectTransform, SideText(), 15f, TextAlignmentOptions.TopLeft);
            body.color = MutedColor;
            body.textWrappingMode = TextWrappingModes.Normal;
            body.lineSpacing = 6f;
            body.rectTransform.anchorMin = Vector2.zero;
            body.rectTransform.anchorMax = Vector2.one;
            body.rectTransform.offsetMin = new Vector2(16f, 16f);
            body.rectTransform.offsetMax = new Vector2(-16f, -100f);

            if (mode == Mode.View || mode == Mode.Codex) return;

            Button confirm = CreateButton("Confirm", card.rectTransform,
                mode == Mode.Draft ? "选择这家企业" : "提升这家企业", 20f);
            RectTransform cf = confirm.GetComponent<RectTransform>();
            cf.anchorMin = cf.anchorMax = new Vector2(0.5f, 0f);
            cf.pivot = new Vector2(0.5f, 0f);
            cf.anchoredPosition = new Vector2(0f, 20f);
            cf.sizeDelta = new Vector2(Mathf.Min(width - 32f, 240f), 46f);
            confirm.onClick.AddListener(() =>
            {
                if (mode == Mode.Draft) BoardPicked?.Invoke(boardId);
                else if (mode == Mode.Advance) AdvancePicked?.Invoke(boardId);
            });
        }

        /// <summary>
        /// 莱茵生命放大查看时的右侧栏：直接展示选定的科室板整图，
        /// 并在每段左侧标出对应的合作等级（L5 最上、L2 最下），避免玩家对着四段奖励猜等级。
        /// </summary>
        private void BuildDepartmentSideCard(int depId, float centerX, float width, float height)
        {
            Image card = CreateImage("DeptSide", content, BoardColor);
            RectTransform cr = card.rectTransform;
            cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f);
            cr.pivot = new Vector2(0.5f, 0.5f);
            cr.anchoredPosition = new Vector2(centerX, 0f);
            cr.sizeDelta = new Vector2(width, height);

            string depName = EnterpriseCatalog.Department(depId)?.Name ?? $"科室板 {depId + 1}";

            TMP_Text title = CreateText("Name", cr, $"科室板：{depName}", 22f, TextAlignmentOptions.Center);
            title.color = TextColor;
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -14f);
            title.rectTransform.sizeDelta = new Vector2(width - 24f, 32f);

            TMP_Text hint = CreateText("Hint", cr,
                "莱茵生命 L5–L2 的效果按这张科室板结算；点左侧效果条可放大看。",
                13f, TextAlignmentOptions.Center);
            hint.color = MutedColor;
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            hint.rectTransform.pivot = new Vector2(0.5f, 1f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, -48f);
            hint.rectTransform.sizeDelta = new Vector2(width - 24f, 20f);

            Texture2D tex = EnterpriseCardArt.Department(depId);
            if (tex == null)
            {
                TMP_Text missing = CreateText("Missing", cr, "读不到科室板图片。", 16f, TextAlignmentOptions.Center);
                missing.color = MutedColor;
                Stretch(missing.rectTransform);
                return;
            }

            const float topPad = 76f;
            const float bottomPad = 14f;
            float availH = Mathf.Max(1f, height - topPad - bottomPad);
            float availW = Mathf.Max(1f, width - 24f);
            float aspect = tex.width / (float)tex.height;
            float cardH = Mathf.Min(availH, availW / aspect);
            float cardW = cardH * aspect;

            RawImage art = CreateRawImage("DeptArt", cr, tex, new Rect(0f, 0f, 1f, 1f),
                new Vector2(0f, (bottomPad - topPad) * 0.5f), new Vector2(cardW, cardH));
            art.rectTransform.sizeDelta = new Vector2(cardW, cardH);

            // 四段等级标记：竖直中心和科室板分区中心对齐
            for (int level = 5; level >= 2; level--)
            {
                float centerRatio = (DeptBandTop[level] + DeptBandBottom[level]) * 0.5f;
                float y = cardH * (0.5f - centerRatio);

                Image tag = CreateImage($"Lv{level}", art.rectTransform, new Color(0f, 0f, 0f, 0.72f));
                tag.raycastTarget = false;
                RectTransform tr = tag.rectTransform;
                tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f);
                tr.pivot = new Vector2(0.5f, 0.5f);
                tr.sizeDelta = new Vector2(34f, 20f);
                tr.anchoredPosition = new Vector2(-cardW * 0.5f + 19f, y);

                TMP_Text label = CreateText($"LvText{level}", tr, level.ToString(), 13f, TextAlignmentOptions.Center);
                label.color = TextColor;
                label.raycastTarget = false;
                Stretch(label.rectTransform);
            }
        }

        private string SideText()
        {
            switch (mode)
            {
                case Mode.Draft:
                    return "左侧完整卡面，点黑色效果条放大看每个等级的效果文字。\n确认后这块板立刻上场，并把你在这块板上的合作等级升到 1 级。";
                case Mode.Advance:
                    return "确认后，你在这块企业板上的合作等级 +1，并立刻结算新等级对应的奖励。";
                case Mode.Codex:
                    return "● 点左侧 L1–L5 的黑色等级方块，可放大查看该等级的效果段。\n\n" +
                           "● 这里只用于查看牌面，不会改动对局。";
                default:
                    return "● 点击 L1–L5 任意一条效果区域，可精确放大那一段效果文字（不含玩家标记）。\n\n" +
                           "● 玩家标记按合作等级停在对应那一行；一行里最多 4 名玩家，2×2 排列。\n\n" +
                           "● 标记水平位置固定，升级时只向上平移。";
            }
        }

        private string EmptyText()
        {
            if (mode == Mode.Draft) return "没有抽到可选的合作企业板。";
            if (mode == Mode.Advance) return "场上没有还能提升合作等级的企业板。";
            return "本局还没有合作企业板上场。";
        }

        private void BuildMissingFaceHint(RectTransform cardRect, EnterpriseBoardDef def, int boardId, float width, bool zoom)
        {
            TMP_Text name = CreateText("Name", cardRect, def != null ? def.Name : $"企业板 {boardId}",
                zoom ? 26f : 15f, TextAlignmentOptions.Top);
            name.color = TextColor;
            name.rectTransform.anchorMin = name.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            name.rectTransform.pivot = new Vector2(0.5f, 1f);
            name.rectTransform.anchoredPosition = new Vector2(0f, -8f);
            name.rectTransform.sizeDelta = new Vector2(-14f, zoom ? 34f : 22f);

            var sb = new StringBuilder();
            for (int level = 5; level >= 0; level--)
            {
                EnterpriseLevel entry = def?.Level(level);
                sb.Append(level).Append("  ").Append(entry != null ? entry.Text : "—").Append('\n');
            }
            TMP_Text body = CreateText("Body", cardRect, sb.ToString(), zoom ? 13f : 9f, TextAlignmentOptions.TopLeft);
            body.color = MutedColor;
            body.textWrappingMode = TextWrappingModes.Normal;
            body.rectTransform.anchorMin = Vector2.zero;
            body.rectTransform.anchorMax = Vector2.one;
            body.rectTransform.offsetMin = new Vector2(width * 0.24f, 8f);
            body.rectTransform.offsetMax = new Vector2(-8f, zoom ? -48f : -32f);
        }

        private string BuildSignature(List<int> cards)
        {
            var sb = new StringBuilder();
            sb.Append((int)mode).Append('#').Append(seat);
            foreach (int b in cards) sb.Append('|').Append(b);
            if (state == null || mode == Mode.RhineDepartment) return sb.ToString();
            for (int i = 0; i < cards.Count; i++)
                for (int s = 0; s < state.Players.Count; s++)
                    sb.Append(':').Append(state.Enterprises.LevelOf(cards[i], s));
            return sb.ToString();
        }

        // —— 小工具 ——

        private static RectTransform CreateRect(string name, RectTransform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            return r;
        }

        private static Image CreateImage(string name, RectTransform parent, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        private TMP_Text CreateText(string name, RectTransform parent, string text, float size, TextAlignmentOptions alignment)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            if (font != null) label.font = font;
            label.text = text;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = TextColor;
            label.raycastTarget = false;
            return label;
        }

        private Button CreateButton(string name, RectTransform parent, string label, float size)
        {
            Image bg = CreateImage(name, parent, new Color(0.45f, 0.36f, 0.28f, 1f));
            Button btn = bg.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            TMP_Text lbl = CreateText("Label", bg.rectTransform, label, size, TextAlignmentOptions.Center);
            Stretch(lbl.rectTransform);
            UiButtonTint.Ensure(btn);
            return btn;
        }

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }
    }
}
