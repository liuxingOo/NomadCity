# 联机准备房间（Lobby 场景）实现计划

## 1. 目标

把「联机等待」从 Game 场景搬到独立的准备房间场景，解决现在一进 Game 就直接把房间码糊在棋盘中央的问题。

准备房间必须提供：

1. 显示 6 位房间码；
2. 显示已加入玩家列表（昵称、是否准备、谁是房主，最多 4 行，空位显示「等待玩家」）；
3. 房主可踢人；
4. 房主可在房间内继续调整人数（2–4）与企业扩展包，改动实时同步给所有客户端；
5. 客户端可「准备 / 取消准备」；
6. 只有「人数满 + 所有加入玩家都准备」时房主才能点「开始游戏」；
7. 开始后主机申请 Relay，把中继码写入 Lobby，所有端各自加载 Game 场景；
8. 房主转让**不做**（原因见第 10 节）。

用户已确认：**独立 `Lobby` 场景** + **加入时输入昵称**。

---

## 2. 现状分析

### 2.1 现有链路

- 主菜单 [MainMenuController.cs](file:///d:/Unity%20Program/Nomad%20City%20The%20Founders/Assets/_Project/Scripts/App/MainMenuController.cs) 三个入口：
  - 单机：`OpenStartPanel()` → `StartGame()` → 加载 Game；
  - 创建游戏：`OpenStartPanelForHost()` → `StartHostGame()` → 写 `GameSession.Role = Host` → 加载 Game；
  - 加入游戏：`OpenJoinPanel()` → `StartClientGame()` → 校验 6 位码 → 写 `Role = Client` + `JoinCode` → 加载 Game。
- [GameBootstrap.cs](file:///d:/Unity%20Program/Nomad%20City%20The%20Founders/Assets/_Project/Scripts/App/GameBootstrap.cs) 在 Game 场景 `Awake` 里：
  - `StartOnlineHost()` 自己 `new RelayTransport` / `new NetworkGameSession` / `new LobbyClient`，登记 Lobby，把房间码显示在 Game 场景的 `LobbyPanelUI` 上；
  - `StartOnlineClient()` 自己加入 Lobby、等中继码、等快照；
  - `Update()` 同时驱动 `LobbyClient` 与 `NetworkGameSession`；
  - `OnDestroy` / `OnApplicationQuit` 关闭全部连接。
- [LobbyClient.cs](file:///d:/Unity%20Program/Nomad%20City%20The%20Founders/Assets/_Project/Scripts/Net/LobbyClient.cs)：只处理房间码与中继码，**满员会自动 `StartRelayNow()`**（新方案必须删掉，否则房主没有控制权）。
- [RelayTransport.cs](file:///d:/Unity%20Program/Nomad%20City%20The%20Founders/Assets/_Project/Scripts/Net/RelayTransport.cs)：已支持主机 / 客户端、握手、快照分包、心跳、30 秒断线判定；**没有单个客户端断开 API**。
- [GameSession.cs](file:///d:/Unity%20Program/Nomad%20City%20The%20Founders/Assets/_Project/Scripts/App/GameSession.cs)：跨场景 ScriptableObject，已有 `PlayerCount / EnterpriseExpansion / Role / JoinCode`，**没有 Nickname**。
- 程序集：`UI` 只引用 `Core + Game + TextMeshPro + UnityEngine.UI`，**不能引用 App / Net**；`App` 引用 `Core / Data / Game / UI / Net`。
- 场景：只有 `MainMenu.unity`、`Game.unity`，Build Settings 里也只有这两个。
- Game 场景的 Canvas 下已有 `LobbyPanelUI`（脚本 [LobbyPanel.cs](file:///d:/Unity%20Program/Nomad%20City%20The%20Founders/Assets/_Project/Scripts/UI/LobbyPanel.cs)），新方案里要移除。
- 当前没有任何 `DontDestroyOnLoad` 常驻对象，也没有 `OnlineSessionHost` / `LobbySceneController`。

### 2.2 关键约束

- 网络对象（`RelayTransport`、`NetworkGameSession`、`LobbyClient`）必须跨 `Lobby → Game` 两个场景存活，否则切场景会断线、丢掉已分配的中继。
- 切场景后必须继续 `Poll`，因此网络驱动不能挂在场景内物体上。
- `GameBootstrap` 不能再自己建房 / 加房，只能接管常驻会话。
- 客户端进 Game 前必须知道主机设置的人数 / 扩展包，否则 2/3 人局会选错地图。
- Unity Lobby 数据：`Lobby.Data` 用 `DataObject`，`Player.Data` 用 `PlayerDataObject`；`DataObject.Value` 只读，更新必须重新构造。已确认可用 API：`CreateLobbyOptions.Player / IsLocked`、`JoinLobbyByCodeOptions.Player`、`UpdateLobbyOptions.MaxPlayers / IsLocked / Data`、`UpdatePlayerOptions.Data`、`RemovePlayerAsync`。

---

## 3. 总体架构

```
MainMenu 场景
  MainMenuController ──写 GameSession（Role / JoinCode / Nickname / 人数 / 扩展）──▶ Lobby 场景
  OnlineSessionHost（DontDestroyOnLoad，常驻）

Lobby 场景
  LobbySceneController（场景内，负责 UI 事件 ↔ 网络层）
    ├─ 读 GameSession
    ├─ 调 OnlineSessionHost.BeginHost / BeginClient
    └─ 收到 GameReady → LoadScene("Game")

OnlineSessionHost（常驻）
  ├─ RelayTransport
  ├─ LobbyClient   （房间码 / 名单 / 准备 / 设置 / 踢人 / 开始）
  └─ NetworkGameSession（Relay 命令流 + 快照）
  每帧 Poll，负责 Lobby → Relay 的交接

Game 场景
  GameBootstrap：不再创建任何网络对象，只从 OnlineSessionHost 取现成的 session
```

### 3.1 开始流程（房主）

1. 房主在 Lobby 点「开始游戏」；
2. `LobbyClient.RequestStart()` 校验 `CanStart` → 抛 `StartRequested`；
3. `OnlineSessionHost` 收到后调用 `NetworkGameSession.Host(setup, playerCount)`；
4. Relay 分配完成、`Session.JoinCode` 有值后 → `LobbyClient.PublishStarted(relayCode)`：
   一次性写入 `IsLocked = true`、`Started = 1`、`RelayJoinCode`、`PlayerCount`、`Enterprise`；
5. 写入成功 → `OnlineSessionHost` 抛 `GameReady` → `LobbySceneController` 加载 Game；
6. 主机 `GameBootstrap` 建立引擎、`DispatchToEngine` 跑开局、`BroadcastSnapshot()`。

### 3.2 开始流程（客户端）

1. 客户端轮询 Lobby（2 秒一次），发现 `Started = 1` 且拿到 `RelayJoinCode`；
2. `NetworkGameSession.Join(relayCode)`；
3. 收到主机的 `Setup`（座位 + 人数 + 扩展）→ 抛 `GameReady` → 加载 Game；
4. 如果快照先到（引擎已在 Lobby 场景建立），`GameBootstrap` 直接用现成 `Engine` 初始化界面；
   如果还没到，则订阅 `SnapshotApplied`，等到后再初始化界面。

---

## 4. 数据协议

### 4.1 Lobby.Data（房主写，所有人读）

| 键 | 值 | 说明 |
| --- | --- | --- |
| `Protocol` | `NetProtocol.Version` 的字符串 | 加入时校验，不一致直接拒绝 |
| `PlayerCount` | `"2"` / `"3"` / `"4"` | 房主在准备房间可改 |
| `Enterprise` | `"0"` / `"1"` | 企业扩展包开关 |
| `Started` | `"0"` / `"1"` | 1 = 已开始，客户端据此连 Relay |
| `RelayJoinCode` | Relay 中继码或空串 | **只在开始后写入**（懒分配） |

`IsLocked = true` 与 `Started = 1` 在同一次 `UpdateLobbyAsync` 里写入，防止开始瞬间还有人挤进来。

### 4.2 Player.Data（每人写自己的）

| 键 | 值 |
| --- | --- |
| `Nickname` | 玩家昵称（≤ 12 字符，空则用默认名） |
| `Ready` | `"0"` / `"1"`（房主固定 `"1"`，界面显示「房主」） |

### 4.3 开始条件（`CanStart`）

```
members.Count == PlayerCount
且 members 中所有非房主成员 Ready == "1"
且 lobby != null && !relayRequested && !IsLocked
```

即：**人满 + 除房主外全部准备**。房主自己不需要按准备。

---

## 5. 代码改动清单

### 5.1 新增 `Assets/_Project/Scripts/Core/Net/LobbyMemberInfo.cs`

放在 Core/Net，是为了 `Net`（LobbyClient）和 `UI`（LobbyScreenPanel）都能引用，同时不违反程序集依赖。

```csharp
namespace NomadCity.Core.Net
{
    public readonly struct LobbyMemberInfo
    {
        public readonly string PlayerId;
        public readonly string Nickname;
        public readonly bool IsHost;
        public readonly bool Ready;
    }

    public readonly struct LobbySettings
    {
        public readonly int PlayerCount;
        public readonly bool Enterprise;
    }
}
```

只读结构 + 全参构造。昵称空值时由 `LobbyClient` 统一填 `"玩家"`。

### 5.2 改造 `Assets/_Project/Scripts/Net/LobbyClient.cs`

保留现有骨架（心跳、刷新、TTL、建房冷却、`EnsureSignedInAsync`、`Shutdown`），做以下替换：

**新增常量**

```csharp
public const string RelayJoinCodeKey = "RelayJoinCode";
public const string ProtocolKey = "Protocol";
public const string PlayerCountKey = "PlayerCount";
public const string EnterpriseKey = "Enterprise";
public const string StartedKey = "Started";
public const string NicknameKey = "Nickname";
public const string ReadyKey = "Ready";
private const int MaxNicknameLength = 12;
```

**新增/替换事件**

```csharp
public event Action<IReadOnlyList<LobbyMemberInfo>> RosterChanged;
public event Action<int, bool> SettingsChanged;     // 人数, 企业扩展
public event Action<bool> StartEligibilityChanged;  // 房主开始按钮是否可点
public event Action StartRequested;                 // 房主点了开始（由 OnlineSessionHost 去申请 Relay）
public event Action<string> StartPublished;         // 客户端：读到 Started + 中继码
public event Action HostStartPublished;             // 房主：Started 已成功写入 Lobby（可以切场景了）
public event Action Kicked;                         // 客户端：自己已不在名单里
public event Action<string> RoomClosed;             // 客户端：房间不存在了
public event Action<string> StatusChanged;
public event Action<string> ErrorArrived;
public event Action<string> RoomCodeReady;
```

删除 `RelayStartRequested`、`RelayJoinCodeArrived`、`PlayersChanged`、`StartRelayNow`（语义被 `StartRequested` / `StartPublished` 取代）。

**公开属性**

```csharp
public string LobbyCode { get; }
public bool IsHost { get; }
public bool RoomReady { get; }
public IReadOnlyList<LobbyMemberInfo> Members { get; }
public int PlayerCountSetting { get; }
public bool EnterpriseSetting { get; }
public bool CanStart { get; }
public string SelfPlayerId { get; }   // AuthenticationService.Instance.PlayerId
```

**公开方法**

```csharp
public void HostRoom(int playerCount, bool enterprise, string nickname);
public void JoinRoom(string lobbyCode, string nickname);
public void SetReady(bool ready);                       // 客户端
public void SetSettings(int playerCount, bool enterprise); // 房主
public void Kick(string playerId);                      // 房主
public void RequestStart();                             // 房主
public void PublishStarted(string relayJoinCode);       // 房主，成功写库后抛 HostStartPublished
public void Poll(float deltaTime);
public void Shutdown();
```

**行为要点**

- `HostRoom`：`CreateLobbyOptions` 带 `IsPrivate = true`、`IsLocked = false`、`Player = new Player { Data = 昵称 + Ready=1 }`、`Data = { Protocol, PlayerCount, Enterprise, Started="0", RelayJoinCode="" }`。昵称裁剪到 12 字符。
- `JoinRoom`：`JoinLobbyByCodeAsync(code, new JoinLobbyByCodeOptions { Player = new Player { Data = 昵称 + Ready=0 } })`。加入后立刻检查 `Protocol`：不一致 → `Shutdown()` + `Fail("房主与你的游戏版本不一致")`；检查 `Started`：为 1 → `Shutdown()` + `Fail("该房间已经开局")`。
- `PollHost`：
  - 每 2 秒 `GetLobbyAsync`，更新 `lobby`、名单、设置，并在内容变化时才抛 `RosterChanged` / `SettingsChanged`（用拼接签名比较，避免每 2 秒重建 UI）；
  - 每次刷新后重算 `CanStart`，值变化时抛 `StartEligibilityChanged`；
  - **删除「满员自动 `StartRelayNow()`」**；
  - 保留 15 分钟等待房 TTL（从建房开始计时）；
  - 保留心跳。
- `PollClient`：
  - 每 2 秒 `GetLobbyAsync`，更新名单 / 设置；
  - 若 `lobby.Players` 中没有 `SelfPlayerId` → 抛 `Kicked` + `Shutdown()`（`Shutdown` 内部对客户端只做 `RemovePlayerAsync`，被移除时该调用失败只打警告，忽略即可）；
  - 连续 3 次刷新失败（房间里已不存在）→ 抛 `RoomClosed("房主已关闭房间")`；
  - **删除 90 秒 `RelayCodeTimeoutSeconds`**（等待准备的时间可能远长于 90 秒）；
  - 读到 `Started == "1"` 且 `RelayJoinCode` 非空 → 只抛一次 `StartPublished(code)`。
- `SetReady`：`UpdatePlayerAsync(lobby.Id, SelfPlayerId, new UpdatePlayerOptions { Data = { [ReadyKey] = new PlayerDataObject(Member, ready ? "1" : "0") } })`。
- `SetSettings`：
  - 校验 `2 ≤ playerCount ≤ 4`，且 `playerCount >= 当前成员数`，否则 `Fail("人数不能少于已在房间里的玩家数")`；
  - `UpdateLobbyAsync(new UpdateLobbyOptions { MaxPlayers = playerCount, Data = { [PlayerCountKey], [EnterpriseKey] } })`。
- `Kick`：拒绝踢自己、拒绝在 `relayRequested` 之后踢；`RemovePlayerAsync(lobby.Id, playerId)`。
- `RequestStart`：`if (!CanStart) { Fail("需要人数满且所有玩家都已准备"); return; }` → `relayRequested = true` → `StartRequested?.Invoke()`。
- `PublishStarted`：`UpdateLobbyAsync` 写入 `IsLocked = true`、`Started = "1"`、`RelayJoinCode`、`PlayerCount`、`Enterprise`；成功后 `HostStartPublished?.Invoke()`。
- 名单构建：把 `lobby.Players` 按 `Joined` 升序（`Joined` 相同则保持服务端顺序）映射成 `LobbyMemberInfo`；`IsHost` 用 `player.Id == lobby.HostId` 判断。

### 5.3 新增 `Assets/_Project/Scripts/App/OnlineSessionHost.cs`

常驻单例，网络对象唯一持有者。

```csharp
public sealed class OnlineSessionHost : MonoBehaviour
{
    public static OnlineSessionHost Instance { get; private set; }

    public LobbyClient Lobby { get; private set; }
    public NetworkGameSession Session { get; private set; }
    public OnlineRole Role { get; private set; }
    public bool GameStarting { get; private set; }

    public event Action GameReady;
    public event Action<string> ErrorArrived;
}
```

- `Awake`：`Instance` 单例，重复实例直接 `Destroy`；`DontDestroyOnLoad(gameObject)`；**不创建任何网络对象**。
- `BeginHost(int playerCount, bool enterprise, string nickname)`：
  - 若 `Lobby != null` 先 `Leave()`；
  - `transport = new RelayTransport()`；`Session = new NetworkGameSession(transport)`；`Lobby = new LobbyClient(transport)`；
  - `Role = Host`；订阅 `Lobby.ErrorArrived / Lobby.StatusChanged / Lobby.StartRequested / Lobby.HostStartPublished`、`Session.ErrorArrived`；
  - `Lobby.StartRequested` → `Session.Host(new GameSetup(playerCount, enterprise), playerCount)`；
  - `Update()` 里若 `Role == Host && Lobby.CanStartRequested && Session.JoinCode != null && !published` → `Lobby.PublishStarted(Session.JoinCode)`（用一个 `publishRequested` 布尔去重）；
  - `Lobby.HostStartPublished` → `GameStarting = true; GameReady?.Invoke();`
  - `Lobby.HostRoom(playerCount, enterprise, nickname)`。
- `BeginClient(string joinCode, string nickname)`：
  - 同上创建三个对象；`Role = Client`；
  - `Lobby.SettingsChanged` → 写回 `GameSession.PlayerCount / EnterpriseExpansion`（保证与主机同图），并转发给界面；
  - `Lobby.StartPublished` → `Session.Join(code)`；
  - `Session.SetupArrived` → `GameStarting = true; GameReady?.Invoke();`
  - `Lobby.Kicked` / `Lobby.RoomClosed` → `ErrorArrived`（由界面提示并回主菜单）；
  - `Lobby.JoinRoom(joinCode, nickname)`。
- `Update()`：`Lobby?.Poll(Time.deltaTime)`、`Session?.Poll(Time.deltaTime)`；`Session.IdleExpired` → 报错 + `Leave()`（30 分钟空闲额度保险）。
- `Leave()`：`Lobby?.Shutdown()`、`Session?.Shutdown()`，字段置 null，`GameStarting = false`，`Role = None`。
- `OnApplicationQuit()` → `Leave()`。
- 不再需要 `GameBootstrap` 里的 `Update` / `ShutdownOnline`。

> `GameSession` 的引用通过 `FindAnyObjectByType` 不靠谱（多场景会重复）。改用 `LobbySceneController` 把 `GameSession` 传给 `BeginClient`，或 `OnlineSessionHost` 暴露 `SetSession(GameSession)`。**决定**：`OnlineSessionHost` 序列化一个 `GameSession` 字段，在 MainMenu 场景里拖同一个资产。

### 5.4 新增 `Assets/_Project/Scripts/App/LobbySceneController.cs`

Lobby 场景入口。

字段：

```csharp
[SerializeField] private GameSession session;
[SerializeField] private LobbyScreenPanel panel;
[SerializeField] private string gameSceneName = "Game";
[SerializeField] private string mainMenuSceneName = "MainMenu";
```

`Start()`：

1. 校验 `session`、`panel`、`OnlineSessionHost.Instance`；缺失 → `panel.SetStatus(...)` 并直接 `LoadScene(MainMenu)` 的逻辑（用 Coroutine 延迟 2 秒）。
2. `panel` 按 `session.Role` 切成房主视图 / 客户端视图。
3. 订阅面板事件：
   - `ApplySettingsRequested` → `host.Lobby.SetSettings(...)`；
   - `ReadyToggleRequested` → `host.Lobby.SetReady(!panel.IsReady)`；
   - `StartRequested` → `host.Lobby.RequestStart()`；
   - `KickRequested` → `host.Lobby.Kick(playerId)`；
   - `LeaveRequested` → `host.Leave()` + 回主菜单。
4. 订阅 `OnlineSessionHost` 事件：
   - `ErrorArrived` → `panel.SetStatus(message)`（被踢 / 房间关闭时 2.5 秒后切回 MainMenu）；
   - `GameReady` → 若 `sceneLoadRequested` 已置位则忽略，否则 `EventSystem.current.SetSelectedGameObject(null)` → `SceneManager.LoadScene(gameSceneName)`。
5. 订阅 `LobbyClient` 事件刷 UI：`RoomCodeReady`、`RosterChanged`、`SettingsChanged`、`StartEligibilityChanged`、`StatusChanged`。
6. 按角色启动：`Role == Host` → `host.BeginHost(session.PlayerCount, session.EnterpriseExpansion, session.Nickname)`；`Role == Client` → `host.BeginClient(session.JoinCode, session.Nickname)`。

`OnDestroy`：**只退订事件，不 Shutdown 网络**（切到 Game 要保留连接）。

### 5.5 新增 `Assets/_Project/Scripts/UI/LobbyScreenPanel.cs`

纯显示层，不引用 App / Net。

字段：

```csharp
[SerializeField] private GameObject root;
[SerializeField] private TMP_Text roomCodeText;
[SerializeField] private TMP_Text statusText;
[SerializeField] private TMP_Text settingsSummaryText;
[SerializeField] private LobbyPlayerRow[] playerRows;   // 固定 4 个元素
[SerializeField] private GameObject hostSettingsRoot;
[SerializeField] private Slider playerCountSlider;
[SerializeField] private Toggle enterpriseToggle;
[SerializeField] private Button applySettingsButton;
[SerializeField] private GameObject clientReadyRoot;
[SerializeField] private Button readyButton;
[SerializeField] private TMP_Text readyButtonLabel;
[SerializeField] private Button startButton;
[SerializeField] private Button leaveButton;
```

事件：`ApplySettingsRequested(int playerCount, bool enterprise)`、`ReadyToggleRequested`、`StartRequested`、`LeaveRequested`、`KickRequested(string playerId)`。

方法：

- `ShowHostView()` / `ShowClientView()`：切换 `hostSettingsRoot` / `clientReadyRoot`；
- `SetRoomCode(string)`；
- `SetStatus(string)`；
- `SetSettings(int playerCount, bool enterprise)`：`SetValueWithoutNotify` / `SetIsOnWithoutNotify` + 刷新摘要文本「3 人 · 企业扩展已启用 / 未启用」；
- `SetMembers(IReadOnlyList<LobbyMemberInfo> members, bool canKick)`：遍历 4 行，有成员调 `SetMember`，超出部分 `SetEmpty()`；
- `SetCanStart(bool)`：只改 `startButton.interactable`（按钮常驻，不隐藏）；
- `SetReady(bool ready)`：改 `readyButtonLabel` 文本为「准备 / 取消准备」并记录 `IsReady`；
- `SetSettingsInteractable(bool)`：开始流程锁定后禁用滑条 / 勾选 / 应用按钮；
- `IsReady { get; private set; }`；
- Inspector 手动绑定的回调：`OnApplySettingsClicked()`、`OnReadyClicked()`、`OnStartClicked()`、`OnLeaveClicked()`。

> 客户端的 `playerCountSlider` / `enterpriseToggle` 置为 `interactable = false`（仅展示主机设置）；不隐藏设置区，避免布局跳动。

`LobbyPlayerRow` 的踢人事件由面板在 `Awake` 里逐行订阅并转发 `KickRequested`。

### 5.6 新增 `Assets/_Project/Scripts/UI/LobbyPlayerRow.cs`

```csharp
public sealed class LobbyPlayerRow : MonoBehaviour
{
    [SerializeField] private GameObject occupiedRoot;
    [SerializeField] private GameObject emptyRoot;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text stateText;
    [SerializeField] private GameObject hostBadge;
    [SerializeField] private Button kickButton;

    public event Action<string> KickRequested;

    public void SetMember(LobbyMemberInfo member, bool canKick);
    public void SetEmpty();
    public void OnKickClicked();   // Inspector 手动绑定到踢人按钮
}
```

- `SetMember`：`occupiedRoot.SetActive(true)`、`emptyRoot.SetActive(false)`；`nameText = member.Nickname`；
  `stateText` = 房主 ? `"房主"` : (Ready ? `"已准备"` : `"未准备"`)；
  `hostBadge.SetActive(member.IsHost)`；`kickButton.gameObject.SetActive(canKick && !member.IsHost)`；按钮 `interactable` 由面板的 `canKick` 决定。
- `SetEmpty`：`occupiedRoot.SetActive(false)`、`emptyRoot.SetActive(true)`，`nameText = "等待玩家…"`。
- 记住当前 `playerId`，`OnKickClicked` 用该 id 抛事件。

### 5.7 修改 `Assets/_Project/Scripts/App/GameSession.cs`

- 新增 `[SerializeField] private string nickname = string.Empty;`
- 新增属性 `public string Nickname { get => nickname; set => nickname = ... }`（裁剪首尾空白，超 12 字符截断）。

### 5.8 修改 `Assets/_Project/Scripts/App/MainMenuController.cs`

- 新增字段：
  ```csharp
  [SerializeField] private TMP_InputField hostNicknameInput;   // 开局设置弹窗内
  [SerializeField] private TMP_InputField clientNicknameInput; // 加入游戏弹窗内
  [SerializeField] private string lobbySceneName = "Lobby";
  ```
- `Start()` 复位时同时 `session.Nickname = string.Empty;`。
- `StartHostGame()`：
  - 读 `hostNicknameInput`，空则用 `"房主"`；
  - 写 `session.Nickname`、`Role = Host`、`JoinCode = ""`；
  - `LoadScene(lobbySceneName)`。
- `StartClientGame()`：
  - 校验 6 位码（保留）；
  - 读 `clientNicknameInput`，空则用 `"玩家"`；
  - 写 `session.Nickname`、`Role = Client`、`JoinCode = code`；
  - `LoadScene(lobbySceneName)`。
- `StartGame()`（单机）继续 `LoadScene(gameSceneName)`，行为不变。
- 把私有 `LoadGameScene` 拆成：
  ```csharp
  private bool ValidateSession();                 // 校验 GameSession + GameSetup.IsValid
  private void LoadScene(string sceneName);       // 清 EventSystem 选中 + LoadScene
  ```

### 5.9 修改 `Assets/_Project/Scripts/App/GameBootstrap.cs`

- 删除字段：`onlineRole`、`onlineJoinCode`、`lobbyPanel`、`onlineSession`（改为局部/复用）、`lobbyClient`。
- 删除方法：`StartOnlineHost`、`OnLobbyStartClicked`、`OnLobbyStartRequested`、`StartOnlineClient`、`OnOnlineSetup`、`OnOnlineSnapshot`、`Update`、`OnDestroy`、`OnApplicationQuit`、`ShutdownOnline`、`ResolveRole`、`ResolveJoinCode`。
- `Awake` 改为：

```csharp
OnlineRole role = session.Role;
NetworkGameSession onlineSession = null;

if (role == OnlineRole.Client)
{
    StartOnlineClient();   // 内部自己 return，不创建引擎
    return;
}

Engine = GameEngine.Create(session.ToGameSetup(), 0, map);
Engine.State.DeveloperMode = role == OnlineRole.None && developerMode;

if (role == OnlineRole.Host)
{
    var host = OnlineSessionHost.Instance;
    if (host == null || host.Session == null)
    {
        Debug.LogError("[联机] 找不到 OnlineSessionHost 或联机会话，无法以主机身份开局。");
        return;
    }
    onlineSession = host.Session;
    onlineSession.BindEngine(Engine);
    onlineSession.ErrorArrived += OnOnlineError;
    localSeat = 0;
}

ApplyDistrictScores(Engine.State, mapData);
InitializePresentation(localSeat);

if (onlineSession != null && screenController != null)
    screenController.SetCommandGateway(onlineSession.Submit);
else if (role == OnlineRole.None && screenController != null)
    screenController.SetCommandGateway(null);   // 保持原有单机路径

// StartGameCommand …（原逻辑不变）
if (developerMode && role == OnlineRole.None) AutoSetupForDeveloperMode(Engine.State);

if (onlineSession != null && onlineSession.IsHost) onlineSession.BroadcastSnapshot();
```

- 新 `StartOnlineClient()`：
  ```csharp
  var host = OnlineSessionHost.Instance;
  if (host == null || host.Session == null) { Debug.LogError(...); return; }
  onlineSession = host.Session;
  onlineSession.ErrorArrived += OnOnlineError;
  if (onlineSession.Engine != null) ApplyOnlineSnapshot(onlineSession.Engine.State);
  else onlineSession.SnapshotApplied += ApplyOnlineSnapshot;
  ```
- `ApplyOnlineSnapshot(GameState state)`：原 `OnOnlineSnapshot` 内容，去掉 `lobbyPanel.Hide()` 与 `Engine = onlineSession.Engine`（保留）；`DeveloperMode = false` 保留。
- `OnOnlineError` 只 `Debug.LogError`（Lobby 场景负责显示）。

### 5.10 删除 Game 场景里的等待面板

- 用户在 Unity 里删除 `Game` 场景 Canvas 下的 `LobbyPanelUI` 物体（**第一步**，先删场景对象）。
- 之后删除脚本文件 `Assets/_Project/Scripts/UI/LobbyPanel.cs` 及其 `.meta`（避免留下死代码）。
- 顺序必须是「先删场景物体，再删脚本」，否则场景会报 Missing Script。

---

## 6. Lobby 场景搭建（用户手动，中文界面 / English）

### 6.1 新建场景

1. `File → New Scene`（文件 → 新建场景）→ 选 `Basic (Built-in)`（基础）→ 保存到 `Assets/_Project/Scenes/`，命名 **`Lobby`**。
2. `File → Build Settings`（文件 → 生成设置）→ `Add Open Scenes`（添加打开的场景），确保顺序为 `MainMenu`、`Lobby`、`Game`。

### 6.2 场景层级

```
Lobby.unity
├─ Main Camera            （2D 默认相机）
├─ EventSystem
├─ LobbySceneRoot         （空物体，挂 LobbySceneController）
└─ Canvas
   └─ LobbyUI             （空物体，挂 LobbyScreenPanel；宿主始终激活）
      └─ Root             （整体开关节点）
         ├─ Background     （Image，铺满）
         ├─ TitleText      （TMP_Text：「联机准备房间」）
         ├─ RoomCodeText   （TMP_Text：「房间码 ------」）
         ├─ PlayerList     （空 RectTransform，不加 Layout Group）
         │  ├─ PlayerRow0  （挂 LobbyPlayerRow）
         │  ├─ PlayerRow1
         │  ├─ PlayerRow2
         │  └─ PlayerRow3
         ├─ HostSettings   （空 RectTransform，仅房主显示）
         │  ├─ PlayerCountSlider （Slider，Min 2 / Max 4 / Whole Numbers）
         │  ├─ EnterpriseToggle  （Toggle）
         │  ├─ ApplyButton       （Button：「应用设置」）
         │  └─ SettingsSummaryText（TMP_Text）
         ├─ ClientReady    （空 RectTransform，仅客户端显示）
         │  └─ ReadyButton （Button，文本子物体命名为 ReadyLabel）
         ├─ StatusText     （TMP_Text）
         ├─ StartButton    （Button：「开始游戏」）
         └─ LeaveButton    （Button：「离开房间」）
```

每个 `PlayerRowN` 的内部结构（4 行都一样）：

```
PlayerRowN
├─ EmptyRoot        （TMP_Text：「等待玩家…」）
├─ OccupiedRoot
│  ├─ NameText      （TMP_Text）
│  ├─ StateText     （TMP_Text）
│  ├─ HostBadge     （Image 或 TMP_Text：「房主」）
│  └─ KickButton    （Button：「踢出」）
```

> 不要给 `PlayerList` 加 `VerticalLayoutGroup`：按项目约定，按钮与行位置由用户在 Scene 视图手动摆放。行高建议 60，纵向间距 8。

### 6.3 接线清单（逐项拖入）

`LobbySceneRoot` 上的 `LobbySceneController`：

| 字段 | 拖入 |
| --- | --- |
| Session | `Assets/_Project/Data/GameSession.asset` |
| Panel | `Canvas/LobbyUI` |
| Game Scene Name | `Game` |
| Main Menu Scene Name | `MainMenu` |

`Canvas/LobbyUI` 上的 `LobbyScreenPanel`：

| 字段 | 拖入 |
| --- | --- |
| Root | `LobbyUI/Root` |
| Room Code Text | `Root/RoomCodeText` |
| Status Text | `Root/StatusText` |
| Settings Summary Text | `Root/HostSettings/SettingsSummaryText` |
| Player Rows | 依次拖 `PlayerRow0…3`（数组长度 4） |
| Host Settings Root | `Root/HostSettings` |
| Player Count Slider | `HostSettings/PlayerCountSlider` |
| Enterprise Toggle | `HostSettings/EnterpriseToggle` |
| Apply Settings Button | `HostSettings/ApplyButton` |
| Client Ready Root | `Root/ClientReady` |
| Ready Button | `ClientReady/ReadyButton` |
| Ready Button Label | `ClientReady/ReadyButton/ReadyLabel` |
| Start Button | `Root/StartButton` |
| Leave Button | `Root/LeaveButton` |

每个 `PlayerRowN` 上的 `LobbyPlayerRow`：

| 字段 | 拖入 |
| --- | --- |
| Occupied Root | `PlayerRowN/OccupiedRoot` |
| Empty Root | `PlayerRowN/EmptyRoot` |
| Name Text | `OccupiedRoot/NameText` |
| State Text | `OccupiedRoot/StateText` |
| Host Badge | `OccupiedRoot/HostBadge` |
| Kick Button | `OccupiedRoot/KickButton` |

按钮 OnClick（Inspector 里手动绑定，**不要**用运行时自动绑定）：

| 按钮 | OnClick 目标 |
| --- | --- |
| `ApplyButton` | `LobbyUI` → `LobbyScreenPanel.OnApplySettingsClicked` |
| `ReadyButton` | `LobbyUI` → `LobbyScreenPanel.OnReadyClicked` |
| `StartButton` | `LobbyUI` → `LobbyScreenPanel.OnStartClicked` |
| `LeaveButton` | `LobbyUI` → `LobbyScreenPanel.OnLeaveClicked` |
| `PlayerRowN/KickButton` | `PlayerRowN` → `LobbyPlayerRow.OnKickClicked` |

### 6.4 MainMenu 需要补的两个输入框

1. `StartPanel` 内新增 `HostNicknameInput`（`UI → Input Field - TextMeshPro`），placeholder「房主昵称」；拖到 `MainMenuController.Host Nickname Input`。
2. `JoinPanel` 内新增 `ClientNicknameInput`，placeholder「你的昵称」；拖到 `MainMenuController.Client Nickname Input`。
3. `MainMenuController` 新增字段 `Lobby Scene Name = Lobby`（默认值已在代码里，确认即可）。
4. `MainMenu` 场景根节点新建空物体 **`OnlineSessionHost`**（不要放在 Canvas 下），挂 `OnlineSessionHost` 组件，拖入 `GameSession.asset`。
5. 确认 `Btn_CreateRoom` 仍绑 `OpenStartPanelForHost`、`Btn_JoinGame` 仍绑 `OpenJoinPanel`、`Btn_StartHost` 绑 `StartHostGame`、`Btn_Join` 绑 `StartClientGame`。

---

## 7. 边界情况

| 情况 | 处理 |
| --- | --- |
| 房主把人数改到小于当前成员数 | `SetSettings` 拒绝，状态栏提示「人数不能少于已在房间里的玩家数」 |
| 未满员或有人未准备就点开始 | `RequestStart` 拒绝并在状态栏说明原因；按钮同时置灰 |
| 开始瞬间有人挤进房间 | 同一次 `UpdateLobbyAsync` 写入 `IsLocked + Started + RelayJoinCode`，之后加入会被服务拒绝 |
| 客户端进房时房间已开始 | 加入后检测 `Started == "1"` → 退出并提示「该房间已经开局」 |
| 客户端被踢 | 轮询发现名单里没有自己 → 提示「你已被房主移出房间」→ 2.5 秒后回主菜单 |
| 房主离开 / 关房 | 客户端连续 3 次刷新失败 → 提示「房主已关闭房间」→ 回主菜单 |
| 房主掉线（未点离开） | 客户端 Lobby 心跳停止，15 分钟内被服务回收；客户端刷新失败 → 回主菜单 |
| 两端协议版本不同 | 加入后校验 `Protocol` 不一致 → 退出并提示「请使用同一版本游戏」 |
| 昵称为空 | 房主用「房主」，客户端用「玩家」，均截断到 12 字符 |
| 客户端在 Lobby 场景就收到快照 | `OnlineSessionHost` 已建立引擎；`GameBootstrap` 检测 `Engine != null` 直接初始化界面 |
| 主机在客户端连上前就广播了快照 | `RelayTransport` 缓存最新快照，`PeerJoined` 时自动补发 |
| 单机 / 直接运行 Game 场景 | `session.Role == None`，完全不碰 `OnlineSessionHost`，行为与现在一致 |
| 直接运行 Lobby 场景调试 | 找不到 `OnlineSessionHost` → 状态栏报错并回主菜单 |
| 同机双开两个客户端 | UGS 匿名账号缓存相同，Lobby 可能视为同一玩家；**只能用两台机器 / 两个不同用户测试** |
| 重复触发 `GameReady` | `LobbySceneController.sceneLoadRequested` 去重 |

---

## 8. 分阶段实施

### 阶段 A：数据与网络层

1. 新增 `Core/Net/LobbyMemberInfo.cs`；
2. 改造 `Net/LobbyClient.cs`（名单 / 准备 / 设置 / 踢人 / 开始 / 删掉满员自动开始）；
3. `GameSession` 加 `Nickname`；
4. 编译验证（用户 Ctrl+R）。

### 阶段 B：常驻会话与场景控制

5. 新增 `App/OnlineSessionHost.cs`；
6. 新增 `App/LobbySceneController.cs`；
7. 新增 `UI/LobbyScreenPanel.cs`、`UI/LobbyPlayerRow.cs`；
8. 编译验证。

### 阶段 C：入口与 Game 场景收尾

9. 改 `MainMenuController`（昵称输入 + 加载 Lobby 场景）；
10. 改 `GameBootstrap`（只消费常驻会话，删除自建网络对象）；
11. 用户删除 Game 场景的 `LobbyPanelUI` 物体；
12. 删除 `UI/LobbyPanel.cs` 与 `.meta`；
13. 编译验证。

### 阶段 D：Unity 手动搭建

14. 新建 `Lobby` 场景并按第 6 节搭建、接线；
15. Build Settings 加入 `Lobby`；
16. MainMenu 加 `OnlineSessionHost` 物体与两个昵称输入框。

### 阶段 E：验证与清理

17. 双机验证（第 9 节）；
18. 删除临时诊断日志（`GameBootstrap` 已随重构删除；`LobbyClient.StartRelayNow` 中的警告一并删除）。

---

## 9. 验证步骤

### 9.1 单机回归

1. 主菜单 →「单机游戏」→ 进入 Game，确认地图、界面、开局流程与改动前一致；
2. 直接打开 Game 场景运行，确认无联机对象也能正常开局。

### 9.2 房主流程（单机可验）

1. 主菜单 →「创建游戏」→ 填昵称 → 进入 Lobby；
2. 确认房间码出现、列表显示「房主 / 自己昵称」、其余 3 行「等待玩家…」；
3. 调整人数 4 → 3：房间上限变化，状态摘要同步；
4. 未满员时「开始游戏」应为灰色；人数改为 2 且只有房主时仍不可开始（无人准备）；
5. 点「离开房间」→ 回主菜单，房间被删除。

### 9.3 双机联机（两台电脑，各用不同 UGS 匿名账号环境）

1. A 创建 2 人房，记下房间码；
2. B「加入游戏」→ 输码 + 昵称 → 进入 Lobby；
3. A 的列表出现 B（未准备），B 看到 A 是房主；
4. B 点「准备」，A 界面 B 状态变「已准备」，A 的「开始游戏」变为可点；
5. A 点「开始游戏」→ 两端都进入 Game，B 的地图人数 / 扩展与 A 一致，B 能正常行动；
6. 重跑一次，验证踢人：B 准备前 A 点 B 行「踢出」→ B 提示被移出并回主菜单；
7. 重跑一次，验证满员门槛：3 人房只进 2 人时开始按钮保持灰色。

### 9.4 额度检查

1. 未点开始前 Relay 不应有分配（Console 无「中继已就绪」）；
2. 点击开始后才出现「中继已就绪」；
3. 房主离开后 Lobby 被删除（Console 出现「房间已删除」）。

---

## 10. 不做项

- **房主转让**：Relay 的权威主机绑定在当前主机的 `RelayTransport` 上；转移房主需要重新申请 Relay、让所有客户端断开重连，Lobby 的 `HostId` 也无法直接改成 Relay 主机。成本远高于收益，本版不做，准备房间不放置转让按钮。
- **电脑对手（单机 AI）**：用户明确本轮不做。
- **同机双开测试**：受匿名账号缓存限制，不提供。
- **游戏内座位与准备房间行号对齐**：Relay 按接入顺序分配最小空位，可能与列表顺序不同；只影响玩家颜色，不影响规则，暂不处理。

---

## 11. 关键决策汇总

1. 准备阶段使用**独立 `Lobby` 场景**，主菜单只负责写配置并切场景。
2. 网络对象由 **`OnlineSessionHost`（DontDestroyOnLoad）** 唯一持有，跨 Lobby / Game 存活并持续 `Poll`。
3. 昵称在 **MainMenu** 输入（房主在开局设置弹窗、客户端在加入弹窗），存入 `GameSession.Nickname`；Lobby 场景不再做昵称门。
4. `LobbyClient` **不再自动开局**，开始完全由房主点击 + `CanStart` 门控。
5. 中继码与 `Started` **一次性原子写入**，写入成功才切 Game。
6. 客户端在 Lobby 阶段就从 `Lobby.Data` 同步人数 / 扩展，回写 `GameSession`，保证两端地图一致。
7. `LobbyMemberInfo` 放 `Core/Net`，让 `UI` 与 `Net` 都能引用，不破坏程序集分层。
8. Game 场景的 `LobbyPanelUI` 与 `LobbyPanel.cs` 全部移除。
