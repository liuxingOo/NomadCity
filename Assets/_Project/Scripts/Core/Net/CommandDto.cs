using System;

namespace NomadCity.Core.Net
{
    /// <summary>
    /// 一条命令的中立传输表示（无多态）。所有参数统一装进 int 槽位，方便线格式与逐字段比较：
    /// <see cref="P0"/>–<see cref="P3"/> 放标量（节点 id、卡 id、下标、座位等），
    /// <see cref="Flags"/> 打包 bool 参数，<see cref="Items"/> 放变长列表（航道 id、多步移动、付给谁 …）。
    /// <see cref="Items"/> 为空时一律为 null（不表示成空数组），这样往返比较才稳定。
    /// </summary>
    public struct CommandDto : IEquatable<CommandDto>
    {
        public CommandKind Kind;

        /// <summary>命令发起座位；引擎内部命令（开局 / 推进阶段）为 -1。</summary>
        public int Seat;

        public int P0;
        public int P1;
        public int P2;
        public int P3;

        /// <summary>bool 参数按位打包：bit0 = AcceptCost，bit1 = GrantRewards（当前仅 ResolveEventOptionCommand 用到）。</summary>
        public uint Flags;

        /// <summary>变长 int 列表；无内容时为 null。</summary>
        public int[] Items;

        /// <summary>逐字段值相等（<see cref="Items"/> 按元素比较，null 与 null 视为相等）。</summary>
        public bool Equals(CommandDto other) =>
            Kind == other.Kind && Seat == other.Seat &&
            P0 == other.P0 && P1 == other.P1 && P2 == other.P2 && P3 == other.P3 &&
            Flags == other.Flags && ItemsEqual(Items, other.Items);

        public override bool Equals(object obj) => obj is CommandDto other && Equals(other);

        /// <summary>两个变长列表是否逐元素相等；同为 null 算相等。</summary>
        public static bool ItemsEqual(int[] a, int[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)Kind;
                h = h * 397 ^ Seat;
                h = h * 397 ^ P0;
                h = h * 397 ^ P1;
                h = h * 397 ^ P2;
                h = h * 397 ^ P3;
                h = h * 397 ^ (int)Flags;
                if (Items != null)
                {
                    for (int i = 0; i < Items.Length; i++) h = h * 397 ^ Items[i];
                }
                return h;
            }
        }

        public override string ToString() =>
            $"{Kind}(seat={Seat}, P=[{P0},{P1},{P2},{P3}], flags={Flags}, items={(Items == null ? "null" : Items.Length.ToString())})";
    }
}
