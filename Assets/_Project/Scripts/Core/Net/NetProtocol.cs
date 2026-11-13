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
        /// <summary>
        /// 当前协议版本。
        ///
        /// 3：ResolveEventOption / BuildFacility / UseCharacterCard / UsePatternSpecialAction 四种命令
        /// 开始用 P3 = 答案个数、Items = 答案来传递「玩家当场点过的选择」（之前这两个槽位是空的）。
        /// 新旧两端混用不会报错，只会让主机按「第一个合法候选」结算、玩家看到的落点却不是自己点的 ——
        /// 所以必须在这里 +1，让版本不一致在握手阶段就被拒绝。
        /// </summary>
        public const int Version = 4;
    }
}
