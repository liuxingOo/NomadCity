using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 命令校验结果。Ok=false 时 Error 说明为什么非法，用于给玩家提示、以及联机时的回滚判定。
    /// 只读结构体：命令校验本身不产生副作用。
    /// </summary>
    public readonly struct CommandResult
    {
        public bool Ok { get; }

        /// <summary>非法原因；Ok=true 时为 null。</summary>
        public string Error { get; }

        private CommandResult(bool ok, string error)
        {
            Ok = ok;
            Error = error;
        }

        public static readonly CommandResult Success = new CommandResult(true, null);

        public static CommandResult Fail(string reason) => new CommandResult(false, reason);

        public override string ToString() => Ok ? "OK" : $"非法：{Error}";
    }

    /// <summary>
    /// 所有玩家输入的统一抽象（规则规格 13.4：单机与联机共用同一条命令通道）。
    /// 命令只描述「谁、想做什么」——Validate 只读不改，Apply 才真正改动 GameState。
    /// 引擎保证：Validate 不通过时 Apply 绝不会被调用，状态一字不变。
    /// </summary>
    public interface ICommand
    {
        /// <summary>发起命令的座位号；引擎内部命令（如开局、推进阶段）用 -1。</summary>
        int ActorSeat { get; }

        /// <summary>调试／日志用名称。</summary>
        string Name { get; }

        /// <summary>是否占用本回合「主要行动」名额：主要行动 true，快速行动 false（规则规格 6.1 / 6.3）。</summary>
        bool ConsumesAction { get; }

        /// <summary>纯校验，不得修改 state。</summary>
        CommandResult Validate(GameState state);

        /// <summary>执行命令。仅在 Validate 返回 Ok 后由命令通道调用。</summary>
        void Apply(GameState state);
    }
}