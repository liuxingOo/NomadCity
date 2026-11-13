using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NomadCity.Core.Model;

namespace NomadCity.UI
{
    /// <summary>
    /// 卡面放大弹窗：点已建成的设施时弹出这张牌的放大版，方便看清卡面细节。
    /// 只负责显示，不管规则 —— 由 <see cref="GameScreenController"/> 决定何时 Show / Hide。
    ///
    /// 场景结构（与 <see cref="CityPatternPanel"/> 一致）：
    /// Canvas
    ///   CardPreviewPopup（宿主容器，挂本脚本，自身永远激活）
    ///     Root                 ← 弹窗开关，由 SetOpen 控制显隐
    ///       Dim                ← 全屏半透明遮罩，Raycast Target 要勾上（挡住底下的地图）
    ///       放大卡面 Image     ← 拖进 Card Face Image（保持宽高比，否则卡面会被拉伸）
    ///       卡名文本           ← 拖进 Card Name Label（可选）
    ///       关闭按钮           ← 拖进 Close Button
    ///
    /// 同一个弹窗被三类操作复用，靠不同的 Show 重载切换按钮含义：
    /// 查看（关闭）/ 建设选费用（两个付费按钮 + 取消建设）/ 盖放角色牌（确定盖放 + 取消）。
    /// </summary>
    public sealed class CardPreviewPopup : MonoBehaviour
    {
        [Tooltip("弹窗开关物体（宿主 → Root → Dim / 卡面 / 关闭按钮）。宿主自身必须保持激活，只切这一个。" +
                 "留空则弹窗会一直显示。")]
        [SerializeField] private GameObject root;

        [Tooltip("放大后的卡面 Image。要保持卡面原始宽高比，请把它的 Preserve Aspect 勾上。")]
        [SerializeField] private Image cardFaceImage;

        [Tooltip("卡名文本（可选）")]
        [SerializeField] private TMP_Text cardNameLabel;

        [Tooltip("关闭按钮")]
        [SerializeField] private Button closeButton;

        [Tooltip("关闭 / 取消按钮上的文案（可选）：查看卡面时写「关闭」，建设选费用时写「取消建设」。")]
        [SerializeField] private TMP_Text closeLabel;

        [Tooltip("主操作按钮：建设时是「选项一 · 用资源费用建造」，盖放角色牌时是「确定盖放」。平时隐藏。")]
        [SerializeField] private Button resourcePayButton;

        [Tooltip("主操作按钮上的文案（建设时是费用明细，盖放时是「确定盖放」）；留空则不改按钮文字。")]
        [SerializeField] private TMP_Text resourcePayLabel;

        [Tooltip("建设：「选项二 · 用金券费用建造」的按钮。只在建设选费用时出现，其余情况隐藏。")]
        [SerializeField] private Button voucherPayButton;

        [Tooltip("金券费用按钮上的文案；留空则不改按钮文字。")]
        [SerializeField] private TMP_Text voucherPayLabel;

        [Tooltip("设施牌卡面 sprite 库，下标映射见 FacilitySupplyPanel.SpriteIndexOf（100–140 → 0–40，" +
                 "核心指挥塔 200–203 → 41–44，延伸枢纽 300–302 → 45–47）。与设施供应区、城市面板共用同一个库。")]
        [SerializeField] private Sprite[] cardFaces = System.Array.Empty<Sprite>();

        [Tooltip("角色牌卡面 sprite 库，下标映射见 CharacterHandPanel.SpriteIndexOf（按角色 0–4，同色共用一张图）。" +
                 "与手牌托盘共用同一个库。")]
        [SerializeField] private Sprite[] characterFaces = System.Array.Empty<Sprite>();

        [Tooltip("城市样式牌卡面 sprite 库，下标映射见 CityPatternPanel.SpriteIndexOf（基础 500–505 → 0–5，" +
                 "企业扩展 550–555 → 6–11）。与城市面板弹窗共用同一个库。")]
        [SerializeField] private Sprite[] patternFaces = System.Array.Empty<Sprite>();

        /// <summary>弹窗是否正开着。</summary>
        public bool IsVisible => root != null && root.activeSelf;

        /// <summary>关闭键当前该做什么：查看时收起弹窗，建设选费用时 = 取消建设（回建设选牌）。</summary>
        private Action closeAction;

        private Action payResourceAction;
        private Action payVoucherAction;

        /// <summary>
        /// 开关弹窗。宿主容器始终激活（避免 Awake 里的按钮绑定失效），只切 Root 的显隐。
        ///
        /// 打开时把自己提到 Canvas 最后一个子物体：这一层在 Unity 里是照子物体顺序画上去的，
        /// 而设施供应区 / 城市面板 / 手牌托盘都在 Canvas 里排在前面 —— 不提到最后，放大图就会被那些面板盖住，
        /// 看起来像「点了设施没反应」。提到最后也意味着它的 Dim 连右下操作区一起挡住，正是放大查看想要的模态效果。
        /// </summary>
        public void SetOpen(bool open)
        {
            if (root == null) return;

            if (open && transform.parent != null) transform.SetAsLastSibling();
            if (root.activeSelf != open) root.SetActive(open);
        }

        public void Hide() => SetOpen(false);

        private void Awake()
        {
            if (root == null)
                Debug.LogWarning("[卡面放大] 没有接「Root」——弹窗会一直显示。请新建弹窗根并拖进本组件的 Root 字段。", this);
            if (closeButton == null)
                Debug.LogWarning("[卡面放大] 没有接「关闭按钮」——只能靠脚本 Hide() 收起。请把关闭按钮拖进 Close Button。", this);

            if (closeButton != null)
            {
                if (closeButton.colors.disabledColor != Color.white)
                {
                    ColorBlock colors = closeButton.colors;
                    colors.disabledColor = Color.white;
                    closeButton.colors = colors;
                }

                closeButton.onClick.AddListener(OnCloseClicked);
            }

            if (resourcePayButton != null)
                resourcePayButton.onClick.AddListener(() => payResourceAction?.Invoke());
            if (voucherPayButton != null)
                voucherPayButton.onClick.AddListener(() => payVoucherAction?.Invoke());

            SetPayButtonsVisible(false);
            SetOpen(false);
        }

        private void OnDestroy()
        {
            if (closeButton != null) closeButton.onClick.RemoveListener(OnCloseClicked);
        }

        private void OnCloseClicked()
        {
            if (closeAction != null) closeAction();
            else Hide();
        }

        /// <summary>把牌 id 对应的卡面放大展示出来（查看用：隐藏两个付费按钮，只留关闭键）。</summary>
        public void Show(GameState state, int cardId)
        {
            ShowFace(state, cardId);

            closeAction = Hide;
            SetLabel(closeLabel, "关闭");
            payResourceAction = null;
            payVoucherAction = null;
            SetPayButtonsVisible(false);

            SetOpen(true);
        }

        /// <summary>
        /// 放大展示一张角色牌（只看不操作）：卡面取自角色库，两个付费按钮一律隐藏，关闭键只收起。
        /// 给「手牌 / 城市面板在非盖放阶段点牌」用 —— 那时牌位只该被看，不该被用。
        /// </summary>
        public void ShowCharacter(GameState state, int cardId)
        {
            int index = CharacterHandPanel.SpriteIndexOf(cardId);
            Sprite face = index >= 0 && index < characterFaces.Length ? characterFaces[index] : null;
            if (face == null) face = OperatorCardArt.SpriteFor(cardId);
            if (face == null && characterFaces.Length == 0)
                Debug.LogWarning("[卡面放大] 角色牌卡面库是空的 —— 放大图只有名字没有牌面。" +
                                 "执行菜单 NomadCity / 卡面：填充卡面库（Card Faces）即可自动填好。", this);

            ShowFaceSprite(face, CharacterNameOf(state, cardId));

            closeAction = Hide;
            SetLabel(closeLabel, "关闭");
            payResourceAction = null;
            payVoucherAction = null;
            SetPayButtonsVisible(false);

            SetOpen(true);
        }

        /// <summary>
        /// 盖放角色牌（§4.2 阶段 1）：先放大卡面看清楚，再让玩家点「确定盖放」才算数。
        /// 关闭键改成「取消」—— 只收起弹窗、什么都不做，手牌原样退回。
        /// </summary>
        public void ShowForCharacterPlacement(GameState state, int cardId, Action onConfirm)
        {
            int index = CharacterHandPanel.SpriteIndexOf(cardId);
            Sprite face = index >= 0 && index < characterFaces.Length ? characterFaces[index] : null;
            if (face == null) face = OperatorCardArt.SpriteFor(cardId);
            if (face == null && characterFaces.Length == 0)
                Debug.LogWarning("[卡面放大] 角色牌卡面库是空的 —— 放大图只有名字没有牌面。" +
                                 "执行菜单 NomadCity / 卡面：填充卡面库（Card Faces）即可自动填好。", this);

            ShowFaceSprite(face, CharacterNameOf(state, cardId));

            // 取消 = 收起来，什么都不做（想盖放就再点一次那张牌）。
            closeAction = Hide;
            SetLabel(closeLabel, "取消");

            // 盖放只需要一个主操作按钮，金券位一律隐藏。
            payVoucherAction = null;
            if (voucherPayButton != null) voucherPayButton.gameObject.SetActive(false);

            payResourceAction = onConfirm;
            if (resourcePayButton != null)
            {
                resourcePayButton.gameObject.SetActive(true);
                resourcePayButton.interactable = true;
            }
            else
            {
                Debug.LogWarning("[卡面放大] 没有接主操作按钮（Resource Pay Button）——" +
                                 "盖放角色牌时点不到「确定盖放」，只能取消。", this);
            }
            SetLabel(resourcePayLabel, "确定盖放");

            SetOpen(true);
        }

        /// <summary>
        /// 放大展示一张城市样式牌（只看不操作）：卡面取自样式库，两个付费按钮一律隐藏，关闭键只收起。
        /// 给「城市面板在非宣告 / 非特殊行动阶段点牌」用。
        /// </summary>
        public void ShowCityPattern(GameState state, int cardId)
        {
            int index = CityPatternPanel.SpriteIndexOf(cardId);
            Sprite face = index >= 0 && index < patternFaces.Length ? patternFaces[index] : null;
            if (face == null && patternFaces.Length == 0)
                Debug.LogWarning("[卡面放大] 城市样式牌卡面库是空的 —— 放大图只有名字没有牌面。" +
                                 "执行菜单 NomadCity / 卡面：填充卡面库（Card Faces）即可自动填好。", this);

            ShowFaceSprite(face, CityPatternNameOf(state, cardId));

            closeAction = Hide;
            SetLabel(closeLabel, "关闭");
            payResourceAction = null;
            payVoucherAction = null;
            SetPayButtonsVisible(false);

            SetOpen(true);
        }

        /// <summary>
        /// 建设第一步（§6.4e 步 1）：放大卡面 + 三个按钮 —— 选项一（资源付费）、选项二（金券付费）、取消建设。
        /// 只有一种费用时，另一个付费按钮自动隐藏；<paramref name="onCancel"/> = 取消建设（回建设选牌弹窗）。
        /// </summary>
        public void ShowForBuild(GameState state, int cardId,
            string resourceText, bool resourceEnabled, Action onResource,
            string voucherText, bool voucherEnabled, Action onVoucher,
            Action onCancel)
        {
            ShowFace(state, cardId);

            closeAction = onCancel;
            SetLabel(closeLabel, "取消建设");

            payResourceAction = onResource;
            payVoucherAction = onVoucher;

            if (resourcePayButton != null)
            {
                resourcePayButton.gameObject.SetActive(!string.IsNullOrEmpty(resourceText));
                resourcePayButton.interactable = resourceEnabled;
            }

            if (voucherPayButton != null)
            {
                voucherPayButton.gameObject.SetActive(!string.IsNullOrEmpty(voucherText));
                voucherPayButton.interactable = voucherEnabled;
            }

            SetLabel(resourcePayLabel, resourceText);
            SetLabel(voucherPayLabel, voucherText);

            SetOpen(true);
        }

        private void ShowFace(GameState state, int cardId)
        {
            int index = FacilitySupplyPanel.SpriteIndexOf(cardId);
            Sprite face = index >= 0 && index < cardFaces.Length ? cardFaces[index] : null;

            ShowFaceSprite(face, NameOf(state, cardId));
        }

        /// <summary>设施牌与角色牌共用的「换面 + 写名」。</summary>
        private void ShowFaceSprite(Sprite face, string name)
        {
            if (cardFaceImage != null)
            {
                cardFaceImage.sprite = face;
                cardFaceImage.enabled = face != null;
            }

            SetLabel(cardNameLabel, name);
        }

        private void SetPayButtonsVisible(bool visible)
        {
            if (resourcePayButton != null) resourcePayButton.gameObject.SetActive(visible);
            if (voucherPayButton != null) voucherPayButton.gameObject.SetActive(visible);
        }

        private static void SetLabel(TMP_Text label, string text)
        {
            if (label != null) label.text = text ?? string.Empty;
        }

        private static string NameOf(GameState state, int cardId)
        {
            FacilityCardDef card = state != null ? state.Catalog.Facility(cardId) : null;
            return card != null ? card.Name : $"设施牌 {cardId}";
        }

        /// <summary>角色牌的显示名；越界或未命名时退回「角色牌 N」，绝不抛异常。</summary>
        private static string CharacterNameOf(GameState state, int cardId)
        {
            if (state == null) return $"角色牌 {cardId}";
            bool isCharacter = (cardId >= CardIds.CharacterBase
                                && cardId < CardIds.CharacterBase + CardCounts.CharacterCards)
                               || (cardId >= CardIds.CharacterExtensionBase
                                   && cardId < CardIds.CharacterExtensionBase + CardCounts.CharacterExtensionCards)
                               || (cardId >= CardIds.OperatorCharacterBase
                                   && cardId < CardIds.OperatorCharacterBase + CardCounts.OperatorCards);
            if (!isCharacter) return $"角色牌 {cardId}";

            CharacterCardDef card = state.Catalog.Character(cardId);
            if (card == null || string.IsNullOrEmpty(card.Name)) return $"角色牌 {cardId}";
            return card.Name;
        }

        /// <summary>城市样式牌的显示名；没录入名字时退回等级占位，绝不抛异常。</summary>
        private static string CityPatternNameOf(GameState state, int cardId)
        {
            if (state == null) return $"样式牌 {cardId}";

            CityPatternCardDef card = state.Catalog.CityPattern(cardId);
            if (card == null) return $"样式牌 {cardId}";
            if (!string.IsNullOrEmpty(card.Name)) return card.Name;
            return card.Level >= 2 ? "II 级样式牌" : "I 级样式牌";
        }
    }
}
