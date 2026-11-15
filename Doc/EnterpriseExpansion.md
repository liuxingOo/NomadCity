# 企业扩展版 · 规则规格

> **来源**：规则书 P15–P22（企业扩展版玩法）
> **定位**：本文件是 `RuleSpec.md` 的**扩展模块**。基础规则不变的部分不再重复，只记录**新增**与**改写**。
> **标注约定**：`⚠️待核实` = 扫描件文字不可读或规则书未展开

---

## 1. 模块概述

企业扩展不是「补几张牌」，而是给游戏加了一套**与分数轨道并行的公共成长系统**——**企业合作等级 + 企业特效**。

它在 `RuleSpec.md` 第 13 节的挂载点上做了以下改动：

| 挂载点 | 改动 |
|---|---|
| 起始设置·事件牌 | 替换 2 张同名事件牌 |
| 起始设置·城市样式牌 | 12 张分 I / II 级两个牌堆，分别抽 4 张与 2 张 |
| 起始设置·设施牌堆 | 混入 3 张设施牌 |
| 起始设置·指示物 | 新增 8 个公路指示物 |
| 起始设置·新增牌堆 | 新增 8 张合作企业板堆 |
| 玩家设置·手牌 | 5 张 → **7 张** |
| 开局入场·新增步骤 | 决定初始企业并提升 1 级 |
| 开局入场·新增步骤 | 弃 2 张手牌放回游戏盒 |
| 开局入场·条件分支 | 罗德岛 / 莱茵生命的特殊处理 |
| 拓荒探索·触发点 | 红色区域开放回合的角色牌盖放阶段前，每名玩家各选 1 家企业，把自己的合作等级提升 1 级 |
| 底层规则·新增 | 公路指示物覆盖航道；角色牌放回游戏盒后的延迟结算 |
| **新增并行系统** | **企业合作等级 + 企业特效** |

---

## 2. 扩展配件列表

| 组件 | 数量 | 备注 |
|---|---|---|
| 初始角色牌 | 8 | 4 种颜色，每种颜色 2 张：**山、玛恩纳** |
| 干员角色牌 | 8 | 见 `CardEffects.md` 第 5 节 |
| 设施牌 | 3 | 洗入设施牌堆 |
| 事件牌 | 2 | 黄色 1 张、红色 1 张 |
| 城市样式牌 | 6 | I 级 4 张、II 级 2 张 |
| 合作企业板 | 8 | 新增牌堆 |
| 莱茵生命科室板 | 3 | 莱茵生命专属 |
| 公路指示物 | 8 | 新增指示物 |

> 在游戏过程中如有需要，玩家可以使用喜欢的指示物替代不足的**公路指示物**。

---

## 3. 规则变化 · 起始设置

### 3.1 公共版面

1. **洗切事件牌前**：使用扩展内包含的 2 张事件牌**替换同名的 2 张事件牌**（牌面详情见 `EventCards.md` §6）。
   - `洞穴遗迹` → `洞穴遗迹ᴱ`（扩展版，黄色 · 牌名右上角标「E」）
   - `采集平台残骸` → `采集平台残骸ᴱ`（扩展版，红色 · 牌名右上角标「E」）
2. **抽取城市样式牌时**：将扩展包内的城市样式牌与基础版内的城市样式牌**共 12 张**按照牌背标识分为 **I 级、II 级两个牌堆**；将 I 级牌堆洗混后**抽取 4 张**，II 级牌堆洗混后**抽取 2 张**，牌面朝上放在地图旁。
3. **准备设施牌堆和设施供应区时**：将扩展包内 **3 张设施牌混入设施牌堆**。
4. **设置指示物时**：将 **8 个公路指示物**放置在地图旁备用。
5. **额外准备合作企业板堆**：取出 **8 张合作企业板**，洗混背面朝上放在地图旁备用。

### 3.2 玩家版面

1. **拿取玩家配件时**：将扩展包内 **2 张对应颜色的初始角色牌**（山、玛恩纳）加入自己的手牌。此时玩家的手牌**共有 7 张**。

   > **等价表述**：每个阵营此时拥有 7 张角色牌（基础 5 + 扩展 2）。随后的「开局入场·弃 2 张手牌」本质上是**从 7 张中挑选 5 张**作为本局的初始角色牌，其余放回游戏盒。两种说法实现结果完全一致。

---

## 4. 规则变化 · 开局入场

### 4.1 在决定初始资源点前：决定初始企业并获得初始企业合作等级

1. 每名玩家各从合作企业板堆**抽取 2 张**合作企业板，从抽取的 2 张中**选择 1 张**作为自己的初始企业板。
2. 将所有玩家选择的合作企业板排列在地图旁备用。**剩余的合作企业板放回游戏盒，本局游戏不会再使用到。**
   - ⚠️**三人游戏例外**：先不要收盒子。先按 4.4 从「未被抽取的企业板」中随机抽 1 张作为第四张排在地图旁，**之后**才把剩下的放回游戏盒。
3. 每名玩家取出 **4 个玩家标记**，在**场上每一张已被选出的合作企业板上各放 1 个**，初始都放在「**0**」级位置。（规则书原文「分别放在**每张**合作企业板的『0』级位置」即此意。）
4. 之后，每名玩家**提升 1 级自己的初始企业板**（把自己在**那张**板上的玩家标记向上移动 1 格），并**结算目标格的奖励**。

> **结果状态**：场上每张企业板都是「**1 个标记在 1 级 + 3 个标记在 0 级**」，处在 1 级的正是**选择该板的玩家**。
> **易错点（已修正）**：此前提过「4 个标记全放在自己那一张板上」的读法，与 6.1 及上述状态矛盾，**以后者为准**。
> **与事件牌的关系**：事件牌上的「选择 1 家企业」**只是选定一张企业板，本身不附带等级变化**（见 `EventCards.md` §6）。

> **范例**：`富森银行` 是红色玩家选择的初始企业板，他需将其上的红色玩家标记向上移动一格。标记到达 1 级，将奖励他**解锁一个【企业特效】**。此时红色玩家**无法触发**该企业特效，即无法立刻获得 8 金券。只有后续使用【触发企业特效】的效果才能使用此特效获得 8 金券。
>
> ✅**已按实体企业板核对（2026-11-11）**：富森银行 **1 级**特效果真是「获得 8 金券」，**3 级**特效是「获得 20 金券 + 每位对手 4 金券」。此前记录的「数字冲突」是两个不同等级，**并不矛盾**。

### 4.2 在决定初始资源点后：弃 2 张手牌

- 每名玩家**选择 2 张手牌背面朝上放回游戏盒**，这些牌本场游戏将不会再使用到。
- 这些放回游戏盒的牌**不是公开信息**，玩家不能在游戏中查看。

> **第一次游玩时推荐**：保留「角色牌·山」和「角色牌·玛恩纳」。

### 4.3 特殊的企业

| 企业 | 特殊处理 |
|---|---|
| **罗德岛** | 被选择时，取出 **8 张干员角色牌**放在地图旁作为**干员牌堆**备用。 |
| **莱茵生命** | 被选择时，取出 **3 张莱茵生命科室板**放在地图旁备用。选择莱茵生命作为初始企业板的玩家**选择一张科室板激活**。 |

### 4.4 三人游戏补充

- 每名玩家选择后，从**未被抽取的企业板中随机选择 1 张**作为**第四张**合作企业板，与玩家选择的合作企业板排列在地图旁备用。将剩余的合作企业板放回游戏盒。
- 随机选择**莱茵生命**时，取出 3 张莱茵生命科室板放在地图旁备用，并**随机选择一张激活**。

---

## 5. 规则变化 · 拓荒探索

### 5.1 使用公路

玩家使用**角色牌·山的策略**，将一条航道变为**公路**后，**所有玩家**使用该公路（进行探索或采集）均**无需支付路费**。

### 5.2 额外的企业合作等级

当游戏进行至**红色区域的资源点开放探索时**（即四人游戏的**第 4 回合**、三人游戏的**第 5 回合**、或二人游戏的**第 6 回合**），在该回合的**角色牌盖放阶段前**，从起始玩家开始，按顺时针顺序**每名玩家选择一家企业提升 1 级企业合作等级**。

> **引擎要点**：这个触发时机的回合数与 `RuleSpec.md` 13.2 的人数变体参数表共用同一配置项，不得各写一份。

---

## 6. 企业合作机制

> 「拓荒，可不只有建设城市。这是门生意，互惠互赢，谁不爱呢？」

企业合作机制是游戏中**所有玩家都可以使用的公共机制**；玩家通过**提升企业合作等级**和**触发企业特效**来逐步解锁奖励与发动企业提供的效果。

### 6.1 游戏初期

游戏开始时，玩家的企业合作等级通常处于 **「0」** 的位置。直到玩家在**开局入场·获得初始企业合作等级**时获得 **1 级**初始企业合作等级。因此：

- **2 人游戏外**，第 1 回合的角色牌盖放阶段，每位玩家会有 **1 个玩家标记**处于不同的合作企业板 **1 级**位置；
- 每位玩家的其余 **3 个玩家标记**均处于 **0 级**位置。

> **重要**：提升企业合作等级与触发企业特效是**两种不同的效果**。

### 6.2 提升企业合作等级

提升一级企业合作等级时，将自己在该企业板上的玩家标记**前进一格**。

1. **目标格不包含解锁企业特效**：玩家**立即结算目标格的奖励**。
2. **目标格包含解锁企业特效**：如果该奖励有【企业特效】标识，则效果被**解锁**。
   - **被解锁的企业特效不能直接发动**，必须通过其他能够触发企业特效的效果来发动。
3. **复数玩家同时提升**：玩家使其他玩家提升企业合作等级时，其他玩家可以正常获得面板上提供的奖励。当复数玩家同时获得奖励时，**由发动提升企业合作等级效果的玩家先获得奖励**，之后其他玩家按照**顺时针顺序**依次获得。

### 6.3 触发企业特效

玩家触发企业特效时，**必须从已解锁的企业特效中选择一个发动**。

### 6.4 卡面图标（2026-11-11 已与规则持有者确认）

事件牌、角色牌（山／玛恩纳）上出现的两种企业图标：

| 图标形状 | 含义 |
|---|---|
| 握手 + 三道向上箭头 | **提升 1 级企业合作等级**（「所有玩家」字样出现时，全部玩家各提升 1 级） |
| 三角框 + 握手 | **触发 1 个已解锁的企业特效**（从自己在该企业板上已解锁的特效中选 1 个发动） |


---

## 7. 底层规则（扩展新增）

### 7.1 被放回游戏盒的角色牌

一些游戏中的效果会要求玩家将角色牌**放回游戏盒**。若玩家该回合使用其策略或计谋，并且其效果带有**「该回合收尾阶段结算」**，这些效果**依旧会在对应游戏阶段正常结算**。

### 7.2 被公路指示物覆盖的航道

当玩家使用**角色牌·山的策略效果**时，会放置一个**公路指示物覆盖该航道的所有影响力空格**。

- 之后，玩家**无法**在该航道上放置影响力，或移动影响力到该航道上。
- 玩家进行**探索或采集**时，使用该航道**无需支付路费**。

> **引擎要点**：公路指示物同时是「影响力空格的禁用标记」和「路费的豁免标记」，需要作为航道的一个独立状态字段（如 `isHighway` / `roadBlocked`）建模。

---

## 8. 企业效果详解

> **来源**：规则书 P21–P22。共 8 家企业，**每家的效果分布在不同的企业合作等级上**（规则书只展开了部分等级）。⚠️未列出的等级效果需从实体合作企业板逐张录入。

### 8.1 黑钢国际（BS BLACKSTEEL）

*泰拉规模最大的安全顾问企业，接受从私人安保到武力干涉在内的多种类安保委托。*

| 等级 | 效果 |
|---|---|
| **4 级** | 玩家可以选择**移除 2 个影响力**或**放置 2 个影响力**，也可以选择**移除 1 个影响力和放置 1 个影响力**。执行的顺序**由玩家自行决定**。 |

### 8.2 海德兄弟（HD）

*来自堡巫山的建材业寡头，在哥伦比亚多个城市的建材市场拥有垄断地位，可提供大额建材订单和工程项目。*

| 等级 | 效果 |
|---|---|
| **4 级** | 使用该特效时，玩家必须将**设施供应区的全部 6 张设施牌洗回牌堆中**，之后**重新抽 6 张**作为设施供应区。当设施供应区的设施牌**不足 6 张**时，玩家需要**尽量执行**该效果。<br>重抽之后，玩家可以从新的设施供应区中**选择 1 张设施牌进行建设**。在建设该设施时**不需要支付费用**。 |

### 8.3 雷神工业（RAYTHEAN）

*富有创新精神的制造业领军者，致力于大型机械、电气产品和军工设施的开发和生产，囊括城市建设的方方面面。*

| 等级 | 效果 |
|---|---|
| **3 级 / 5 级** | 玩家可以进行**不占用主要行动的建设**。企业特效在**一回合内没有使用次数限制**。当玩家通过企业特效建设了「**企业办事处**」时，玩家依旧可以因为「企业办事处」的入场效果**再次触发企业特效**，进行一次新的建设。 |
| **5 级** | 玩家从牌堆中选择设施时，如果**无法满足建造的条件**（玩家城市面板上没有空位、玩家没有足够的资源建造任意一张设施牌、或玩家不想建造），则必须**洗混设施牌堆结束企业特效的结算**。 |

### 8.4 雷姆必拓（ARIM SOLUTIONS）

*由多家矿业企业组成的联合集团，其矿区管理和开采技术在全泰拉首屈一指，目前正在哥伦比亚开拓业务。*

| 等级 | 效果 |
|---|---|
| **4 级** | 玩家可以**立刻结算一次采集**，其效果等同于该玩家**单人进行采集阶段**。计算路费时必须**按照当前局面**进行计算。该玩家在玩家行动阶段结束后，**依旧可以进行采集阶段**。 |

> **引擎要点**：这是「插入一次额外采集」的效果，需要在采集阶段之外调用采集结算逻辑，且**不可重复领取**——需要给采集动作加上「本回合已执行」的标记或直接复用阶段入口。

### 8.5 富森银行

*老牌金融企业，自独立前就设有拓荒金融服务。根据建设规模和信用评级向拓荒城市提供大额金券借贷，为拓荒者换来物无价未来。*

| 等级 | 效果 |
|---|---|
| **5 级** | 获得 3 分（即时奖励）。 |
| **4 级** | 【企业特效】你获得 1 分 → 1 位对手失去 1 分。 |
| **3 级** | 【企业特效】玩家**先获得 20 金券**，之后**除了发动该效果的玩家以外，其余玩家均获得 4 金券**。 |
| **2 级** | （空白，无奖励。） |
| **1 级** | 【企业特效】获得 8 金券。 |

> ✅**已按实体企业板核对（2026-11-11）**：8 金券出自 **1 级**、20 金券出自 **3 级**，两者是不同等级，此前记录的「规则书内部矛盾」不成立。

### 8.6 约翰老妈（MAMA JOHN'S）

*从贸易城市提卡伦多发展起来的本土综合性企业，业务涉及食品、制造业、金融等方面，是拓荒运动中联邦授权的主要合作商之一。*

| 等级 | 效果 |
|---|---|
| ⚠️待核实 | 规则书未展开该企业的具体特效等级与效果 |

> **已知的关联信息**（来自 P18 范例）：玩家与约翰老妈的企业合作等级**提升至 3 级时，可以解锁一个企业特效**。等级提升时**不得分**，之后触发约翰老妈的 **3 级**企业特效时，才能获得 1 分。

### 8.7 莱茵生命

*哥伦比亚的本土科技巨头，旗下科室的研究范围囊括源石技艺、新型材料、实验性武装等诸多前沿领域。*

**特征**：**不依赖触发企业特效依然能发挥作用**——它的 **2 级到 5 级奖励都会在玩家标记移动到上面时一次性生效**，和其他合作企业板上的分数奖励相同。

- 莱茵生命附带 **3 张不同科室的面板**，拥有不同的效果，但**只有 1 张能处于激活状态**；玩家可以通过发动其「企业特效」来**替换激活的面板**。
- 玩家因为**角色牌·山的策略效果**让其他玩家也提升企业合作等级时，他们**也可以获得激活面板上提供的奖励**。但是玩家触发 **1 级**企业特效使得莱茵生命的激活面板被替换时，**其他玩家不会因为这个变化获得对应的奖励**。

**开局入场时**：额外取出 3 张莱茵生命科室板放在地图旁备用。由选择莱茵生命作为初始企业板的玩家选择一张激活。

### 8.8 罗德岛制药

*具备应对源石感染专业能力的制药企业，其业务范围可延伸至基于源石感染危机的准军事行动，其行动干员可在多方面提供战术支持。*

**特征**：**玩家提升企业等级后，可以获得特殊的干员角色牌替换手牌中的角色牌。**

**开局入场时**：如果罗德岛被选出，额外取出 **8 张干员角色牌**放在地图旁作为**干员牌堆**备用。

| 等级 | 效果 |
|---|---|
| **3 级 / 5 级** | **干员特派**：玩家可以从干员牌堆**抽 2 张查看**，之后玩家有 2 种选择：<br>a. 在 2 张干员角色牌中**选择 1 张加入手牌**，将未选择的干员角色牌**洗回干员牌堆**。此时，玩家**必须从自己的手牌或角色牌弃牌区中选择 1 张角色牌向所有玩家展示，之后放回游戏盒**。<br>b. 将抽到的干员角色牌**洗回干员牌堆**。之后**获得 1 分**。 |
| **4 级** | 如果玩家角色牌**弃牌区没有牌**，则**不能使用**该特效。<br>玩家在使用该特效时，需要**先从角色牌弃牌区回收 1 张角色牌并向所有玩家展示**。之后，**除了被回收的角色牌，所有手牌面朝上进入弃牌区**。 |

> **补漏记录（2026-10-09）**：3/5 级 a 项的「必须从手牌或弃牌区选 1 张角色牌展示后放回游戏盒」原先遗漏。这是罗德岛「减手牌」的真正来源，也是下一段「角色牌放回游戏盒后遗留效果依旧生效」范例的前提，引擎必须实现。

> **补充规则**：角色牌**放回游戏盒后，该角色牌遗留的效果依旧可以生效**。
> **范例**：玩家在使用角色牌·雷蛇的策略后结算罗德岛的 3 级奖励，抽取罗德岛干员后将角色牌·雷蛇放回游戏盒；在该回合的收尾阶段，玩家**依旧需要移除 1 个自己的影响力**。

---

## 9. 待核实清单

| # | 缺项 |
|---|---|
| 1 | ~~企业合作的初始等级标记位——每张企业板从 0 级到 5 级各格的奖励内容~~ —— **已按卡面录入**，见 `Core/Model/Enterprise/EnterpriseCatalog.cs` |
| 2 | ~~**约翰老妈**的全部企业特效~~ —— **已按卡面录入** |
| 3 | ~~**莱茵生命** 3 张科室板的具体效果~~ —— **已按卡面录入** |
| 4 | ~~2 张扩展事件牌（`洞穴遗迹`、`采集平台残骸`）的替换后内容~~ —— **已录入**，见 `EventCards.md` §6 |
| 5 | ~~3 张扩展设施牌的牌面（费用 / 分数 / 入场效果）~~ —— **已录入**，见 `FacilityCards.md` §6.3（id 600–602，`CardCatalog.BuildEnterpriseFacilities`） |
| 6 | ~~6 张扩展城市样式牌（I 级 4 张、II 级 2 张）的宣告条件与奖励~~ —— **已录入**（id 550–555，`CardCatalog.BuildEnterpriseCityPatterns`） |
| 7 | ~~山、玛恩纳的**卡面完整文案**~~ —— **已录入**，见 §3.2 / §4.2 与本文件 9.2 |
| 8 | ~~「企业办事处」「干员特派」等设施的入场效果原文~~ —— **已核实**（2026-11-15）：企业办事处卡面（`card_facility.jpg` 切片 49–51）入场效果就是「选择 1 家企业」+「触发已解锁企业特效」图标，与现有实现一致；干员特派见 §2 罗德岛 3 / 5 级 |

> **补录建议**：合作企业板、科室板、扩展事件牌、扩展设施牌、扩展城市样式牌需要整批扫描后逐张录入。

---

## 9.1 落码（2026-11-10，第一阶段）

> 企业板 / 科室板卡面已扫描录入（`Assets/_Project/Art/Cards/Enterprise/card_enterprise.jpg`、`laiyin1–3.png`），
> 上面「待核实清单」的 1／2／3 项已按卡面补全；以下为实现现状。

| 已实现 | 位置 |
|---|---|
| 8 块企业板 × 0–5 级、3 张科室板 × 2–5 级的文字与可执行效果 | `Core/Model/Enterprise/EnterpriseCatalog.cs` |
| 企业牌堆、场上企业板、各座位合作等级、科室板、每回合特效使用记录 | `Core/Model/Enterprise/EnterpriseState.cs` |
| 开局洗牌、3／4 人局每人抽 2 张、2 人局每人抽 4 张、2／3 人局补足 4 块、初始资金 +5 金券 | `Core/Engine/EnterpriseRules.cs`、`Core/Engine/BoardSetup.cs` |
| 抽 2 选 1 / 2 人局抽 4 选 2（`ChooseInitialEnterpriseCommand`）、提升 1 级（`AdvanceEnterpriseLevelCommand`）、触发特效（`TriggerEnterpriseEffectCommand`） | `Core/Engine/Actions/EnterpriseCommands.cs` |
| 联机注册与快照（快照版本升到 5） | `Core/Net/CommandKind.cs`、`CommandCodec.cs`、`GameStateSnapshot*.cs` |
| 界面入口：操作区「企业」按钮 → 开局抽选 / 待提升 / 总览查看；企业特效本身只能由卡牌效果（角色牌 / 设施 / 城市样式特殊行动 / 事件牌）解锁并触发 | `UI/GameScreenController.cs` |

## 9.2 落码（第二阶段：山 / 玛恩纳 + 公路）
| 已实现 | 位置 |
|---|---|
| 手牌 5 → 7：新增四色 × {山, 玛恩纳} 共 8 张角色牌（id 20–27），统一走扩展 id 段 | `Core/Model/Cards/CardCatalog.cs` |
| 开局入场「7 选 5」：`DiscardSetupHandCardsCommand`（放弃 2 张放回游戏盒；未完成不能行动 / 盖放） | `Core/Engine/Actions/CharacterCardCommands.cs`、`ActionRules.cs` |
| 手牌区运行时补足 7 个牌位；卡面库索引扩到 7（山 = 5、玛恩纳 = 6） | `UI/CharacterHandPanel.cs`、`Editor/CardFaceFiller.cs` |
| 公路指示物：`RouteState.IsHighway`、覆盖影响力格、探索 / 采集免路费、地图渲染 | `Core/Model/MapState.cs`、`ActionRules.cs`、`TollCalculator.cs`、`Game/MapVisuals/*` |
| 4 个角色效果：开辟商路 / 商业网络 / 公事公办（二选一）/ 游说赞助 | `Core/Engine/Effects/EnterpriseEffectExtension.cs` |
| 状态同步：`PlayerState.SetupDiscardDone`、`RouteState.IsHighway` 进快照（版本 5） | `Core/Net/GameStateSnapshot*.cs` |

## 9.3 落码（第三阶段：开局自动抽选 + 企业板总览 + 公路素材）

| 已实现 | 位置 |
|---|---|
| 开局抽选自动接入：企业家选完 → 决定初始企业 → 决定初始资源点 → 7 选 5；未选完企业时不能选点 / 弃牌 | `UI/GameScreenController.cs`（`TryBeginEnterpriseDraft`） |
| 2 人局「抽 4 选 2」：每人 2 家初始企业，两块板上各有自己的 1 个标记停在 1 级 | `Core/Engine/EnterpriseRules.cs`、`EnterpriseCommands.cs`、`EnterpriseState.cs` |
| 企业板总览：操作区「企业」按钮 → 并排展示场上企业板 + 各座位标记（数字 = 座位号）+ 单板放大 | `UI/EnterpriseBoardPanel.cs` |
| 公路美术：`Art/Icons/highway.jpg` 按航道方向拉伸覆盖；多端航道按分支各铺一条 | `Game/MapVisuals/MapStateRenderer.cs`（`DrawHighway`） |


**当时的接入状态（已被 §9.4 / §9.5 取代）**：干员特派、罗德岛 1 级选企业、罗德岛 2 级金券、罗德岛 4 级弃牌区回手均已接入；
富森银行 4 级「1 位对手失分」仍按分数最高的对手结算；其余企业特效应由卡牌效果解锁触发。

## 9.4 落码（第四阶段：扩展设施 / 扩展事件 / 城市样式牌开局）

| 已实现 | 位置 |
|---|---|
| 企业办事处 3 张（id 600–602 红/蓝/黄）：启用扩展时混入设施牌堆（41 → 44），正常建造、可进图鉴 | `Core/Model/Cards/CardCatalog.cs`（`BuildEnterpriseFacilities`、`FacilityDeckIds(bool)`）、`BoardSetup.PrepareFacilityBoard` |
| 企业办事处卡面：`card_facility.jpg` 的 `Enterprise-Office_*_exp` 切片，卡面库 48 → 51 | `Editor/CardFaceFiller.cs`、`UI/FacilitySupplyPanel.cs`（`SpriteIndexOf`） |
| 扩展事件 2 张（450/451）按 `VariantOfId` 替换 407/418，牌堆总数不变 | `CardCatalog.EventDeckIds(bool)`、`UI/EventCardPanel.cs` |
| 城市样式牌扩展开局：基础 6 + 扩展 6 共 12 张按 I / II 级分堆，各堆洗混后抽 4 + 2；基础 6 张时不洗、顺序不变 | `Core/Engine/BoardSetup.cs`（`RevealCityPatterns`）、`CardCatalog.CityPatternIds(bool)` |
| 等级放大查看只在单卡窗口生效；返回键回到单卡窗口 | `UI/EnterpriseBoardPanel.cs` |
| 红色区域开放：红区回合盖放阶段前，从起始玩家顺时针，每名玩家各选 1 家企业把自己的等级 +1（可选自己所在企业；满级自动跳过；走完才放行盖放阶段；进度进快照） | `Core/Engine/EnterpriseRules.cs`（`BeginRedZoneAdvance` / `ContinueRedZoneAdvance`）、`TurnFlow.cs`、`EnterpriseCommands.cs`、`GameStateSnapshot(.Codec).cs` |

## 9.5 落码（第五阶段：企业特效共用结算 + 图鉴企业页卡面）

| 已实现 | 位置 |
|---|---|
| 企业特效结算抽成共用入口 `EnterpriseEffectResolver.Resolve`：命令 `Apply` 与界面的「先收集点选」会话走同一段，带点选的特效（罗德岛 4 级选弃牌区角色牌）现在会真的弹选择窗 | `Core/Engine/Actions/EnterpriseCommands.cs`、`UI/GameScreenController.cs`（`BeginEnterpriseTrigger`） |
| 罗德岛 4 级：角色牌弃牌区为空时直接拒绝（牌面规则「弃牌区为空则不能使用」） | `EnterpriseCommands.cs`（`TriggerEnterpriseEffectCommand.Validate`） |
| 图鉴「企业」页改成 8 张企业板卡面缩略图（不再堆文字说明）；点缩略图 → 单板查看，再点左侧 L1–L5 等级方块 → 放大该级效果段 | `UI/CodexPanel.cs`（`BuildEnterprisePage`）、`UI/EnterpriseCardArt.cs`（`BoardSprite`）、`UI/EnterpriseBoardPanel.cs`（`Mode.Codex` / `OpenSingle`） |
| 图鉴单板视图不需要对局状态（标题界面也能看），没有玩家标记与确认按钮 | `UI/EnterpriseBoardPanel.cs` |
| 干员特派（罗德岛 3 / 5 级）：抽 2 张干员牌（2 人局 3 张）→ 选 1 张加入手牌、其余洗回，再从手牌 / 弃牌区选 1 张角色牌放回游戏盒；或全部洗回并得 1 分 | `Core/Engine/Actions/EnterpriseCommands.cs`（`DispatchOperators`）、`Core/Model/Enterprise/EnterpriseState.cs`（`OperatorDeck`）、`EffectResolver` 的 `ChooseOperatorCard` / `ChooseCharacterCardIntoBox` |
| 8 张干员角色牌（700–707，全蓝）：银灰 / 泥岩 / 森蚺 / 可露希尔 / 凯尔希 / W / 多萝西 / 异客；卡面按 `rhodes_operators_exp.png` 的 4×2 网格运行时取图，不用手动切片 | `Core/Model/Cards/CardCatalog.cs`（`BuildOperatorCharacters`）、`UI/OperatorCardArt.cs` |
| 干员牌可直接打出的 4 个效果：可露希尔·采购协议、凯尔希·派遣特使、W·伺机出动、多萝西·拓荒人脉 | `CardCatalog.BuildOperatorStrategyOps` |
| 海德兄弟 4 级：支付 6 源岩 → 供应区全部牌放回设施牌堆并补满 → 免费建造 1 次（免单、不占主要行动、不要求正处于自己的行动窗口） | `TriggerEnterpriseEffectCommand.ReshuffleSupplyAndFreeBuild`、`BuildFacilityCommand`（`freeBuild` / `ignoreActionBudget`）、`IEffectChoices.ChooseBuild(freeOnly)` |
| 莱茵生命 1 级【切换科室板】：从其余两张科室板里选 1 张换过去，再按**当前合作等级**结算新科室板的奖励（1 级时科室板没有奖励，只换板） | `EnterpriseEffectResolver.SwitchDepartment`、`IEffectChoices.ChooseRhineDepartment` |
| 莱茵生命 2–5 级：升级时按**当前科室板**结算对应等级奖励；结构科 5 级先把莱茵生命降 1 级，再结算降级后那一级的奖励 | `EnterpriseEffectResolver.SettleDepartment`、`EnterpriseRules.SettleLevel` |
| 升级也走「先收集点选」会话：科室板奖励里的放置 / 移动影响力、防御科付费、工程科建造都能让玩家真选，而不是自动取第一个候选 | `AdvanceEnterpriseLevelCommand(choices)`、`UI/GameScreenController.cs`（`BeginEnterpriseAdvanceSettle`）、`CommandCodec`（P3 / Items 答案） |
| 不占用主要行动的建设（雷神工业 3 级、工程科 4 级）；工程科 5 级改为「从设施供应区免费建造 1 张金券费用 ≤ 23 的设施」 | `EnterpriseEffectResolver.BuildWithoutAction`、`EnterpriseSpecial.BuildFreeFromSupply`、`EnterpriseLevel.Amount` |
| 雷神工业 5 级：从设施牌堆选 1 张建设（正常付费、不占主要行动），随后洗混设施牌堆；牌堆顺序由「回合 / 行动座位 / 牌堆内容」派生的固定种子重排，主机与各端、复盘结果一致 | `EnterpriseEffectResolver.BuildFromDeckAndShuffle`、`ShuffleFacilityDeck` |
| 富森银行 4 级：对手由玩家点选（放弃选择时退回分数最高的对手） | `EnterpriseRules.LetOneOpponentLoseScore`、`IEffectChoices.ChooseOpponent` |
| 雷姆必拓 4 级：失去 1 分后立刻进行 1 次采集，逐点询问「采哪个点 / 是否继续」，路费与资格判定复用采集阶段 | `EnterpriseEffectResolver.ExtraHarvest`、`HarvestResolver.ResolveSingle` |
| 开发者模式面板（仅从主菜单「以开发者模式启动」进入后显示）：选中玩家后，给全部企业板列出等级「+ / -」直接设级（未上场会自动上场，`DeveloperSetEnterpriseLevelCommand`），并对已经解锁的黑色特效等级显示「触发 Lx」，点了直接走正式触发结算；正式界面**不**提供无条件触发按钮 | `UI/DeveloperPanel.cs`、`UI/GameScreenController.cs`（`EnsureDeveloperButton` / `SubmitDeveloperCommand`）、`Core/Engine/Actions/EnterpriseCommands.cs`（`DeveloperSetEnterpriseLevelCommand`、`TriggerEnterpriseEffectCommand`） |
| 企业合作图标（`RewardKind.ChooseEnterprise`）= 在全场所有企业里挑 1 个**已解锁、本回合未用过**的企业特效触发（先选企业板 → 再选该板的特效等级 → 走正式触发结算）；企业办事处入场效果与 2 张扩展事件牌的「选择 1 家企业」都按这条结算 | `Core/Engine/Effects/EnterpriseEffectExtension.cs`（`TryTriggerEffect`）、`CardDefinitions.cs`（`RewardKind.ChooseEnterprise`） |
| 卡牌带出来的企业效果现在都会转交企业扩展：事件牌选项、设施入场效果、城市样式特殊行动、企业等级奖励、延迟结算 | `FlowCommands.cs`、`BuildFacilityCommand.cs`、`UsePatternSpecialActionCommand.cs`、`EnterpriseRules.cs`、`CleanupResolver.cs` |
| 卡牌给出的触发时机不受「玩家行动阶段 / 自己行动」限制（事件牌在事件阶段触发也合法）；只有从企业板界面主动触发才做时机校验 | `TriggerEnterpriseEffectCommand(ignoreTiming)` |
| 效果日志在「成功且有说明」时也显示说明：罗德岛 2 级会写明「按 N 个计数单位结算，获得 N 金券」，干员特派 / 4 级取回牌也会显示抽到 / 取回了什么 | `Core/Engine/Effects/EffectResolver.cs`（`EffectLogEntry.ToString`、`EffectReport.Describe`、`GainVouchersPerCount`） |

**当时的未接入清单**：8 张干员牌里需要新机制的 12 个效果（已被 §9.6 部分取代）。

## 9.6 落码（第六阶段：干员牌效果 · 第一/二批）

| 已实现 | 位置 |
|---|---|
| 新增干员专属效果类型 `OperatorSellToPlayer` / `OperatorBalanceInfluence` / `OperatorPayResourceBuild` / `OperatorRelocateInsteadOfRemove` / `OperatorSupplyToBox` / `TriggerAllEnterpriseEffects` / `OperatorReplaceTwoOpponents` / `OperatorTakeOpponentResources`（枚举值 41–48），统一由企业扩展效果入口处理 | `Core/Model/Cards/CardDefinitions.cs`、`Core/Engine/Effects/EnterpriseEffectExtension.cs` |
| 银灰·策略「商业手腕」：选 1 种基础资源 + 1 位玩家（**可以是自己**），该玩家按 4 金券 / 个卖光该资源；发动者得 1 分 | `CardCatalog.BuildOperatorStrategyOps`、`IEffectChoices.ChoosePlayer`（新增） |
| 银灰·计谋「制衡战略」：所有玩家（含自己）各移除 1 个自己的影响力 | `EnterpriseEffectExtension.TryBalanceInfluence` |
| 泥岩·策略「源岩D.I.Y」：支付 1 源岩后建造 1 次；付不起时整条效果不执行（不会白送建造） | `EnterpriseEffectExtension.TryPayResourceBuild` |
| 森蚺·策略「我寻思能行」：本回合免费的移动城市本应移除对手影响力时，改为把这些影响力按各自所有者的部署规则移到别的合法空格（找不到落点才退回供应堆） | `PlayerState.FreeMoveRelocatesInfluenceThisTurn`、`EffectResolver.RelocateOthersInfluence` |
| 森蚺·计谋「暴躁铁皮」：从设施供应区选至多 2 张名称不同的设施牌放回游戏盒，每张后立刻补牌；获得 3 异铁 | `EnterpriseEffectExtension.TrySupplyToBox` |
| 凯尔希·计谋「掌控全局」：选 1 家企业，按等级从低到高触发其每项已解锁、本回合未用过的企业特效（逐级重新校验费用，低等级给的资源可用于支付高等级费用） | `EnterpriseEffectExtension.TryTriggerAllEffects` |
| W·计谋「致命攻势」：支付 4 金券 → 选 2 名不同对手，各用你的 1 个影响力替换其 1 个 | `EnterpriseEffectExtension.TryReplaceTwoOpponents` |
| 异客·策略「沁礁黑市」：选 1 种基础资源 + 1 位对手，从该对手处取得至多 4 个；发动者与该对手各得 1 分 | `EnterpriseEffectExtension.TryTakeOpponentResources` |
| 联机：`DeveloperSetEnterpriseLevelCommand` 补登记 `CommandKind` / `CommandCatalog` / `CommandCodec`（Flags bit2 = 是否结算）；`FreeMoveRelocatesInfluenceThisTurn` 进快照（版本升到 7，`NetProtocol` 升到 5） | `Core/Net/*` |

**~~尚未接入~~（已被 §9.9 取代）**：4 张「持续留在场上（∞）」的计谋 —— 泥岩·泥岩小队、可露希尔·工程订购、多萝西·为了大家、异客·隐蔽仓库，已于 §9.9 连同 `PersistentTacticState` / `PersistentTacticRules` 一并接入。因此 8 张干员牌 × 2 个效果 = 16 个效果已全部落码。

## 9.7 企业板全等级复核（2026-11-14）

逐条把 8 块企业板（0–5 级）与 3 张科室板（2–5 级）的卡面文字和实际结算对照后，修正了 3 处缺口：

| 问题 | 原状 | 修正 |
|---|---|---|
| 海德兄弟 4 级 | 只写了支付 6 源岩，**没有挂上**「洗回供应区 + 补满 + 免费建造」特殊结算，付了资源什么都不会发生 | 改用 `UnlockSpecialCost(..., SupplyReshuffleAndFreeBuild)` |
| 雷神工业 3 / 5 级 | 卡面写明「一回合内没有使用次数限制」，但被通用的「同一特效一回合一次」拦住 | `EnterpriseLevel.UnlimitedPerTurn`（3 / 5 级为 true），`Validate` 与 `TriggerableLevels` 放行 |
| 雷姆必拓 2 级 | 「替换 1 个**在资源点上**的影响力」实际可以选择航道或任意节点 | 新增 `RewardKind.ReplaceResourceNodeInfluence`，候选只限带资源指示物的节点 |

其余等级与科室板逐条核对无误；测试补了 3 条对应用例（总计 286 条全绿）。

## 9.8 全量代码审计（2026-11-14）

按「不采信文档、只以代码为准」把此前声称完成的项目逐条读码复核，结论如下。

**已核实为真（代码里确有实现）**

| 项目 | 代码证据 |
|---|---|
| 开局整合（7 张手牌、+5 券、设施 41→44、事件 2 张替换、城市样式 4+2） | `BoardSetup.cs`、`CardCatalog.FacilityDeckIds/EventDeckIds/CityPatternIds(bool)` |
| 2 / 3 / 4 人企业抽选（2 人抽 4 选 2、3 人补第 4 块、升 1 级结算） | `EnterpriseRules.Prepare`、`ChooseInitialEnterpriseCommand.Apply` |
| 红区全体提升（盖放阶段前，起始玩家顺时针，满级跳过，进度进快照） | `EnterpriseRules.BeginRedZoneAdvance/ContinueRedZoneAdvance`，`TurnFlow` 调用 |
| 山 / 玛恩纳 4 个效果与公路（禁止放置 / 移动影响力、探索采集免路费、渲染） | `EnterpriseEffectExtension`、`ActionRules.CanPlaceInfluence`、`TollCalculator`、`MapStateRenderer.DrawHighway` |
| 干员牌堆（8 张 700–707、抽 2 / 2 人局抽 3、洗回、加入手牌、进快照） | `EnterpriseState.OperatorDeck`、`DispatchOperators`、`OperatorCardArt` |
| 干员 12 / 16 个效果 | `CardCatalog.BuildOperatorStrategyOps/TacticsOps` + `EnterpriseEffectExtension` 41–48 |
| 企业特效每回合使用掩码、重置 | `EnterpriseState.MarkEffectUsed/HasUsedEffectThisTurn`、收尾重置 |
| 界面选择请求 | 各会话统一按 `ChoiceRequest` 的选项 / 地图点选渲染，新增选择类型无需单独分支 |
| 快照 / 编解码 | 报价、场上板、各座位等级、科室板、已用掩码、干员牌堆、`FreeMoveRelocatesInfluenceThisTurn` 均在 `GameStateSnapshot(.Codec)` |

**审计中修正的 2 处偏差**

| 问题 | 原状 | 修正 |
|---|---|---|
| 雷神工业 5 级「无法满足建造条件时必须洗混设施牌堆」 | 无空位、牌堆无可建设施、选中后校验失败三条路径都直接返回，**没有洗牌** | 三条路径都补 `ShuffleFacilityDeck` |
| 莱茵生命科室板被当成「每座位一份」 | 非莱茵玩家因「所有玩家提升 1 级」被带到莱茵生命时，`Departments[seat] < 0` 被写成 0，拿到的是固定第 1 张面板 | 新增 `ActiveDepartmentId`：本人无记录时取选择莱茵生命那位的激活面板；切换科室板也写回该座位 |

**仍未完成 / 仍存疑（不是已完成）**

| # | 项 | 状态 |
|---|---|---|
| 1 | 泥岩小队、工程订购、为了大家、隐蔽仓库 4 张 ∞ 计谋 | **本轮已接入**（见 §9.9），打出后留在场上、收尾阶段暂停并可发动 |
| 2 | 3 人局随机抽到莱茵生命时「随机激活 1 张科室板」 | **已改为真随机**（2026-11-15）：`EnterpriseRules.Prepare` 用开局同一颗 `Random` 抽定科室板（同种子可复现、联机一致）；`PickInitialRhineDepartmentIfNeeded` 只作为旧存档 / 测试绕过开局时的兜底 |
| 3 | 黑钢国际 1 级「移除 1 个影响力」的归属 | **已确认**：按速查表，未写明「自己的」影响力的图标一律可移除其他玩家的影响力；代码现实现（候选含任意玩家的影响力）正确，无需改动 |
| 4 | 扩展城市样式 554 / 555 的阵型 | **已确认**：按工程内 `Art/Cards/CityPattern/card_cp_lv2.jpg` 逐格核对，554 与 504 同阵、555 与 505 同阵（同行列颜色一致），代码无需改动 |
| 5 | 快照不含延迟结算队列 | **已补**：`GameStateSnapshot.DeferredEffects` 递归携带 `RewardOp`（含分支 / 内嵌 ops / 可分配资源），快照版本 9、`NetProtocol` 7 |

> 验证口径：`AllCompileCheck` 编译 0 警告 0 错误；EditMode 313 / 313 通过。**未在 Unity Play 模式实测**界面表现。

## 9.9 本轮收尾（2026-11-14）

审计里剩下的四块按「引擎 + 测试，界面只做最小提示」补齐：

**1. 红色 · 一次性计谋「用后放回游戏盒」**

`CharacterCardDef` 新增 `StrategyReturnsToBox / TacticsReturnsToBox`，`UseCharacterCardCommand` 用后置
`PlayerState.PlacedCardReturnsToBox`，`DiscardPlacedCard` 据此跳过弃牌区。覆盖 3 张：银灰「制衡战略」、
凯尔希、W「致命攻势」。普通策略 / 计谋照常进弃牌区。

**2. 莱茵生命科室板改为板级**

`EnterpriseState.Departments[seat]` 改为 `int RhineDepartment`（-1 = 未选）。`SettleDepartment` /
`SwitchDepartment` / `ActiveDepartmentId` 全部读写这一字段，任何座位结算的都是同一张科室板。

**3. 6 张企业城市样式牌的特殊行动（550–555）**

| 牌 | 费用 | 效果 |
|---|---|---|
| 550 城建开拓区 | 失去 1 分 | 从设施牌堆选 1 张并直接建设（`RewardKind.BuildFromFacilityDeck`） |
| 551 拓荒者据点 | — | 在移动城市所在资源点放 2 个影响力；再按牌上标记数移动自己的影响力（`PlaceInfluenceAtCityNode` / `RelocateInfluencePerPatternMarkers`） |
| 552 核心商业区 | — | 四选一（`ChooseTwoOfThree` 之外的 `Alternative` 分支）+ 1 次「特殊行动以外」的主要行动（`ExtraMainActionsExcludingSpecial`） |
| 553 大型企业园 | — | 同一企业板连续触发 2 个已解锁特效（`TriggerEnterpriseEffectTwice`） |
| 554 警备队辖区 | — | 三选二（①支付 2 源岩 + 1 异铁替换 1 个影响力；②放置 1 个影响力得 1 源石；③移除 1 个影响力得 2 异铁）+ 本轮禁用角色牌 |
| 555 拓荒指挥中枢 | — | 1 次「特殊行动以外」的主要行动 + 本轮可连发同一张角色牌的两种效果（`BothCharacterEffectsThisRound`） |

> 554 的费用与三条奖励已按工程内卡面（`Art/Cards/CityPattern/card_cp_lv2.jpg`）逐项核对：
> 图标颜色与速查表图例对应为——金券=金色纸券、源岩=褐色六棱石、源石=深红尖晶、异铁=蓝色尖晶；
> ②的绿色向下箭头是「放置影响力」，③的红色向上箭头是「移除影响力」，①箭头后的双箭头方块是「替换影响力」。
> 若以后实体卡数值调整，只需改 `CardCatalog.BuildEnterpriseCityPatterns` 的三个 `EffectBranch` 与
> `EnterpriseEffectExtension.ExecutePoliceOption`。

「额外主要行动（不含特殊行动）」由 `ActionRules.ValidateMainActionAvailable(..., specialAction: true)`
拦住城市样式特殊行动，避免把这份额度拿去用特殊行动；`EndTurnCommand` 与 `TurnFlow` 负责消费。

**4. 四张 ∞ 持续计谋**

新增 `PersistentTacticState`（卡 id / 座位 / 类型 / 压在牌下的设施 / 标记 / 本回合是否已用）与
`PersistentTacticRules`：

| 计谋 | 打出 | 收尾能力 | 其它 |
|---|---|---|---|
| 泥岩小队 | 留在场上 | 支付 2 源岩，放置 1 个影响力 | — |
| 工程订购 | 从设施供应区压 2 张异名设施到牌下 | — | 建造时可从牌下取牌；牌下空了进弃牌区 |
| 为了大家 | 放至多 4 个标记 | 移除 2 个标记，放置 1 个影响力 | 自己的影响力被移除时，可选择放到这张牌上（标记 +1） |
| 隐蔽仓库 | 放至多 5 个标记 | — | 支付资源时可用标记代替（每次至多 2 个），用完进弃牌区 |

- 收尾阶段：`CleanupResolver` 先 `ResetCleanupUsage`；`TurnFlow.TryAutoAdvance` 在
  `PersistentTacticRules.HasCleanupActionsAvailable` 为真时**停在 `TurnPhase.Cleanup`**，
  玩家用 `UsePersistentTacticCleanupCommand` 发动，或发 `AdvancePhaseCommand` 跳过。
- 界面：`GameScreenController.TryBeginPersistentCleanup` 在收尾阶段弹出选择窗，列出可发动的持续计谋 + 「跳过收尾」；
  选定后的落点等选择与建设 / 角色牌 / 特殊行动同一套 —— `EffectChoiceSession` 试跑收集答案，
  可点地图或弹窗作答，取消键逐层回退（回退到底则回到「选哪张计谋」）。
- 线路：`CommandKind.UsePersistentTacticCleanup`、`GameStateSnapshot.Version = 9`、`NetProtocol.Version = 7`。
- 快照：延迟结算队列（含递归分支 / 内嵌效果）已纳入 `GameStateSnapshot`，不再在队列非空时拒绝抓快照。

新增测试 `Assets/Tests/EditMode/EnterpriseRemainingFeatureTests.cs`（23 条）覆盖以上四块与快照递归往返。

---

## 9.10 速查表一致性核查（2026-11-14）

对照工程内两张速查表（`Assets/_Project/Art/QueryTable1.jpg`、`QueryTable2.jpg`）逐条核对关键词规则，
并补了回归测试（落在 `BuildTests` / `CharacterCardTests`）：

| 规则 | 现状 | 测试 |
|---|---|---|
| 移除影响力可指向任意玩家（没写「自己」即包含对手） | 实现正确 | `EffectResolverTests` 拿走影响力… |
| 替换影响力可指向任意玩家；目标是**对手移动城市所在资源点**时只能移除、不能再放 | 实现正确（`TryReplaceInfluence` 移除后再走 `CanPlaceInfluence`） | `替换影响力_该点有对手城市时只移除不放置` |
| 唯一：自己面板已有同名牌时不能再建（按牌名判，跨颜色也算同名） | 实现正确 | 新增 `建设_唯一关键词_同名设施已建时不能再建` |
| 免费：跳过支付 | 实现正确（`IsFreeToBuild`，费用全 0 亦视作免费） | 新增 `建设_免费关键词_跳过全部支付` |
| 起始玩家可两种角色牌效果都发动，顺序自选 | **执行侧漏判企业放开标记，已修** | 新增 `使用_起始玩家可以先计谋后策略`、`使用_拓荒指挥中枢放开限制后非起始玩家也能连发两种效果` |
| 起始标记争夺：本回合获得，下一回合才生效 | 实现正确（`PendingStartMarkerSeat` → `TurnFlow.NextTurn`） | `传递起始标记_下一回合由指定玩家先手` |
| 终局结算：区块控制 + 剩余资源，平局比金券 / 至纯源石 | 实现正确 | `FinalScoringTests` |
| 使用中的角色牌既不在手牌也不在弃牌区（∞ 牌留在场上、不占盖放位） | 实现正确 | `CharacterCardTests` 盖放 / 使用 + `EnterpriseRemainingFeatureTests` |
| 移动城市不能停靠到对手城市所在资源点；无指示物 / 有对手城市的点不能放影响力 | 实现正确 | `ActionTests` 城市移动 / 部署 |

修复点：`UseCharacterCardCommand.Apply` 原仅按 `FirstPlayerSeat` 决定是否留住牌等第二种效果，
与 `Validate` 里 `BothCharacterEffectsThisTurn` 的放开口径不一致 —— 被放开的玩家用掉第一种后牌被直接弃掉，
第二种效果永远用不出来。现改为两处共用同一判定。

同轮还按卡面修正了 554 警备队辖区的费用与三条奖励（见 §9.9），并新增「替换任意玩家影响力」的回归测试。

验证：`AllCompileCheck` 0 错误 0 警告；EditMode 313 / 313 通过。

---

## 9.11 全量卡面自查（2026-11-14）

按工程内卡图逐张复核 22 张事件牌、41 张设施牌、20 张基础角色牌与 8 张干员牌，发现并修正 3 处偏差：

| 卡牌 | 卡面 | 修正前代码 | 修正 |
|---|---|---|---|
| W·策略「伺机出动」 | 放置 **3** 次影响力 + 收尾移除 2 个 | 放置 2 次 | `CardCatalog.BuildOperatorStrategyOps` 改为 3 |
| 极境·计谋「导航通讯」 | 支付 **3 异铁**（蓝色尖晶） | 3 至纯源石 | `CardCatalog.BuildRoleTacticsOps` 改为 `Oriron` |
| 山·策略「开辟商路」 | 被移除影响力的玩家各 **1 分**，你 **1 分** | 双方各 1 金券 | `EnterpriseEffectExtension.TryPlaceHighway` 改为 `Score` |

同步更新测试：`CharacterCardTests` 极境计谋改为支付异铁、`EnterpriseTests.开辟商路` 改为断言分数、
`EnterpriseRemainingFeatureTests` 新增 `W策略_放置3个影响力并登记收尾移除2个`。
事件 / 设施 / 城市样式 / 其余角色牌与实现一致，无需改动；山与玛恩纳当时因未找到卡图无法逐字核对（见 `CardEffects.md` §6），**已于 §9.14 补做**（卡面就在 `bas_operators.png` 的 `Mountain_exp` / `Młynar_exp` 切片里，读图核对后与代码一致）。

验证：`AllCompileCheck` 0 错误 0 警告；EditMode 314 / 314 通过。

---

## 9.12 交互界面完善（2026-11-14）

- **收尾持续计谋**：选定后不再使用引擎默认答案。`GameScreenController.BeginPersistentCleanup` 用 `EffectChoiceSession`
  试跑收集答案，落点直接点地图、其余选择走 `ChoicePopup`；取消键逐层回退，退到底回到「选哪张计谋」。
  引导文案由 `CleanupEffectHint` 生成，与建设 / 角色牌 / 特殊行动同规格。
- **企业状态提示**：`ExpansionStatusHint` 把「本回合不能使用角色牌 / 还可再执行 N 次主要行动 /
  可连发策略与计谋」附在行动提示末尾，避免玩家误以为按钮失效。
- **终局结算窗**：`OnGameEnded` 用与确认窗相同的遮罩 + 标题 + 单选项规格弹出胜者与最终名次
  （`BuildFinalPrompt`），只弹一次；关闭后仍可在状态栏查看摘要。

---

## 9.13 公路指示物与公开展示（2026-11-15）

- **公路指示物不设上限**：规则书扩展配件列表下注明「在游戏过程中如有需要，玩家可以使用喜欢的指示物替代不足的公路指示物」，
  8 个只是配件数量、不是硬上限。引擎因此只保留 `RouteState.IsHighway`，不对可铺公路数做限制。
- **企业合作等级上限固定为 5 级**：`EnterpriseBoardDef.MaxLevel` = 5，`EnterpriseState.Advance` 在 5 级封顶，
  `AdvanceEnterpriseLevelCommand` 对已满级拒绝提升，企业板总览按 0–5 级显示。
- **向所有玩家展示**：罗德岛 3/5 级【干员特派】放回游戏盒的角色牌、4 级从弃牌区回收的角色牌，
  会追加到 `GameState.PublicReveals`；`GameScreenController.ShowPendingPublicReveals` 在刷新时把最新一条
  放大成卡面弹窗（热座下等同把牌亮给所有人看，联机各端按同一份状态各自弹出）。快照版本升到 **10**。
- 测试：`EnterpriseRemainingFeatureTests.罗德岛干员特派_放回游戏盒的牌记入公开展示` / `罗德岛四级_回收的牌记入公开展示`。

---

## 9.14 全量复审与素材核对（2026-11-15）

本轮改为「以代码与美术资源为准」，不再采信旧文档结论；同时修正了几处已经过期的说明。

**1. 素材清点（`Assets/_Project/Art/Cards/`）**

| 目录 / 文件 | 内容 | 代码接线 |
|---|---|---|
| `Character/bas_operators.png`（2400×1700，8 切片） | 5 张基础角色 + `bas_operators_Mountain_exp`（山）+ `bas_operators_Młynar_exp`（玛恩纳）+ 1 个空位 | `Editor/CardFaceFiller.cs` 按切片名映射：`Mountain_exp → 5`、`Młynar_exp → 6`；场景 `CharacterHandPanel.cardFaces` 已有 7 张（5 基础 + 山 + 玛恩纳） |
| `Character/rhodes_operators_exp.png`（2400×1700，4×2） | 8 张干员牌（银灰 / 泥岩 / 森蚺 / 可露希尔 / 凯尔希 / W / 多萝西 / 异客） | `UI/OperatorCardArt.cs` 运行时按格子 `Sprite.Create`，顺序与 `CardCatalog.OperatorNames` 一致 |
| `Character/op_back_*.png`、`rhodes_op_back_exp.png` | 四色角色牌背 + 干员牌背 | 卡背素材齐全（界面暂未使用干员牌背） |
| `Enterprise/`、`Entrepreneur/`、`Event/`、`Facility/`、`CityPattern/` | 企业板 / 科室板 / 企业家 / 事件 / 设施 / 样式牌拼版图 | 各自的 `*CardArt` 或编辑器切片已接线 |

> **纠正**：此前 §9.11 与 `CardEffects.md` §6 记的「山、玛恩纳无卡图」不成立 —— 卡图一直在 `bas_operators.png` 中，只是没有单独 PNG。

**2. 山 / 玛恩纳卡面逐字核对（读切片图 vs 代码）**

| 卡 | 卡面文字 | 代码 | 结论 |
|---|---|---|---|
| 山·策略「开辟商路」 | 选 1 条航道，移除其上所有影响力，在该航道放置 1 个公路指示物；**被移除影响力的玩家各得 1 分**，你获得 1 分 | `EnterpriseEffectExtension.TryPlaceHighway`（`Score += op.Amount`，发动者再 +1） | 一致 |
| 山·计谋「商业网络」 | **支付 2 金券** → 选 1 家企业：提升 1 级 **并**触发 1 个已解锁特效 | `PayVouchers(2)` + `AdvanceAndTriggerEnterprise()` | 一致 |
| 玛恩纳·策略「公事公办」 | 二选一：① 选 1 家企业，**所有玩家**提升 1 级；② 选 1 家企业，触发 1 个已解锁特效 | `Alternative(AdvanceEnterpriseAllPlayers, TriggerEnterpriseEffectOnce)` | 一致 |
| 玛恩纳·计谋「游说赞助」 | 选 1 家企业提升 1 级；获得 1 源岩 + 1 源石 + 1 异铁 | `AdvanceOwnEnterpriseLevel(1)` + 三色资源各 1 | 一致 |

**3. 代码现状复核（不采信文档）**

| 项目 | 结论 | 证据 |
|---|---|---|
| 公路：禁止放置 / 移动影响力、探索采集免路费 | 已实现 | `ActionRules.CanPlaceInfluence`（`IsHighway` 拒绝）、`TollCalculator`（`IsHighway → 0`）、`MapStateRenderer.DrawHighway` |
| 「所有玩家提升 1 级」结算顺序 | 发动者先、其余顺时针 | `EnterpriseEffectExtension.TryAdvanceAllPlayers` |
| 12 种企业特殊结算 | 全部有分支 | `EnterpriseCommands`（`Apply` / `Validate` 两处 switch 齐全） |
| 莱茵生命科室板 | 板级单张、切换/结算/降级都已接 | `EnterpriseState.RhineDepartment`、`SettleDepartment` / `SwitchDepartment`、快照 `EnterpriseRhineDepartment` |
| 红区提升 | 引擎 + 界面接续 + 快照都齐 | `EnterpriseRules.BeginRedZoneAdvance` / `ContinueRedZoneAdvance`、`GameScreenController` 续接下一位、快照 3 个字段 |
| 4 张 ∞ 计谋 | 已接入（§9.6 的「尚未接入」是过期描述） | `GameState.PersistentTactics`、`PersistentTacticRules`、收尾选择会话 |
| 联机 | 5 条企业命令都进 `CommandKind` / `CommandCodec`，覆盖测试含 `PickRhineDepartment` | `CommandCodec.cs`、`CommandCodecCoverageTests` |
| 公开展示 | 记录 + 卡面放大 + 快照版本 10 | `GameState.PublicReveals`、`GameScreenController.ShowPendingPublicReveals` |

**4. 仍未做 / 仍存疑**

| # | 项 | 状态 |
|---|---|---|
| 1 | ~~图鉴没有 8 张干员角色牌（700–707）~~ | **已补**（2026-11-15）：`CodexPanel.CharacterIds()` 追加 700–707；`CharacterSprite` 在场景卡面取不到时回退 `OperatorCardArt.SpriteFor`，8 张干员牌现在有独立格子与卡面 |
| 2 | ~~「企业办事处」入场效果原文~~ | **已核实**（2026-11-15）：读 `card_facility.jpg` 切片 49–51，入场效果为「选择 1 家企业」+ 触发已解锁特效，现有实现正确 |
| 3 | ~~3 人局补的第 4 块板、莱茵初始科室板~~ | **已确认 / 已改**（2026-11-15）：第 4 块板本来就是「洗牌后从牌堆顶翻」，是真随机；科室板改为开局用同一颗 `Random` 抽定（`EnterpriseRules.Prepare`），不再是确定性取模，同种子仍可复现 |
| 4 | ~~公开展示快照往返~~ | **已补测试**（2026-11-15）：`EnterpriseRemainingFeatureTests.公开展示_快照往返保留记录` |
| 5 | Unity Play 实机 | 未实测（企业板标记定位、公路渲染、等级放大裁图都是运行时像素逻辑） |

验证口径：`AllCompileCheck` 编译 0 警告 0 错误；EditMode **327 / 327** 通过。

---

## 10. 与后续扩展（感染者扩展）的接口要求

企业扩展已验证的注入点已经覆盖了「新增牌堆 / 改写设置 / 新增并行系统 / 新增触发时机 / 改写底层规则」五类改动。**感染者扩展的接入应按同一套挂载点设计**，若出现无法挂载的新需求，应回头扩充挂载点定义，而不是在基础流程里加分支。
