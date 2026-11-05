# 联机方案：Unity Relay + Lobby 实现计划

## Context

目标：给这个桌游加**互联网联机**（异地房间），**PC + Android 双端可互连**。

已与用户确认的三项决策：
1. **托管** = Unity Relay + Lobby（UGS 官方）；**大厅形式** = 房间码（创建拿码 / 输码加入）。
2. **底层** = 只装 `com.unity.transport` + `com.unity.services.*`，**不引入 Netcode for GameObjects**；自己用 `NetworkDriver` 写连接握手。
3. **不上 Steam**（Steam 客户端仅 PC，安卓不可用，已排除）。

现状核查结论——这个项目的结构对联机极其有利：

| 事实 | 位置 | 为什么是利好 |
|---|---|---|
| 引擎唯一写入口是 `GameEngine.Dispatch(ICommand) → CommandResult`，`CommandDispatcher` 是 `GameState` 的唯一写路径 | [GameEngine.cs:74-99](file:///d:/Unity%20Program/Nomad%20City%20The%20Founders/Assets/_Project/Scripts/Core/Engine/GameEngine.cs#L74-L99) | 联网只需转发命令，不用同步状态 |
| UI **从不**直接写 `GameState`（全项目 0 处），只读 `engine.State` 并订阅 `CommandApplied` / `PhaseChanged` / `SeatAdvanced` | [GameScreenController.cs:176-229](file:///d:/Unity%20Program/Nomad%20City%20The%20Founders/Assets/_Project/Scripts/UI/GameScreenController.cs#L176-L229) | 客户端重放同一条命令流，界面自动跟着更新 |
| 命令收口点极少：`Dispatch(ICommand)` 私有封装（2109）+ 直连 790、1130 + `ResolveHarvest` 2477 | [GameScreenController.cs](file:///d:/Unity%20Program/Nomad%20City%20The%20Founders/Assets/_Project/Scripts/UI/GameScreenController.cs#L2106-L2121) | 拦截点只有 4 处 |
| `NomadCity.Core` 的 asmdef 是 `noEngineReferences: true` | [NomadCity.Core.asmdef](file:///d:/Unity%20Program/Nomad%20City%20The%20Founders/Assets/_Project/Scripts/Core/NomadCity.Core.asmdef) | 协议与 DTO 可以放进去，零 Unity 依赖 |
| `GameSetup` 是纯数据（`PlayerCount` + `ExpansionFlags`） | [GameSetup.cs:19-48](file:///d:/Unity%20Program/Nomad%20City%20The%20Founders/Assets/_Project/Scripts/Core/Rules/GameSetup.cs#L19-L48) | 开局参数可直接随包发 |
| `GameState.Clone()` 深拷贝完整 | [GameState.cs:136-180](file:///d:/Unity%20Program/Nomad%20City%20The%20Founders/Assets/_Project/Scripts/Core/Model/GameState.cs#L136-L180) | 开局快照 / 主机复核有现成基础 |

环境：Unity **6000.6.0f1**。截至 2026-10-21，`Packages/manifest.json` 已装 `com.unity.transport 6.6.0`、`com.unity.services.core 1.18.0`、`com.unity.services.authentication 3.8.0`、`com.unity.services.relay 1.2.0`、`com.unity.services.lobby 1.3.0`；Unity Cloud 项目已绑定（`cloudProjectId` 以 `d93e50c7-` 开头）。

## 进度记录（以磁盘实际状态为准，2026-10-21）

| 项 | 状态 |
|---|---|
| 0.1 网络可达性预检 | ✅ 通过（三个域名可达；热态 TLS 270–410ms） |
| 0.2 项目与包配置 | ✅ 完成；**Android 相关步骤跳过**（未切 IL2CPP/ARM64） |
| 0.3 最小探针 | ❌ 未写（阶段 2 代码已先行，仍建议补一次真机实测） |
| 0.4 双端矩阵 | ❌ 未做（Android 跳过 → 目前只能做 PC↔PC） |
| 阶段 1 传输抽象 | ✅ `Core/Net/` 6 个文件 + `App/LocalTransport.cs` + 覆盖测试；回归 255/255 通过 |
| 阶段 2 Relay 直连 | ✅ `Net/NomadCity.Net.asmdef` + `Net/RelayTransport.cs`；`App.asmdef` 已引用 `NomadCity.Net` |
| 阶段 3 Lobby / 座位 / 开局同步 | ⏳ 进行中（快照、命令路由、座位闸门、GameBootstrap 接线、Lobby 房间码、额度保险 1/2/3/4/5/6、协议版本握手已完成；等待房场景拖线与热座流程按座位过滤未做） |
| Lobby 房间码 | ✅ 完成（2026-10-22）：`Net/LobbyClient.cs` + asmdef 追加 `Unity.Services.Lobbies`；主机建私有 Lobby → 发布中继码；客户端按 6 位码加入 → 读中继码连 Relay；15 秒心跳 |
| 主菜单联机入口 | ✅ 代码完成（2026-10-22）：`MainMenuController.StartHostGame` / `StartClientGame`（读 `joinCodeInput`）+ `GameSession.Role/JoinCode`；等待面板脚本 `UI/LobbyPanel.cs` 与 `GameBootstrap.lobbyPanel` 已就绪，**场景搭建与拖线待用户手动完成** |
| GameBootstrap 联机接线 | ✅ 完成（2026-10-22）：`OnlineRole` 三态 + 房间码字段；主机开局后广播快照；客户端收快照后建引擎并初始化界面；`Update` 轮询、`OnDestroy` / `OnApplicationQuit` 释放 |
| 后加入者补发 | ✅ 完成（2026-10-22）：`ITransport.PeerJoined` → 主机重新抓「当前」快照补发 |
| 传输层开局快照 | ✅ 完成（2026-10-22）：`ITransport.SnapshotArrived/BroadcastSnapshot`，`RelayTransport` 按 800 字节切片收发 + 后加入补发 |
| 传输层心跳与断线 | ✅ 完成（2026-10-22）：`WireMessage.Heartbeat`（主机每 5 秒）+ `ITransport.ConnectionLost`（客户端 30 秒无消息 → 主动断开、不重连） |
| `NetworkGameSession` | ✅ 完成（2026-10-22）：主机执行+广播、客户端只发送、客户端收快照建引擎、座位闸门、30 分钟空闲 TTL |
| UI 命令网关 | ✅ 完成（2026-10-22）：`GameScreenController.SetCommandGateway`，`DispatchToEngine` 唯一入口可被联机层接管 |
| `GameStateSnapshot` 序列化 | ✅ 完成（2026-10-21）：`Core/Net/GameStateSnapshot.cs` + `GameStateSnapshotCodec.cs` + 往返测试 |
| `ResolveHarvest` 收口为命令 | ✅ 完成（2026-10-21）：`ResolveHarvestCommand` + `CommandKind.ResolveHarvest` + 编解码 + catalog 登记 |
| 协议版本握手 | ✅ 完成（2026-10-22）：`Core/Net/NetProtocol.Version`；客户端连上先发 `Hello`，主机校验后不一致直接断开；`Setup` 也携带版本，客户端反向校验 |
| 额度保险（8 条） | ⏳ 第 1/2/3/4/5/6 条已落地；第 7 条（开发者默认本地，已是默认行为）、第 8 条（用量对账）依赖 Unity Cloud 后台 |

已核实的事实：

- `NomadCity.Net.asmdef` 引用：`NomadCity.Core`、`Unity.Collections`、`Unity.Networking.Transport`、`Unity.Services.Core`、`Unity.Services.Authentication`、`Unity.Services.Relay`、`Unity.Services.Lobbies`。
- `RelayTransport` 现在由 `GameBootstrap` 在 `OnlineRole = Host / Client` 时实例化；`OnlineRole = None`（默认）时完全不碰 UGS，**单机流程未受影响**。
- 已实现链路：匿名登录 → Lobby 建房/加房（6 位房间码）→ 人数到齐后 `CreateAllocationAsync(_seatCount)` → 10 秒内 `Bind` → `Listen` / `Connect` → `Setup` / `Command` / `Broadcast` / `Snapshot` / `Heartbeat` 五种线消息 → 主机用分配座位覆盖客户端自报的 `Seat`。

已知缺口（下一轮处理）：

1. ~~`_nextSeat` 只增不减~~ ✅ 已修（2026-10-21）：改为 `AllocateSeat()` 复用 1 ~ `_seatCount-1` 中最小空位；无空位时直接断开新连接，不再下发越界座位。
2. ~~没有协议/版本握手~~ ✅ 已补（2026-10-22）：`NetProtocol.Version` + `WireMessage.Hello`；`Setup` 也带版本，客户端反向校验。`appVersion`（显示用的游戏版本号）暂未加入，协议版本一致即视为兼容。
3. ~~`HostAsync` / `JoinAsync` 是 `async void` 泄漏~~ ✅ 已修（2026-10-21）：每个 `await` 后检查 `_shutdown`；driver 创建后兜底释放；关闭后不再触发 `Fail`。
4. Relay 直连尚未经过真实对局验证——代码链路（通道 + 快照 + 命令路由 + 座位闸门 + GameBootstrap 接线）已齐，但还没有两台机器实际跑过。
5. ~~`ITransport` 目前没有快照事件~~ ✅ 已补（2026-10-22）：`SnapshotArrived` + `BroadcastSnapshot` + `PeerJoined`。
6. 本机 `dotnet` SDK 已不在（`dotnet --info` 报「No SDKs were found」，只剩运行时），命令行跑不了 Core 测试；改在 Unity Test Runner（EditMode）里验证。
7. 采集、初始资源点、角色牌盖放等流程目前仍是「一台机器上依次操作所有座位」的热座写法；联机时非主机玩家无法替别人提交，需要后续按 `LocalSeat` 过滤流程（已知缺口，不影响单机）。
8. 联机中途加入的客户端能拿到「当前」快照，但主机若已进入延迟结算队列（`DeferredEffects` 非空）会抓快照失败并报错；该类效果目前只有一张牌使用，后续再补序列化。
9. 等待房 UI：脚本已就绪（`UI/LobbyPanel.cs` + `GameBootstrap.lobbyPanel`），只差在场景里搭面板并拖线；主菜单入口 `MainMenuController.StartHostGame` / `StartClientGame` 已就绪，`GameSession.Role` / `JoinCode` 负责把身份与房间码带进 Game 场景。
10. 懒分配的自动触发条件目前是「Lobby 人数到齐」；人数不足却想强行开局，由等待面板的「开始游戏」按钮调 `LobbyClient.StartRelayNow()`。

## 方案总览

**主机权威（listen server，主机同时也是玩家）+ 命令流广播 + 客户端本地重放**

```
主机（跑引擎 + 完整 UI）                     客户端（跑一份只读 GameState 副本）
  ├ 收到任意座位的命令 → Validate → Dispatch   ├ 收到命令流 → 本地 Dispatch 重放
  ├ 广播「命令 + 结果 + 单调 Seq」              ├ 于是探索路线/路费/样式匹配预览全本地算
  └ 开局广播一次 GameState 快照（含洗牌结果）    └ 不做决策，只渲染 + 发命令
```

为什么不用确定性锁步：`System.Random` 不可复现（[BoardSetup.cs:23](file:///d:/Unity%20Program/Nomad%20City%20The%20Founders/Assets/_Project/Scripts/Core/Engine/BoardSetup.cs#L23)）。改用「主机权威 + 开局快照」，绕开改 RNG 的需求。

客户端必须持有 `GameState` 副本——`GameScreenController` 全篇读 `engine.State` 渲染地图，没有副本界面根本无法显示。这不是可选项。

## 阶段 0：UGS 连通性验证（先做这个，再写任何联机业务代码）

目的：用最小成本确认「国内网络 + 双端」能不能撑起方案 A。**任何一项不达标就转方案 B**，不要继续投入。

### 通过标准

| 项 | 通过线 | 不通过的含义 |
|---|---|---|
| 匿名登录 | < 3 秒，无异常 | Unity 服务不可达 → 方案 A 不成立 |
| Relay 分配 + 取中继码 | < 3 秒 | 区域不可用或项目未开服务 |
| Bind | 分配后 **10 秒内**成功 | 网络拦截 UDP，换区域或转 B |
| 客户端 Connect | < 5 秒 | NAT/中继不通 |
| 往返延迟（p95） | < 300ms | 线路差，换新加坡/东京/首尔再试 |
| 10 分钟保持连接 | 0 次断线 | 稳定性不达标 |
| Lobby 创建/加入 | < 3 秒 | Lobby 服务不可达 |
| Lobby 心跳（15 秒） | 30 分钟内不掉 | 心跳链路异常 |

### 0.1 网络可达性预检（5 分钟，不需要改项目）

在 PowerShell 里执行，确认 Unity 服务域名能完成 TLS 握手：

```powershell
$hosts = @('services.api.unity.com','player-auth.services.api.unity.com','lobby.services.api.unity.com')
foreach ($h in $hosts) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  try {
    $r = Invoke-WebRequest -Uri "https://$h" -Method Head -TimeoutSec 10 -UseBasicParsing
    "$h  OK  HTTP $($r.StatusCode)  $($sw.ElapsedMilliseconds)ms"
  } catch {
    "$h  FAIL  $($_.Exception.Message)  $($sw.ElapsedMilliseconds)ms"
  }
}
```

判定：**返回 401/403/404 都算可达**（说明 TLS 握手成功），只有超时、DNS 解析失败才算不可达。三个域名里只要有一个超时，先解决网络问题，别急着写代码。

### 0.2 项目与包配置（手工，约 10 分钟）

1. Unity Cloud 建项目 → 开启 **Relay / Lobby / Authentication（匿名）**。
2. `Edit > Project Settings > Services` 绑定该项目。
3. Package Manager 安装并**锁定版本**：`com.unity.transport`、`com.unity.services.core`、`com.unity.services.authentication`、`com.unity.services.relay`、`com.unity.services.lobby`。
4. Player Settings：Android 用 **IL2CPP + ARM64**，`Api Compatibility Level = .NET Standard 2.1`。
5. 记录装上的 Relay SDK 版本号——探针代码里的函数名以该版本文档为准。

### 0.3 最小探针（临时脚本，验证后保留为 RelayTransport 的骨架）

新增一个**独立的临时程序集**，不接入现有游戏流程，也不做任何 UI 接线：

- `Assets/_Project/Scripts/NetProbe/NomadCity.NetProbe.asmdef`（引用 Core + UGS 包，`autoReferenced:false`）
- `Assets/_Project/Scripts/NetProbe/UgsProbe.cs`（MonoBehaviour，挂在空场景的一个空物体上）

探针行为（用命令行参数驱动，避免运行时调参）：

| 参数 | 行为 |
|---|---|
| `-probe host -region asia-southeast1` | 匿名登录 → `CreateAllocationAsync(4, region)` → `GetJoinCodeAsync` → 建 driver → `Bind(AnyIpv4)` → `Listen()` → 打印中继码，并把收到的每条探测包原样回显 |
| `-probe join <中继码> -region asia-southeast1` | 匿名登录 → `JoinAllocationAsync(code)` → 建 driver → `Connect` → 每 5 秒发一条带序号与时间戳的探测包，统计 RTT |
| `-probe lobby-host` / `-probe lobby-join <房间码>` | 只验证 Lobby 创建/加入 + 15 秒心跳，不建 Relay |
| `-transport udp` / `-transport dtls` | 选择连接类型；**两端必须一致** |

探针输出（统一前缀 `[PROBE]`，便于 `adb logcat -s Unity` 过滤）：

- 各阶段耗时：登录、分配、取码、Bind、Connect
- 中继码
- RTT 的 min / avg / p95 / max
- 累计收发字节数（用来估算一局游戏的额度消耗）
- 断开事件（含原因）

保留 `Allocation → RelayServerData → NetworkSettings.WithRelayParameters → NetworkDriver` 这一段，阶段 2 会原样搬进 `RelayTransport.cs`。

### 0.4 双端联调矩阵

按顺序做，前一项通过再进入下一项：

| 序 | 组合 | 验证什么 |
|---|---|---|
| 1 | PC ↔ PC（同一台机器两个实例） | 代码与 API 用法正确 |
| 2 | PC ↔ Android（同一 WiFi） | 跨端、Android 构建可用 |
| 3 | PC（家庭宽带）↔ Android（5G） | 真实异地、NAT |
| 4 | 重复 3，时间选 **20:00–23:00** | 晚高峰国际线路质量 |
| 5 | 同一对玩家，分别用新加坡 / 东京 / 首尔 | 选出最优区域 |

每项至少保持连接 10 分钟，除 5 秒一次的正常探测外，再做一次 30 秒的 1 包/秒突发。

### 0.5 判定与处置

| 结果 | 处置 |
|---|---|
| 全部达标 | 进入阶段 2，按通过的 `region` 与 `transport` 固化默认值 |
| 登录 / Lobby 不可达 | 方案 A 不成立 → 转方案 B（自建中继） |
| Relay 分配失败（所有区域） | 检查项目服务开关；仍失败 → 转 B |
| 能连但 RTT p95 > 500ms 或 10 分钟内断线 | 先换区域；仍不达标 → 转 B |
| 仅 Android 失败 | 检查 IL2CPP/ARM64 与网络权限；仍失败 → 单独评估 |

验证结论（通过/不通过、所选区域与连接类型）记入本文件的「验证」一节。

### 0.6 验证记录

**0.1 网络可达性预检（2026-10-21，开发机 / 家用网络）**

| 域名 | 结果 | 热态耗时（3 次采样） |
|---|---|---|
| services.api.unity.com | 可达（HTTP 200） | 393 / 405ms（首次含 DNS，1069ms） |
| player-auth.services.api.unity.com | 可达（HTTP 404） | 277 / 274ms（首次 471ms） |
| lobby.services.api.unity.com | 可达（HTTP 404） | 301 / 298ms（首次 889ms） |

结论：三个域名全部可达，**0.1 通过**。热态 TLS 握手 270–410ms（约 2 个往返），折算单程 RTT 约 130–200ms，属于「回合制可用、但不算快」的量级；真实游戏 RTT 仍需 0.3 的探针实测。

未完成：0.2 项目配置、0.3 探针、0.4 双端矩阵、0.5 判定。

## 阶段划分

### 阶段 1：厂商中立的传输抽象（单机行为逐帧不变）

这一步不碰任何云端，做完后单机跟现在完全一样，但插槽就位。

**新增**（纯 C#，放 `Assets/_Project/Scripts/Core/Net/`，不新建 asmdef、不破坏 `noEngineReferences`）：
- `ITransport.cs` —— 连接 / 收发 / 事件回调接口：
  `bool IsMultiplayer / IsHost / int LocalSeat`、`event CommandSubmitted`、`RemoteCommandArrived`、`SnapshotArrived`、`SetupArrived`、`ErrorArrived`、`Host(...) / Join(code) / Submit(dto) / Broadcast(dto, result) / Poll(dt) / Shutdown()`
- `CommandKind.cs` —— enum，覆盖全部命令（约 18 枚：StartGame / AdvancePhase / ChooseStartNode / ResolveEventOption / DiscardSupplyFacility / BuildFacility / PlaceCharacterCard / UseCharacterCard / EndCharacterCardUse / DeclareCityPattern / Explore / ExploreInfluence / MoveMobileCity / MoveCityInfluence / PlaceInfluence / RelocateInfluence / UsePatternSpecialAction / ResolveHarvest）
- `CommandDto.cs` —— `struct { CommandKind Kind; int Seat; int P0..P3; uint Flags; int[] Items; }`。参数全是 int/bool，`Items` 承载 RouteIds / Moves / Answers / 采集选择，无多态。
- `CommandCodec.cs` —— `Encode(ICommand)` / `Decode(CommandDto)` 两个 `switch(Kind)` 同文件、`default: throw`。**不直接序列化 ICommand 对象**，避免多态序列化。
- `CommandCatalog.cs` —— `Dictionary<Type, CommandKind>`，供新增命令时登记。
- `GameStateSnapshot.cs` / `GameStateSnapshotCodec.cs` —— 开局快照的序列化。

**新增**：`Assets/_Project/Scripts/App/LocalTransport.cs` —— `IsMultiplayer=false`，`Submit` 同步回调 `CommandSubmitted`，其余空实现 → 单机路径 100% 等价。

**改动**（尽量小）：
- [GameScreenController.cs](file:///d:/Unity%20Program/Nomad%20City%20The%20Founders/Assets/_Project/Scripts/UI/GameScreenController.cs)：把 4 个收口点（`Dispatch` 封装 2109、790、1130、`ResolveHarvest` 2477）统一改为经由 transport 下发；命令**构造代码一行不改**。
- [GameBootstrap.cs](file:///d:/Unity%20Program/Nomad%20City%20The%20Founders/Assets/_Project/Scripts/App/GameBootstrap.cs)：持有 transport，取代直接持有引擎调用的部分。

**新增测试**：`Assets/Tests/EditMode/CommandCodecCoverageTests.cs` —— 反射扫 Core 全部 `ICommand` 具体类，断言「每个都已在 catalog 登记」且 `Decode(Encode(x))` 往返一致。**这是防「新增命令忘了编码」的红线测试**。

### 阶段 2：Relay 直连跑通（先不接 Lobby）

**新增** `Assets/_Project/Scripts/Net/`：
- `NomadCity.Net.asmdef` —— references：`NomadCity.Core` + `Unity.Networking.Transport` + `Unity.Services.Relay` + `Unity.Services.Lobbies` + `Unity.Services.Core` + `Unity.Services.Authentication`；`autoReferenced: false`。只依赖 Core，分层不破。
- `RelayTransport.cs` —— 官方 "Use Relay with UTP" 流程：
  主机 `CreateAllocationAsync(maxConnections, region)`（**分配后需在 10 秒窗口内 Bind**）→ `GetJoinCodeAsync` → `RelayServerData` → `NetworkSettings.WithRelayParameters(ref data)` → `NetworkDriver.Create` → `Bind(AnyIpv4)` → `Listen()`；
  客户端 `JoinAllocationAsync(code)` → 同法建 driver → `Connect(endpoint)`。
  循环内 `ScheduleUpdate().Complete()` + `Accept()` + `PopEventForConnection`，发送走 `BeginSend/EndSend`。

- [NomadCity.App.asmdef](file:///d:/Unity%20Program/Nomad%20City%20The%20Founders/Assets/_Project/Scripts/App/NomadCity.App.asmdef) references 追加 `NomadCity.Net`。

### 阶段 3：Lobby 房间码 + 座位 + 开局同步

- `Net/LobbyClient.cs`：主机 `CreateLobbyAsync(..., IsPrivate: true, Data["relayJoinCode"])`；客户端 `JoinLobbyByCodeAsync(code)` → 读 `Data["relayJoinCode"]` → `JoinAllocationAsync`。
- 心跳：主机每 15 秒 `SendHeartbeatPingAsync`（低于 15 秒 Lobby 会话会超时）。
- 座位：主机 = 0，客户端按加入顺序 `lobby.Players.Count - 1`，映射到现有 `SeatIndex` / `localSeat`。
- 开局配置：房主用主菜单现有的 `playerCountSlider` / `enterpriseToggle` → `GameSession.ToGameSetup()`，经 `SetupArrived` 广播；客户端锁死这两个控件，各自按人数 `MapView.SelectFor(n)` 建图（地图是构建期资源，各端一致）。
- `App/NetworkGameSession.cs` —— 收口 transport + Lobby + 座位闸门（`ActiveSeat == localSeat` 才允许 `Submit`）。
- `MainMenuController` 新增「创建房间 / 输码加入」两个 public 方法（挂到按钮 OnClick，沿用现有接线风格）。

### 阶段 4（后期，本次不做）

断线重连 / 中途加入 / 观战 / 主机复核反作弊。

## 必须由你本人在 Unity 里完成

1. **绑定 Unity Cloud**：`Edit > Project Settings > Services` 绑定 Org / Project ID，并在后台开启 **Relay、Lobby、Authentication（匿名）**。
2. **装包**（Package Manager，锁定版本号、不要用 latest）：`com.unity.transport`、`com.unity.services.core`、`com.unity.services.authentication`、`com.unity.services.relay`、`com.unity.services.lobby`。
3. **Player Settings**：Android 用 **IL2CPP + ARM64**；`Api Compatibility Level = .NET Standard 2.1`（UGS 要求）；PC 建议同样 IL2CPP。当前 `manifest.json` 与 Player Settings 都还没配。
4. **联调方式**：用 Unity 内置的 **Multiplayer Play Mode（MPPM）** 跑 2 个虚拟实例验证命令流，再用真机 Build 验证跨端。

## 额度保险（避免超出免费额度）

免费额度口径：Relay **50 平均月 CCU ≈ 每月 2,160,000 连接分钟**（4 人两小时一局约 480 人·分钟，折合约 4,500 局/月）；Lobby 按带宽计费，每个区域组每月约 10 GiB。真正烧额度的不是流量，而是**房间空转**。

以下 8 条是硬约束，实现时逐条落实：

1. **懒分配 Relay** ✅：先建 Lobby（带宽计费，不按连接分钟），人数到齐或房主点「开始」（`LobbyClient.StartRelayNow`）时才 `CreateAllocationAsync` 并发布中继码。
2. **等待房 TTL** ✅：创建后 15 分钟无人到齐 → `DeleteLobby` + `Shutdown`。
3. **对局空闲 TTL** ✅：30 分钟没有任何命令 → 视为放弃，销毁房间并断开（`NetworkGameSession.IdleExpired`）。
4. **断线判定** ✅：客户端 30 秒收不到任何消息 → 判定断线并主动 `Shutdown`，**不做无限后台重连**（主机每 5 秒发心跳）。
5. **退出即释放** ✅：`OnDestroy` / `OnApplicationQuit` → `Shutdown` + `DeleteLobby` + 停心跳。
6. **创建冷却** ✅：同一客户端 10 秒内不能重复建房；单房间连接数上限严格等于 `GameSetup.PlayerCount`。
7. **开发构建不走 Relay** ✅（默认即满足）：`GameBootstrap.OnlineRole` 默认 `None`，不初始化 UGS；只有手动切成 Host / Client 才碰 Relay。
8. **用量对账** ⏳：Unity Cloud 用量页设置告警、开发期每周核对；本地记录进/出房时间戳（日志已带，但未做汇总）。

## 联机 UI 接线清单（手动搭建）

主菜单（`MainMenuController`，三个一级按钮）：

| Inspector 字段 / 绑定 | 中文 | English |
| --- | --- | --- |
| 按钮 OnClick → `OpenStartPanel` | 单机游戏（弹窗确认按钮显示「进入游戏」） | Local Game |
| 按钮 OnClick → `OpenStartPanelForHost` | 创建游戏（同一个弹窗，确认按钮显示「创建联机房间」） | Create Game |
| 按钮 OnClick → `OpenJoinPanel` | 加入游戏（弹出房间码弹窗） | Join Game |
| `localConfirmButton` | 开局设置弹窗里的「进入游戏」按钮（单机确认） | Local Confirm Button |
| `hostConfirmButton` | 开局设置弹窗里的「创建联机房间」按钮（联机确认） | Host Confirm Button |
| `joinPanel` | 加入游戏弹窗（输入框 + 加入 + 返回） | Join Panel |
| `joinCodeInput` | 房间码输入框（TMP_InputField，6 位） | Join Code Input |
| 加入弹窗「加入」按钮 → `StartClientGame` | 加入房间 | Join Room |
| 加入弹窗「返回」按钮 → `CloseJoinPanel` | 返回 | Back |

`menuButtons` 数组里要放入三个一级按钮（单机游戏 / 创建游戏 / 加入游戏），弹窗打开时统一隐藏。

Game 场景（`GameBootstrap`）：

| Inspector 字段 | 中文 | English |
| --- | --- | --- |
| `lobbyPanel` | 联机等待面板 | Lobby Panel |

等待面板（`LobbyPanel`，挂在始终激活的空物体上）：

| Inspector 字段 | 中文 | English |
| --- | --- | --- |
| `root` | 整体开关节点（子物体，含遮罩与面板） | Root |
| `roomCodeText` | 房间码文本（TMP_Text） | Room Code Text |
| `playersText` | 人数文本（TMP_Text） | Players Text |
| `statusText` | 状态文本（TMP_Text） | Status Text |
| `startButton` | 开始游戏按钮（Button） | Start Button |

开始按钮的 OnClick 手动指向 `LobbyPanel.OnStartButtonClicked`（**不要**同时再写别的监听，否则会重复开局）。

## 风险与坑

- **计费**：Relay 按中继流量 + 分配时长，Lobby 按额度。桌游流量极小，但**空房间不关会持续烧额度** → 退出即 `Shutdown` + 删 Lobby + 停心跳。
- **大陆无 Relay 节点**：官方亚洲区域只有新加坡 / 东京 / 首尔 / 雅加达 / 孟买 / 悉尼；且**整局所有流量只走房主选的那一个区域**。国内玩家应选新加坡或东京，必须在晚高峰用真实的电信/联通/移动网络实测延迟与丢包，不能只看开发机结果。
- **连接类型必须两端一致**（`udp` / `dtls` 混用会报「无法连接服务器」）；`Bind` 有 10 秒窗口。
- **`DeveloperMode` 联机必须关**（现在 [GameBootstrap.cs](file:///d:/Unity%20Program/Nomad%20City%20The%20Founders/Assets/_Project/Scripts/App/GameBootstrap.cs) 会直接赋 `GameState.DeveloperMode`）；`localSeat` 客户端只读。
- **`EffectChoiceSession` 不在 `GameState` 里**（只活在 UI 字段），重连无法恢复。因此客户端**只能提交最终完整命令，绝不发中间答案**，否则状态发散。
- **其它发散点**：`ResolveHarvest` 绕过 `ICommand`（已列为收口点）；洗牌随机由开局快照消除；命令需带单调 `Seq`，客户端本地 `Validate` 与主机不符时向主机请求重发快照。
- **版本漂移**：UGS 正把 Lobby/Relay 并入 Multiplayer Services SDK，注意锁版本。

## 验证

1. **阶段 1 回归**：`dotnet test C:\Users\liuxi\AppData\Local\Temp\nomadcheck\NomadCheck.csproj --nologo -v q` —— 必须仍是 **253/253**；新增的 codec 覆盖测试全绿。
2. **阶段 1 单机等价**：Unity 里跑一局完整 8 回合，行为与改动前逐帧一致。
3. **阶段 2**：MPPM 起两个实例，A 当主机、B 输房间码；B 的操作能驱动 A 的界面同步，A 的操作也同步到 B。
4. **阶段 3**：真机 Android + PC 各一，验证跨端互连与开局人数/企业扩展同步。

## 不做

- 不引入 NGO（已定）。
- 不做 Steam 通道（已排除）。
- 第一版不做断线重连、中途加入、观战、反作弊。
