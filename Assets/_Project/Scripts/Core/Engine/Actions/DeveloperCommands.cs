using System.Collections.Generic;
using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>开发者面板可以修改的玩家数值。</summary>
    public enum DeveloperPlayerStat
    {
        Score = 0,
        Vouchers = 1,
        Markers = 2,
        Orirock = 3,
        Originium = 4,
        Oriron = 5,
        OriginitePrime = 6,
    }

    /// <summary>
    /// 开发者模式：直接设置回合（TurnNumber）与行动轮次（ActionRound）。
    /// 只改流程数字，不碰阶段、牌堆或玩家状态。
    /// </summary>
    public sealed class DeveloperSetTurnCommand : ICommand
    {
        public readonly int TurnNumber;
        public readonly int ActionRound;

        public DeveloperSetTurnCommand(int turnNumber, int actionRound)
        {
            TurnNumber = turnNumber;
            ActionRound = actionRound;
        }

        public int ActorSeat => -1;
        public string Name => "开发者：设置回合";
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            if (!DeveloperRules.Enabled(state)) return CommandResult.Fail("当前不是开发者模式。");
            if (TurnNumber < 1 || TurnNumber > TurnFlow.TotalTurns)
                return CommandResult.Fail($"回合数必须在 1–{TurnFlow.TotalTurns} 之间。");
            if (ActionRound < 1 || ActionRound > 2)
                return CommandResult.Fail("行动轮次必须是 1 或 2。");
            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            // 换到新一轮时清掉「本轮已做过主要行动」，让新回合能正常行动。
            if (state.ActionRound != ActionRound) state.MainActionTakenThisRound = false;
            state.TurnNumber = TurnNumber;
            state.ActionRound = ActionRound;
        }
    }

    /// <summary>开发者模式：把某位玩家的分数 / 金券 / 标记 / 四种资源直接设成指定值。</summary>
    public sealed class DeveloperSetPlayerStatCommand : ICommand
    {
        public readonly DeveloperPlayerStat Stat;
        public readonly int Value;

        public DeveloperSetPlayerStatCommand(int seat, DeveloperPlayerStat stat, int value)
        {
            ActorSeat = seat;
            Stat = stat;
            Value = value;
        }

        public int ActorSeat { get; }
        public string Name => "开发者：修改玩家数值";
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            if (!DeveloperRules.Enabled(state)) return CommandResult.Fail("当前不是开发者模式。");
            if (ActorSeat < 0 || ActorSeat >= state.Players.Count)
                return CommandResult.Fail($"座位号 {ActorSeat} 无效。");
            if (Value < 0) return CommandResult.Fail("数值不能是负数。");
            if (Stat == DeveloperPlayerStat.Markers && Value > 30)
                return CommandResult.Fail("玩家标记最多只能设到 30 个。");
            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            PlayerState player = state.Player(ActorSeat);
            switch (Stat)
            {
                case DeveloperPlayerStat.Score: player.Score = Value; break;
                case DeveloperPlayerStat.Vouchers: player.Vouchers = Value; break;
                case DeveloperPlayerStat.Markers: player.MarkersInSupply = Value; break;
                case DeveloperPlayerStat.Orirock: player.Resources.Set(ResourceType.Orirock, Value); break;
                case DeveloperPlayerStat.Originium: player.Resources.Set(ResourceType.Originium, Value); break;
                case DeveloperPlayerStat.Oriron: player.Resources.Set(ResourceType.Oriron, Value); break;
                case DeveloperPlayerStat.OriginitePrime: player.Resources.Set(ResourceType.OriginitePrime, Value); break;
            }
        }
    }

    /// <summary>
    /// 开发者模式：从设施牌堆里挑一张，免费建到指定玩家的城市面板空格上。
    /// 只放牌、加分，不结算入场效果（入场效果可能需要玩家点选，留给正式流程）。
    /// </summary>
    public sealed class DeveloperBuildFacilityCommand : ICommand
    {
        public readonly int FacilityId;
        public readonly int PanelSlot;

        public DeveloperBuildFacilityCommand(int seat, int facilityId, int panelSlot)
        {
            ActorSeat = seat;
            FacilityId = facilityId;
            PanelSlot = panelSlot;
        }

        public int ActorSeat { get; }
        public string Name => "开发者：指定设施建设";
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            if (!DeveloperRules.Enabled(state)) return CommandResult.Fail("当前不是开发者模式。");
            if (ActorSeat < 0 || ActorSeat >= state.Players.Count)
                return CommandResult.Fail($"座位号 {ActorSeat} 无效。");

            if (!state.FacilityDeck.Contains(FacilityId))
                return CommandResult.Fail("这张设施牌不在设施牌堆里。");

            FacilityCardDef card = state.Catalog.Facility(FacilityId);
            if (card == null) return CommandResult.Fail($"设施牌 {FacilityId} 不存在。");

            PlayerState player = state.Player(ActorSeat);
            if (PanelSlot < 0 || PanelSlot >= PlayerState.CityPanelSlotCount)
                return CommandResult.Fail($"城市面板格号 {PanelSlot} 无效。");
            if (PanelSlot == PlayerState.CityPanelCoreSlot)
                return CommandResult.Fail("核心指挥塔所在的格子不能建设设施。");
            if (player.CityPanelSlots[PanelSlot] >= 0)
                return CommandResult.Fail("这个格子上已经有设施了。");

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            PlayerState player = state.Player(ActorSeat);
            FacilityCardDef card = state.Catalog.Facility(FacilityId);

            state.FacilityDeck.Remove(FacilityId);
            player.CityPanelSlots[PanelSlot] = FacilityId;
            player.CityPanelUsed[PanelSlot] = false;
            player.BuiltFacilities.Add(FacilityId);
            player.Score += card.Score;
        }
    }

    /// <summary>
    /// 开发者模式：移除玩家城市面板上的设施（核心指挥塔除外）。
    /// <see cref="PanelSlot"/> = -1 表示移除所有非核心设施；移除的牌放回设施牌堆，方便重新指定。
    /// </summary>
    public sealed class DeveloperRemoveFacilityCommand : ICommand
    {
        public readonly int PanelSlot;

        public DeveloperRemoveFacilityCommand(int seat, int panelSlot)
        {
            ActorSeat = seat;
            PanelSlot = panelSlot;
        }

        public int ActorSeat { get; }
        public string Name => "开发者：移除设施";
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            if (!DeveloperRules.Enabled(state)) return CommandResult.Fail("当前不是开发者模式。");
            if (ActorSeat < 0 || ActorSeat >= state.Players.Count)
                return CommandResult.Fail($"座位号 {ActorSeat} 无效。");

            if (PanelSlot < -1 || PanelSlot >= PlayerState.CityPanelSlotCount)
                return CommandResult.Fail($"城市面板格号 {PanelSlot} 无效。");
            if (PanelSlot == PlayerState.CityPanelCoreSlot)
                return CommandResult.Fail("核心指挥塔不能被移除。");

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            PlayerState player = state.Player(ActorSeat);

            if (PanelSlot >= 0)
            {
                RemoveAt(state, player, PanelSlot);
                return;
            }

            for (int slot = 0; slot < PlayerState.CityPanelSlotCount; slot++)
            {
                if (slot == PlayerState.CityPanelCoreSlot) continue;
                RemoveAt(state, player, slot);
            }
        }

        private static void RemoveAt(GameState state, PlayerState player, int slot)
        {
            int cardId = player.CityPanelSlots[slot];
            player.CityPanelUsed[slot] = false;
            if (cardId < 0) return;

            player.CityPanelSlots[slot] = -1;
            player.BuiltFacilities.Remove(cardId);
            state.FacilityDeck.Add(cardId);
        }
    }

    /// <summary>开发者模式：把玩家城市面板上「已用于宣告而倒置」的设施标记重置回未使用。</summary>
    public sealed class DeveloperResetPanelUseCommand : ICommand
    {
        /// <summary>-1 = 重置全部格子。</summary>
        public readonly int PanelSlot;

        public DeveloperResetPanelUseCommand(int seat, int panelSlot = -1)
        {
            ActorSeat = seat;
            PanelSlot = panelSlot;
        }

        public int ActorSeat { get; }
        public string Name => "开发者：重置倒置设施";
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            if (!DeveloperRules.Enabled(state)) return CommandResult.Fail("当前不是开发者模式。");
            if (ActorSeat < 0 || ActorSeat >= state.Players.Count)
                return CommandResult.Fail($"座位号 {ActorSeat} 无效。");
            if (PanelSlot < -1 || PanelSlot >= PlayerState.CityPanelSlotCount)
                return CommandResult.Fail($"城市面板格号 {PanelSlot} 无效。");
            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            PlayerState player = state.Player(ActorSeat);
            if (PanelSlot >= 0)
            {
                player.CityPanelUsed[PanelSlot] = false;
                return;
            }

            for (int slot = 0; slot < PlayerState.CityPanelSlotCount; slot++)
                player.CityPanelUsed[slot] = false;
        }
    }

    /// <summary>
    /// 开发者模式：从**全部**城市样式牌（不只是场上的 6 张）里指定一张直接宣告。
    /// 不走宣告条件校验；仍然消耗 1 个玩家标记并结算牌面分数。场上没有这张牌时先把它放上场。
    /// </summary>
    public sealed class DeveloperDeclareCityPatternCommand : ICommand
    {
        public readonly int PatternCardId;

        public DeveloperDeclareCityPatternCommand(int seat, int patternCardId)
        {
            ActorSeat = seat;
            PatternCardId = patternCardId;
        }

        public int ActorSeat { get; }
        public string Name => "开发者：指定宣告城市样式";
        public bool ConsumesAction => false;

        public CommandResult Validate(GameState state)
        {
            if (!DeveloperRules.Enabled(state)) return CommandResult.Fail("当前不是开发者模式。");
            if (ActorSeat < 0 || ActorSeat >= state.Players.Count)
                return CommandResult.Fail($"座位号 {ActorSeat} 无效。");

            if (!ContainsPattern(state, PatternCardId))
                return CommandResult.Fail($"城市样式牌 {PatternCardId} 不在本局的牌库里。");

            PlayerState player = state.Player(ActorSeat);
            if (player.MarkersInSupply <= 0)
                return CommandResult.Fail("玩家供应堆里没有玩家标记，无法宣告城市样式。");

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            PlayerState player = state.Player(ActorSeat);
            CityPatternCardDef card = state.Catalog.CityPattern(PatternCardId);

            if (!state.CityPatternCards.Contains(PatternCardId))
            {
                state.CityPatternCards.Add(PatternCardId);
                state.CityPatternStates.Add(new CityPatternState(PatternCardId, card.Level));
            }

            player.MarkersInSupply--;
            state.PatternStateOf(PatternCardId)?.PlaceMarker(ActorSeat, 0);
            player.Score += card.Score;
        }

        private static bool ContainsPattern(GameState state, int cardId)
        {
            bool enterprise = state.Setup.Has(NomadCity.Core.Rules.ExpansionFlags.Enterprise);
            foreach (int id in state.Catalog.CityPatternIds(enterprise))
                if (id == cardId) return true;
            return false;
        }
    }

    /// <summary>
    /// 开发者模式：无视盖放 / 回合次数 / 起始玩家等限制，直接发动某张角色牌的策略或计谋。
    /// 只要这张牌在该玩家的「已盖放 / 手牌 / 弃牌堆」里即可；效果本身按牌面结算（含费用）。
    /// </summary>
    public sealed class DeveloperUseCharacterCardCommand : ICommand
    {
        public readonly int CardId;
        public readonly CharacterEffectKind Effect;

        private readonly IEffectChoices choices;

        public DeveloperUseCharacterCardCommand(int seat, int cardId, CharacterEffectKind effect,
            IEffectChoices choices = null)
        {
            ActorSeat = seat;
            CardId = cardId;
            Effect = effect;
            this.choices = choices;
        }

        public int ActorSeat { get; }
        public string Name => "开发者：使用角色牌";
        public bool ConsumesAction => false;

        /// <summary>效果点选答案（联机编码用）。</summary>
        public IEffectChoices EffectChoices => choices;

        /// <summary>本次发动效果的结果（界面状态行用）。</summary>
        public EffectReport Report { get; private set; }

        public CommandResult Validate(GameState state)
        {
            if (!DeveloperRules.Enabled(state)) return CommandResult.Fail("当前不是开发者模式。");
            if (ActorSeat < 0 || ActorSeat >= state.Players.Count)
                return CommandResult.Fail($"座位号 {ActorSeat} 无效。");

            PlayerState player = state.Player(ActorSeat);
            if (player.PlacedCharacterCard != CardId
                && !player.HandCharacterCards.Contains(CardId)
                && !player.DiscardCharacterCards.Contains(CardId))
                return CommandResult.Fail("这张角色牌不在该玩家的已盖放 / 手牌 / 弃牌堆里。");

            CharacterCardDef def = state.Catalog.Character(CardId);
            if (def == null) return CommandResult.Fail($"角色牌 {CardId} 不存在。");

            bool persistent = Effect == CharacterEffectKind.Tactics
                              && def.PersistentTactic != PersistentTacticKind.None;
            if (def.OpsOf(Effect).Count == 0 && !persistent)
                return CommandResult.Fail($"「{def.NameOf(Effect)}」没有可发动的效果。");

            return CommandResult.Success;
        }

        public void Apply(GameState state)
        {
            CharacterCardDef def = state.Catalog.Character(CardId);
            IReadOnlyList<RewardOp> ops = def.OpsOf(Effect);

            PersistentTacticState made = null;
            if (Effect == CharacterEffectKind.Tactics
                && def.PersistentTactic != PersistentTacticKind.None
                && state.FindPersistentTactic(ActorSeat, def.PersistentTactic) == null)
            {
                made = new PersistentTacticState
                {
                    CardId = CardId,
                    Seat = ActorSeat,
                    Kind = def.PersistentTactic,
                };
                state.PersistentTactics.Add(made);
            }

            Report = EffectResolver.Execute(state, ActorSeat, ops, choices,
                EnterpriseEffectExtension.Instance);

            if (made != null) PersistentTacticRules.DiscardIfEmpty(state, made);
        }
    }

    /// <summary>开发者命令共用的准入判断。</summary>
    internal static class DeveloperRules
    {
        public static bool Enabled(GameState state) => state != null && state.DeveloperMode;
    }
}
