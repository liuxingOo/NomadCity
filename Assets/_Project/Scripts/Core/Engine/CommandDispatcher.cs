using System;
using NomadCity.Core.Model;

namespace NomadCity.Core.Engine
{
    /// <summary>
    /// 命令通道 —— GameState 的唯一写入口。
    /// 先 Validate 再 Apply：校验失败时状态保持原样，这是联机回滚与「非法操作」提示的基础。
    /// UI 与联机端都不得直接改 GameState，只能经由这里。
    /// </summary>
    public sealed class CommandDispatcher
    {
        /// <summary>一条命令成功执行后触发，此时 state 已经更新完毕。</summary>
        public event Action<GameState, ICommand> CommandApplied;

        public CommandResult Dispatch(GameState state, ICommand command)
        {
            if (state == null) return CommandResult.Fail("引擎没有游戏状态。");
            if (command == null) return CommandResult.Fail("命令为空。");

            CommandResult result = command.Validate(state);
            if (!result.Ok) return result;

            command.Apply(state);
            CommandApplied?.Invoke(state, command);
            return CommandResult.Success;
        }
    }
}