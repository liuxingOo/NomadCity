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
    /// 右侧每名玩家一块状态栏：企业家圆形头像、压在头像底边的昵称，以及两排「图标 + 数字」：
    /// 第一排 3 组（得分 / 金券 / 标记），第二排 4 组（源岩 / 源石 / 异铁 / 至纯源石）；
    /// 图标是场景里的静态图片，这里只往数字文本框里写值。
    /// 手牌与盖放的角色牌属于私有信息，不在这里显示。
    /// </summary>
    public sealed class GameHud : MonoBehaviour
    {
        [Tooltip("顶栏：回合 · 阶段 · 当前行动座位")]
        [SerializeField] private TMP_Text turnText;

        [Tooltip("最近一次命令的结果或失败原因")]
        [SerializeField] private TMP_Text statusText;

        [Tooltip("状态栏槽位：数组顺序即从上到下的显示顺序，运行时按本局行动顺序填写，不要在编辑器里把某一行绑给某个颜色")]
        [SerializeField] private GameHudRow[] rows = Array.Empty<GameHudRow>();

        [Tooltip("圆形头像：按 PlayerColor 顺序（绿 / 黄 / 红 / 蓝）")]
        [SerializeField] private Sprite[] avatarSprites = Array.Empty<Sprite>();

        [Tooltip("玩家标记图标：按 PlayerColor 顺序（绿 / 黄 / 红 / 蓝），画在「玩家标记」那格图标上")]
        [SerializeField] private Sprite[] playerMarkerSprites = new Sprite[4];

        /// <summary>
        /// 点了某一行左侧的「查看」小按钮：参数是这一行对应的座位号（不是行号）。
        /// 由 GameScreenController 接住，弹该玩家的城市面板。
        /// </summary>
        public event Action<int> SeatInspectRequested;

        private GameEngine engine;

        /// <summary>各座位的玩家昵称（按座位号）；缺省时回退成「玩家 N」。</summary>
        private string[] playerNames = Array.Empty<string>();

        /// <summary>
        /// 每行左侧的小按钮各绑自己那一行。按钮只记「行号」，点击时才换算成座位号 ——
        /// 行动顺序每局随机、四行的座位会变，缓存座位会点错人。
        /// </summary>
        private void Awake()
        {
            for (int i = 0; i < rows.Length; i++)
            {
                GameHudRow row = rows[i];
                if (row == null || row.Inspect == null) continue;

                int index = i;   // 闭包捕获：一个按钮绑一行
                row.Inspect.onClick.AddListener(() => OnInspectClicked(index));
            }
        }

        private void OnInspectClicked(int index)
        {
            if (engine == null) return;

            int seat = SeatAt(engine.State, index);
            if (seat < 0) return;

            SeatInspectRequested?.Invoke(seat);
        }

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

            // 行是从上到下的显示槽位，不是座位号：第 i 行显示本局行动顺序里的第 i 个人。
            // 行动顺序每局随机排定（GameState.SeatOrder），所以谁出现在哪一行由引擎决定，
            // 编辑器里四行保持完全一样即可，运行时再按玩家的颜色 / 企业家换底板与头像。
            for (int i = 0; i < rows.Length; i++)
            {
                GameHudRow row = rows[i];
                if (row == null) continue;

                int seat = SeatAt(state, i);
                bool used = seat >= 0;

                // 空槽位整行关掉：图标是场景装饰，代码只认识数值文本，只关文本会把图标留在屏幕上。
                if (row.Root != null) row.Root.SetActive(used);
                else SetRowPartsActive(row, used);
                if (!used) continue;

                PlayerState player = state.Players[seat];

                // 底板现在是场景里的占位色块（StatusBar 那套图已作废），这里不再换图、也不再改颜色，
                // 免得分不清哪个是生成器摆好的占位。头像仍按企业家换图（绿 / 黄 / 红 / 蓝）。
                if (row.Avatar != null)
                {
                    Sprite face = ColorSprite(avatarSprites, player.Color);
                    if (face != null) row.Avatar.sprite = face;
                }

                // 「玩家标记」那格的图标跟着玩家颜色换 token 素材（绿 / 黄 / 红 / 蓝）。
                if (row.MarkerIcon != null)
                {
                    Sprite marker = ColorSprite(playerMarkerSprites, player.Color);
                    if (marker != null)
                    {
                        row.MarkerIcon.sprite = marker;
                        row.MarkerIcon.color = Color.white;
                    }
                }

                ApplyStats(state, row, player);
            }
        }

        /// <summary>没接整行根对象时的兜底：把代码认识的部件逐个开关（图标管不到）。</summary>
        private static void SetRowPartsActive(GameHudRow row, bool active)
        {
            Activate(row.Background, active);
            Activate(row.Avatar, active);
            Activate(row.Name, active);
            Activate(row.Inspect, active);

            if (row.Values == null) return;
            for (int i = 0; i < row.Values.Length; i++) Activate(row.Values[i], active);
        }

        private static void Activate(Component part, bool active)
        {
            if (part != null) part.gameObject.SetActive(active);
        }

        /// <summary>
        /// 第 <paramref name="index"/> 行对应本局行动顺序里的哪个座位。
        /// 行动顺序（<see cref="GameState.SeatOrder"/>）没设置或长度不对时退回自然顺序；
        /// 超出玩家人数的行返回 -1，表示这一行不用。
        /// </summary>
        private static int SeatAt(GameState state, int index)
        {
            if (index < 0 || index >= state.Players.Count) return -1;

            int[] order = state.SeatOrder;
            if (order != null && order.Length == state.Players.Count)
            {
                int seat = order[index];
                if (seat >= 0 && seat < state.Players.Count) return seat;
            }

            return index;
        }

        private static Sprite ColorSprite(Sprite[] sprites, PlayerColor color)
        {
            int index = (int)color;
            return sprites != null && index >= 0 && index < sprites.Length ? sprites[index] : null;
        }

        /// <summary>
        /// 填一行的数据：昵称压在头像圆框底边，其余数值按场景里图标的摆放顺序写进对应的数字文本框。
        /// 图标本身是场景里的静态图片，代码只负责把数字写对，不引图标资源。
        /// </summary>
        private void ApplyStats(GameState state, GameHudRow row, PlayerState player)
        {
            if (row.Name != null)
            {
                // 盖放 / 待弃牌的提示接在昵称后面：昵称那行字号很小，再单起一行会挤到图标区。
                row.Name.text = SeatName(state, player.SeatIndex) + PlacementTag(state, player);
            }

            ResourcePool r = player.Resources;

            // 下标必须与场景里图标的顺序一致，见 GameHudRow.Values 的 Tooltip。
            WriteValue(row, 0, player.Score);            // 得分
            WriteValue(row, 1, player.Vouchers);         // 金券
            WriteValue(row, 2, player.MarkersInSupply);  // 玩家标记
            WriteValue(row, 3, r.Orirock);               // 源岩
            WriteValue(row, 4, r.Originium);             // 源石
            WriteValue(row, 5, r.Oriron);                // 异铁
            WriteValue(row, 6, r.OriginitePrime);        // 至纯源石
        }

        private static void WriteValue(GameHudRow row, int index, int value)
        {
            if (row.Values == null || index < 0 || index >= row.Values.Length) return;

            TMP_Text text = row.Values[index];
            if (text != null) text.text = value.ToString();
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

    /// <summary>
    /// HUD 里的一名玩家：状态栏底板 + 圆形头像 + 压在头像底边的昵称 + 一排「图标 + 数字」的数值。
    /// 图标是场景里摆好的静态 Image（代码不引用，「得分 / 金券 / 标记 / 源岩 / 源石 / 异铁 / 至纯源石」按这个顺序排），
    /// 这里只接数值文本框。
    /// </summary>
    [Serializable]
    public sealed class GameHudRow
    {
        [Tooltip("整行根对象（就是 Row 自己）：空槽位时整行关掉，免得图标留在屏幕上")]
        public GameObject Root;

        [Tooltip("状态栏底板（占位色块，运行时不换图；要换美术就替换它的 Source Image）")]
        public Image Background;

        [Tooltip("左侧圆形头像（按玩家企业家换图）")]
        public Image Avatar;

        [Tooltip("昵称：压在头像圆框底边（上半截在圆内、下半截在状态栏左下角）")]
        public TMP_Text Name;

        [Tooltip("「玩家标记」那格的图标（按玩家颜色换 token 素材；由生成器接上 Stat2_Marker/Icon）")]
        public Image MarkerIcon;

        [Tooltip("行左侧的小「查看」按钮（眼睛图标）：点了弹这名玩家的城市面板（由生成器接上 Row/Inspect）")]
        public Button Inspect;

        [Tooltip("数值文本，顺序固定：0 得分 / 1 金券 / 2 玩家标记 / 3 源岩 / 4 源石 / 5 异铁 / 6 至纯源石")]
        public TMP_Text[] Values = Array.Empty<TMP_Text>();
    }
}
