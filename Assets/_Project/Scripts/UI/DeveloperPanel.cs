using System;
using System.Collections.Generic;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Model.Enterprise;
using NomadCity.Core.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 开发者模式面板（只有从主菜单「以开发者模式启动」进入的对局才会创建）。
    ///
    /// 它不改规则本身：所有操作都走 <see cref="ICommand"/>，由 Core 的开发者命令完成，
    /// 因此每一步都和正式流程一样经过命令通道与状态刷新。
    /// </summary>
    public sealed class DeveloperPanel : MonoBehaviour
    {
        private static readonly Color WindowColor = new Color(0.247f, 0.208f, 0.153f, 0.97f);
        private static readonly Color RowColor = new Color(0.165f, 0.142f, 0.110f, 0.98f);
        private static readonly Color TextColor = new Color(0.95f, 0.92f, 0.86f, 1f);
        private static readonly Color MutedColor = new Color(0.76f, 0.72f, 0.63f, 1f);
        private static readonly Color ErrorColor = new Color(0.95f, 0.55f, 0.45f, 1f);

        private const float WindowWidth = 1180f;
        private const float WindowHeight = 720f;
        private const float PadX = 18f;
        private const float PadTop = 92f;
        private const float TabWidth = 118f;
        private const float TabHeight = 44f;
        private const float TabGap = 8f;
        private const float SeatBarHeight = 36f;
        private const float StatusHeight = 26f;

        private enum Tab { Basic = 0, Build = 1, Pattern = 2, Character = 3, Enterprise = 4 }

        private static readonly string[] TabNames = { "基础", "建设", "城市样式", "角色牌", "企业" };

        /// <summary>命令提交入口：由 GameScreenController 包一层状态提示后提交到引擎。</summary>
        public Func<ICommand, CommandResult> Submit;

        /// <summary>
        /// 开发者触发企业特效时转交给游戏界面：有些特效要点地图 / 选牌，
        /// 直接派发命令只会用默认答案静默结算，看起来像「没反应」。
        /// 参数：座位、企业板 id、等级。
        /// </summary>
        public Action<int, int, int> TriggerEnterpriseEffectRequested;

        public bool IsOpen => root != null && root.activeSelf;

        private TMP_FontAsset font;
        private GameObject root;
        private RectTransform window;
        private RectTransform content;
        private TMP_Text status;

        private GameState state;
        private Tab tab = Tab.Basic;
        private int seat;
        private int targetSlot = -1;
        private string lastStatus = "所有修改都通过命令通道执行，不会绕过状态校验。";
        private bool lastStatusError;

        private float ContentWidth => WindowWidth - PadX * 2f - TabWidth - 12f;
        private float ContentHeight => WindowHeight - PadTop - PadX - StatusHeight - 8f;

        public static DeveloperPanel Create(Transform canvas, TMP_FontAsset font)
        {
            GameObject host = new GameObject("DeveloperPanel", typeof(RectTransform));
            host.transform.SetParent(canvas, false);
            var panel = host.AddComponent<DeveloperPanel>();
            panel.font = font;
            panel.Build();
            return panel;
        }

        public void Open(GameState gameState, int initialSeat)
        {
            if (root == null) return;
            state = gameState;
            seat = ClampSeat(initialSeat);
            if (targetSlot < 0 || targetSlot >= PlayerState.CityPanelSlotCount
                || targetSlot == PlayerState.CityPanelCoreSlot)
                targetSlot = NextEmptySlot();
            lastStatus = "所有修改都通过命令通道执行，不会绕过状态校验。";
            lastStatusError = false;
            root.SetActive(true);
            root.transform.SetAsLastSibling();
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
            seat = ClampSeat(seat);
            Rebuild();
        }

        // —— 窗口骨架 ——

        private void Build()
        {
            root = gameObject;
            var rootRect = (RectTransform)root.transform;
            Stretch(rootRect);

            Image dim = CreateImage("Dim", rootRect, new Color(0f, 0f, 0f, 0.55f));
            dim.raycastTarget = true;
            Stretch(dim.rectTransform);

            Image windowImage = CreateImage("Window", rootRect, WindowColor);
            window = windowImage.rectTransform;
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.pivot = new Vector2(0.5f, 0.5f);
            window.anchoredPosition = Vector2.zero;
            window.sizeDelta = new Vector2(WindowWidth, WindowHeight);

            TMP_Text title = CreateText("Title", window, "开发者模式", 26f, TextAlignmentOptions.Center);
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -12f);
            title.rectTransform.sizeDelta = new Vector2(WindowWidth - 220f, 38f);

            TMP_Text hint = CreateText("Hint", window,
                "改轮次 / 数值、指定建设与宣告、重置倒置、直接使用角色牌、调整并触发企业特效。",
                14f, TextAlignmentOptions.Center);
            hint.color = MutedColor;
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            hint.rectTransform.pivot = new Vector2(0.5f, 1f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, -52f);
            hint.rectTransform.sizeDelta = new Vector2(WindowWidth - 80f, 24f);

            Button close = CreateButton("Close", window, "关闭", 20f);
            var closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-16f, -14f);
            closeRect.sizeDelta = new Vector2(72f, 32f);
            close.onClick.AddListener(Close);

            // 左侧页签
            for (int i = 0; i < TabNames.Length; i++)
            {
                int picked = i;
                Button tabButton = CreateButton($"Tab{i}", window, TabNames[i], 17f);
                var rect = tabButton.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(PadX, -PadTop - i * (TabHeight + TabGap));
                rect.sizeDelta = new Vector2(TabWidth, TabHeight);
                tabButton.onClick.AddListener(() =>
                {
                    tab = (Tab)picked;
                    Rebuild();
                });
            }

            // 底部状态行
            status = CreateText("Status", window, lastStatus, 14f, TextAlignmentOptions.Left);
            status.rectTransform.anchorMin = new Vector2(0f, 0f);
            status.rectTransform.anchorMax = new Vector2(1f, 0f);
            status.rectTransform.pivot = new Vector2(0.5f, 0f);
            status.rectTransform.anchoredPosition = new Vector2(0f, 6f);
            status.rectTransform.offsetMin = new Vector2(PadX, 6f);
            status.rectTransform.offsetMax = new Vector2(-PadX, 6f + StatusHeight);

            // 内容区
            content = CreateRect("Content", window);
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(1f, 1f);
            content.offsetMin = new Vector2(PadX + TabWidth + 12f, PadX + StatusHeight + 8f);
            content.offsetMax = new Vector2(-PadX, -PadTop);

            root.SetActive(false);
        }

        private void Rebuild()
        {
            if (content == null) return;

            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);

            if (status != null)
            {
                status.text = lastStatus;
                status.color = lastStatusError ? ErrorColor : MutedColor;
            }

            if (state == null || state.Players.Count == 0)
            {
                AddText(content, "还没有可操作的对局状态。", 0f, 0f, ContentWidth, 30f, 16f,
                    TextAlignmentOptions.Left, MutedColor);
                return;
            }

            seat = ClampSeat(seat);
            BuildSeatBar();
            float bodyTop = -SeatBarHeight - 10f;

            switch (tab)
            {
                case Tab.Build: BuildBuildTab(bodyTop); break;
                case Tab.Pattern: BuildPatternTab(bodyTop); break;
                case Tab.Character: BuildCharacterTab(bodyTop); break;
                case Tab.Enterprise: BuildEnterpriseTab(bodyTop); break;
                default: BuildBasicTab(bodyTop); break;
            }
        }

        private void BuildSeatBar()
        {
            AddText(content, "玩家：", 0f, 0f, 56f, 30f, 15f, TextAlignmentOptions.Left);

            int count = state.Players.Count;
            for (int i = 0; i < count; i++)
            {
                int picked = i;
                string label = i == seat ? $"玩家 {i + 1} ●" : $"玩家 {i + 1}";
                Button button = AddButton(content, label, 60f + i * 100f, 0f, 92f, 30f,
                    () => { seat = picked; Rebuild(); });
                button.interactable = i != seat;
            }
        }

        // —— 基础：轮次 / 分数 / 金券 / 资源 ——

        private void BuildBasicTab(float top)
        {
            PlayerState player = state.Player(seat);
            string summary = $"玩家 {seat + 1}：分数 {player.Score} ｜ 金券 {player.Vouchers} ｜ " +
                             $"标记 {player.MarkersInSupply} ｜ 源岩 {player.Resources.Get(ResourceType.Orirock)} ｜ " +
                             $"源石 {player.Resources.Get(ResourceType.Originium)} ｜ 异铁 {player.Resources.Get(ResourceType.Oriron)} ｜ " +
                             $"至纯源石 {player.Resources.Get(ResourceType.OriginitePrime)}";
            AddText(content, summary, 0f, top, ContentWidth, 26f, 15f, TextAlignmentOptions.Left, MutedColor);

            float y = top - 38f;
            AddStepperRow("回合（TurnNumber）", state.TurnNumber.ToString(), y,
                () => ChangeTurn(-1), () => ChangeTurn(1));
            y -= 44f;
            AddStepperRow("行动轮次（ActionRound）", state.ActionRound.ToString(), y,
                () => ChangeRound(-1), () => ChangeRound(1));
            y -= 52f;

            AddText(content, "玩家数值", 0f, y, 200f, 26f, 17f, TextAlignmentOptions.Left);
            y -= 34f;

            AddStatRow("分数", player.Score, DeveloperPlayerStat.Score, y); y -= 44f;
            AddStatRow("金券", player.Vouchers, DeveloperPlayerStat.Vouchers, y); y -= 44f;
            AddStatRow("玩家标记", player.MarkersInSupply, DeveloperPlayerStat.Markers, y); y -= 52f;

            AddText(content, "资源", 0f, y, 200f, 26f, 17f, TextAlignmentOptions.Left);
            y -= 34f;

            AddStatRow("源岩", player.Resources.Get(ResourceType.Orirock), DeveloperPlayerStat.Orirock, y); y -= 44f;
            AddStatRow("源石", player.Resources.Get(ResourceType.Originium), DeveloperPlayerStat.Originium, y); y -= 44f;
            AddStatRow("异铁", player.Resources.Get(ResourceType.Oriron), DeveloperPlayerStat.Oriron, y); y -= 44f;
            AddStatRow("至纯源石", player.Resources.Get(ResourceType.OriginitePrime), DeveloperPlayerStat.OriginitePrime, y);
            y -= 62f;

            // 行动事件（卡牌效果授予的行动）测试入口。放在右侧空白列，避免把「基础」页撑出可视区。
            float eventX = 600f;
            float eventY = top - 38f - 44f - 52f;
            AddText(content, "行动事件测试（效果授予）", eventX, eventY, 320f, 26f, 17f, TextAlignmentOptions.Left);
            eventY -= 40f;
            AddButton(content, "授予探索", eventX, eventY, 130f, 34f,
                () => Run(new DeveloperQueuePendingActionCommand(seat, PendingActionKind.Explore)));
            AddButton(content, "授予建设", eventX + 140f, eventY, 130f, 34f,
                () => Run(new DeveloperQueuePendingActionCommand(seat, PendingActionKind.Build)));
            eventY -= 42f;
            AddButton(content, "授予移除影响力", eventX, eventY, 170f, 34f,
                () => Run(new DeveloperQueuePendingActionCommand(seat, PendingActionKind.RemoveInfluence)));
        }

        private void ChangeTurn(int delta)
        {
            int next = Mathf.Clamp(state.TurnNumber + delta, 1, TurnFlow.TotalTurns);
            Run(new DeveloperSetTurnCommand(next, state.ActionRound));
        }

        private void ChangeRound(int delta)
        {
            int next = Mathf.Clamp(state.ActionRound + delta, 1, 2);
            Run(new DeveloperSetTurnCommand(state.TurnNumber, next));
        }

        private void AddStepperRow(string label, string value, float y, UnityAction minus, UnityAction plus)
        {
            AddText(content, label, 0f, y, 240f, 34f, 16f, TextAlignmentOptions.Left);
            AddText(content, value, 250f, y, 80f, 34f, 18f, TextAlignmentOptions.Center);
            AddButton(content, "-1", 340f, y, 52f, 34f, minus);
            AddButton(content, "+1", 400f, y, 52f, 34f, plus);
        }

        private void AddStatRow(string label, int value, DeveloperPlayerStat stat, float y)
        {
            AddText(content, label, 0f, y, 240f, 34f, 16f, TextAlignmentOptions.Left);
            AddText(content, value.ToString(), 250f, y, 80f, 34f, 18f, TextAlignmentOptions.Center);
            AddButton(content, "-5", 340f, y, 52f, 34f, () => ChangeStat(stat, -5));
            AddButton(content, "-1", 400f, y, 52f, 34f, () => ChangeStat(stat, -1));
            AddButton(content, "+1", 460f, y, 52f, 34f, () => ChangeStat(stat, 1));
            AddButton(content, "+5", 520f, y, 52f, 34f, () => ChangeStat(stat, 5));
        }

        private void ChangeStat(DeveloperPlayerStat stat, int delta)
        {
            int current = CurrentStat(stat);
            int next = Mathf.Max(0, current + delta);
            if (stat == DeveloperPlayerStat.Markers) next = Mathf.Min(30, next);
            Run(new DeveloperSetPlayerStatCommand(seat, stat, next));
        }

        private int CurrentStat(DeveloperPlayerStat stat)
        {
            PlayerState player = state.Player(seat);
            switch (stat)
            {
                case DeveloperPlayerStat.Score: return player.Score;
                case DeveloperPlayerStat.Vouchers: return player.Vouchers;
                case DeveloperPlayerStat.Markers: return player.MarkersInSupply;
                case DeveloperPlayerStat.Orirock: return player.Resources.Get(ResourceType.Orirock);
                case DeveloperPlayerStat.Originium: return player.Resources.Get(ResourceType.Originium);
                case DeveloperPlayerStat.Oriron: return player.Resources.Get(ResourceType.Oriron);
                case DeveloperPlayerStat.OriginitePrime: return player.Resources.Get(ResourceType.OriginitePrime);
                default: return 0;
            }
        }

        // —— 建设与移除 ——

        private void BuildBuildTab(float top)
        {
            const float leftWidth = 330f;
            float rightX = leftWidth + 20f;
            float rightWidth = ContentWidth - rightX;

            // 左：当前设施
            AddText(content, "当前设施（核心指挥塔保留）", 0f, top, leftWidth, 26f, 16f,
                TextAlignmentOptions.Left);

            float y = top - 34f;
            PlayerState player = state.Player(seat);
            int shown = 0;
            for (int slot = 0; slot < PlayerState.CityPanelSlotCount; slot++)
            {
                if (slot == PlayerState.CityPanelCoreSlot) continue;
                int cardId = player.CityPanelSlots[slot];
                if (cardId < 0) continue;

                int picked = slot;
                string name = FacilityName(cardId);
                string used = player.CityPanelUsed[slot] ? "（已倒置）" : string.Empty;
                AddText(content, $"{slot}  {name}{used}", 0f, y, leftWidth - 78f, 30f, 14f,
                    TextAlignmentOptions.Left, player.CityPanelUsed[slot] ? MutedColor : TextColor);
                AddButton(content, "移除", leftWidth - 72f, y, 68f, 30f,
                    () => Run(new DeveloperRemoveFacilityCommand(seat, picked)));
                y -= 34f;
                shown++;
            }

            if (shown == 0)
            {
                AddText(content, "（没有可移除的设施）", 0f, y, leftWidth, 28f, 14f,
                    TextAlignmentOptions.Left, MutedColor);
                y -= 34f;
            }

            y -= 8f;
            AddButton(content, "移除全部非核心设施", 0f, y, 210f, 34f,
                () => Run(new DeveloperRemoveFacilityCommand(seat, -1)));
            y -= 42f;
            AddButton(content, "重置全部倒置", 0f, y, 150f, 34f,
                () => Run(new DeveloperResetPanelUseCommand(seat)));

            // 右：目标格 + 设施牌堆
            AddText(content, "目标格", rightX, top, 60f, 30f, 15f, TextAlignmentOptions.Left);
            AddText(content, $"{targetSlot}", rightX + 64f, top, 50f, 30f, 17f,
                TextAlignmentOptions.Center);
            AddButton(content, "-", rightX + 118f, top, 38f, 30f,
                () => { targetSlot = PrevBuildSlot(targetSlot); Rebuild(); });
            AddButton(content, "+", rightX + 162f, top, 38f, 30f,
                () => { targetSlot = NextBuildSlot(targetSlot); Rebuild(); });
            AddText(content, "点设施牌免费建到目标格；7 号核心格不可用。", rightX + 214f, top,
                rightWidth - 214f, 30f, 13f, TextAlignmentOptions.Left, MutedColor);

            float gridTop = top - 44f;
            const int columns = 4;
            const float gap = 6f;
            float cellWidth = (rightWidth - gap * (columns - 1)) / columns;
            const float cellHeight = 34f;
            float rowStep = cellHeight + 4f;

            List<int> deck = state.FacilityDeck;
            for (int i = 0; i < deck.Count; i++)
            {
                int cardId = deck[i];
                int column = i % columns;
                int row = i / columns;
                float x = rightX + column * (cellWidth + gap);
                float cellY = gridTop - row * rowStep;
                Button button = AddButton(content, FacilityName(cardId), x, cellY, cellWidth, cellHeight,
                    () => BuildFacility(cardId), 12f);
                button.interactable = CanBuildInto(targetSlot);
            }

            if (deck.Count == 0)
                AddText(content, "设施牌堆已经空了。", rightX, gridTop, rightWidth, 30f, 15f,
                    TextAlignmentOptions.Left, MutedColor);
        }

        private void BuildFacility(int facilityId)
        {
            if (!CanBuildInto(targetSlot)) return;
            if (Run(new DeveloperBuildFacilityCommand(seat, facilityId, targetSlot)))
                targetSlot = NextEmptySlot();
        }

        private bool CanBuildInto(int slot)
        {
            if (state == null || slot < 0 || slot >= PlayerState.CityPanelSlotCount) return false;
            if (slot == PlayerState.CityPanelCoreSlot) return false;
            return state.Player(seat).CityPanelSlots[slot] < 0;
        }

        private int NextEmptySlot()
        {
            if (state == null) return 0;
            PlayerState player = state.Player(seat);
            for (int slot = 0; slot < PlayerState.CityPanelSlotCount; slot++)
            {
                if (slot == PlayerState.CityPanelCoreSlot) continue;
                if (player.CityPanelSlots[slot] < 0) return slot;
            }
            return 0;
        }

        private static int NextBuildSlot(int slot)
        {
            for (int i = 1; i <= PlayerState.CityPanelSlotCount; i++)
            {
                int next = (slot + i) % PlayerState.CityPanelSlotCount;
                if (next != PlayerState.CityPanelCoreSlot) return next;
            }
            return 0;
        }

        private static int PrevBuildSlot(int slot)
        {
            for (int i = 1; i <= PlayerState.CityPanelSlotCount; i++)
            {
                int next = (slot - i + PlayerState.CityPanelSlotCount) % PlayerState.CityPanelSlotCount;
                if (next != PlayerState.CityPanelCoreSlot) return next;
            }
            return 0;
        }

        // —— 城市样式 ——

        private void BuildPatternTab(float top)
        {
            AddText(content, "点任意一张城市样式牌即宣告；无视宣告条件，仍消耗 1 个玩家标记并结算牌面分数。",
                0f, top, ContentWidth, 26f, 14f, TextAlignmentOptions.Left, MutedColor);

            bool enterprise = state.Setup.Has(ExpansionFlags.Enterprise);
            List<int> ids = new List<int>(state.Catalog.CityPatternIds(enterprise));
            ids.Sort();

            const int columns = 2;
            const float gap = 10f;
            float cellWidth = (ContentWidth - gap) / columns;
            const float cellHeight = 42f;
            float rowStep = cellHeight + 6f;
            float gridTop = top - 38f;

            for (int i = 0; i < ids.Count; i++)
            {
                int cardId = ids[i];
                CityPatternCardDef card = state.Catalog.CityPattern(cardId);
                if (card == null) continue;
                int column = i % columns;
                int row = i / columns;
                float x = column * (cellWidth + gap);
                float y = gridTop - row * rowStep;
                string label = $"{card.Name}（{card.Level} 级 / {card.Score} 分）";
                Button button = AddButton(content, label, x, y, cellWidth, cellHeight,
                    () => Run(new DeveloperDeclareCityPatternCommand(seat, cardId)), 14f);
                button.interactable = state.Player(seat).MarkersInSupply > 0;
            }
        }

        // —— 角色牌 ——

        private void BuildCharacterTab(float top)
        {
            AddText(content, "可用范围：已盖放 / 手牌 / 弃牌堆。不检查回合、次数与起始玩家；效果本身仍按牌面结算。",
                0f, top, ContentWidth, 26f, 14f, TextAlignmentOptions.Left, MutedColor);

            PlayerState player = state.Player(seat);
            List<int> cards = CharacterCards(player);
            float y = top - 38f;

            if (cards.Count == 0)
            {
                AddText(content, "这名玩家的三个区域里都没有角色牌。", 0f, y, ContentWidth, 30f, 15f,
                    TextAlignmentOptions.Left, MutedColor);
                return;
            }

            for (int i = 0; i < cards.Count; i++)
            {
                int cardId = cards[i];
                CharacterCardDef def = state.Catalog.Character(cardId);
                if (def == null) continue;

                AddText(content, $"{def.Name}（{ZoneOf(player, cardId)}）", 0f, y, 300f, 36f, 15f,
                    TextAlignmentOptions.Left);

                bool hasStrategy = def.StrategyOps.Count > 0;
                bool hasTactics = def.TacticsOps.Count > 0
                                  || def.PersistentTactic != PersistentTacticKind.None;

                Button strategy = AddButton(content, "策略", 310f, y, 78f, 34f,
                    () => Run(new DeveloperUseCharacterCardCommand(seat, cardId,
                        CharacterEffectKind.Strategy)));
                strategy.interactable = hasStrategy;

                Button tactics = AddButton(content, "计谋", 396f, y, 78f, 34f,
                    () => Run(new DeveloperUseCharacterCardCommand(seat, cardId,
                        CharacterEffectKind.Tactics)));
                tactics.interactable = hasTactics;

                y -= 42f;
            }
        }

        private List<int> CharacterCards(PlayerState player)
        {
            var result = new List<int>();
            var seen = new HashSet<int>();

            if (player.PlacedCharacterCard >= 0 && seen.Add(player.PlacedCharacterCard))
                result.Add(player.PlacedCharacterCard);
            foreach (int cardId in player.HandCharacterCards)
                if (seen.Add(cardId)) result.Add(cardId);
            foreach (int cardId in player.DiscardCharacterCards)
                if (seen.Add(cardId)) result.Add(cardId);

            return result;
        }

        private static string ZoneOf(PlayerState player, int cardId)
        {
            if (player.PlacedCharacterCard == cardId) return "已盖放";
            if (player.HandCharacterCards.Contains(cardId)) return "手牌";
            return "弃牌堆";
        }

        // —— 企业 ——

        private void BuildEnterpriseTab(float top)
        {
            if (!state.Setup.Has(ExpansionFlags.Enterprise))
            {
                AddText(content, "本局没有启用企业扩展包。", 0f, top, ContentWidth, 30f, 16f,
                    TextAlignmentOptions.Left, MutedColor);
                return;
            }

            AddText(content, "等级对当前选中的玩家生效；「+ / -」直接设等级（不在场上会自动上场）。" +
                             "「触发」只列出已经解锁的黑色特效等级，点一下按正常流程结算（面板会自动收起）。",
                0f, top, ContentWidth, 26f, 14f, TextAlignmentOptions.Left, MutedColor);

            float y = top - 38f;
            int currentLevel = -1;

            for (int boardId = 0; boardId < EnterpriseCatalog.BoardCount; boardId++)
            {
                EnterpriseBoardDef def = EnterpriseCatalog.Board(boardId);
                string name = def != null ? def.Name : $"企业板 {boardId}";
                bool active = state.Enterprises.IsActive(boardId);
                currentLevel = active ? state.Enterprises.LevelOf(boardId, seat) : -1;

                AddText(content, active ? name : $"{name}（未上场）", 0f, y, 220f, 40f, 15f,
                    TextAlignmentOptions.Left, active ? TextColor : MutedColor);
                AddText(content, currentLevel >= 0 ? $"Lv {currentLevel}" : "—", 224f, y, 64f, 40f,
                    16f, TextAlignmentOptions.Center);

                int board = boardId;
                AddButton(content, "-", 292f, y, 38f, 32f, () => ChangeEnterpriseLevel(board, -1));
                AddButton(content, "+", 336f, y, 38f, 32f, () => ChangeEnterpriseLevel(board, 1));

                // 已经解锁的黑色特效等级
                float x = 394f;
                for (int level = 1; level <= 5; level++)
                {
                    EnterpriseLevel entry = def?.Level(level);
                    if (entry == null || !entry.IsEffect || currentLevel < level) continue;

                    int pickedLevel = level;
                    AddButton(content, $"触发 L{level}", x, y, 88f, 32f,
                        () => TriggerEnterpriseEffect(board, pickedLevel), 13f);
                    x += 94f;
                }

                y -= 56f;
            }
        }

        private void ChangeEnterpriseLevel(int boardId, int delta)
        {
            int current = state.Enterprises.IsActive(boardId)
                ? state.Enterprises.LevelOf(boardId, seat)
                : -1;

            int next;
            if (current < 0) next = delta > 0 ? 1 : 0;
            else next = Mathf.Clamp(current + delta, 0, 5);

            Run(new DeveloperSetEnterpriseLevelCommand(seat, boardId, next));
        }

        private void TriggerEnterpriseEffect(int boardId, int level)
        {
            if (TriggerEnterpriseEffectRequested == null)
            {
                lastStatus = "没有接上企业特效通道，无法触发。";
                Rebuild();
                return;
            }

            // 特效可能要点地图或弹窗选目标：先收起面板，把画面和点击让出来。
            Close();
            TriggerEnterpriseEffectRequested(seat, boardId, level);
        }

        // —— 提交 ——

        private bool Run(ICommand command)
        {
            CommandResult result = Submit != null ? Submit(command) : CommandResult.Fail("没有接上命令通道。");
            lastStatusError = !result.Ok;
            lastStatus = result.Ok ? $"已执行：{command.Name}" : result.Error;
            Rebuild();
            return result.Ok;
        }

        // —— 派生数据 ——

        private int ClampSeat(int value)
        {
            if (state == null || state.Players.Count == 0) return 0;
            return Mathf.Clamp(value, 0, state.Players.Count - 1);
        }

        private string FacilityName(int cardId)
        {
            FacilityCardDef card = state != null ? state.Catalog.Facility(cardId) : null;
            return card != null ? card.Name : $"设施 {cardId}";
        }

        // —— 小工具 ——

        private TMP_Text AddText(RectTransform parent, string text, float x, float y, float width, float height,
            float size, TextAlignmentOptions alignment, Color? color = null)
        {
            TMP_Text label = CreateText("Text", parent, text, size, alignment);
            label.color = color ?? TextColor;
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            return label;
        }

        private Button AddButton(RectTransform parent, string label, float x, float y, float width, float height,
            UnityAction onClick, float size = 15f)
        {
            Button button = CreateButton("Button", parent, label, size);
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            button.onClick.AddListener(onClick);
            return button;
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
            return image;
        }

        private TMP_Text CreateText(string name, RectTransform parent, string text, float size,
            TextAlignmentOptions alignment)
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
            Image background = CreateImage(name, parent, RowColor);
            Button button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            TMP_Text text = CreateText("Label", background.rectTransform, label, size,
                TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
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
    }
}
