# 事件卡 UI 面板 — 实现计划

## Context（为什么做）

开局入场 §4.3 步 2 的**引擎侧已经打通**：
- `ChooseStartNodeCommand`：选绿点 → 停靠移动城市 → 翻绿色牌 + 放资源点指示物（+ B-01 特例）
- `ResolveEventOptionCommand`：结算事件选项奖励（条件式、尽量执行语义都已实现）

但**没有任何 UI**：玩家点绿色资源点后，引擎已经翻了牌、放了指示物，屏幕上却看不到卡、也点不了选项 → 奖励领不到，开局闭环缺最后一环。本计划补上「把翻开的事件卡用中央模态弹窗展示出来，玩家点卡面选项行领奖励」。

## 已核实现状

- 三张正面拼版图（`Art/Cards/Event/card_event_green|yellow|red.jpg`）的 `.meta` 目前 `spriteMode=2` 但**只切出 1 个覆盖整图的 sprite**，尚未按 **850×600** 切成单卡。
- [EventCards.md](file:///d:/Unity%20Program/Nomad%20City%20The%20Founders/Doc/EventCards.md#L3-L28) 已给出每张卡的**素材位置 `r行c列`**（网格 850×600，按 id 从左到右、从上到下编号）与牌面版式（标题栏 / 描述文 / 事件区）。
- 开局只会翻**绿色牌（400–405，共 6 张）**，每张 **2 个选项且全部无代价**（代价式选项只出现在黄/红牌）。
- 卡面上的卡名、描述文、选项文案与奖励图标**都已烘进图片** → 用卡面图展示最省事，也不会踩 TMP 缺字。
- 场景 `Canvas` 为 1920×1080 `Scale With Screen Size`；[GameBootstrap.cs](file:///d:/Unity%20Program/Nomad%20City%20The%20Founders/Assets/_Project/Scripts/App/GameBootstrap.cs#L29-L68) 用 `[SerializeField]` 接线各 UI；[GameScreenController.cs](file:///d:/Unity%20Program/Nomad%20City%20The%20Founders/Assets/_Project/Scripts/UI/GameScreenController.cs#L163-L214) 的 `HandleChooseStartNode` 派发后无后续 —— 正是接入点。

## 设计决策（已与用户确认）

1. **选项交互**：热区叠在卡面选项行上（点击卡面文字所在那一行）。
2. **面板形式**：屏幕中央模态弹窗 + 半透明遮罩，选完关闭。
3. **落地方式**：用户手动切图与在场景里搭面板；我写脚本 + 接线 + 双语步骤。

> 为什么不能由我生成预制体：新脚本的 `.prefab` 引用需要脚本 `GUID`，而 `GUID` 要等 Unity 导入 `.cs` 后才生成；手写 `.prefab`/`.unity` 不可靠，且项目规定不手改场景文件。

## 改动清单

### A. 我写/改的代码（2 个文件）

**1. 新增 `Assets/_Project/Scripts/UI/EventCardPanel.cs`**（UI 层；只做展示与发意图，不做规则判断，与 [ActionPanel.cs](file:///d:/Unity%20Program/Nomad%20City%20The%20Founders/Assets/_Project/Scripts/UI/ActionPanel.cs#L18-L29) 同一定位）

- `[SerializeField]` 字段：
  - `GameObject root`（整块弹窗根，Show/Hide 用）
  - `Image dim`（半透明遮罩，**保留 raycastTarget**，用来挡住地图点击）
  - `Image cardFace`（卡面，`raycastTarget` 关闭）
  - `Button[] optionHotzones`（长度 3；开局绿牌只用前 2 个，第 3 个留槽位）
  - `Sprite[] cardFaces`（长度 24；下标映射见下）
  - `TextMeshProUGUI statusText`（可选：显示结算结果）
- 事件：`public event Action<int> OptionSelected;`（选项下标；由 GameScreenController 派发命令）
- 方法：`Show(int cardId, int optionCount)` / `Hide()` / `ShowReport(EffectReport)` / `static int SpriteIndexOf(int cardId)`
- 逻辑：`Show` 把 `cardFaces[SpriteIndexOf(cardId)]` 贴到 `cardFace`，按 `optionCount` 启用前 N 个热区、隐藏其余并刷新布局。
- 下标映射 `SpriteIndexOf`：`400–421 → 0–21`（即 `id-400`）；企业牌 `450 → 22`、`451 → 23`。

**2. 改 `Assets/_Project/Scripts/UI/GameScreenController.cs`**
- 新增 `[SerializeField] EventCardPanel eventCardPanel;`
- `HandleChooseStartNode` 派发成功后：取 `node.EventCardId` 与 `state.Catalog.Event(id).Options.Count`，调 `eventCardPanel.Show(...)`
- 订阅 `eventCardPanel.OptionSelected` → 派发 `ResolveEventOptionCommand(seat, nodeId, index, acceptCost: true)` → 读该命令实例的 `.Report` → 结果写到 HUD 状态行（无合适方法则在 HUD 加一行）→ `eventCardPanel.Hide()` → `RefreshAll()`
- B-01 选点走完全相同的路径（它也翻牌、也有选项）

### B. 用户手动完成（执行时我给双语分步指引）

1. **切图**：三张拼版图各开 Sprite Editor → Slice → `Grid By Cell Size` = **850 × 600** → Apply（绿 6 / 黄 12 格 / 红 8 格）。
2. **搭面板**：在 Canvas 内新建层级（见下），挂上 `EventCardPanel` 组件并填引用。
3. **填卡面**：把切好的子 sprite 按 绿(6)→黄(10)→红(6) 分组拖到 `cardFaces`（Unity 选中多个子贴图拖到数组首元素会顺序填充）。
4. **接线**：`GameScreenController` 的 `eventCardPanel` 槽位、`GameBootstrap` 若有需要一并补。

## 建议层级结构

```
Canvas
└─ ModalLayer                      ← Canvas 最后一个子节点（保证最上层）
   └─ EventCardPanel (root)       ← 挂 EventCardPanel
      ├─ Dim        (Image, 黑/半透明, raycastTarget = ON)
      └─ Card       (RectTransform, 例如 700×494)
         ├─ Face      (Image, 贴 cardFaces[...], raycastTarget = OFF)
         ├─ Hotzone1  (Button + 透明 Image, raycastTarget = ON)
         ├─ Hotzone2  (Button + 透明 Image, raycastTarget = ON)
         ├─ Hotzone3  (Button + 透明 Image, 默认隐藏)
         └─ StatusText (TMP，显示结算结果)
```

> 注意：`Dim` 和 3 个热区是**功能性**元素，`raycastTarget` 必须保留 ON；卡面、装饰元素保持 OFF（与项目既有约定一致）。

## 待实现时校准的点

- **3 个热区默认位置**：按「事件区在卡面下 8%~42% 高度、宽向留边」给一组默认值。因为要看实际切出来的卡面，执行时我会对着卡面调一次；你也能直接在 Inspector 里拖 RectTransform 对齐选项行（符合你手动微调的习惯）。
- **卡面显示尺寸**：默认 700×494（0.82×），可按视觉调整。

## 已知限制（本次不做）

- 开局只翻绿牌、选项都无代价 → 命令一律 `acceptCost = true`。黄/红牌的「支付代价 → 奖励」二选一交互留到做探索 / 城市移动时再补。
- 「放置影响力」类奖励仍自动取第一个合法空格（`FirstAvailableChoices`），等选项 UI 完善后再换成玩家点选。
- 企业扩展替换牌 `450 / 451` 的卡面素材归属未确认（黄/红拼版图的空格是否为它们）；本次不涉及。

## 验证

- EditMode 不涉及 Core，104 项测试应保持全过（改完用临时 csproj 命令行跑一次确认）。
- Play 模式：4 人游戏 → 第 1 回合点绿色资源点 → 弹出卡面 → 点选项行 → HUD 显示奖励 → 面板关闭 → 指示物已在资源点上、绿色牌堆少一张。
- 逐项检查：遮罩挡住地图点击、装饰元素取消 raycast、面板关闭后能继续选下一个玩家的绿点。
