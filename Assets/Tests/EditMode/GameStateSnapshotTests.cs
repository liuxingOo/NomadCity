using System;
using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Core.Net;
using NomadCity.Core.Rules;
using NUnit.Framework;

namespace NomadCity.Tests
{
    /// <summary>
    /// 阶段 3 的开局快照红线测试：抓取 → 编码 → 解码 → 还原 → 再抓取，字节必须完全一致。
    /// 只要漏掉任何一个运行时字段，第二次编码就会与第一次不同。
    /// </summary>
    public sealed class GameStateSnapshotTests
    {
        private static MapState SmallMap()
        {
            var map = new MapState();
            map.Nodes.Add(new MapNodeState(0, "A-01", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(1, "A-02", District.A, NodeColor.Green, false, 2));
            map.Nodes.Add(new MapNodeState(2, "B-01", District.B, NodeColor.Yellow, false, 2));

            map.Routes.Add(new RouteState(0, new[] { 0, 1 }, District.A, 1));
            map.Routes.Add(new RouteState(1, new[] { 1, 2 }, District.B, 2));

            map.Nodes[0].Slots.Place(0, 2);
            map.Routes[1].Slots.Place(1, 1);
            map.Nodes[1].HasResourceToken = true;
            map.Nodes[1].TokenResource = ResourceType.Oriron;
            map.Nodes[1].TokenAmount = 2;
            map.Nodes[1].EventCardId = 401;
            return map;
        }

        [Test]
        public void 快照编码解码还原后再编码字节完全一致()
        {
            GameEngine engine = GameEngine.Create(new GameSetup(3), 1, SmallMap(),
                PlaceholderCardCatalog.Default, new Random(20261021));

            GameState original = engine.State;
            original.Players[2].Vouchers = 7;
            original.Players[0].PlacedCharacterCard = original.Players[0].HandCharacterCards[0];
            original.CityPatternStates[0].PlaceMarker(1, 0);
            original.Map.Nodes[2].ExplorePending = true;

            byte[] first = GameStateSnapshotCodec.Write(GameStateSnapshot.Capture(original));
            GameStateSnapshot decoded = GameStateSnapshotCodec.Read(first, 0, first.Length);
            GameState restored = decoded.Restore(PlaceholderCardCatalog.Default);
            byte[] second = GameStateSnapshotCodec.Write(GameStateSnapshot.Capture(restored));

            CollectionAssert.AreEqual(first, second, "快照往返后有字段丢失或被改写。");

            // 再抽查几个容易漏掉的可变字段。
            Assert.AreEqual(original.TurnNumber, restored.TurnNumber);
            Assert.AreEqual(original.FirstPlayerSeat, restored.FirstPlayerSeat);
            Assert.AreEqual(original.Players[2].Vouchers, restored.Players[2].Vouchers);
            Assert.AreEqual(original.Players[0].PlacedCharacterCard, restored.Players[0].PlacedCharacterCard);
            Assert.AreEqual(original.Map.Nodes[1].EventCardId, restored.Map.Nodes[1].EventCardId);
            Assert.AreEqual(2, restored.Map.Routes[1].Slots.Capacity);
            Assert.AreEqual(1, restored.Map.Routes[1].Slots.OwnerOf(1));
            Assert.AreEqual(1, restored.CityPatternStates[0].CountOf(1));
            Assert.AreEqual(1, restored.CityPatternStates[0].Markers.Count);
        }
    }
}
