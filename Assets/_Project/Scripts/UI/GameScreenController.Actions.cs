using System.Collections.Generic;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Game;
using UnityEngine;

namespace NomadCity.UI
{
    /// <summary>
    /// 「行动事件」框架：把卡牌效果授予的行动（探索 / 建设 / 移除影响力……）交给界面走**同一套**
    /// 完整流程（选点、路线、事件卡、付费、落点、结算确认）。
    ///
    /// 分工：
    /// - 引擎只登记挂起行动（<see cref="PendingAction"/>，含次数与限制），并把它们写进快照；
    /// - 界面发现「轮到自己 + 还有挂起行动」就接管，按种类切进对应的成熟模式；
    /// - 行动完成时对应的命令（ExploreInfluenceCommand / BuildFacilityCommand / RemoveInfluenceCommand）
    ///   会消费挂起记录，这里再 <see cref="CompleteAction"/> 驱动下一条。
    ///
    /// 效果给的行动必须执行：界面上没有「取消这次行动」的出口，只有「没有合法目标」时由引擎侧自动跳过。
    /// </summary>
    public sealed partial class GameScreenController
    {
        /// <summary>当前正在由界面驱动的行动事件（null = 普通行动，不来自卡牌效果）。</summary>
        private sealed class ActionRequest
        {
            public int Seat;
            public int PendingActionId;
            public PendingActionKind Kind;
            public PendingActionLimits Limits;
            public string Source;
        }

        private ActionRequest activeRequest;

        /// <summary>当前正由界面驱动的「探索」挂起行动 id（0 = 普通主要行动）。</summary>
        private int explorePendingId;

        /// <summary>当前正由界面驱动的「建设」挂起行动 id（0 = 普通主要行动）。</summary>
        private int buildPendingId;

        /// <summary>正在走的「城市移动」行动事件（0 = 操作区的普通城市移动）。</summary>
        private int cityMovePendingId;

        /// <summary>这次城市移动是否免费（效果授予且限制 Free）。</summary>
        private bool cityMoveFree;

        /// <summary>刚走完一条行动事件：下一帧再接管下一条，避开正在拆掉的弹窗 / 会话。</summary>
        private int pendingResumeFrame = -1;

        /// <summary>已经提醒过「在排队等」的那条挂起行动 id：同一条只提示一次，避免每帧刷状态行。</summary>
        private int pendingNoticeId = -1;

        /// <summary>卡牌效果授予的探索候选目标（用于地图资源点高亮）。</summary>
        private readonly List<int> exploreGrantedCandidates = new();

        /// <summary>当前「移除影响力」可以点的格子。</summary>
        private readonly List<InfluenceSlotRef> removeCandidates = new();

        /// <summary>这次行动属于谁：卡牌效果授予时以挂起记录为准，否则是当前行动玩家。</summary>
        private int ActionSeat => activeRequest != null ? activeRequest.Seat : engine.State.ActiveSeat;

        /// <summary>这次探索的限制（没有挂起记录就是不限）。</summary>
        private PendingActionLimits ExploreLimits() => PendingActionLimitsOf(explorePendingId);

        /// <summary>这次建设的限制（没有挂起记录就是不限）。</summary>
        private PendingActionLimits BuildLimits() => PendingActionLimitsOf(buildPendingId);

        private PendingActionLimits PendingActionLimitsOf(int pendingActionId)
        {
            PendingAction pending = engine != null && pendingActionId > 0
                ? engine.State.FindPending(pendingActionId)
                : null;
            return pending != null ? pending.Limits : PendingActionLimits.Unlimited;
        }

        /// <summary>挂起行动对应的界面模式。</summary>
        private static ActionMode ModeOf(PendingActionKind kind) => kind switch
        {
            PendingActionKind.Explore => ActionMode.Explore,
            PendingActionKind.Build => ActionMode.Build,
            PendingActionKind.RemoveInfluence => ActionMode.RemoveInfluence,
            PendingActionKind.CityMove => ActionMode.MoveCity,
            PendingActionKind.RelocateInfluence => ActionMode.Relocate,
            _ => ActionMode.None,
        };

        /// <summary>
        /// 每帧检查：轮到自己、界面空闲、还有卡牌效果授予的行动没做完，就接管它。
        /// 引擎只在自己行动阶段登记挂起行动，所以这里不用再判断阶段以外的场合。
        /// </summary>
        private void TryBeginPendingAction()
        {
            if (engine == null || engine.State == null) return;

            if (activeRequest != null)
            {
                // 中途被「关闭」丢掉了模式：重新接管同一件事，保证效果给的行动不会半途而废。
                if (mode == ActionMode.None && Time.frameCount > pendingResumeFrame) BeginAction(activeRequest);
                return;
            }

            if (Time.frameCount < pendingResumeFrame) return;

            // 联机：上一条命令还在等主机回放，别拿同一份旧状态再驱动一遍行动事件。
            if (onlineClient && onlineSubmitPending) return;

            GameState state = engine.State;
            PendingAction waiting = FirstPending(state);

            // 有行动在等、但此刻接不了手：说清楚为什么，别让界面看起来像卡死。
            if (state.Phase != TurnPhase.PlayerActions)
            {
                if (waiting != null) AnnounceWaiting(waiting, "要等到玩家行动阶段才会继续");
                return;
            }

            if (lockToLocalSeat && state.ActiveSeat != localSeat)
            {
                if (waiting != null) AnnounceWaiting(waiting, "要轮到你操作时才会继续");
                return;
            }

            if (mode != ActionMode.None) return;

            // 还有别的流程在跑（事件牌 / 小选择窗 / 确认窗 / 各类效果会话 / 弃牌窗）：让它们先走完。
            if (choicePopup != null && choicePopup.IsVisible) return;
            if (confirmWindow != null && confirmWindow.IsVisible) return;
            if (eventCardPanel != null && eventCardPanel.IsVisible) return;
            if (eventSession != null || characterSession != null || specialSession != null
                || enterpriseSession != null || buildSession != null || cleanupSession != null) return;
            if (supplyDiscardMode || pendingEventNodeId >= 0) return;

            PendingAction pending = FirstPending(state);
            if (pending == null) return;

            if (!PendingActionRules.HasAnyLegalTarget(state, pending.Seat, pending.Kind, pending.Limits))
            {
                AutoAbandon(pending);
                return;
            }

            BeginAction(new ActionRequest
            {
                Seat = pending.Seat,
                PendingActionId = pending.Id,
                Kind = pending.Kind,
                Limits = pending.Limits,
                Source = string.IsNullOrEmpty(pending.SourceLabel) ? "卡牌效果" : pending.SourceLabel,
            });
        }

        /// <summary>
        /// 有挂起行动但此刻接不了手（阶段 / 座位不对）：在状态行里说一声。
        /// 同一条行动只提醒一次，避免每帧刷屏。
        /// </summary>
        private void AnnounceWaiting(PendingAction pending, string reason)
        {
            if (pendingNoticeId == pending.Id) return;
            pendingNoticeId = pending.Id;
            SetStatus($"「{pending.Label}」{reason}（来源：{pending.SourceLabel}）。");
        }

        /// <summary>按挂起行动的种类切进对应的成熟流程（探索 / 建设 / 移除影响力）。</summary>
        private void BeginAction(ActionRequest request)
        {
            if (request == null || engine == null) return;

            GameState state = engine.State;
            PendingAction pending = state.FindPending(request.PendingActionId);
            string remain = pending != null && pending.Remaining > 1 ? $"（剩余 {pending.Remaining} 次）" : string.Empty;
            string source = string.IsNullOrEmpty(request.Source) ? "卡牌效果" : request.Source;

            activeRequest = request;

            switch (request.Kind)
            {
                case PendingActionKind.Explore:
                    explorePendingId = request.PendingActionId;
                    exploreGrantedCandidates.Clear();
                    exploreGrantedCandidates.AddRange(
                        PendingActionRules.ExploreCandidates(state, request.Seat, request.Limits));
                    SetMode(ActionMode.Explore);
                    SetStatus($"[{source}] 执行 1 次探索{remain}：请点一个高亮的未探索资源点作为目标地点。");
                    break;

                case PendingActionKind.Build:
                    buildPendingId = request.PendingActionId;
                    buildSeat = request.Seat;
                    SetMode(ActionMode.Build);
                    SetStatus($"[{source}] 建设 1 次{remain}：请从设施供应区选一张符合限制的牌。");
                    break;

                case PendingActionKind.RemoveInfluence:
                    SetMode(ActionMode.RemoveInfluence);
                    SetStatus($"[{source}] 移除 1 个影响力{remain}：请点一个高亮的影响力格。");
                    break;

                case PendingActionKind.CityMove:
                    cityMovePendingId = request.PendingActionId;
                    cityMoveFree = request.Limits.Free;
                    SetMode(ActionMode.MoveCity);
                    SetStatus($"[{source}] 进行 1 次城市移动{remain}：" +
                              $"请点一个相邻的资源点{(cityMoveFree ? "（免费）" : "（需 3 源石）")}。");
                    break;

                case PendingActionKind.RelocateInfluence:
                    SetMode(ActionMode.Relocate);
                    SetStatus($"[{source}] 进行 1 次调度{remain}：先点自己的一个影响力，再点目标空格。");
                    break;

                default:
                    activeRequest = null;
                    AutoAbandon(pending);
                    break;
            }

            // 开场先把「分几步、每步点什么」讲清楚，点「开始」再进入选点。
            if (activeRequest != null)
                ShowActionIntro(request, remain, source);
        }

        /// <summary>
        /// 行动事件的开场提示：来源 + 还要做几次 + 分步指引。
        /// 效果授予的行动按既定规则必须执行，所以只有「开始」一颗按钮（中途的取消由各步骤自己的确认窗负责）。
        /// </summary>
        private void ShowActionIntro(ActionRequest request, string remain, string source)
        {
            string steps = request.Kind switch
            {
                PendingActionKind.Explore =>
                    "步骤：① 点一个高亮资源点作为目标地点；② 点航道空格增删默认路线；③ 再点目标点，在确认窗里执行。",
                PendingActionKind.Build =>
                    "步骤：① 在设施供应区选一张牌；② 选一种付费方式；③ 点城市面板空位；④ 确认建造，落地后再结算入场效果。",
                PendingActionKind.RemoveInfluence =>
                    "步骤：① 点地图上高亮的影响力格；② 在确认窗里点「确定移除」。",
                PendingActionKind.CityMove =>
                    "步骤：① 点一个高亮的相邻资源点；② 在确认窗里点「确定移动」，之后按提示翻牌、在出发点放影响力。",
                PendingActionKind.RelocateInfluence =>
                    "步骤：① 点自己的一个影响力作为起点；② 点目标空格排一步（至多 2 步）；③ 在确认窗里点「确定执行」。",
                _ => "按提示选好目标后，在确认窗里执行。",
            };

            string title = $"[{source}] {PendingAction.LabelOf(request.Kind)}{remain}";

            ConfirmWindow window = EnsureConfirmWindow();
            if (window != null)
            {
                window.Show(title, steps, new[] { "开始" }, _ => { }, allowCancel: false);
                return;
            }

            if (choicePopup == null) return;

            choicePopup.Show(
                $"{title}\n{steps}",
                new[] { "开始" }, _ => { }, allowCancel: false);
        }

        /// <summary>一条行动事件走完：清掉暂存、下一帧再接管下一条。</summary>
        private void CompleteAction()
        {
            activeRequest = null;
            explorePendingId = 0;
            buildPendingId = 0;
            cityMovePendingId = 0;
            cityMoveFree = false;
            exploreGrantedCandidates.Clear();
            removeCandidates.Clear();
            pendingResumeFrame = Time.frameCount + 1;
            pendingNoticeId = -1;

            if (renderer != null)
            {
                renderer.SetHighlightedNodes(null);
                renderer.SetHighlightedSlots(null);
            }
        }

        /// <summary>没有合法目标：自动放弃，避免玩家被一条做不了的行动卡死。</summary>
        private void AutoAbandon(PendingAction pending)
        {
            pendingResumeFrame = Time.frameCount + 1;
            if (pending == null) return;

            if (Dispatch(new AbandonPendingActionCommand(pending.Seat, pending.Id)))
                SetStatus($"「{pending.Label}」没有可以执行的目标，已自动跳过。");
        }

        /// <summary>
        /// 找一条该由界面接管的挂起行动：优先当前行动座位；
        /// 找不到就退回「本机可操作座位」的那一条 —— 开发者模式的指定建设会把行动挂给非当前座位，
        /// 单人热座更是一个人操作所有座位，只认 ActiveSeat 会让这类行动永远没人接管（界面看起来就是卡住了）。
        /// </summary>
        private PendingAction FirstPending(GameState state)
        {
            PendingAction fallback = null;

            foreach (PendingAction pending in state.PendingActions)
            {
                if (pending.Seat == state.ActiveSeat) return pending;

                if (fallback == null && (!lockToLocalSeat || pending.Seat == localSeat)) fallback = pending;
            }

            return fallback;
        }

        /// <summary>「移除影响力」选格：只接受本次候选里的格子，确认后才派发命令。</summary>
        private void HandleRemoveInfluence(MapHit hit, GameState state)
        {
            if (activeRequest == null || activeRequest.Kind != PendingActionKind.RemoveInfluence) return;

            if (!hit.IsSlot)
            {
                SetStatus("请点地图上高亮的影响力格（资源点或航道上的圆形标记）。");
                return;
            }

            InfluenceSlotRef slot = hit.Slot;
            if (!removeCandidates.Contains(slot))
            {
                SetStatus("这个格子不在本次「移除影响力」的可选范围里。");
                return;
            }

            int owner = state.Map.OccupantOf(slot);
            if (owner < 0) return;

            string where = slot.Kind == InfluenceSlotKind.Node
                ? state.Map.Node(slot.TargetId).Code
                : $"航道 {state.Map.RouteLabel(slot.TargetId)}";
            string who = owner == ActionSeat && lockToLocalSeat ? "你" : SeatLabel(owner);

            int seat = ActionSeat;
            int pendingId = activeRequest.PendingActionId;

            ConfirmAction($"移除{who}在 {where} 的 1 个影响力？", () =>
            {
                // 被移除方有「为了大家」时让他自己选：标记放回供应堆，还是放到那张牌上。
                if ((choicePopup != null || EnsureConfirmWindow() != null) &&
                    state.FindPersistentTactic(owner, PersistentTacticKind.ForEveryone) != null)
                {
                    ShowChoiceWindow("「为了大家」",
                        $"移除{who}在 {where} 的影响力后，这个标记怎么处理？",
                        new[] { "放回标记供应堆", "放到「为了大家」牌上" }, pick =>
                        {
                            DispatchRemoveInfluence(seat, slot, pendingId, pick < 0 ? 0 : pick);
                        });
                    return;
                }

                DispatchRemoveInfluence(seat, slot, pendingId, 0);
            }, "确定移除");
        }

        /// <summary>派发移除影响力命令并收尾；<paramref name="forEveryonePick"/> 见「为了大家」。</summary>
        private void DispatchRemoveInfluence(int seat, InfluenceSlotRef slot, int pendingId, int forEveryonePick)
        {
            var choices = new RecordedChoices(new[] { forEveryonePick }, strict: false);
            var command = new RemoveInfluenceCommand(seat, slot, pendingId, choices);
            if (!Dispatch(command))
            {
                SetStatus("移除影响力失败，请重新选一个目标。");
                return;
            }

            SetMode(ActionMode.None);
            CompleteAction();
        }

    }
}
