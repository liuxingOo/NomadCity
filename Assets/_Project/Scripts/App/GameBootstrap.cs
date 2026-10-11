using NomadCity.Core.Engine;
using NomadCity.Core.Model;
using NomadCity.Data;
using NomadCity.Game;
using UnityEngine;

namespace NomadCity.App
{
    /// <summary>
    /// Game 场景的入口。开局读主菜单写入的 GameSession，选地图、构建地图状态，并建立规则引擎。
    /// 后续的具体行动规则（部署 / 调度 / 探索 / 路费 …）都会挂在引擎的命令通道上。
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Tooltip("主菜单写入的跨场景配置")]
        [SerializeField] private GameSession session;

        [Tooltip("场景里唯一的地图容器")]
        [SerializeField] private MapView mapView;

        /// <summary>本局的规则引擎；地图构建完成后建立。UI 通过它提交命令、订阅事件。</summary>
        public GameEngine Engine { get; private set; }

        private void Awake()
        {
            if (session == null || mapView == null)
            {
                Debug.LogError("[开局] GameBootstrap 的 session / mapView 未指定。", this);
                return;
            }

            int playerCount = session.PlayerCount;
            MapData mapData = mapView.SelectFor(playerCount);

            if (mapData == null)
            {
                Debug.LogError($"[开局] {playerCount} 人局没有对应的地图数据。", this);
                return;
            }

            var map = mapView.Build(mapData);

            Engine = GameEngine.Create(session.ToGameSetup(), 0, map);
            ApplyDistrictScores(Engine.State, mapData);

            CommandResult start = Engine.Dispatch(new StartGameCommand(0));
            if (!start.Ok)
            {
                Debug.LogError($"[开局] 无法开始游戏：{start.Error}", this);
                return;
            }

            var state = Engine.State;
            Debug.Log($"[开局] {playerCount} 人局 → 地图「{mapData.DisplayName}」：" +
                      $"资源点 {map.Nodes.Count} 个，航道 {map.Routes.Count} 条。\n" +
                      $"[引擎] 回合 {state.TurnNumber}/{TurnFlow.TotalTurns}，" +
                      $"阶段 {state.Phase}，起始座位 {state.FirstPlayerSeat}，行动座位 {state.ActiveSeat}。");
        }

        /// <summary>
        /// 把地图上配置的区块控制分（§9.2）覆盖到引擎状态。此处只覆盖地图里**显式列出**的区块，
        /// 没配置的区块保留引擎里的标准表取值（例如三人地图没有 G 区，无需覆盖）。
        /// </summary>
        private static void ApplyDistrictScores(GameState state, MapData mapData)
        {
            foreach (DistrictScore entry in mapData.DistrictScores)
            {
                state.DistrictScores[(int)entry.District] = entry.Score;
            }
        }
    }
}