using System;
using NomadCity.Core.Net;
using NomadCity.Core.Rules;

namespace NomadCity.App
{
    /// <summary>
    /// <see cref="ITransport"/> 的单机（本地热座）实现：没有网络、没有远端，行为与改动前逐帧一致。
    /// <see cref="Submit"/> 同步把 <see cref="CommandSubmitted"/> 抛给订阅者（本机命令立即生效）。
    ///
    /// 关于联机专用入口的处理：
    /// - <see cref="Poll"/> / <see cref="Shutdown"/> 留空实现 —— 它们是主循环 / 退场会无条件调用的生命周期钩子，
    ///   单机下抛异常只会白白炸掉正常流程，留空最安全；
    /// - <see cref="Host"/> / <see cref="Join"/> / <see cref="Broadcast"/> 抛 <see cref="InvalidOperationException"/> ——
    ///   它们是明确的联机操作，单机态下被调用属于编程错误，静默忽略反而会隐藏 bug，所以这里大声拒绝。
    /// </summary>
    public sealed class LocalTransport : ITransport
    {
        public bool IsMultiplayer => false;

        /// <summary>单机里本机就是「唯一权威」。</summary>
        public bool IsHost => true;

        /// <summary>单机固定本机座位 0。</summary>
        public int LocalSeat => 0;

        public event Action<CommandDto> CommandSubmitted;

        // 下面这些是 ITransport 的接口成员，单机实现永远不会触发它们（没有远端、没有连接错误、没有快照），
        // 编译器因此报 CS0067「事件从未使用」。删掉成员会编译不过，所以就地关闭该警告。
#pragma warning disable 0067
        public event Action<CommandDto, int> RemoteCommandArrived;
        public event Action<GameSetup, int> SetupArrived;
        public event Action<string> ErrorArrived;
        public event Action<byte[]> SnapshotArrived;
        public event Action<int> PeerJoined;
        public event Action<string> ConnectionLost;
#pragma warning restore 0067

        /// <summary>单机没有网络发送，提交即当作本机命令同步抛出。</summary>
        public void Submit(CommandDto command) => CommandSubmitted?.Invoke(command);

        public void Host(GameSetup setup, int seatCount) =>
            throw new InvalidOperationException("单机模式下不应调用 Host。");

        public void Join(string joinCode) =>
            throw new InvalidOperationException("单机模式下不应调用 Join。");

        public void Broadcast(CommandDto command, bool ok, string error) =>
            throw new InvalidOperationException("单机模式下不应调用 Broadcast。");

        public void BroadcastSnapshot(byte[] snapshot) =>
            throw new InvalidOperationException("单机模式下不应调用 BroadcastSnapshot。");

        /// <summary>单机没有需要推进的连接状态，留空。</summary>
        public void Poll(float deltaTime) { }

        /// <summary>单机没有连接要关闭，留空。</summary>
        public void Shutdown() { }
    }
}
