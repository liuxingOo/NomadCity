using System;

namespace NomadCity.Core.Model
{
    /// <summary>
    /// 卡牌 / 企业 / 城市样式效果授予的「行动事件」种类。
    /// 效果不再在引擎里一次性跑完，而是挂起一条待执行记录，由界面用完整行动流程接管
    /// （Doc/EnterpriseExpansion.md 与 .trae/documents/行动事件统一重构.md）。
    /// </summary>
    public enum PendingActionKind
    {
        None = 0,

        /// <summary>执行 1 次探索：选资源点 → 付路费 → 翻事件牌 → 选选项 → 放影响力。</summary>
        Explore = 1,

        /// <summary>进行 1 次建设：选设施牌 → 选面板格 → 付费。</summary>
        Build = 2,

        /// <summary>移除 1 个影响力：点影响力格 → 确认。</summary>
        RemoveInfluence = 3,

        /// <summary>进行 1 次城市移动：选相邻资源点 → 付源石（或免费）→ 翻牌结算 → 出发点放影响力。</summary>
        CityMove = 4,

        /// <summary>进行 1 次调度：把自己的影响力挪到别的空格，一次至多 2 步。</summary>
        RelocateInfluence = 5,
    }

    /// <summary>「城市移动」的落点限制。</summary>
    public enum PendingNodeScope
    {
        /// <summary>任何相邻的已开放资源点。</summary>
        Any = 0,

        /// <summary>只限「有自己的影响力」的资源点（极境·计谋）。</summary>
        OwnInfluence = 1,
    }

    /// <summary>「移除影响力」的候选范围。</summary>
    public enum PendingInfluenceScope
    {
        /// <summary>任何玩家的影响力都可以移除。</summary>
        Any = 0,

        /// <summary>只能移除自己的影响力。</summary>
        Own = 1,

        /// <summary>只能移除其他玩家的影响力。</summary>
        Others = 2,
    }

    /// <summary>行动事件携带的限制。默认值用 <see cref="Unlimited"/> 构造，不要直接 new。</summary>
    public struct PendingActionLimits
    {
        /// <summary>免费：探索路费为 0，建设不付费用。</summary>
        public bool Free;

        /// <summary>建设时最多支付的设施金券费用；-1 表示不限。</summary>
        public int MaxVoucherSpend;

        /// <summary>移除影响力的候选范围。</summary>
        public PendingInfluenceScope Scope;

        /// <summary>城市移动的落点限制（任何相邻点 / 只限有自己的影响力）。</summary>
        public PendingNodeScope NodeScope;

        public static PendingActionLimits Unlimited =>
            new PendingActionLimits
            {
                Free = false,
                MaxVoucherSpend = -1,
                Scope = PendingInfluenceScope.Any,
                NodeScope = PendingNodeScope.Any,
            };

        public override string ToString() =>
            $"免费={Free}，金券上限={MaxVoucherSpend}，移除范围={Scope}，落点={NodeScope}";
    }

    /// <summary>
    /// 一条等待界面执行的行动。由效果结算时入队，界面启动对应行动流程，
    /// 行动完成的命令按 <see cref="Id"/> 消费它；<see cref="Remaining"/> &gt; 1 表示还要再做几次。
    /// </summary>
    public sealed class PendingAction
    {
        /// <summary>在整局内唯一（从 1 开始，0 表示「没有挂起行动」）。</summary>
        public int Id;

        /// <summary>行动属于哪位玩家。</summary>
        public int Seat;

        public PendingActionKind Kind;

        /// <summary>还要执行的次数；每完成一次减 1，归零时整条移除。</summary>
        public int Remaining = 1;

        public PendingActionLimits Limits = PendingActionLimits.Unlimited;

        /// <summary>来源卡牌 id（-1 = 不是某张具体牌，例如企业特效）。</summary>
        public int SourceCardId = -1;

        /// <summary>来源说明，展示在行动提示里（「载具仓库」等）。</summary>
        public string SourceLabel = string.Empty;

        public PendingAction()
        {
        }

        public PendingAction(int id, int seat, PendingActionKind kind, int remaining,
            PendingActionLimits limits, int sourceCardId, string sourceLabel)
        {
            Id = id;
            Seat = seat;
            Kind = kind;
            Remaining = remaining < 1 ? 1 : remaining;
            Limits = limits;
            SourceCardId = sourceCardId;
            SourceLabel = sourceLabel ?? string.Empty;
        }

        /// <summary>行动种类的中文名，用于状态行与日志。</summary>
        public static string LabelOf(PendingActionKind kind) => kind switch
        {
            PendingActionKind.Explore => "探索",
            PendingActionKind.Build => "建设",
            PendingActionKind.RemoveInfluence => "移除影响力",
            PendingActionKind.CityMove => "城市移动",
            PendingActionKind.RelocateInfluence => "调度",
            _ => "行动",
        };

        public string Label => LabelOf(Kind);

        public PendingAction Clone() => new PendingAction
        {
            Id = Id,
            Seat = Seat,
            Kind = Kind,
            Remaining = Remaining,
            Limits = Limits,
            SourceCardId = SourceCardId,
            SourceLabel = SourceLabel,
        };

        public override string ToString() =>
            $"挂起行动 #{Id}：{Label}（座位 {Seat}，剩余 {Remaining}，来源 {SourceLabel}）";
    }
}
