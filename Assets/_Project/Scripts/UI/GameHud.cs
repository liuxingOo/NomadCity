using System;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.UI
{
    /// <summary>
    /// 顶部信息栏：本回合进度 + 每名玩家的公开状态。
    /// 只显示公开信息（分数、资源、金券、供应堆标记、移动城市位置）；
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

        private GameEngine engine;

        /// <summary>各座位的玩家昵称（按座位号）；缺省时回退成「玩家 N」。</summary>
        private string[] playerNames = Array.Empty<string>();

        /// <summary>本机座位（-1 = 单机热座，不标「你」）。</summary>
        private int localSeat = -1;

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
                turnText.text = $"回合 {state.TurnNumber}/{TurnFlow.TotalTurns} · {StageLabel(state)}{RoundSuffix(state)} · 行动座位 {SeatName(state, state.ActiveSeat)}";

            for (int i = 0; i < rows.Length; i++)
            {
                GameHudRow row = rows[i];
                if (row == null) continue;

                bool used = i < state.Players.Count;
                if (row.Swatch != null) row.Swatch.gameObject.SetActive(used);
                if (row.Text != null) row.Text.gameObject.SetActive(used);
                if (!used) continue;

                PlayerState player = state.Players[i];
                if (row.Swatch != null) row.Swatch.color = MapPalette.Player(player.Color);
                if (row.Text != null) row.Text.text = Describe(state, player);
            }
        }

        private string Describe(GameState state, PlayerState player)
        {
            ResourcePool r = player.Resources;
            string city = player.MobileCityNodeId < 0
                ? "未停靠"
                : state.Map.Node(player.MobileCityNodeId).Code;

            // 行动顺位：SeatOrder[0] 是起始玩家（持有起始玩家标记）；旧快照没有顺序时按座位号回退。
            int orderIndex = Array.IndexOf(state.SeatOrder ?? Array.Empty<int>(), player.SeatIndex);
            string order = orderIndex >= 0
                ? $"顺位 {orderIndex + 1}{(orderIndex == 0 ? "（起始）" : string.Empty)}"
                : $"顺位 {player.SeatIndex + 1}";

            return $"{SeatName(state, player.SeatIndex)}  {EntrepreneurCatalog.NameOf(player.Color)} · {order}  " +
                   $"分 {player.Score} · 券 {player.Vouchers} · " +
                   $"岩 {r.Orirock} 石 {r.Originium} 铁 {r.Oriron} 纯 {r.OriginitePrime} · " +
                   $"标记 {player.MarkersInSupply} · 城市 {city}";
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
                           && ChooseStartNodeCommand.NextChooser(state) >= 0;

            return opening ? "开局入场：决定初始资源点" : PhaseName(state.Phase);
        }
    }

    /// <summary>HUD 里的一名玩家行：颜色块 + 信息文本。</summary>
    [Serializable]
    public sealed class GameHudRow
    {
        [Tooltip("玩家颜色块")]
        public Image Swatch;

        [Tooltip("该玩家的信息文本")]
        public TMP_Text Text;
    }
}
