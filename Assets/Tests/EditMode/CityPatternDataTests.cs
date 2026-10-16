using System.Collections.Generic;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>
    /// 城市样式牌牌面数据的录入校验（基础版 Doc/RuleSpec.md §10.2；扩展 Doc/EnterpriseExpansion.md §2 / §3.1）。
    /// 共 12 张：基础 I 级 4 + II 级 2、扩展 I 级 4 + II 级 2。
    /// 牌面数值抄错时这里先亮红灯，免得进了 UI 才发现。
    /// </summary>
    public sealed class CityPatternDataTests
    {
        // 牌面切片 card_cp_lv1.jpg / card_cp_lv2.jpg 的读数（I 级在前、II 级在后）。
        private static readonly int[] BaseScores = { 2, 3, 3, 2, 6, 7 };
        private static readonly int[] BaseLevels = { 1, 1, 1, 1, 2, 2 };
        private static readonly int[] EnterpriseScores = { 5, 2, 3, 3, 6, 7 };
        private static readonly int[] EnterpriseLevels = { 1, 1, 1, 1, 2, 2 };

        [Test]
        public void 数据_基础版六张牌的等级与分数()
        {
            CityPatternCardDef[] cards = Resolve(CardIds.CityPatternBase, BaseScores.Length);

            for (int i = 0; i < cards.Length; i++)
            {
                Assert.AreEqual(BaseLevels[i], cards[i].Level, $"牌 {cards[i].Id} 的样式等级。");
                Assert.AreEqual(BaseScores[i], cards[i].Score, $"牌 {cards[i].Id} 的宣告奖励分数。");
            }
        }

        [Test]
        public void 数据_企业扩展六张牌的等级与分数()
        {
            CityPatternCardDef[] cards = Resolve(CardIds.CityPatternEnterpriseBase, EnterpriseScores.Length);

            for (int i = 0; i < cards.Length; i++)
            {
                Assert.AreEqual(EnterpriseLevels[i], cards[i].Level, $"牌 {cards[i].Id} 的样式等级。");
                Assert.AreEqual(EnterpriseScores[i], cards[i].Score, $"牌 {cards[i].Id} 的宣告奖励分数。");
            }
        }

        [Test]
        public void 数据_十二张牌都有牌名与非空宣告条件()
        {
            foreach (CityPatternCardDef card in AllCards())
            {
                Assert.IsFalse(string.IsNullOrEmpty(card.Name), $"牌 {card.Id} 应当有牌名。");
                Assert.IsTrue(card.HasCondition, $"牌 {card.Id} 应当已录入宣告条件。");
                Assert.Greater(card.Condition.Cells.Count, 0, $"牌 {card.Id} 的条件不能一个格子都没有。");
            }
        }

        [Test]
        public void 数据_每张牌的格阵落在城市面板范围内_且没有重复坐标()
        {
            // 城市面板是 3 行 × 4 列（§10.2）；条件自己的坐标超出面板就永远摆不出来。
            foreach (CityPatternCardDef card in AllCards())
            {
                var seen = new HashSet<string>();
                foreach (PatternCell cell in card.Condition.Cells)
                {
                    Assert.GreaterOrEqual(cell.Row, 0, $"牌 {card.Id} 有负行号。");
                    Assert.Less(cell.Row, CityPanelQuery.Rows, $"牌 {card.Id} 的行号超出面板高度。");
                    Assert.GreaterOrEqual(cell.Column, 0, $"牌 {card.Id} 有负列号。");
                    Assert.Less(cell.Column, CityPanelQuery.Columns, $"牌 {card.Id} 的列号超出面板宽度。");

                    Assert.IsTrue(seen.Add($"{cell.Row},{cell.Column}"),
                        $"牌 {card.Id} 的格阵里 ({cell.Row},{cell.Column}) 出现了不止一次。");
                }
            }
        }

        [Test]
        public void 数据_斜分格_i级两张各有一个两色格()
        {
            // 牌面读数：500 = 蓝/黄斜分 + 红；503 = 蓝/红斜分 + 黄。其余牌都是单色格。
            Assert.AreEqual(1, MultiColorCellCount(CardIds.CityPatternBase), "牌 500 应当只有一个斜分格。");
            Assert.AreEqual(1, MultiColorCellCount(CardIds.CityPatternBase + 3), "牌 503 应当只有一个斜分格。");

            foreach (CityPatternCardDef card in AllCards())
            {
                if (card.Id == CardIds.CityPatternBase || card.Id == CardIds.CityPatternBase + 3) continue;
                Assert.AreEqual(0, MultiColorCellCount(card.Id), $"牌 {card.Id} 的牌面读数里没有斜分格。");
            }
        }

        // —— 工具 ——

        private static IEnumerable<CityPatternCardDef> AllCards()
        {
            foreach (CityPatternCardDef card in Resolve(CardIds.CityPatternBase, 6)) yield return card;
            foreach (CityPatternCardDef card in Resolve(CardIds.CityPatternEnterpriseBase, 6)) yield return card;
        }

        private static CityPatternCardDef[] Resolve(int firstId, int count)
        {
            var cards = new CityPatternCardDef[count];
            for (int i = 0; i < count; i++)
                cards[i] = PlaceholderCardCatalog.Default.CityPattern(firstId + i);
            return cards;
        }

        /// <summary>这张牌的条件里有几个格子允许两种以上颜色（即牌面上画成斜分的格子）。</summary>
        private static int MultiColorCellCount(int patternId)
        {
            int count = 0;
            foreach (PatternCell cell in PlaceholderCardCatalog.Default.CityPattern(patternId).Condition.Cells)
                if (cell.Colors.Count > 1) count++;
            return count;
        }
    }
}
