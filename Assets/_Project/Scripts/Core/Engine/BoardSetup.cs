using System;
using System.Collections.Generic;
using NomadCity.Core.Model;
using NomadCity.Core.Rules;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 开局设置（规则规格 §4）：把公共版面、玩家版面、以及开局入场中的「发牌 / 设施供应区 / 初始资金」准备到位。
    /// 只做「摆好牌」，不推进回合（那是 <see cref="TurnFlow"/> 的职责）。
    /// 牌一律用 int id 表示，与卡面文案、美术预制体无关；洗牌用注入的 <see cref="Random"/>，
    /// 因此传入相同种子即可复现同一副牌（测试与联机对局都靠它）。
    /// </summary>
    public static class BoardSetup
    {
        /// <summary>设施供应区的张数（规则规格 §4.1 步 4）。</summary>
        public const int FacilitySupplySize = 6;

        /// <summary>
        /// 完整走一遍开局设置。firstPlayerSeat 是 §4.3 步 1 选出的起始玩家，
        /// 初始资金按「从起始玩家开始顺时针」的顺位发放（§4.3 步 3）。
        /// </summary>
        public static void Run(GameState state, ICardCatalog catalog, Random rng, int firstPlayerSeat)
        {
            PrepareEventDecks(state, catalog, rng);
            RevealCityPatterns(state, catalog, rng);
            PrepareFacilityBoard(state, catalog, rng);
            PrepareExtensionHubs(state, catalog);
            DealCharacterCards(state, catalog);
            AssignCoreCommandTowers(state, catalog);
            PlaceScoreStartMarkers(state);
            DealInitialFunds(state, firstPlayerSeat);

            // 企业扩展：洗混 8 张合作企业板，3／4 人局每人抽 2 张、2 人局每人抽 4 张
            // （正式选择由 ChooseInitialEnterpriseCommand 完成）。
            // 未启用时也要按人数复位一次，保证联机快照 / 各端状态的结构一致。
            if (state.Setup.IsEnterpriseEnabled) EnterpriseRules.Prepare(state, rng);
            else state.Enterprises.Reset(state.Players.Count);

            // 开局入场·步 2「决定初始资源点」是需要玩家逐位交互的步骤，不在这里预置：
            // 由 ChooseStartNodeCommand 在玩家点选绿色资源点时执行
            // （移动城市停靠 + 翻开绿色事件牌放上资源点指示物 + B-01 特例）。
        }

        /// <summary>
        /// 洗切三色事件牌堆，分别牌背朝上放好（§4.1 步 2）。
        /// 2／3 人局要把正面标有「4」的事件牌放回游戏盒（§4.1 步 2 / §12 / §13.2）：
        /// 那 4 张正好对应 3 人地图相较 4 人地图少掉的 G 区，留着会在三人面地图上无处可放。
        /// </summary>
        public static void PrepareEventDecks(GameState state, ICardCatalog catalog, Random rng)
        {
            bool enterprise = state.Setup.IsEnterpriseEnabled;
            bool fourPlayerMap = state.Players.Count >= 4;

            FillShuffled(state.EventDeckGreen,
                EventIdsFor(catalog, NodeColor.Green, enterprise, fourPlayerMap), rng);
            FillShuffled(state.EventDeckYellow,
                EventIdsFor(catalog, NodeColor.Yellow, enterprise, fourPlayerMap), rng);
            FillShuffled(state.EventDeckRed,
                EventIdsFor(catalog, NodeColor.Red, enterprise, fourPlayerMap), rng);
        }

        /// <summary>
        /// 某一颜色的事件牌 id 列表；四人面保留全部，2／3 人面剔除标有「4」的那几张（§12 / §13.2）。
        /// </summary>
        private static IReadOnlyList<int> EventIdsFor(ICardCatalog catalog, NodeColor color,
            bool enterprise, bool fourPlayerMap)
        {
            IReadOnlyList<int> all = catalog.EventDeckIds(color, enterprise);
            if (fourPlayerMap) return all;

            var kept = new List<int>(all.Count);
            foreach (int id in all)
            {
                if (catalog.Event(id).FourPlayerOnly) continue; // 放回游戏盒
                kept.Add(id);
            }
            return kept;
        }

        /// <summary>
        /// 把城市样式牌牌面朝上放到场上（§4.1 步 3）。
        /// 启用企业扩展时，基础 6 张 + 扩展 6 张共 12 张，按牌背标识分成 I / II 两个牌堆，
        /// 各堆洗混后抽 I 级 4 张、II 级 2 张（EnterpriseExpansion.md §3.1 第 2 条）。
        /// </summary>
        public static void RevealCityPatterns(GameState state, ICardCatalog catalog, Random rng)
        {
            state.CityPatternCards.Clear();
            state.CityPatternStates.Clear();

            var level1 = new List<int>();
            var level2 = new List<int>();
            foreach (int id in catalog.CityPatternIds(state.Setup.IsEnterpriseEnabled))
            {
                int level = catalog.CityPattern(id).Level;
                if (level == 1) level1.Add(id);
                else if (level == 2) level2.Add(id);
            }

            // 基础版每种等级的张数正好等于要抽的张数，不需要洗也不会变；只有启用扩展后才洗混。
            if (level1.Count > CardCounts.CityPatternLevel1) Shuffle(level1, rng);
            if (level2.Count > CardCounts.CityPatternLevel2) Shuffle(level2, rng);

            int take1 = level1.Count < CardCounts.CityPatternLevel1 ? level1.Count : CardCounts.CityPatternLevel1;
            int take2 = level2.Count < CardCounts.CityPatternLevel2 ? level2.Count : CardCounts.CityPatternLevel2;

            for (int i = 0; i < take1; i++) AddCityPattern(state, catalog, level1[i]);
            for (int i = 0; i < take2; i++) AddCityPattern(state, catalog, level2[i]);
        }

        private static void AddCityPattern(GameState state, ICardCatalog catalog, int id)
        {
            state.CityPatternCards.Add(id);
            state.CityPatternStates.Add(new CityPatternState(id, catalog.CityPattern(id).Level));
        }

        /// <summary>设施牌堆洗混，并翻开 6 张作为设施供应区（§4.1 步 4）。</summary>
        public static void PrepareFacilityBoard(GameState state, ICardCatalog catalog, Random rng)
        {
            state.FacilityDeck.Clear();
            state.FacilityDeck.AddRange(catalog.FacilityDeckIds(state.Setup.IsEnterpriseEnabled));
            Shuffle(state.FacilityDeck, rng);

            state.FacilitySupply.Clear();
            RefillFacilitySupply(state);
        }

        /// <summary>
        /// 把设施供应区补至 6 张（§6.4e 补充规则：供应区被取走后立刻从牌堆补牌）。
        /// 牌堆不足时能补多少补多少。
        /// </summary>
        public static void RefillFacilitySupply(GameState state)
        {
            while (state.FacilitySupply.Count < FacilitySupplySize && state.FacilityDeck.Count > 0)
            {
                state.FacilitySupply.Add(DrawTop(state.FacilityDeck));
            }
        }

        /// <summary>延伸枢纽（红 / 黄 / 蓝各 1 张）单独取出放在一旁（§4.1 步 4）。</summary>
        public static void PrepareExtensionHubs(GameState state, ICardCatalog catalog)
        {
            state.ExtensionHubSupply.Clear();
            state.ExtensionHubSupply.AddRange(catalog.ExtensionHubIds());
        }

        /// <summary>
        /// 每人拿取本色的初始角色牌作为手牌：基础版 5 张（§4.2 步 1）；
        /// 启用企业扩展时再加上山 / 玛恩纳，共 7 张（EnterpriseExpansion.md §3.2 / §4.2）。
        /// </summary>
        public static void DealCharacterCards(GameState state, ICardCatalog catalog)
        {
            bool enterprise = state.Setup.IsEnterpriseEnabled;
            int count = enterprise ? CardCounts.CharacterHandSizeWithExtension : CardCounts.CharacterRoles;

            foreach (PlayerState player in state.Players)
            {
                player.HandCharacterCards.Clear();
                player.DiscardCharacterCards.Clear();
                player.SetupDiscardDone = !enterprise;

                IReadOnlyList<int> ids = catalog.CharacterCardIds(player.Color);
                for (int i = 0; i < ids.Count && i < count; i++)
                    player.HandCharacterCards.Add(ids[i]);
            }
        }

        /// <summary>每人把本色的核心指挥塔放到自己的城市面板上（§4.2 步 2）。</summary>
        public static void AssignCoreCommandTowers(GameState state, ICardCatalog catalog)
        {
            foreach (PlayerState player in state.Players)
            {
                int towerId = catalog.CoreCommandTowerId(player.Color);
                player.BuiltFacilities.Add(towerId);

                // 指挥塔开局就摆好，占住面板上印着「核心指挥塔」的那一格（第 3 行中间），不参与建设选位。
                player.CityPanelSlots[PlayerState.CityPanelCoreSlot] = towerId;
            }
        }

        /// <summary>
        /// 按顺位发放初始资金（§4.3 步 3）：从起始玩家开始顺时针，各顺位金额由人数变体决定。
        /// 金券没有数量限制（可用替代品），发出去即可，不必从任何供应堆扣除。
        /// </summary>
        public static void DealInitialFunds(GameState state, int firstPlayerSeat)
        {
            PlayerCountVariant variant = PlayerCountVariant.For(state.Players.Count);
            int seat = firstPlayerSeat;

            for (int order = 0; order < state.Players.Count; order++)
            {
                PlayerState player = state.Players[seat];
                int vouchers = variant.InitialVouchers(order);
                // 企业扩展：每名玩家的初始资金额外 +5 金券（企业扩展设置第 4 步）。
                if (state.Setup.IsEnterpriseEnabled) vouchers += 5;
                player.Vouchers += vouchers;
                player.Score += variant.InitialBonusScore(order);
                seat = state.NextSeat(seat);
            }
        }

        /// <summary>
        /// §4.2 步 3：每名玩家从自己的供应堆拿 1 个玩家标记放到计分轨的「0」处（用于记录分数）。
        /// 这个标记在整局游戏里**永久占住计分轨**，不再回到供应堆 —— 所以每人可用的玩家标记是 29 个。
        /// </summary>
        public static void PlaceScoreStartMarkers(GameState state)
        {
            foreach (PlayerState player in state.Players)
                if (player.MarkersInSupply > 0) player.MarkersInSupply--;
        }

        private static void FillShuffled(List<int> target, IReadOnlyList<int> source, Random rng)
        {
            target.Clear();
            target.AddRange(source);
            Shuffle(target, rng);
        }

        /// <summary>从牌堆顶（列表头部）取一张。</summary>
        private static int DrawTop(List<int> deck)
        {
            int card = deck[0];
            deck.RemoveAt(0);
            return card;
        }

        /// <summary>Fisher–Yates 洗牌。</summary>
        private static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}