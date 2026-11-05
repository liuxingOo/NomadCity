using System;
using System.Linq;
using NomadCity.Core.Engine;
using NomadCity.Core.Net;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>
    /// 阶段 1 传输抽象的红线测试，防止「新增命令忘了登记 / 忘了编码」：
    /// 1) 反射扫描 Core 全部 <see cref="ICommand"/> 具体类，断言每一个都能在 <see cref="CommandCatalog"/> 查到 Kind；
    /// 2) 为每一个 <see cref="CommandKind"/> 造一个字段填满的代表性 DTO，验证
    ///    「线格式往返（Write→Read）逐字段相等」且「Encode(Decode(dto)) 回到同一个 DTO」。
    /// <see cref="ICommand"/> 实现没有重写 Equals，所以全程只比较 DTO，不比较命令对象。
    /// </summary>
    public sealed class CommandCodecCoverageTests
    {
        /// <summary>反射收集 Core 里所有「非抽象、实现了 ICommand 的类」。</summary>
        private static Type[] CommandTypes() =>
            typeof(ICommand).Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(ICommand).IsAssignableFrom(t))
                .ToArray();

        [Test]
        public void 全部命令都已登记到catalog()
        {
            Type[] commandTypes = CommandTypes();

            Assert.IsNotEmpty(commandTypes, "Core 里应当至少登记 17 个 ICommand 具体类。");

            foreach (Type type in commandTypes)
            {
                // KindOf 未登记会抛异常，这里只要能拿到非 None 的 Kind 即通过。
                CommandKind kind = CommandCatalog.KindOf(type);
                Assert.AreNotEqual(CommandKind.None, kind, $"{type.Name} 的 Kind 不能是 None。");
            }
        }

        [Test]
        public void 每个Kind的DTO都能通过线格式与编解码往返()
        {
            foreach (CommandKind kind in Enum.GetValues(typeof(CommandKind)))
            {
                if (kind == CommandKind.None) continue;

                CommandDto dto = Sample(kind);

                // 1) 线格式往返：Write → Read 必须逐字段相等。
                byte[] bytes = CommandWire.Write(dto);
                CommandDto wireRoundTrip = CommandWire.Read(bytes, 0, bytes.Length);
                Assert.IsTrue(wireRoundTrip.Equals(dto), $"{kind} 的线格式往返不一致。");

                // 2) 编解码往返：Decode → Encode 必须回到同一个 DTO。
                ICommand command = CommandCodec.Decode(dto);
                CommandDto codecRoundTrip = CommandCodec.Encode(command);
                Assert.IsTrue(codecRoundTrip.Equals(dto), $"{kind} 的 Encode(Decode()) 往返不一致。");
            }
        }

        /// <summary>为每个 Kind 造一个「各槽位都填了代表性值」的 DTO（取值落在各命令构造函数能接受的范围内）。</summary>
        private static CommandDto Sample(CommandKind kind) => kind switch
        {
            CommandKind.StartGame =>
                new CommandDto { Kind = kind, Seat = -1, P0 = 1 },

            CommandKind.AdvancePhase =>
                new CommandDto { Kind = kind, Seat = -1 },

            CommandKind.ChooseStartNode =>
                new CommandDto { Kind = kind, Seat = 2, P0 = 5 },

            // Flags 两位都置 1，覆盖 AcceptCost / GrantRewards；P3 = 答案个数 2，Items = 答案（含「放弃」-1）。
            CommandKind.ResolveEventOption =>
                new CommandDto
                {
                    Kind = kind, Seat = 1, P0 = 3, P1 = 0, P3 = 2, Items = new[] { 1, -1 },
                    Flags = CommandCodec.AcceptCostBit | CommandCodec.GrantRewardsBit,
                },

            CommandKind.DiscardSupplyFacility =>
                new CommandDto { Kind = kind, Seat = 0, P0 = 21 },

            // P2 = (int)FacilityPayment.Vouchers；P3 = 入场效果答案个数，Items = 答案。
            CommandKind.BuildFacility =>
                new CommandDto { Kind = kind, Seat = 2, P0 = 12, P1 = 3, P2 = 2, P3 = 1, Items = new[] { 3 } },

            CommandKind.PlaceCharacterCard =>
                new CommandDto { Kind = kind, Seat = 3, P0 = 17 },

            // P0 = (int)CharacterEffectKind.Tactics；P3 = 效果答案个数，Items = 答案。
            CommandKind.UseCharacterCard =>
                new CommandDto { Kind = kind, Seat = 1, P0 = 1, P3 = 2, Items = new[] { 0, 2 } },

            CommandKind.EndCharacterCardUse =>
                new CommandDto { Kind = kind, Seat = 2 },

            CommandKind.DeclareCityPattern =>
                new CommandDto { Kind = kind, Seat = 1, P0 = 8, P1 = 2 },

            // Items = [routeId 2 个..., 付给谁 1 对]；付给谁按航道 id 升序，保证 Encode 复现同序。
            CommandKind.Explore =>
                new CommandDto { Kind = kind, Seat = 0, P0 = 7, P1 = 2, P2 = 1, Items = new[] { 1, 2, 0, 1 } },

            CommandKind.ExploreInfluence =>
                new CommandDto { Kind = kind, Seat = 0, P0 = 7 },

            CommandKind.MoveMobileCity =>
                new CommandDto { Kind = kind, Seat = 1, P0 = 9 },

            CommandKind.MoveCityInfluence =>
                new CommandDto { Kind = kind, Seat = 1, P0 = 4 },

            // P0 = (int)InfluenceSlotKind.Route；P1 = 航道 id；P2 = 槽位下标。
            CommandKind.PlaceInfluence =>
                new CommandDto { Kind = kind, Seat = 3, P0 = 1, P1 = 9, P2 = 2 },

            // P0 = 步数 2；每步 6 个 int：FromKind/FromTarget/FromSlot/ToKind/ToTarget/ToSlot。
            CommandKind.RelocateInfluence =>
                new CommandDto
                {
                    Kind = kind,
                    Seat = 0,
                    P0 = 2,
                    Items = new[] { 0, 4, 1, 0, 6, 2, 1, 5, 0, 1, 7, 1 },
                },

            // P0 = PatternCardId；P3 = 效果答案个数，Items = 答案。
            CommandKind.UsePatternSpecialAction =>
                new CommandDto { Kind = kind, Seat = 0, P0 = 8, P3 = 1, Items = new[] { 4 } },

            // P0 = 座位；P1 = 选点数；Items = 本座位选中的资源点 id。
            CommandKind.SubmitHarvestChoice =>
                new CommandDto { Kind = kind, Seat = 1, P0 = 1, P1 = 2, Items = new[] { 5, 7 } },

            // P0 = 座位数 2；Items = [座位, 选点数, 资源点 id..., 座位, 选点数, 资源点 id...]，按座位升序。
            CommandKind.ResolveHarvest =>
                new CommandDto
                {
                    Kind = kind,
                    Seat = -1,
                    P0 = 2,
                    Items = new[] { 0, 1, 5, 1, 2, 6, 7 },
                },

            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind,
                "新增了 CommandKind 却没在往返测试里给出样本 DTO。"),
        };
    }
}
