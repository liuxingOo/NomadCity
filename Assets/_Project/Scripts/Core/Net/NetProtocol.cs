namespace NomadCity.Core.Net
{
    /// <summary>
    /// 联机线格式版本。两端版本不一致时必须在握手阶段拒绝，而不是带着不同规则静默开局
    /// —— 后者会表现为「双方都觉得自己算对了」，很难排查。
    ///
    /// 修改任何 <c>RelayTransport</c> 的线格式（新增 / 调整消息字段）、或规则引擎的序列化结构时，这里 +1。
    /// </summary>
    public static class NetProtocol
    {
        /// <summary>当前协议版本。</summary>
        public const int Version = 2;
    }
}
