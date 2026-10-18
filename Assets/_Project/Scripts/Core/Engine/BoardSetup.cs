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
            RevealCityPatterns(state, catalog);
            PrepareFacilityBoard(state, catalog, rng);
            PrepareExtensionHubs(state, catalog);
            DealCharacterCards(state, catalog);
            AssignCoreCommandTowers(state, catalog);
            DealInitialFunds(state, firstPlayerSeat);

            // 开局入场·步 2「决定初始资源点」是需要玩家逐位交互的步骤，不在这里预置：
            // 由 ChooseStartNodeCommand 在玩家点选绿色资源点时执行
            // （移动城市停靠 + 翻开绿色事件牌放上资源点指示物 + B-01 特例）。
        }

        /// <summary>洗切三色事件牌堆，分别牌背朝上放好（§4.1 步 2）。</summary>
        public static void PrepareEventDecks(GameState state, ICardCatalog catalog, Random rng)
        {
            bool enterprise = state.Setup.IsEnterpriseEnabled;
            FillShuffled(state.EventDeckGreen, catalog.EventDeckIds(NodeColor.Green, enterprise), rng);
            FillShuffled(state.EventDeckYellow, catalog.EventDeckIds(NodeColor.Yellow, enterprise), rng);
            FillShuffled(state.EventDeckRed, catalog.EventDeckIds(NodeColor.Red, enterprise), rng);
        }

        /// <summary>把 6 张城市样式牌牌面朝上放到场上（§4.1 步 3）。</summary>
        public static void RevealCityPatterns(GameState state, ICardCatalog catalog)
        {
            state.CityPatternCards.Clear();
            state.CityPatternStates.Clear();

            foreach (int id in catalog.CityPatternIds())
            {
                state.CityPatternCards.Add(id);
                state.CityPatternStates.Add(new CityPatternState(id, catalog.CityPattern(id).Level));
            }
        }

        /// <summary>设施牌堆洗混，并翻开 6 张作为设施供应区（§4.1 步 4）。</summary>
        public static void PrepareFacilityBoard(GameState state, ICardCatalog catalog, Random rng)
        {
            state.FacilityDeck.Clear();
            state.FacilityDeck.AddRange(catalog.FacilityDeckIds());
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

        /// <summary>每人拿取本色的初始角色牌作为手牌（基础版 5 张，§4.2 步 1）。</summary>
        public static void DealCharacterCards(GameState state, ICardCatalog catalog)
        {
            foreach (PlayerState player in state.Players)
            {
                player.HandCharacterCards.Clear();
                player.DiscardCharacterCards.Clear();
                player.HandCharacterCards.AddRange(catalog.CharacterCardIds(player.Color));
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
                player.Vouchers += vouchers;
                player.Score += variant.InitialBonusScore(order);
                seat = state.NextSeat(seat);
            }
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