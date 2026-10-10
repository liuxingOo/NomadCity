using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NomadCity.Core.Model;
using NomadCity.Data;
using NomadCity.Game;
using UnityEditor;
using UnityEngine;

namespace NomadCity.EditorTools
{
    /// <summary>
    /// 把地图 Prefab 里的资源点／航道导出成清单，并检查常见填写错误。
    /// 清单每次都从 Prefab 现读，永远和实际填的内容一致，也不需要人工维护。
    /// </summary>
    public static class MapLayoutExporter
    {
        private const string OutputFolder = "Doc";

        /// <summary>两个物体的坐标差小于这个值就算重合。</summary>
        private const float PositionTolerance = 0.01f;

        [MenuItem("NomadCity/地图：导出并检查清单")]
        private static void ExportAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:MapData");
            if (guids.Length == 0)
            {
                Debug.LogError("[地图清单] 没有找到任何 MapData 资产。");
                return;
            }

            string folder = Path.Combine(ProjectRoot(), OutputFolder);
            Directory.CreateDirectory(folder);

            int exported = 0;
            int totalProblems = 0;

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                MapData data = AssetDatabase.LoadAssetAtPath<MapData>(assetPath);
                if (data == null) continue;

                MapReport report = BuildReport(data);
                exported++;
                totalProblems += report.Problems.Count;

                string fullPath = Path.Combine(folder, $"MapLayout_{data.name}.md");
                File.WriteAllText(fullPath, report.Text, new UTF8Encoding(false));

                if (report.Problems.Count > 0)
                    Debug.LogWarning($"[地图清单]「{data.DisplayName}」发现 {report.Problems.Count} 处问题 → {fullPath}");
                else
                    Debug.Log($"[地图清单]「{data.DisplayName}」检查通过 → {fullPath}");
            }

            Debug.Log(totalProblems > 0
                ? $"[地图清单] 已导出 {exported} 份，共 {totalProblems} 处问题。"
                : $"[地图清单] 已导出 {exported} 份，没有发现问题。");
        }

        private sealed class MapReport
        {
            public string Text;
            public readonly List<string> Problems = new List<string>();
            public readonly List<string> Hints = new List<string>();
        }

        private static MapReport BuildReport(MapData data)
        {
            var report = new MapReport();
            var text = new StringBuilder();

            text.AppendLine($"# 地图清单：{data.DisplayName}");
            text.AppendLine();
            text.AppendLine("> 由菜单 **NomadCity → 地图：导出并检查清单** 自动生成，请勿手工编辑（下次导出会覆盖）。");
            text.AppendLine($"> MapData：`{AssetDatabase.GetAssetPath(data)}`");

            if (data.MapPrefab == null)
            {
                report.Problems.Add("MapData 没有指定地图 Prefab。");
                text.AppendLine();
                text.AppendLine("**地图 Prefab 未指定，无法导出内容。**");
                report.Text = text.ToString();
                return report;
            }

            text.AppendLine($"> 地图 Prefab：`{AssetDatabase.GetAssetPath(data.MapPrefab)}`");
            text.AppendLine();

            MapNodeView[] nodes = data.MapPrefab.GetComponentsInChildren<MapNodeView>(true);
            MapRouteView[] routes = data.MapPrefab.GetComponentsInChildren<MapRouteView>(true);

            CheckNodes(nodes, report);
            CheckRoutes(nodes, routes, report);

            AppendNodeTable(text, nodes);
            AppendRouteTable(text, routes);
            AppendCheckResult(text, nodes, report);

            report.Text = text.ToString();
            return report;
        }

        private static void CheckNodes(MapNodeView[] nodes, MapReport report)
        {
            var seen = new Dictionary<string, int>();

            foreach (MapNodeView node in nodes)
            {
                string objectName = node.gameObject.name;
                string code = node.Code;

                if (string.IsNullOrWhiteSpace(code))
                {
                    report.Problems.Add($"资源点物体「{objectName}」的 Code 是空的。");
                }
                else
                {
                    if (code != objectName)
                        report.Problems.Add($"资源点「{objectName}」的 GameObject 名字和 Code「{code}」不一致。");

                    seen.TryGetValue(code, out int count);
                    seen[code] = count + 1;

                    char head = code[0];
                    if (TryDistrictFromLetter(head, out District expect) && expect != node.District)
                        report.Problems.Add($"资源点「{code}」的编号首字母是 {char.ToUpperInvariant(head)}，但 District 填的是 {node.District}。");
                }

                if (node.InfluenceCapacity <= 0)
                    report.Problems.Add($"资源点「{code}」的影响力容量是 {node.InfluenceCapacity}，应当至少 1。");

                if (node.IsRedZone && node.Color != NodeColor.Red)
                    report.Hints.Add($"资源点「{code}」勾了红色区域，但颜色是 {node.Color}，确认一下。");

                if (!node.IsRedZone && node.Color == NodeColor.Red)
                    report.Hints.Add($"资源点「{code}」颜色是 Red，但没勾红色区域，确认一下。");
            }

            foreach (KeyValuePair<string, int> pair in seen)
            {
                if (pair.Value > 1)
                    report.Problems.Add($"编号「{pair.Key}」重复出现了 {pair.Value} 次。");
            }

            // 编号连续性：小区块里出现 A-01、A-03 却缺 A-02，多半是漏做了。
            foreach (IGrouping<District, MapNodeView> group in nodes.GroupBy(n => n.District))
            {
                var numbers = group.Select(n => ParseTailNumber(n.Code)).Where(v => v > 0).OrderBy(v => v).ToList();
                if (numbers.Count == 0) continue;

                int max = numbers[numbers.Count - 1];
                var missing = Enumerable.Range(1, max).Except(numbers).ToList();
                if (missing.Count > 0)
                {
                    string list = string.Join("、", missing.Select(i => $"{group.Key}-{i:00}"));
                    report.Hints.Add($"{group.Key} 区的编号不连续，缺：{list}");
                }
            }

            if (nodes.Length == 0)
                report.Hints.Add("这张图还没有任何资源点。");

            // 位置重复：两个资源点坐标一样，多半是复制出来忘了拖位置。
            for (int i = 0; i < nodes.Length; i++)
            {
                for (int j = i + 1; j < nodes.Length; j++)
                {
                    if (Vector3.Distance(nodes[i].transform.localPosition, nodes[j].transform.localPosition) < PositionTolerance)
                    {
                        report.Problems.Add($"资源点「{nodes[i].Code}」和「{nodes[j].Code}」的位置完全相同，多半是复制后忘了拖开。");
                    }
                }
            }
        }

        private static void CheckRoutes(MapNodeView[] nodes, MapRouteView[] routes, MapReport report)
        {
            var known = new HashSet<MapNodeView>(nodes);
            var connected = new HashSet<MapNodeView>();
            var pairs = new HashSet<string>();

            foreach (MapRouteView route in routes)
            {
                string name = route.gameObject.name;
                var ends = new List<MapNodeView>();
                bool ok = true;

                foreach (MapNodeView end in route.Ends)
                {
                    if (end == null)
                    {
                        report.Problems.Add($"航道「{name}」的端点列表里有空槽，没有指定资源点。");
                        ok = false;
                        break;
                    }

                    if (!known.Contains(end))
                    {
                        report.Problems.Add($"航道「{name}」的端点「{end.Code}」不在本 Prefab 的资源点列表里。");
                        ok = false;
                        break;
                    }

                    if (ends.Contains(end))
                    {
                        report.Problems.Add($"航道「{name}」把资源点「{end.Code}」连了两次。");
                        ok = false;
                        break;
                    }

                    ends.Add(end);
                }

                if (!ok) continue;

                if (ends.Count < 2)
                {
                    report.Problems.Add($"航道「{name}」只连了 {ends.Count} 个资源点，至少要 2 个。");
                    continue;
                }

                foreach (MapNodeView end in ends) connected.Add(end);

                // 端点集合完全一样的两条航道是重复的（端点顺序无关）。
                string key = string.Join(" - ", ends.Select(e => e.Code).OrderBy(c => c));
                if (!pairs.Add(key))
                    report.Problems.Add($"航道「{name}」和另一条航道连的是同一组资源点（{key}）。");

                if (route.InfluenceCapacity <= 0)
                    report.Problems.Add($"航道「{name}」的影响力容量是 {route.InfluenceCapacity}，应当至少 1。");
            }

            var isolated = nodes.Where(n => !connected.Contains(n)).Select(n => n.Code).ToList();
            if (isolated.Count > 0)
                report.Hints.Add($"以下资源点还没有连任何航道：{string.Join("、", isolated)}");

            for (int i = 0; i < routes.Length; i++)
            {
                for (int j = i + 1; j < routes.Length; j++)
                {
                    if (Vector3.Distance(routes[i].transform.localPosition, routes[j].transform.localPosition) < PositionTolerance)
                    {
                        report.Problems.Add($"航道「{routes[i].gameObject.name}」和「{routes[j].gameObject.name}」的位置完全相同，多半是复制后忘了拖开。");
                    }
                }
            }
        }

        private static void AppendNodeTable(StringBuilder text, MapNodeView[] nodes)
        {
            text.AppendLine($"## 资源点（{nodes.Length} 个）");
            text.AppendLine();
            text.AppendLine("| 编号 | 区块 | 颜色 | 红色区域 | 影响力容量 | 局部坐标 X | 局部坐标 Y |");
            text.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");

            foreach (MapNodeView node in nodes.OrderBy(n => n.Code))
            {
                Vector3 p = node.transform.localPosition;
                text.AppendLine($"| {node.Code} | {node.District} | {node.Color} | {(node.IsRedZone ? "是" : "")} | {node.InfluenceCapacity} | {p.x:F2} | {p.y:F2} |");
            }

            text.AppendLine();
        }

        private static void AppendRouteTable(StringBuilder text, MapRouteView[] routes)
        {
            text.AppendLine($"## 航道（{routes.Length} 条）");
            text.AppendLine();
            text.AppendLine("| 物体名 | 区块 | 影响力容量 | 连到的资源点 | 局部坐标 X | 局部坐标 Y |");
            text.AppendLine("| --- | --- | --- | --- | --- | --- |");

            foreach (MapRouteView route in routes.OrderBy(r => r.gameObject.name))
            {
                Vector3 p = route.transform.localPosition;
                string ends = route.Ends.Count == 0
                    ? "（空）"
                    : string.Join("、", route.Ends.Select(e => e != null ? e.Code : "（空）"));
                text.AppendLine($"| {route.gameObject.name} | {route.District} | {route.InfluenceCapacity} | {ends} | {p.x:F2} | {p.y:F2} |");
            }

            text.AppendLine();
        }

        private static void AppendCheckResult(StringBuilder text, MapNodeView[] nodes, MapReport report)
        {
            text.AppendLine("## 统计");
            text.AppendLine();
            foreach (IGrouping<District, MapNodeView> group in nodes.GroupBy(n => n.District).OrderBy(g => g.Key.ToString()))
                text.AppendLine($"- {group.Key} 区：{group.Count()} 个资源点");
            text.AppendLine($"- 资源点合计：{nodes.Length}");
            text.AppendLine();

            text.AppendLine("## 检查结果");
            text.AppendLine();
            if (report.Problems.Count == 0)
            {
                text.AppendLine("未发现问题。");
            }
            else
            {
                text.AppendLine($"共 {report.Problems.Count} 处问题：");
                text.AppendLine();
                foreach (string problem in report.Problems)
                    text.AppendLine($"- [问题] {problem}");
            }

            if (report.Hints.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("## 提示（不一定是错，供人工确认）");
                text.AppendLine();
                foreach (string hint in report.Hints)
                    text.AppendLine($"- [提示] {hint}");
            }
        }

        private static int ParseTailNumber(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return 0;
            int dash = code.LastIndexOf('-');
            string tail = dash >= 0 ? code.Substring(dash + 1) : code;
            return int.TryParse(tail, out int value) ? value : 0;
        }

        private static bool TryDistrictFromLetter(char letter, out District district)
        {
            switch (char.ToUpperInvariant(letter))
            {
                case 'A': district = District.A; return true;
                case 'B': district = District.B; return true;
                case 'C': district = District.C; return true;
                case 'D': district = District.D; return true;
                case 'E': district = District.E; return true;
                case 'F': district = District.F; return true;
                case 'G': district = District.G; return true;
                case 'R': district = District.R; return true;
                default: district = District.A; return false;
            }
        }

        private static string ProjectRoot()
        {
            DirectoryInfo parent = Directory.GetParent(Application.dataPath);
            return parent != null ? parent.FullName : Application.dataPath;
        }
    }
}
