using System;
using System.Collections.Generic;
using NomadCity.Core.Model;
using NomadCity.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 图鉴弹窗：操作区「图鉴」按钮打开的大窗口，高度对齐地图窗口，宽度按 1100 : 720 等比放大。
    ///
    /// 八个页签：
    ///   规则书      —— 逐页翻看规则书图片（Rulebook Pages）
    ///   速查表      —— 逐张翻看速查表图片（两张）
    ///   设施        —— 全部设施卡面网格，点一张放大
    ///   城市样式    —— 基础 6 张 + 企业扩展 6 张
    ///   角色牌      —— 5 种角色各一张（四色同名角色效果相同，卡面也共用）
    ///   事件卡      —— 基础 22 张 + 企业扩展替换件 2 张
    ///   企业        —— 企业扩展 8 家合作企业的效果说明
    ///   企业家决策  —— 4 位企业家（阵营颜色），感染者扩展技能暂未实装
    ///
    /// 卡面图不在这里重复拖 60+ 张：直接复用设施供应区 / 城市样式 / 手牌托盘三块面板的卡面库。
    /// </summary>
    public sealed class CodexPanel : MonoBehaviour, ICenteredPanel
    {
        private enum Page
        {
            Rules = 0,
            QuickRef = 1,
            Facility = 2,
            Pattern = 3,
            Character = 4,
            Event = 5,
            Enterprise = 6,
            Entrepreneur = 7,
        }

        private static readonly string[] TabNames =
        {
            "规则书", "速查表", "设施", "城市样式", "角色牌", "事件卡", "企业", "企业家决策",
        };

        /// <summary>设计尺寸：宽高比固定 1100 : 720；实际高度对齐地图窗口，宽度按同一比例放大。</summary>
        private const float DesignWidth = 1100f;
        private const float DesignHeight = 720f;

        /// <summary>地图窗口还没初始化时的兜底：占画布高度的比例。</summary>
        private const float FallbackHeightFraction = 0.92f;

        private static readonly Color WindowColor = new Color(0.247f, 0.208f, 0.153f, 0.78f);
        private static readonly Color ContentColor = new Color(0.12f, 0.10f, 0.08f, 0.85f);
        private static readonly Color TextColor = new Color(0.95f, 0.92f, 0.86f, 1f);
        private static readonly Color TabActiveColor = new Color(0.62f, 0.49f, 0.36f, 1f);
        private static readonly Color TabInactiveColor = new Color(0.30f, 0.25f, 0.20f, 1f);
        private static readonly Color ButtonColor = new Color(0.45f, 0.36f, 0.28f, 1f);

        [Tooltip("操作区「图鉴」按钮：点它开关本弹窗")]
        [SerializeField] private Button toggleButton;

        [Tooltip("中文 TMP 字体（与其他弹窗一致）")]
        [SerializeField] private TMP_FontAsset font;

        [Tooltip("规则书图片，按页序拖入（rules_01…rules_28）")]
        [SerializeField] private Sprite[] rulebookPages = Array.Empty<Sprite>();

        [Tooltip("速查表图片（目前两张，左右并排）")]
        [SerializeField] private Sprite[] quickReferences = Array.Empty<Sprite>();

        [Tooltip("地图相机：用来把图鉴窗口高度对齐地图窗口高度")]
        [SerializeField] private MapCameraController mapCamera;

        [Tooltip("企业家头像，下标 = PlayerColor（绿 Tiag / 黄 Martin / 红 Agnes / 蓝 Paul）")]
        [SerializeField] private Sprite[] entrepreneurFaces = new Sprite[4];

        [Tooltip("设施供应区面板：图鉴的设施页复用它配置好的卡面库")]
        [SerializeField] private FacilitySupplyPanel facilityPanel;

        [Tooltip("城市样式面板：图鉴的样式页复用它配置好的卡面库")]
        [SerializeField] private CityPatternPanel cityPatternPanel;

        [Tooltip("角色牌手牌托盘：图鉴的角色页复用它配置好的卡面库")]
        [SerializeField] private CharacterHandPanel characterPanel;

        [Tooltip("事件卡面板：图鉴的事件卡页复用它配置好的卡面库（不拖则运行时自动找）")]
        [SerializeField] private EventCardPanel eventCardPanel;

        private GameObject root;
        private RectTransform windowRect;
        private RectTransform contentRect;
        private bool built;
        private readonly List<Button> tabButtons = new List<Button>();
        private readonly List<GameObject> pages = new List<GameObject>();
        private Page currentPage = (Page)(-1);

        // 规则书页
        private Image ruleImage;
        private TMP_Text rulePageLabel;
        private int rulePageIndex;

        // 速查表页
        private Image quickImage;
        private TMP_Text quickPageLabel;
        private int quickPageIndex;

        // 卡面放大层
        private GameObject previewRoot;
        private Image previewImage;
        private TMP_Text previewLabel;

        public bool IsOpen => built && root != null && root.activeSelf;

        private void Awake()
        {
            CenteredPanelGroup.Register(this);

            if (toggleButton != null)
            {
                UiButtonTint.Ensure(toggleButton);
                toggleButton.onClick.AddListener(Toggle);
            }
            else
            {
                Debug.LogWarning("[图鉴] 没有接「图鉴按钮」—— 只能靠脚本 Toggle() 打开。请把 Btn_Codex 的 Button 拖进来。", this);
            }

            // 弹窗等到第一次打开再建：那时地图相机已经算好窗口，才能把高度对齐地图。
            UiButtonTint.SetLit(toggleButton, false);
        }

        private void OnDestroy()
        {
            CenteredPanelGroup.Unregister(this);
            if (toggleButton != null) toggleButton.onClick.RemoveListener(Toggle);
        }

        public void Toggle() => SetOpen(!IsOpen);

        public void SetOpen(bool open)
        {
            if (open && !built) Build();
            if (root == null) return;

            if (open)
            {
                CenteredPanelGroup.BeforeOpen(this);
                root.transform.SetAsLastSibling();
                ClosePreview();
                SetPage(currentPage == (Page)(-1) ? Page.Rules : currentPage);
            }

            if (root.activeSelf != open) root.SetActive(open);
            UiButtonTint.SetLit(toggleButton, open);
        }

        public void ForceClose() => SetOpen(false);

        // —— 构建 ——

        private void Build()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                Debug.LogWarning("[图鉴] 找不到父级 Canvas，弹窗无法生成。", this);
                return;
            }

            RectTransform rootRect = CreateRect("CodexPanel", (RectTransform)canvas.transform);
            Stretch(rootRect);
            root = rootRect.gameObject;
            rootRect.SetAsLastSibling();

            Image dim = CreateImage("Dim", rootRect, new Color(0f, 0f, 0f, 0.6f));
            Stretch(dim.rectTransform);
            Button dimButton = dim.gameObject.AddComponent<Button>();
            dimButton.transition = Selectable.Transition.None;
            dimButton.targetGraphic = dim;
            dimButton.onClick.AddListener(() => SetOpen(false));

            Image window = CreateImage("Window", rootRect, WindowColor);
            // 窗口本身要吃掉点击，否则点空白处会穿透到底下的遮罩把图鉴关掉。
            window.raycastTarget = true;
            windowRect = window.rectTransform;
            windowRect.anchorMin = new Vector2(0.5f, 0.5f);
            windowRect.anchorMax = new Vector2(0.5f, 0.5f);
            windowRect.pivot = new Vector2(0.5f, 0.5f);
            windowRect.anchoredPosition = Vector2.zero;
            windowRect.sizeDelta = MeasureWindowSize((RectTransform)canvas.transform);

            TMP_Text title = CreateText("Title", windowRect, "图鉴", 28f, TextAlignmentOptions.Left);
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, -16f), new Vector2(300f, 40f), new Vector2(0f, 1f));

            Button close = CreateButton("Close", windowRect, "关闭", 20f, out _);
            SetRect(close.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-14f, -14f), new Vector2(72f, 32f), new Vector2(1f, 1f));
            close.onClick.AddListener(() => SetOpen(false));

            Image content = CreateImage("Content", windowRect, ContentColor);
            contentRect = content.rectTransform;
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = new Vector2(210f, 20f);
            contentRect.offsetMax = new Vector2(-20f, -70f);

            BuildTabs(windowRect);
            BuildPages();
            BuildPreview(windowRect);

            SetPage(Page.Rules);
            built = true;
        }

        /// <summary>
        /// 高度对齐地图窗口（屏幕高度 × 地图窗口占比），宽度按设计稿 1100 : 720 等比放大。
        /// </summary>
        private Vector2 MeasureWindowSize(RectTransform canvasRect)
        {
            float canvasHeight = canvasRect != null && canvasRect.rect.height > 1f
                ? canvasRect.rect.height
                : 1080f;

            float fraction = mapCamera != null ? mapCamera.WindowHeightFraction : 0f;
            if (fraction <= 0.01f) fraction = FallbackHeightFraction;
            fraction = Mathf.Clamp(fraction, 0.7f, 0.96f);

            float height = canvasHeight * fraction;
            return new Vector2(height * (DesignWidth / DesignHeight), height);
        }

        private void BuildTabs(RectTransform windowRect)
        {
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                Button tab = CreateButton("Tab_" + TabNames[i], windowRect, TabNames[i], 20f, out _);
                SetRect(tab.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(20f, -70f - i * 54f), new Vector2(170f, 46f), new Vector2(0f, 1f));
                tab.onClick.AddListener(() => SetPage((Page)index));
                tabButtons.Add(tab);
            }
        }

        private void BuildPages()
        {
            pages.Add(BuildRulesPage());
            pages.Add(BuildQuickRefPage());
            pages.Add(BuildGridPage("Facility", FacilityIds(), FacilitySprite, FacilityName));
            pages.Add(BuildGridPage("Pattern", PatternIds(), PatternSprite, PatternName));
            pages.Add(BuildGridPage("Character", CharacterIds(), CharacterSprite, CharacterName));
            pages.Add(BuildGridPage("Event", EventIds(), EventSprite, EventName));
            pages.Add(BuildEnterprisePage());
            pages.Add(BuildEntrepreneurPage());
        }

        private void SetPage(Page page)
        {
            if (pages.Count != TabNames.Length) return;

            currentPage = page;
            int active = (int)page;

            for (int i = 0; i < pages.Count; i++) pages[i].SetActive(i == active);

            for (int i = 0; i < tabButtons.Count; i++)
            {
                bool lit = i == active;
                Image background = tabButtons[i].GetComponent<Image>();
                if (background != null) background.color = lit ? TabActiveColor : TabInactiveColor;

                TMP_Text label = tabButtons[i].GetComponentInChildren<TMP_Text>();
                if (label != null) label.color = lit ? Color.white : new Color(0.85f, 0.82f, 0.76f, 1f);
            }

            ClosePreview();
        }

        // —— 规则书 / 速查表（同一套翻页浏览） ——

        private GameObject BuildRulesPage()
        {
            RectTransform page = BuildImagePager("Page_Rules", NextRulePage,
                out ruleImage, out rulePageLabel, out Button prev, out Button next);
            prev.onClick.AddListener(() => StepRulePage(-1));
            next.onClick.AddListener(() => StepRulePage(1));

            RefreshRulePage();
            return page.gameObject;
        }

        private void RefreshRulePage()
            => RefreshImagePager(ruleImage, rulePageLabel, rulebookPages, rulePageIndex,
                "规则书图片未导入（Rulebook Pages）");

        private void StepRulePage(int delta)
        {
            if (rulebookPages == null || rulebookPages.Length == 0) return;

            rulePageIndex = (rulePageIndex + delta + rulebookPages.Length) % rulebookPages.Length;
            RefreshRulePage();
        }

        private void NextRulePage() => StepRulePage(1);

        private GameObject BuildQuickRefPage()
        {
            RectTransform page = BuildImagePager("Page_QuickRef", NextQuickRef,
                out quickImage, out quickPageLabel, out Button prev, out Button next);
            prev.onClick.AddListener(() => StepQuickRef(-1));
            next.onClick.AddListener(() => StepQuickRef(1));

            RefreshQuickRef();
            return page.gameObject;
        }

        private void RefreshQuickRef()
            => RefreshImagePager(quickImage, quickPageLabel, quickReferences, quickPageIndex,
                "速查表图片未导入（Quick Reference）");

        private void StepQuickRef(int delta)
        {
            if (quickReferences == null || quickReferences.Length == 0) return;

            quickPageIndex = (quickPageIndex + delta + quickReferences.Length) % quickReferences.Length;
            RefreshQuickRef();
        }

        private void NextQuickRef() => StepQuickRef(1);

        /// <summary>一页一张图 + 底部「上一页 / 下一页」+ 页码，点图片也能翻到下一张。</summary>
        private RectTransform BuildImagePager(string name, Action onImageClick, out Image image,
            out TMP_Text pageLabel, out Button prev, out Button next)
        {
            RectTransform page = CreatePage(name);

            image = CreateImage("Image", page, Color.white);
            RectTransform imageRect = image.rectTransform;
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = new Vector2(14f, 56f);
            imageRect.offsetMax = new Vector2(-14f, -14f);
            image.preserveAspect = true;
            image.raycastTarget = true;

            Button imageButton = image.gameObject.AddComponent<Button>();
            imageButton.transition = Selectable.Transition.None;
            imageButton.targetGraphic = image;
            imageButton.onClick.AddListener(() => onImageClick?.Invoke());

            prev = CreateButton("Prev", page, "上一页", 18f, out _);
            SetRect(prev.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(14f, 10f), new Vector2(90f, 36f), new Vector2(0f, 0f));

            next = CreateButton("Next", page, "下一页", 18f, out _);
            SetRect(next.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-14f, 10f), new Vector2(90f, 36f), new Vector2(1f, 0f));

            pageLabel = CreateText("PageLabel", page, string.Empty, 18f, TextAlignmentOptions.Center);
            SetRect(pageLabel.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 10f), new Vector2(0f, 36f), new Vector2(0.5f, 0f));

            return page;
        }

        private static void RefreshImagePager(Image image, TMP_Text label, Sprite[] sprites, int index,
            string emptyHint)
        {
            if (sprites == null || sprites.Length == 0)
            {
                if (image != null) image.enabled = false;
                if (label != null) label.text = emptyHint;
                return;
            }

            if (image != null)
            {
                image.enabled = true;
                image.sprite = sprites[index];
                image.preserveAspect = true;
            }

            if (label != null) label.text = $"第 {index + 1} / {sprites.Length} 页";
        }

        // —— 卡面网格 ——

        private GameObject BuildGridPage(string name, List<int> ids, Func<int, Sprite> spriteOf,
            Func<int, string> nameOf)
        {
            RectTransform page = CreatePage("Page_" + name);

            RectTransform viewport = CreateRect("Viewport", page);
            Stretch(viewport);
            viewport.offsetMin = new Vector2(8f, 8f);
            viewport.offsetMax = new Vector2(-8f, -8f);

            Image viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform gridRect = CreateRect("Content", viewport);
            gridRect.anchorMin = new Vector2(0f, 1f);
            gridRect.anchorMax = new Vector2(1f, 1f);
            gridRect.pivot = new Vector2(0.5f, 1f);
            gridRect.anchoredPosition = Vector2.zero;
            gridRect.sizeDelta = Vector2.zero;

            GridLayoutGroup grid = gridRect.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(128f, 172f);
            grid.spacing = new Vector2(12f, 12f);
            grid.padding = new RectOffset(12, 12, 12, 12);
            grid.childAlignment = TextAnchor.UpperLeft;
            // 窗口变宽后按内容区宽度自动排更多列，不再固定 6 列。
            grid.constraint = GridLayoutGroup.Constraint.Flexible;

            ContentSizeFitter fitter = gridRect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = page.gameObject.AddComponent<ScrollRect>();
            scroll.content = gridRect;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
            scroll.scrollSensitivity = 30f;

            for (int i = 0; i < ids.Count; i++)
            {
                int cardId = ids[i];
                Sprite face = spriteOf(cardId);
                string label = nameOf(cardId);
                BuildTile(gridRect, face, label, () => ShowPreview(face, label));
            }

            return page.gameObject;
        }

        private void BuildTile(RectTransform parent, Sprite face, string label, Action onClick)
        {
            Image tile = CreateImage("Card", parent, new Color(0.20f, 0.17f, 0.14f, 1f));
            tile.raycastTarget = true;

            Button button = tile.gameObject.AddComponent<Button>();
            button.targetGraphic = tile;
            button.onClick.AddListener(() => onClick?.Invoke());

            if (face != null)
            {
                Image faceImage = CreateImage("Face", tile.rectTransform, Color.white);
                Stretch(faceImage.rectTransform);
                faceImage.sprite = face;
                faceImage.preserveAspect = true;
                faceImage.raycastTarget = false;
            }
            else
            {
                TMP_Text fallback = CreateText("Name", tile.rectTransform, label, 16f, TextAlignmentOptions.Center);
                Stretch(fallback.rectTransform);
                fallback.margin = new Vector4(6f, 6f, 6f, 6f);
            }
        }

        // —— 企业 ——

        private GameObject BuildEnterprisePage()
        {
            RectTransform page = CreatePage("Page_Enterprise");
            BuildTextScroll(page, EnterpriseText());
            return page.gameObject;
        }

        private static string EnterpriseText()
        {
            return
                "<b>企业合作（企业扩展）</b>\n" +
                "开局选定初始企业后，企业合作等级会随游戏推进提升；达到指定等级即可触发该企业的特效。\n" +
                "以下按规则书已展开的牌面整理，未列出的等级以实体合作企业板为准。\n\n" +
                "<b>黑钢国际（BS BLACKSTEEL）</b>\n" +
                "泰拉规模最大的安全顾问企业，接受从私人安保到武力干涉在内的多种类安保委托。\n" +
                "· 4 级：移除 2 个影响力，或放置 2 个影响力，也可以移除 1 个 + 放置 1 个，执行顺序自定。\n\n" +
                "<b>海德兄弟（HD）</b>\n" +
                "来自堡巫山的建材业寡头，在哥伦比亚多个城市的建材市场拥有垄断地位。\n" +
                "· 4 级：把设施供应区全部 6 张牌洗回牌堆并重抽 6 张；之后可免费建设其中 1 张。\n\n" +
                "<b>雷神工业（RAYTHEAN）</b>\n" +
                "富有创新精神的制造业领军者，业务囊括城市建设的方方面面。\n" +
                "· 3 / 5 级：可进行不占用主要行动的建设，一回合内次数不限；由企业特效建出的「企业办事处」可再次触发。\n" +
                "· 5 级：若无法建造任意设施，必须洗混设施牌堆并结束本次结算。\n\n" +
                "<b>雷姆必拓（ARIM SOLUTIONS）</b>\n" +
                "由多家矿业企业组成的联合集团，矿区管理与开采技术首屈一指。\n" +
                "· 4 级：立刻按当前局面结算一次单人采集；行动阶段结束后仍可正常进行采集阶段。\n\n" +
                "<b>富森银行</b>\n" +
                "老牌金融企业，根据建设规模与信用评级提供大额金券借贷。\n" +
                "· 3 级：自己先获得 20 金券，其余玩家各获得 4 金券。\n\n" +
                "<b>约翰老妈（MAMA JOHN'S）</b>\n" +
                "从提卡伦多发展起来的本土综合性企业，涉及食品、制造业与金融。\n" +
                "· 3 级：可解锁企业特效（可获得 1 分）；具体效果以实体企业板为准。\n\n" +
                "<b>莱茵生命</b>\n" +
                "哥伦比亚本土科技巨头，研究范围囊括源石技艺、新型材料与实验性武装。\n" +
                "· 2–5 级奖励在标记移上去时一次性生效；附带 3 张科室板，同时只能激活 1 张，可用企业特效替换。\n\n" +
                "<b>罗德岛制药</b>\n" +
                "具备应对源石感染专业能力的制药企业，干员可提供多方面战术支持。\n" +
                "· 3 / 5 级「干员特派」：抽 2 张干员牌，选 1 张加入手牌（未选的洗回，且须从手牌 / 弃牌区选 1 张角色牌展示后放回游戏盒）；或全部洗回并得 1 分。\n" +
                "· 4 级：先从角色牌弃牌区回收 1 张并展示，之后除它以外所有手牌面朝上进入弃牌区（弃牌区为空则不能用）。\n\n" +
                "<i>注：部分等级效果规则书未展开，以实体合作企业板为准。</i>";
        }

        // —— 企业家决策 ——

        private GameObject BuildEntrepreneurPage()
        {
            RectTransform page = CreatePage("Page_Entrepreneur");

            const float tileSize = 160f;
            const float spacing = 24f;
            float total = 4f * tileSize + 3f * spacing;
            // 按内容区实际宽度居中（窗口宽度不再固定）。
            float pageWidth = contentRect != null && contentRect.rect.width > 1f
                ? contentRect.rect.width
                : DesignWidth - 230f;
            float startX = Mathf.Max(0f, (pageWidth - total) * 0.5f);

            for (int color = 0; color < 4; color++)
            {
                PlayerColor playerColor = (PlayerColor)color;
                string name = EntrepreneurCatalog.NameOf(playerColor);
                Sprite face = entrepreneurFaces != null && color < entrepreneurFaces.Length
                    ? entrepreneurFaces[color]
                    : null;

                Image tile = CreateImage("Entrepreneur_" + name, page, new Color(0.20f, 0.17f, 0.14f, 1f));
                SetRect(tile.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(startX + color * (tileSize + spacing), -26f),
                    new Vector2(tileSize, tileSize), new Vector2(0f, 1f));
                tile.raycastTarget = false;

                if (face != null)
                {
                    Image portrait = CreateImage("Face", tile.rectTransform, Color.white);
                    Stretch(portrait.rectTransform);
                    portrait.sprite = face;
                    portrait.preserveAspect = true;
                    portrait.raycastTarget = false;
                }

                TMP_Text label = CreateText("Name", page, $"{name} · {ColorName(playerColor)}", 20f,
                    TextAlignmentOptions.Center);
                SetRect(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(startX + color * (tileSize + spacing), -26f - tileSize - 8f),
                    new Vector2(tileSize, 30f), new Vector2(0f, 1f));
            }

            TMP_Text body = CreateText("Body", page,
                "企业家只决定阵营颜色，基础版与企业扩展中没有任何额外技能。\n\n" +
                "挑选顺序：按行动顺序倒序进行 —— 最后一名玩家先挑，起始玩家最后挑；" +
                "每人挑一张，已被挑走的企业家其他人不能再选。\n\n" +
                "感染者扩展将为不同企业家加入专属技能；该部分规则目前尚未实装，后续版本补充。",
                19f, TextAlignmentOptions.TopLeft);
            body.textWrappingMode = TextWrappingModes.Normal;
            body.lineSpacing = 6f;
            body.margin = new Vector4(28f, 16f, 28f, 16f);
            RectTransform bodyRect = body.rectTransform;
            bodyRect.anchorMin = new Vector2(0f, 0f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.offsetMin = new Vector2(20f, 20f);
            bodyRect.offsetMax = new Vector2(-20f, -240f);

            return page.gameObject;
        }

        private static string ColorName(PlayerColor color)
        {
            switch (color)
            {
                case PlayerColor.Red: return "红";
                case PlayerColor.Blue: return "蓝";
                case PlayerColor.Yellow: return "黄";
                case PlayerColor.Green: return "绿";
                default: return "—";
            }
        }

        // —— 卡面放大层 ——

        private void BuildPreview(RectTransform windowRect)
        {
            RectTransform preview = CreateRect("Preview", windowRect);
            Stretch(preview);
            previewRoot = preview.gameObject;

            Image dim = CreateImage("Dim", preview, new Color(0f, 0f, 0f, 0.9f));
            Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            Button dimButton = dim.gameObject.AddComponent<Button>();
            dimButton.transition = Selectable.Transition.None;
            dimButton.targetGraphic = dim;
            dimButton.onClick.AddListener(ClosePreview);

            // 放大层跟着窗口一起变大：方形取窗口高度的大部分，卡面 / 速查表都按比例塞进去。
            float windowHeight = windowRect != null && windowRect.rect.height > 1f
                ? windowRect.rect.height
                : DesignHeight;
            float windowWidth = windowRect != null && windowRect.rect.width > 1f
                ? windowRect.rect.width
                : DesignWidth;
            float previewSide = Mathf.Max(320f, Mathf.Min(windowHeight * 0.74f, windowWidth - 160f));

            previewImage = CreateImage("Face", preview, Color.white);
            SetRect(previewImage.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -20f), new Vector2(previewSide, previewSide), new Vector2(0.5f, 0.5f));
            previewImage.preserveAspect = true;
            previewImage.raycastTarget = false;

            previewLabel = CreateText("Name", preview, string.Empty, 24f, TextAlignmentOptions.Center);
            SetRect(previewLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -20f), new Vector2(0f, 40f), new Vector2(0.5f, 1f));

            Button back = CreateButton("Back", preview, "返回", 20f, out _);
            SetRect(back.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 24f), new Vector2(110f, 40f), new Vector2(0.5f, 0f));
            back.onClick.AddListener(ClosePreview);

            previewRoot.SetActive(false);
        }

        private void ShowPreview(Sprite face, string label)
        {
            if (previewRoot == null) return;

            previewRoot.SetActive(true);
            previewRoot.transform.SetAsLastSibling();

            if (previewImage != null)
            {
                previewImage.sprite = face;
                previewImage.enabled = face != null;
            }

            if (previewLabel != null) previewLabel.text = label ?? string.Empty;
        }

        private void ClosePreview()
        {
            if (previewRoot != null && previewRoot.activeSelf) previewRoot.SetActive(false);
        }

        // —— 卡牌数据 ——

        private static List<int> FacilityIds()
        {
            var ids = new List<int>();
            for (int i = 0; i < CardCounts.StandardFacilities; i++) ids.Add(CardIds.FacilityBase + i);
            for (int i = 0; i < CardCounts.CoreCommandTowers; i++) ids.Add(CardIds.CoreCommandTowerBase + i);
            for (int i = 0; i < CardCounts.ExtensionHubs; i++) ids.Add(CardIds.ExtensionHubBase + i);
            return ids;
        }

        private static List<int> PatternIds()
        {
            var ids = new List<int>();
            for (int i = 0; i < CardCounts.CityPatterns; i++) ids.Add(CardIds.CityPatternBase + i);
            for (int i = 0; i < CardCounts.CityPatternEnterprise; i++) ids.Add(CardIds.CityPatternEnterpriseBase + i);
            return ids;
        }

        private static List<int> CharacterIds()
        {
            // 20 张 = 4 色 × 5 名，同名角色四色效果相同、卡面也共用：图鉴每种只放一张。
            var ids = new List<int>();
            for (int i = 0; i < CardCounts.CharacterRoles; i++) ids.Add(CardIds.CharacterBase + i);
            return ids;
        }

        /// <summary>
        /// 全部事件牌：基础 22 张（400–421）+ 企业扩展替换件 2 张（450/451）。
        /// 事件牌不受颜色限制，一张就是一张，不需要像角色牌那样去重。
        /// </summary>
        private static List<int> EventIds()
        {
            var ids = new List<int>();
            for (int i = 0; i < CardCounts.EventCards; i++) ids.Add(CardIds.EventBase + i);
            for (int i = 0; i < CardIds.EventEnterpriseCount; i++) ids.Add(CardIds.EventEnterpriseBase + i);
            return ids;
        }

        private Sprite FacilitySprite(int cardId)
        {
            Sprite[] faces = facilityPanel != null ? facilityPanel.CardFaces : null;
            int index = FacilitySupplyPanel.SpriteIndexOf(cardId);
            return faces != null && index >= 0 && index < faces.Length ? faces[index] : null;
        }

        private Sprite PatternSprite(int cardId)
        {
            Sprite[] faces = cityPatternPanel != null ? cityPatternPanel.CardFaces : null;
            int index = CityPatternPanel.SpriteIndexOf(cardId);
            return faces != null && index >= 0 && index < faces.Length ? faces[index] : null;
        }

        private Sprite CharacterSprite(int cardId)
        {
            Sprite[] faces = characterPanel != null ? characterPanel.CardFaces : null;
            int index = CharacterHandPanel.SpriteIndexOf(cardId);
            return faces != null && index >= 0 && index < faces.Length ? faces[index] : null;
        }

        private Sprite EventSprite(int cardId)
        {
            // 场景里没拖引用时自动找一次：事件卡面板本身就带着 400–421 / 450–451 的整卡贴图。
            // 用 FindAnyObjectByType（包含未激活对象）：Unity 6.6 起 FindFirstObjectByType 已过时。
            if (eventCardPanel == null)
                eventCardPanel = FindAnyObjectByType<EventCardPanel>(FindObjectsInactive.Include);

            Sprite[] faces = eventCardPanel != null ? eventCardPanel.CardFaces : null;
            int index = EventCardPanel.SpriteIndexOf(cardId);
            return faces != null && index >= 0 && index < faces.Length ? faces[index] : null;
        }

        private static string FacilityName(int cardId) => PlaceholderCardCatalog.Default.Facility(cardId).Name;

        private static string PatternName(int cardId)
        {
            CityPatternCardDef card = PlaceholderCardCatalog.Default.CityPattern(cardId);
            if (card == null) return $"样式牌 {cardId}";
            if (!string.IsNullOrEmpty(card.Name)) return card.Name;
            return card.Level >= 2 ? "II 级样式牌" : "I 级样式牌";
        }

        private static string CharacterName(int cardId) => PlaceholderCardCatalog.Default.Character(cardId).Name;

        private static string EventName(int cardId)
        {
            EventCardDef card = PlaceholderCardCatalog.Default.Event(cardId);
            if (card == null) return $"事件牌 {cardId}";
            return card.IsEnterpriseCard ? $"{card.Name}（企业扩展）" : card.Name;
        }

        // —— 文本滚动页 ——

        private TMP_Text BuildTextScroll(RectTransform page, string content)
        {
            RectTransform viewport = CreateRect("Viewport", page);
            Stretch(viewport);
            viewport.offsetMin = new Vector2(8f, 8f);
            viewport.offsetMax = new Vector2(-8f, -8f);

            Image viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            TMP_Text text = CreateText("Text", viewport, content, 18f, TextAlignmentOptions.TopLeft);
            text.richText = true;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.lineSpacing = 5f;
            text.margin = new Vector4(14f, 12f, 14f, 12f);
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.pivot = new Vector2(0.5f, 1f);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = Vector2.zero;

            ContentSizeFitter fitter = text.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = page.gameObject.AddComponent<ScrollRect>();
            scroll.content = textRect;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
            scroll.scrollSensitivity = 30f;
            return text;
        }

        // —— 小工具 ——

        private RectTransform CreatePage(string name)
        {
            RectTransform page = CreateRect(name, contentRect);
            Stretch(page);
            return page;
        }

        private static RectTransform CreateRect(string name, RectTransform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static Image CreateImage(string name, RectTransform parent, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private TMP_Text CreateText(string name, RectTransform parent, string content, float size,
            TextAlignmentOptions alignment)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.text = content;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = TextColor;
            text.raycastTarget = false;
            return text;
        }

        private Button CreateButton(string name, RectTransform parent, string label, float size, out TMP_Text labelText)
        {
            Image background = CreateImage(name, parent, ButtonColor);
            background.raycastTarget = true;

            Button button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            labelText = CreateText("Label", background.rectTransform, label, size, TextAlignmentOptions.Center);
            Stretch(labelText.rectTransform);

            UiButtonTint.Ensure(button);
            return button;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 position,
            Vector2 size, Vector2 pivot)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
