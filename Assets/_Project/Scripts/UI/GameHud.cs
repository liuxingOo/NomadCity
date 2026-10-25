using System;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 顶部信息栏：本回合进度 + 每名玩家的公开状态。
    /// 右侧每名玩家一块状态栏：企业家圆形头像、昵称、得分、金券、四种资源与供应堆标记；
    /// 手牌与盖放的角色牌属于私有信息，不在这里显示。
    /// </summary>
    public sealed class GameHud : MonoBehaviour
    {
        [Tooltip("顶栏：回合 · 阶段 · 当前行动座位")]
        [SerializeField] private TMP_Text turnText;

        [Tooltip("最近一次命令的结果或失败原因")]
        [SerializeField] private TMP_Text statusText;

        [Tooltip("每个座位一行，数组顺序即座位号")]
        [SerializeField] private GameHudRow[] rows = Array.Empty<GameHudRow>();

        [Tooltip("状态栏底板：按 PlayerColor 顺序（绿 / 黄 / 红 / 蓝）")]
        [SerializeField] private Sprite[] statusBarSprites = Array.Empty<Sprite>();

        [Tooltip("圆形头像：按 PlayerColor 顺序（绿 / 黄 / 红 / 蓝）")]
        [SerializeField] private Sprite[] avatarSprites = Array.Empty<Sprite>();

        private GameEngine engine;

        /// <summary>各座位的玩家昵称（按座位号）；缺省时回退成「玩家 N」。</summary>
        private string[] playerNames = Array.Empty<string>();

        /// <summary>本机座位（-1 = 单机热座，不标「你」）。</summary>
        private int localSeat = -1;

        /// <summary>单机热座下的实际操作者座位（-1 = 直接用引擎的行动座位）。</summary>
        private int operatorSeat = -1;

        /// <summary>绑定引擎并刷新一次。之后每次状态变化由 GameScreenController 调 <see cref="Refresh"/>。</summary>
        public void Bind(GameEngine engine)
        {
            this.engine = engine;
            Refresh();
        }

        /// <summary>准备房间同步来的昵称，按座位号排列。</summary>
        public void SetPlayerNames(string[] names)
        {
            playerNames = names ?? Array.Empty<string>();
            Refresh();
        }

        /// <summary>本机座位；联机时用来在名字后面标「（你）」，免得分不清自己是哪一边。</summary>
        public void SetLocalSeat(int seat)
        {
            localSeat = seat;
            Refresh();
        }

        /// <summary>
        /// 单机热座：盖放 / 采集阶段引擎不轮转行动座位，顶栏要显示「实际正在操作的人」。
        /// 传 -1 表示跟随引擎的行动座位（联机一律这样）。
        /// </summary>
        public void SetOperatorSeat(int seat)
        {
            operatorSeat = seat;
        }

        /// <summary>写状态行。命令失败时把引擎给的原因原样透出。</summary>
        public void SetStatus(string text)
        {
            if (statusText != null) statusText.text = text ?? string.Empty;
        }

        public void Refresh()
        {
            if (engine == null) return;
            GameState state = engine.State;

            if (turnText != null)
            {
                turnText.text = $"回合 {state.TurnNumber}/{TurnFlow.TotalTurns} · {StageLabel(state)}{RoundSuffix(state)} · {OperatorLabel(state)}";
            }

            for (int i = 0; i < rows.Length; i++)
            {
                GameHudRow row = rows[i];
                if (row == null) continue;

                bool used = i < state.Players.Count;
                if (row.Background != null) row.Background.gameObject.SetActive(used);
                if (row.Avatar != null) row.Avatar.gameObject.SetActive(used);
                if (row.Text != null) row.Text.gameObject.SetActive(used);
                if (!used) continue;

                PlayerState player = state.Players[i];

                // 底板与头像都按玩家颜色换图（绿 / 黄 / 红 / 蓝），美术里每个企业家一套。
                if (row.Background != null)
                {
                    Sprite bar = ColorSprite(statusBarSprites, player.Color);
                    if (bar != null) row.Background.sprite = bar;
                    row.Background.color = Color.white;
                }

                if (row.Avatar != null)
                {
                    Sprite face = ColorSprite(avatarSprites, player.Color);
                    if (face != null) row.Avatar.sprite = face;
                }

                if (row.Text != null) row.Text.text = Describe(state, player);
            }
        }

        private static Sprite ColorSprite(Sprite[] sprites, PlayerColor color)
        {
            int index = (int)color;
            return sprites != null && index >= 0 && index < sprites.Length ? sprites[index] : null;
        }

        /// <summary>状态栏正文：昵称（带盖放状态）+ 得分 / 金券 / 标记 + 四种资源。</summary>
        private string Describe(GameState state, PlayerState player)
        {
            ResourcePool r = player.Resources;
            string tag = PlacementTag(state, player);

            return $"<b><size=17>{SeatName(state, player.SeatIndex)}</size></b>" +
                   (tag.Length > 0 ? $"<size=13>{tag}</size>" : string.Empty) + "\n" +
                   $"分 {player.Score} · 金券 {player.Vouchers} · 标记 {player.MarkersInSupply}\n" +
                   $"<size=14>源岩 {r.Orirock} · 源石 {r.Originium} · 异铁 {r.Oriron} · 至纯源石 {r.OriginitePrime}</size>";
        }

        /// <summary>
        /// 盖放阶段每名玩家后面的状态标识：盖放是所有人同时进行的，右侧状态栏直接标出「盖放中 / 已盖放」。
        /// 不在盖放阶段、或第 1 回合的开局入场还没走完时返回空串。
        /// </summary>
        private static string PlacementTag(GameState state, PlayerState player)
        {
            if (state.Phase != TurnPhase.CharacterPlacement) return string.Empty;

            // 第 1 回合要先逐个选初始资源点并结算翻出的事件牌，这之前还没轮到盖放。
            if (state.TurnNumber == 1 && ChooseStartNodeCommand.CurrentOperator(state) >= 0) return string.Empty;

            if (state.SupplyDiscardRemaining > 0)
                return player.SeatIndex == state.SupplyDiscardSeat ? " · 待弃牌" : string.Empty;

            return player.PlacedCharacterCard >= 0 ? " · 已盖放" : " · 盖放中";
        }

        /// <summary>
        /// 顶栏最后一段「谁在操作」。
        ///
        /// 引擎的 <see cref="GameState.ActiveSeat"/> 只在玩家行动阶段有意义：盖放阶段被固定为起始玩家，
        /// 采集 / 收尾阶段也没有「轮到某人」的概念。所以这里按阶段分开写，免得非行动阶段一直显示同一个名字。
        /// 单机热座由 <see cref="SetOperatorSeat"/> 传入实际点鼠标的人，优先级最高。
        /// </summary>
        private string OperatorLabel(GameState state)
        {
            if (operatorSeat >= 0 && operatorSeat < state.Players.Count)
                return operatorSeat != state.ActiveSeat
                    ? $"当前操作 {SeatName(state, operatorSeat)}"
                    : $"行动座位 {SeatName(state, operatorSeat)}";

            switch (state.Phase)
            {
                case TurnPhase.CharacterPlacement:
                {
                    // 开局入场按顺序一个个来：选完初始资源点还要把事件牌结算完，才算这个人的回合结束。
                    int chooser = ChooseStartNodeCommand.CurrentOperator(state);
                    if (chooser >= 0) return $"当前操作 {SeatName(state, chooser)}";
                    if (state.SupplyDiscardRemaining > 0 && state.SupplyDiscardSeat >= 0)
                        return $"当前操作 {SeatName(state, state.SupplyDiscardSeat)}";

                    // 联机时所有人在自己机器上同时盖放，显示本机玩家即可。
                    if (localSeat >= 0 && localSeat < state.Players.Count)
                        return $"盖放角色牌 {SeatName(state, localSeat)}";
                    return $"行动座位 {SeatName(state, state.ActiveSeat)}";
                }

                case TurnPhase.Harvest:
                    if (localSeat >= 0 && localSeat < state.Players.Count)
                        return $"采集中 {SeatName(state, localSeat)}";
                    return $"行动座位 {SeatName(state, state.ActiveSeat)}";

                case TurnPhase.Cleanup:
                    // 收尾是引擎自动结算，没有操作者；这里只报阶段，不再挂一个人名。
                    return "收尾结算";

                default:
                    return $"行动座位 {SeatName(state, state.ActiveSeat)}";
            }
        }

        /// <summary>座位名：优先用准备房间同步来的昵称；没有时回退成「玩家 1..N」；座位号非法（如 -1）时显示「—」。</summary>
        private string SeatName(GameState state, int seat)
        {
            if (seat < 0 || seat >= state.Players.Count) return "—";

            string name = seat < playerNames.Length && !string.IsNullOrWhiteSpace(playerNames[seat])
                ? playerNames[seat]
                : $"玩家 {seat + 1}";

            return seat == localSeat ? name + "（你）" : name;
        }

        /// <summary>
        /// 玩家行动阶段显示「（第 x/2 轮）」—— 一个回合里要走两轮玩家行动（§5 / §6.1），
        /// 只有把这一轮走完、进到下一轮，画面上才看得出来，所以标在这里。
        /// 其余阶段没有「轮」的概念，不显示。
        /// </summary>
        private static string RoundSuffix(GameState state) =>
            state.Phase == TurnPhase.PlayerActions
                ? $"（第 {state.ActionRound}/{TurnFlow.ActionRoundsPerTurn} 轮）"
                : string.Empty;

        private static string PhaseName(TurnPhase phase) => phase switch
        {
            TurnPhase.CharacterPlacement => "角色牌盖放",
            TurnPhase.PlayerActions => "玩家行动",
            TurnPhase.Harvest => "采集",
            _ => "收尾",
        };

        /// <summary>
        /// 当前该显示什么「阶段」。界面顶栏与行动记录共用这一份，免得两处写得不一样。
        ///
        /// 第 1 回合开头虽然按引擎的流程算在「角色牌盖放」里，但规则书里这一段其实是
        /// §4.3 开局入场（决定初始资源点），角色牌盖放是回合开始后才做的事（§5 阶段 1）——
        /// 所以只要还有人在选初始资源点，就显示「开局入场」，不要写成「角色牌盖放」。
        /// </summary>
        public static string StageLabel(GameState state)
        {
            if (state == null) return string.Empty;

            bool opening = state.TurnNumber == 1
                           && state.Phase == TurnPhase.CharacterPlacement
                           && ChooseStartNodeCommand.CurrentOperator(state) >= 0;

            return opening ? "开局入场" : PhaseName(state.Phase);
        }
    }

    /// <summary>HUD 里的一名玩家：状态栏底板 + 圆形头像 + 信息文本。</summary>
    [Serializable]
    public sealed class GameHudRow
    {
        [Tooltip("状态栏底板（按玩家颜色换图）")]
        public Image Background;

        [Tooltip("左侧圆形头像（按玩家企业家换图）")]
        public Image Avatar;

        [Tooltip("昵称 / 得分 / 金券 / 资源 / 标记")]
        public TMP_Text Text;
    }
}
