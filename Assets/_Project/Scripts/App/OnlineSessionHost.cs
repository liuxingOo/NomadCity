using System;
using System.Collections.Generic;
using NomadCity.Core.Net;
using NomadCity.Core.Rules;
using NomadCity.Net;
using UnityEngine;

namespace NomadCity.App
{
    /// <summary>
    /// 联机会话的常驻宿主（DontDestroyOnLoad）。准备房间（Lobby 场景）与对局（Game 场景）
    /// 都要用它手里的同一套网络对象，切场景不能重建，否则会断线并丢掉已分配的中继。
    ///
    /// 职责：
    /// - 唯一持有 <see cref="RelayTransport"/> / <see cref="NetworkGameSession"/> / <see cref="LobbyClient"/>；
    /// - 每帧驱动 Poll；
    /// - 把「准备房间里的开始」翻译成「申请 Relay → 把中继码写回 Lobby → 允许切场景」；
    /// - 把 Lobby 事件转发给场景 UI，UI 不直接引用 Net 层。
    ///
    /// 放在 MainMenu 场景的一个根物体上；第一局开始后它会跟着进入后续所有场景。
    /// </summary>
    public sealed class OnlineSessionHost : MonoBehaviour
    {
        [Tooltip("跨场景配置资产：Assets/_Project/Data/GameSession.asset（与主菜单、GameBootstrap 用同一个）")]
        [SerializeField] private GameSession session;

        public static OnlineSessionHost Instance { get; private set; }

        /// <summary>准备房间层；未进入联机时为 null。</summary>
        public LobbyClient Lobby { get; private set; }

        /// <summary>Relay 对局会话；未进入联机时为 null。</summary>
        public NetworkGameSession Session { get; private set; }

        public OnlineRole Role { get; private set; } = OnlineRole.None;

        /// <summary>已经可以加载 Game 场景（房主写库成功 / 客户端握手成功）。</summary>
        public bool GameStarting { get; private set; }

        /// <summary>可以加载 Game 场景了。LobbySceneController 收到后切场景。</summary>
        public event Action GameReady;

        public event Action<string> ErrorArrived;

        // —— Lobby 事件转发（UI 只订阅这里，不直接碰 Net 层） ——
        public event Action<string> RoomCodeReady;
        public event Action<IReadOnlyList<LobbyMemberInfo>> RosterChanged;
        public event Action<int, bool> SettingsChanged;
        public event Action<bool> StartEligibilityChanged;
        public event Action<string> StatusChanged;

        /// <summary>房主已经开始申请中继，正在等 Relay 分配。</summary>
        private bool waitingForRelayCode;

        /// <summary>中继码已经交给 LobbyClient 去写库（只写一次）。</summary>
        private bool publishRequested;

        private bool gameReadyRaised;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[联机] 场景里出现了第二个 OnlineSessionHost，已销毁重复的那个。", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>房主：登记准备房间。此时不申请 Relay。</summary>
        public void BeginHost(int playerCount, bool enterprise, string nickname)
        {
            if (!Prepare())
                return;

            Role = OnlineRole.Host;

            Lobby.StartRequested += OnHostStartRequested;
            Lobby.HostStartPublished += OnHostStartPublished;
            Lobby.HostRoom(playerCount, enterprise, nickname);

            Debug.Log($"[联机] 正在登记 {playerCount} 人房间；房间码出来后把它告诉好友。", this);
        }

        /// <summary>客户端：用房间码加入准备房间。</summary>
        public void BeginClient(string joinCode, string nickname)
        {
            if (!Prepare())
                return;

            if (string.IsNullOrWhiteSpace(joinCode))
            {
                Fail("客户端模式需要 6 位房间码。");
                return;
            }

            Role = OnlineRole.Client;
            Lobby.StartPublished += OnClientStartPublished;
            Lobby.JoinRoom(joinCode.Trim(), nickname);

            Debug.Log($"[联机] 以客户端身份加入房间：{joinCode}", this);
        }

        /// <summary>客户端切换准备状态。</summary>
        public void SetReady(bool ready) => Lobby?.SetReady(ready);

        /// <summary>房主修改人数 / 企业扩展包。</summary>
        public void SetSettings(int playerCount, bool enterprise) => Lobby?.SetSettings(playerCount, enterprise);

        /// <summary>开局前选阵营颜色（房主与客户端都调用，写进 Lobby 的玩家数据同步给别人）。</summary>
        public void SetColor(int color) => Lobby?.SetColor(color);

        /// <summary>名单顺序（= 座位顺序）的已选颜色；未选为 -1。</summary>
        public int[] SeatColors => Lobby != null ? Lobby.SeatColors : System.Array.Empty<int>();

        /// <summary>本局行动顺序（座位号，下标 0 = 起始玩家）；房主开局时随机排出，所有玩家共享。</summary>
        public int[] SeatOrder => Lobby != null ? Lobby.SeatOrder : System.Array.Empty<int>();

        /// <summary>房主踢人。</summary>
        public void Kick(string playerId) => Lobby?.Kick(playerId);

        /// <summary>房主点开始。</summary>
        public void RequestStart() => Lobby?.RequestStart();

        /// <summary>离开房间 / 收房：删 Lobby、断开 Relay。幂等。</summary>
        public void Leave()
        {
            if (Lobby != null)
            {
                Lobby.Shutdown();
                Lobby = null;
            }

            if (Session != null)
            {
                Session.Shutdown();
                Session = null;
            }

            Role = OnlineRole.None;
            GameStarting = false;
            gameReadyRaised = false;
            waitingForRelayCode = false;
            publishRequested = false;
        }

        private bool Prepare()
        {
            if (session == null)
            {
                Fail("OnlineSessionHost 没有指定 GameSession 资产。");
                return false;
            }

            // 重进准备房间时（上一局已结束）先清掉旧连接，避免同时存在两套网络对象。
            if (Lobby != null || Session != null)
                Leave();

            var transport = new RelayTransport();
            Session = new NetworkGameSession(transport);
            Lobby = new LobbyClient();

            Session.ErrorArrived += Fail;
            Lobby.ErrorArrived += Fail;
            Lobby.StatusChanged += message =>
            {
                Debug.Log("[联机] " + message, this);
                StatusChanged?.Invoke(message);
            };
            Lobby.RoomCodeReady += code => RoomCodeReady?.Invoke(code);
            Lobby.RosterChanged += OnRosterChanged;
            Lobby.StartEligibilityChanged += value => StartEligibilityChanged?.Invoke(value);
            Lobby.SettingsChanged += OnSettingsChanged;
            Lobby.Kicked += OnKicked;
            Lobby.RoomClosed += OnRoomClosed;

            return true;
        }

        private void Update()
        {
            Lobby?.Poll(Time.deltaTime);
            Session?.Poll(Time.deltaTime);

            // 房主：点了开始 → 等 Relay 分配 → 把中继码写回 Lobby。
            if (Role == OnlineRole.Host
                && waitingForRelayCode
                && !publishRequested
                && Session != null
                && !string.IsNullOrEmpty(Session.JoinCode))
            {
                publishRequested = true;
                Lobby?.PublishStarted(Session.JoinCode);
            }

            // 额度保险第 3 条：30 分钟没有任何命令 → 视为放弃，关房并断开。
            if (Session != null && Session.IdleExpired)
            {
                Fail("对局 30 分钟没有任何操作，已自动关房（节省 Relay 额度）。");
                Leave();
            }
        }

        private void OnApplicationQuit() => Leave();

        // —— 房主流程 ——

        private void OnHostStartRequested()
        {
            if (Session == null) return;

            waitingForRelayCode = true;
            Session.Host(session.ToGameSetup(), session.PlayerCount);
        }

        private void OnHostStartPublished()
        {
            RaiseGameReady();
        }

        // —— 客户端流程 ——

        private void OnClientStartPublished(string relayJoinCode)
        {
            if (Session == null) return;

            Session.SetupArrived += OnClientSetupArrived;
            Session.Join(relayJoinCode);
        }

        private void OnClientSetupArrived(GameSetup setup, int seat)
        {
            // 客户端的地图按主机下发的人数 / 扩展构建，避免两端地图不一致。
            session.PlayerCount = setup.PlayerCount;
            session.EnterpriseExpansion = setup.IsEnterpriseEnabled;

            Debug.Log($"[联机] 已被分配座位 {seat + 1}。", this);
            RaiseGameReady();
        }

        private void OnSettingsChanged(int playerCount, bool enterprise)
        {
            // 房主在准备房间改过人数 / 扩展包后，开局必须按最新值建图；
            // 客户端也靠这里同步房主设置，保证两端地图一致。
            if (session != null)
            {
                session.PlayerCount = playerCount;
                session.EnterpriseExpansion = enterprise;
            }

            SettingsChanged?.Invoke(playerCount, enterprise);
        }

        /// <summary>
        /// 名单变化：按加入顺序（也就是 Relay 座位分配顺序）把昵称记进跨场景配置，
        /// 对局里顶栏就能显示玩家名而不是「玩家 1 / 玩家 2」。
        /// </summary>
        private void OnRosterChanged(IReadOnlyList<LobbyMemberInfo> list)
        {
            if (session != null)
            {
                var names = new string[list.Count];
                for (int i = 0; i < list.Count; i++)
                    names[i] = list[i].Nickname;

                session.PlayerNicknames = names;
            }

            RosterChanged?.Invoke(list);
        }

        private void OnKicked()
        {
            // 先收摊再报错：界面据此判断「Role 已复位 = 需要离开准备房间」。
            Leave();
            ErrorArrived?.Invoke("你已被房主移出房间。");
        }

        private void OnRoomClosed(string reason)
        {
            Leave();
            ErrorArrived?.Invoke(string.IsNullOrEmpty(reason) ? "房主已关闭房间。" : reason);
        }

        private void RaiseGameReady()
        {
            if (gameReadyRaised) return;

            gameReadyRaised = true;
            GameStarting = true;
            GameReady?.Invoke();
        }

        private void Fail(string message)
        {
            Debug.LogError("[联机] " + message, this);
            ErrorArrived?.Invoke(message);
        }
    }
}
