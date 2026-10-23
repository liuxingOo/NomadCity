using System;
using NomadCity.Core.Rules;

namespace NomadCity.Core.Net
{
    /// <summary>
    /// 厂商中立的传输抽象（阶段 1）。单机与联机共用同一条命令通道：
    /// UI 只管把命令 <see cref="Submit"/> 出去、并消费事件；具体是本地直通还是走 Relay 由实现决定。
    /// 阶段 1 只有单机实现 <c>NomadCity.App.LocalTransport</c>，行为与改动前逐帧一致。
    /// </summary>
    public interface ITransport
    {
        /// <summary>是否处于联机对局。</summary>
        bool IsMultiplayer { get; }

        /// <summary>本机是否是主机（主机权威，负责校验与广播）。</summary>
        bool IsHost { get; }

        /// <summary>本机座位号。</summary>
        int LocalSeat { get; }

        /// <summary>本机提交了一条命令。</summary>
        event Action<CommandDto> CommandSubmitted;

        /// <summary>收到远端命令（参数 2 = 发起座位）。</summary>
        event Action<CommandDto, int> RemoteCommandArrived;

        /// <summary>收到开局配置（参数 2 = 分配给我的座位）。</summary>
        event Action<GameSetup, int> SetupArrived;

        /// <summary>传输层错误或主机拒绝（可直接展示给玩家）。</summary>
        event Action<string> ErrorArrived;

        /// <summary>
        /// 收到主机发来的开局快照（负载是 <see cref="GameStateSnapshotCodec"/> 编码后的字节）。
        /// 客户端据此建立与主机一致的状态；分包与拼包由传输实现负责，这里收到的永远是完整一份。
        /// </summary>
        event Action<byte[]> SnapshotArrived;

        /// <summary>有新客户端接入（参数 = 主机分配给它的座位）。主机据此把当前状态补发一份，避免它停在旧快照上。</summary>
        event Action<int> PeerJoined;

        /// <summary>
        /// 连接被动断开（主机掉线 / 30 秒收不到主机数据）。与 <see cref="ErrorArrived"/> 分开：
        /// 这是不可恢复的断线，上层必须退出房间并释放 Relay，不能继续假装能操作。
        /// </summary>
        event Action<string> ConnectionLost;

        /// <summary>作为主机开启一局：广播开局配置与座位数。</summary>
        void Host(GameSetup setup, int seatCount);

        /// <summary>作为客户端用中继码加入（6 位房间码 → 中继码由联机层的大厅完成）。</summary>
        void Join(string joinCode);

        /// <summary>提交一条命令（本机想做的操作）。</summary>
        void Submit(CommandDto command);

        /// <summary>主机广播一条命令的执行结果（ok / 失败原因）。</summary>
        void Broadcast(CommandDto command, bool ok, string error);

        /// <summary>主机广播一份开局快照；后加入的客户端会在接入时自动补发最近一份。</summary>
        void BroadcastSnapshot(byte[] snapshot);

        /// <summary>每帧推进传输层（连接 / 收发）。</summary>
        void Poll(float deltaTime);

        /// <summary>关闭连接并释放资源。</summary>
        void Shutdown();
    }
}
