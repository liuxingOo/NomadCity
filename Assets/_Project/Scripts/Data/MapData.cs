using System;
using System.Collections.Generic;
using NomadCity.Core.Model;
using UnityEngine;

namespace NomadCity.Data
{
    /// <summary>
    /// 一套地图（三人面 / 四人面）的静态定义。
    /// 美术与每个资源点 / 航道的具体位置都摆在 MapPrefab 里，这里只放「这张图整体是什么」。
    /// 两份资产：三人面、四人面。
    /// </summary>
    [CreateAssetMenu(menuName = "NomadCity/Map Data", fileName = "MapData")]
    public sealed class MapData : ScriptableObject
    {
        [Tooltip("调试与日志用，如「三人面地图」")]
        [SerializeField] private string displayName = "三人面地图";

        [Tooltip("这套地图的 Prefab：底图 + 每个资源点的 MapNodeView + 每条航道的 MapRouteView")]
        [SerializeField] private GameObject mapPrefab;

        [Tooltip("终局区块控制分。不同地图不同（4 人面多出 R 区）")]
        [SerializeField] private DistrictScore[] districtScores = Array.Empty<DistrictScore>();

        public string DisplayName => displayName;

        public GameObject MapPrefab => mapPrefab;

        public IReadOnlyList<DistrictScore> DistrictScores => districtScores;

        /// <summary>某个区块的控制分；未配置时返回 0。</summary>
        public int ScoreOf(District district)
        {
            foreach (DistrictScore entry in districtScores)
            {
                if (entry.District == district) return entry.Score;
            }
            return 0;
        }
    }

    /// <summary>区块 → 控制分（规则规格 9.2）。</summary>
    [Serializable]
    public struct DistrictScore
    {
        public District District;
        public int Score;
    }
}
