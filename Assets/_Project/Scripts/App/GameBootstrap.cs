using NomadCity.Data;
using NomadCity.Game;
using UnityEngine;

namespace NomadCity.App
{
    /// <summary>
    /// Game 场景的入口。开局读主菜单写入的 GameSession，选地图、构建地图状态。
    /// 后续的规则引擎（RuleSet / GameState / 命令通道）都会挂在这里。
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Tooltip("主菜单写入的跨场景配置")]
        [SerializeField] private GameSession session;

        [Tooltip("场景里唯一的地图容器")]
        [SerializeField] private MapView mapView;

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

            Debug.Log($"[开局] {playerCount} 人局 → 地图「{mapData.DisplayName}」：" +
                      $"资源点 {map.Nodes.Count} 个，航道 {map.Routes.Count} 条。");
        }
    }
}
