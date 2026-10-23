using System;

namespace NomadCity.Core.Net
{
    /// <summary>
    /// <see cref="CommandDto"/> 的纯 C# 字节编解码（Core 里没有 Unity 的 DataStreamWriter，所以手写）。
    /// 线格式（全部小端序）：
    /// <code>
    /// ushort Kind
    /// int    Seat
    /// int    P0
    /// int    P1
    /// int    P2
    /// int    P3
    /// uint   Flags
    /// int    ItemCount      // 为 -1 表示 Items 为 null；否则接 ItemCount 个 int
    /// int[]  Items
    /// </code>
    /// </summary>
    public static class CommandWire
    {
        /// <summary>固定头部长度：Kind(2) + Seat/P0/P1/P2/P3(20) + Flags(4) + ItemCount(4)。</summary>
        public const int HeaderSize = 2 + 20 + 4 + 4;

        /// <summary>把 DTO 编码成字节数组。Items 为 null 或空时写 ItemCount = -1。</summary>
        public static byte[] Write(CommandDto dto)
        {
            int itemCount = (dto.Items == null || dto.Items.Length == 0) ? -1 : dto.Items.Length;
            var buffer = new byte[HeaderSize + (itemCount > 0 ? itemCount * 4 : 0)];

            int pos = 0;
            WriteUShort(buffer, ref pos, (ushort)dto.Kind);
            WriteInt(buffer, ref pos, dto.Seat);
            WriteInt(buffer, ref pos, dto.P0);
            WriteInt(buffer, ref pos, dto.P1);
            WriteInt(buffer, ref pos, dto.P2);
            WriteInt(buffer, ref pos, dto.P3);
            WriteUInt(buffer, ref pos, dto.Flags);
            WriteInt(buffer, ref pos, itemCount);
            if (itemCount > 0)
            {
                for (int i = 0; i < itemCount; i++) WriteInt(buffer, ref pos, dto.Items[i]);
            }
            return buffer;
        }

        /// <summary>从字节区间 [offset, offset+length) 读回 DTO；ItemCount 为 -1 时 Items 为 null。</summary>
        public static CommandDto Read(byte[] buffer, int offset, int length)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0) throw new ArgumentException($"offset（{offset}）不能为负。", nameof(offset));
            if (length < HeaderSize)
                throw new ArgumentException($"长度 {length} 不足以容纳命令头部（至少 {HeaderSize} 字节）。", nameof(length));
            if (offset + length > buffer.Length)
                throw new ArgumentException("读取区间超出了缓冲区范围。", nameof(length));

            int pos = offset;
            var dto = new CommandDto();
            dto.Kind = (CommandKind)ReadUShort(buffer, ref pos);
            dto.Seat = ReadInt(buffer, ref pos);
            dto.P0 = ReadInt(buffer, ref pos);
            dto.P1 = ReadInt(buffer, ref pos);
            dto.P2 = ReadInt(buffer, ref pos);
            dto.P3 = ReadInt(buffer, ref pos);
            dto.Flags = ReadUInt(buffer, ref pos);
            int itemCount = ReadInt(buffer, ref pos);

            if (itemCount < -1)
                throw new ArgumentException($"非法的 Items 长度 {itemCount}。", nameof(buffer));
            if (itemCount > 0)
            {
                if (length < HeaderSize + itemCount * 4)
                    throw new ArgumentException("Items 个数与可读长度不符。", nameof(length));
                dto.Items = new int[itemCount];
                for (int i = 0; i < itemCount; i++) dto.Items[i] = ReadInt(buffer, ref pos);
            }
            return dto;
        }

        // —— 小端序读写小工具 ——

        private static void WriteUShort(byte[] buffer, ref int pos, ushort value)
        {
            buffer[pos] = (byte)value;
            buffer[pos + 1] = (byte)(value >> 8);
            pos += 2;
        }

        private static void WriteInt(byte[] buffer, ref int pos, int value)
        {
            buffer[pos] = (byte)value;
            buffer[pos + 1] = (byte)(value >> 8);
            buffer[pos + 2] = (byte)(value >> 16);
            buffer[pos + 3] = (byte)(value >> 24);
            pos += 4;
        }

        private static void WriteUInt(byte[] buffer, ref int pos, uint value)
        {
            buffer[pos] = (byte)value;
            buffer[pos + 1] = (byte)(value >> 8);
            buffer[pos + 2] = (byte)(value >> 16);
            buffer[pos + 3] = (byte)(value >> 24);
            pos += 4;
        }

        private static ushort ReadUShort(byte[] buffer, ref int pos)
        {
            ushort value = (ushort)(buffer[pos] | (buffer[pos + 1] << 8));
            pos += 2;
            return value;
        }

        private static int ReadInt(byte[] buffer, ref int pos)
        {
            int value = buffer[pos]
                        | (buffer[pos + 1] << 8)
                        | (buffer[pos + 2] << 16)
                        | (buffer[pos + 3] << 24);
            pos += 4;
            return value;
        }

        private static uint ReadUInt(byte[] buffer, ref int pos)
        {
            uint value = (uint)(buffer[pos]
                                | (buffer[pos + 1] << 8)
                                | (buffer[pos + 2] << 16)
                                | (buffer[pos + 3] << 24));
            pos += 4;
            return value;
        }
    }
}
