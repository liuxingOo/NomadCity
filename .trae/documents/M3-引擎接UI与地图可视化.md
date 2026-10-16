# M3：引擎接 UI + 地图可视化

## Context（为什么做这一期）

P2 规则引擎已完成，73 个 EditMode 测试全绿，但**整局游戏目前不可见、不可操作**：
`GameBootstrap` 建好引擎后只往 Console 打一行日志就结束；地图上既看不到影响力、移动城市、资源点指示物，也没有任何按钮能把命令送进引擎。UI 层（`Assets/_Project/Scripts/UI/`）除 asmdef 外完全为空，Game 场景里**没有 Canvas、没有 EventSystem**。

本期目标：给 Game 场景加上 HUD 与行动面板，把引擎状态渲染到地图上，并让玩家能通过「点地图 + 点按钮」真正走通 **部署 / 调度 / 城市移动** 三个已实现的行动。为后续的卡牌数据接入、采集选点 UI 打好视图层地基。

本期**不依赖卡面文案**，因此不被 `Doc/RuleSpec.md` §14 的牌面待录入清单阻塞。

## 现状与关键约束

- 分层（asmdef 强制单向）：`App → UI → Game → Data → Core`。已确认：`App` 引用 `Core/Data/Game/UI`；`Game` 引用 `Core/Data`；`UI` 引用 `Core/Game`。Unity asmdef **引用不传递**，所以放代码时要按此对齐。
- Game 场景根对象仅 4 个：`Main Camera`（正交，Size 5.4，z=-10）、`Global Light 2D`、`MapRoot`（挂 `MapView`）、`GameRoot`（挂 `GameBootstrap`，已连线 session 资产与 MapRoot）。
- 主菜单是 **uGUI + TextMeshPro + 新 Input System**（`EventSystem` 上挂 `InputSystemUIInputModule`）。`activeInputHandler = 1`（**仅新 Input System**）→ 传统 `OnMouseDown` 不可用。
- 项目约定：地图元素必须是**世界空间 `SpriteRenderer`**，挂在底图根节点下，**禁止用 Canvas 承载地图坐标**；底图 SortingOrder = -10。
- 素材现状（已核对 `Assets/_Project/Art`）：**没有**影响力标记 / 移动城市 / 资源点指示物素材（`Art/Tokens`、`Art/Icons/*` 全空）；卡牌目录里是模板图与牌背，未按 id 切分。→ 本期用**程序生成的圆形占位**（用户已确认）。
- `MapNodeView` / `MapRouteView` 的 Gizmos 里各有一份「槽位相对偏移」的算式（节点：`x=-0.40`，`y` 按 `0.22` 竖排；航道：`x` 按 `0.22` 横排）。渲染与拾取都要用同一套坐标，**必须提到公共方法**避免三处各写一遍。

### 必须点出的一个缺口：采集阶段目前空转

`HarvestResolver.Resolve/Plan` 已实现且有测试，但 `TurnFlow.AdvancePhase` 从 `PlayerActions → Harvest` 时**只改阶段名、不做任何结算**（只 `Harvest → Cleanup` 时调了 `CleanupResolver`）。所以本期需要临时补一个收口。

**本轮做法（临时占位，非最终规则）**：进入采集阶段时，按「每个座位自动采集其全部合法资源点」调用
`HarvestResolver.Resolve(state, seat → HarvestResolver.EligibleNodes(state, seat))`。
`Plan` 内部已会按预算自动跳过付不起的点，因此这个默认值不会产生非法状态。
**完整遵循 §7 的「玩家自选采集哪些点 + 路费预览」UI 留到下一期**，届时替换掉这段自动结算。
（若你希望本轮就做选点 UI，请直接说明，我会把它并入本期。）

## 方案总览

```
[点击地图] MapPointerInput ──Camera/UI 过滤──▶ MapStateRenderer.TryPick(Vector2)
                                                      │ MapHit
                                                      ▼
[点击按钮] ActionPanel ──事件──▶ GameScreenController ──▶ GameEngine.Dispatch(ICommand)
                                        ▲                        │
                                        └── Dispatcher.CommandApplied ──┐
                                                                        ▼
                          MapStateRenderer.Refresh() + GameHud.Refresh()
```

**落点分工**

| 层 | 职责 | 新增/改动 |
|---|---|---|
| Core | 不动（本轮不新增规则） | 无 |
| Game | 地图状态渲染、点击拾取、槽位坐标 | 新增 4 个文件，改 3 个文件 |
| UI | HUD、行动面板、行动模式与选点状态机 | 新增 3 个文件 |
| App | 在 `GameBootstrap` 里把三方接起来 | 改 1 个文件 |
| 场景 | Canvas / EventSystem / HUD / 面板 + Inspector 连线 | `Game.unity` |

## 分步实施

### Phase A —— Game 层：槽位坐标与视图暴露

1. `Scripts/Game/MapNodeView.cs`：新增 `public Vector3 SlotLocalPosition(int index)`，返回节点自身坐标系下的第 index 个影响力槽位偏移（沿用现有 Gizmos 的 `x=-0.40, y=0.22` 竖排算法，容量 ≤1 时居中）。`OnDrawGizmos` 改为调用它，删掉重复算式。
2. `Scripts/Game/MapRouteView.cs`：同样新增 `SlotLocalPosition(int index)`（沿用 `x=0.22` 横排算法），`OnDrawGizmos` 复用。
3. `Scripts/Game/MapView.cs`：`Build()` 时把实例根与世界坐标视图缓存下来并暴露只读访问器：`Transform MapRoot`、`IReadOnlyList<MapNodeView> NodeViews`、`IReadOnlyList<MapRouteView> RouteViews`（`Clear()` 时一并清空）。**不改**现有 `MapState` 构建逻辑。

### Phase B —— Game 层：渲染

4. 新增 `Scripts/Game/MapVisuals/MapPalette.cs`：`PlayerColor → Color`（绿/黄/红/蓝，与 `PlayerColor` 枚举同序）、`NodeColor → Color`、`ResourceType → Color`、以及中性灰/高亮色，集中一处，禁止散落硬编码颜色。
5. 新增 `Scripts/Game/MapVisuals/MapSpriteFactory.cs`：运行时生成并缓存两种 Sprite —— `Disc(size)`（实心圆）与 `Ring(size, thickness)`（空心圆环）。用 `Texture2D` + 带抗锯齿 alpha 的 `Sprite.Create`，全项目共用一份实例。用于：已占位影响力/移动城市 = 实心圆（按玩家色着色）；空影响力槽位 = 空心圆环（灰色，作为可点击落点提示）；资源点指示物 = 实心圆（按资源色）+ 数量为 2 时叠一圈描边。
6. 新增 `Scripts/Game/MapVisuals/MapStateRenderer.cs`（MonoBehaviour，挂在 `MapRoot`）：
   - `Initialize(MapState state, Transform markerRoot, IReadOnlyList<MapNodeView> nodes, IReadOnlyList<MapRouteView> routes)`
   - `Refresh()`：**全量销毁重建**一个名为 `StateMarkers` 的子容器下的所有标记（地图只有 22 点 + 22 航道，全量重建足够且不易出 bug）。逐个资源点：按 `Slots.OwnerOf(i)` 生成实心/空环标记；`HasResourceToken` 时在右上方生成资源色标记（`TokenAmount==2` 时加描边）；某玩家 `MobileCityNodeId == node.Id` 时生成更大的玩家色圆并加白描边。逐条航道：在 `MapRouteView.SlotLocalPosition(i)` 处生成槽位标记。每个资源点旁生成世界空间 TMP 标签显示 `Code`（可在 Inspector 关闭）。
   - 排序：底图 -10；槽位/指示物 0；影响力 5；移动城市 10；标签 20。
   - `bool TryPick(Vector2 world, out MapHit hit)`：在 `Refresh()` 时同步维护一张 `List<MapHit>`（世界坐标 + 半径 + 类型 + 优先级），拾取时取「在半径内且距离最近」的目标，同距按优先级（移动城市 > 影响力 > 槽位 > 资源点）取。
   - 可选：`SetHighlight(IEnumerable<InfluenceSlotRef> / nodeIds)`，把当前模式的合法目标描边高亮，便于操作。若实现成本可控就做，否则留待下期。
7. 新增 `Scripts/Game/MapVisuals/MapHit.cs`：`readonly struct MapHit { MapHitKind Kind; InfluenceSlotRef Slot; int NodeId; Vector2 World; }` + `enum MapHitKind { NodeSlot, RouteSlot, NodeBody }`。
8. 新增 `Scripts/Game/MapVisuals/MapPointerInput.cs`（MonoBehaviour，挂在 `MapRoot`）：序列化 `Camera` 与 `MapStateRenderer` 引用；`public event Action<MapHit> Clicked`。`Update()` 里：`Mouse.current` 为空则返回；`Mouse.current.leftButton.wasPressedThisFrame` 为真时，先判 `EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()` —— 命中 UI 就**直接丢弃**（防止点 HUD 时穿透到地图）；否则 `camera.ScreenToWorldPoint(...)` → `renderer.TryPick(...)` → 命中则 `Clicked?.Invoke(hit)`。

### Phase C —— UI 层：HUD 与行动面板

9. 新增 `Scripts/UI/GameHud.cs`：`Bind(GameEngine engine)` + `Refresh()`。顶栏显示 `回合 N/8 · 阶段 · 当前行动座位`；下方每个座位一行：颜色块、分数、金券、三种基础资源数 + 至纯源石、供应堆标记数、移动城市所在资源点编号（`-1` 显示「未停靠」）。只显示公开信息，**不显示**手牌 / 盖放牌。另含一行状态文本用于显示最近一次命令结果或失败原因。
10. 新增 `Scripts/UI/ActionPanel.cs`：一组按钮 + 状态文本。`event Action<ActionMode> ModeRequested`、`event Action CommitRequested`、`event Action CancelRequested`、`event Action AdvancePhaseRequested`。按钮：`部署` / `调度` / `城市移动` / `推进阶段`（`AdvancePhaseCommand`）。未实现的行动（探索 / 建设 / 特殊行动 / 使用角色牌 / 宣告样式）**置灰并标注「待实现」**，不占用逻辑。`SetHint(string)` 用于显示「已选 1/2 步」「请点击一个空格」这类引导。
11. 新增 `Scripts/UI/GameScreenController.cs`（MonoBehaviour，挂在 `GameRoot` 或 Canvas）：持有 `engine` / `renderer` / `input` / `hud` / `panel` 引用，`Initialize(...)` 里订阅事件。内部状态机：
    - `ActionMode.None / Place / Relocate / MoveCity`，选模式时校验 `engine.State.Phase == PlayerActions` 且当前有 `ActiveSeat`。
    - **Place**：点槽位 → 派发 `PlaceInfluenceCommand(ActiveSeat, slot)` → 成功后回到 `None`。
    - **Relocate**：点自己的影响力槽 → 记 `from`；点空槽 → 压入 `pendingMoves`（上限 2，超出提示）；面板 `执行调度` → 派发 `RelocateInfluenceCommand(ActiveSeat, pendingMoves)`（1–2 步）；`清除` 清空。
    - **MoveCity**：点资源点 → 派发 `MoveMobileCityCommand(ActiveSeat, nodeId)`。
    - 每次派发后：`!Ok` 时把 `result.Error` 原样写进 HUD 状态文本（引擎已给出很完整的中文原因，直接透出即可，**不要另造一套提示**）；`Ok` 时清空选点状态并刷新。
    - 订阅 `engine.Dispatcher.CommandApplied` 统一做 `renderer.Refresh() + hud.Refresh()`。

### Phase D —— App 层与场景接线

12. `Scripts/App/GameBootstrap.cs`：新增序列化字段 `mapStateRenderer` / `mapPointerInput` / `gameHud` / `actionPanel` / `screenController`。在现有 `Engine` 建好之后追加：`renderer.Initialize(...)` → `screenController.Initialize(engine, renderer, input, hud, panel)` → 首次 `Refresh()`。`Awake` 末尾保留现有的开局日志。
13. `Scripts/Core/Engine/TurnFlow.cs`：在 `AdvancePhase` 的 `PlayerActions → Harvest` 分支后，用上面「临时占位」的自动采集调用 `HarvestResolver.Resolve`，并把报告写进一个可被 UI 读取的地方（如 `GameEngine` 抛一个 `HarvestResolved` 事件，或在 `GameState` 上留最近一次报告字段）。**这是本轮唯一一处 Core 改动，且已标注为临时方案。**

### Phase E —— 场景手工搭建（编辑器内）

14. `Game.unity` 里：新增 `EventSystem`（挂 `InputSystemUIInputModule`，照抄 MainMenu 场景）+ `Canvas`（Screen Space - Overlay，`CanvasScaler` 参考分辨率与 MainMenu 保持一致，`GraphicRaycaster`）。Canvas 下建 `HUD`（顶部条 + 玩家行容器 + 状态文本）与 `ActionPanel`（底部按钮组 + 提示文本），字体用已有的 `NotoSansSC-Regular SDF`。
15. 把 `MapStateRenderer` + `MapPointerInput` 挂到 `MapRoot`；`GameHud` / `ActionPanel` 挂到 Canvas 对应节点；`GameScreenController` 挂到 `GameRoot`；在 `GameRoot` 的 `GameBootstrap` 上把 5 个新引用逐一拖好。

## 明确不在本轮范围

- 探索 / 建设 / 特殊行动 / 使用角色牌 / 宣告城市样式（依赖卡面文案，见 §14）
- 采集阶段的选点 UI（本轮用自动采集占位）
- 卡牌 ScriptableObject 化与卡面接线、抽卡动画、圆角卡框
- 联机同步、AI 对手、存档

## 验证方式

1. 在 Unity 里打开 `Assets/_Project/Scenes/Game.unity` 直接 Play（`GameSession` 默认 4 人），确认：无报错、Console 出现开局日志、地图上出现各资源点编号标签与空影响力槽位圆环、HUD 显示 4 名玩家。
2. 点「推进阶段」越过角色牌盖放，进入玩家行动阶段；确认 HUD 的「当前行动座位」高亮随之变化。
3. 依次验证三个行动，每步都要肉眼核对「地图标记」与「HUD 数值」一致：
   - 部署：点任意空槽 → 出现对应玩家色圆点，该玩家供应堆标记数 -1
   - 调度：点自己的圆点再点另一个空槽 → 执行调度 → 圆点搬过去，标记总数不变
   - 城市移动：先通过若干次部署/调度把某玩家城市所在点留出空槽 → 点相邻资源点 → 城市圆标移动、出发点出现影响力、源石 -3
4. 逐条触发非法操作（例如轮到别的座位时点自己、城市移动点到不相邻的点），确认 HUD 状态文本显示引擎给出的中文原因、且地图状态**没有**被改动。
5. 连续按「推进阶段」走完 8 个回合，确认采集到收尾到下一回合的循环能自然推进、终局能走到结算。
6. 在「窗口 → 常规 → 测试运行程序」跑 EditMode，确认原有 **73 个测试仍然全绿**。
7. 取景核对：若 4 人图超出视野，调整 `Main Camera` 的 `Size`（或给 `MapRoot` 加缩放）——这是手动微调，不做自动适配组件。

## 风险与注意点

- **命名空间 / 分层**：新增的 Game 层代码一律用 `NomadCity.Game`，UI 层一律 `NomadCity.UI`；不要为了图方便把 UI 代码塞进 App。
- **点击穿透**：`EventSystem.IsPointerOverGameObject()` 这个判断不能省，否则点底部按钮会同时点到底下的地图。
- **重入**：`Refresh()` 全量重建标记时，先 `Destroy` 旧容器再新建；注意 `TryPick` 用的命中列表必须与本次重建同步，避免点到上一帧的残留目标。
- **`StartGame` 之后阶段是 `CharacterPlacement`**：必须靠「推进阶段」才能到 `PlayerActions`，这是当前骨架的设计，本期不改（角色牌盖放逻辑未实现）。
- **Core 只改一处**（Phase D 第 13 步的自动采集），其余全部是新增的视图/交互代码，符合「改动尽量小」的偏好。
