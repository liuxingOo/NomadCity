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

            // 联机时窗口失焦不能让游戏循环停掉：
            // 主机一旦被切到后台（同一台机器双开测试、或玩家 alt-tab），Update 不再跑，
            // 客户端的命令就要等到主机窗口重新获得焦点才被处理 —— 表现就是「延迟突然几百毫秒甚至几秒」。
            Application.runInBackground = true;
        }

        /// <summary>房主：登记准备房间。此时不申请 Relay。</summary>
        public void BeginHost(int playerCount, bool enterprise, string nickname)
        {
            if (!Prepare())
                return;

            Role = OnlineRole.Host;

            Lobby.StartRequested += OnHostStartRequested;
            Lobby.HostStartPublished += OnHostStartPublished;
            Lobby.StartPublishFailed += OnStartPublishFailed;
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
        public void SetColor(int color) => Lobby?.SetColor(color, Session?.LocalSeat ?? -1);

        /// <summary>名单顺序（= 座位顺序）的已选颜色；未选为 -1。</summary>
        public int[] SeatColors => Lobby != null ? Lobby.SeatColors : System.Array.Empty<int>();

        /// <summary>
        /// 按**座位**给出各玩家已挑的颜色（未挑 = -1）。
        /// 名单是「按加入时间排序」的，而 Relay 座位是「按连入顺序」分配的：两个客户端几乎同时
        /// 收到开局信息时两者可能错位，直接按名单下标取会把颜色发给错的人。
        /// 这里先用身份令牌把名单项对到座位上，对不上再退回名单顺序（2 人局恒等）。
        /// </summary>
        public int[] ColorsBySeat()
        {
            int[] byIndex = SeatColors;
            if (byIndex.Length == 0 || Lobby == null || Session == null) return byIndex;

            var members = Lobby.Members;
            var bySeat = new int[byIndex.Length];

            for (int seat = 0; seat < bySeat.Length; seat++)
            {
                int index = -1;
                string token = Session.SeatToken(seat);

                // 首选：玩家自己写进 Lobby 的座位号（最精确，客户端和房主都能用）。
                for (int k = 0; k < members.Count; k++)
                {
                    if (members[k].Seat == seat)
                    {
                        index = k;
                        break;
                    }
                }

                // 其次：按身份令牌对齐（座位号还没写进 Lobby 时）。
                if (index < 0 && !string.IsNullOrEmpty(token))
                {
                    for (int k = 0; k < members.Count; k++)
                    {
                        if (members[k].Identity == token)
                        {
                            index = k;
                            break;
                        }
                    }
                }

                // 房主不参与握手（座位 0 就是自己），按「谁是房主」对齐。
                if (index < 0 && seat == 0)
                {
                    for (int k = 0; k < members.Count; k++)
                    {
                        if (members[k].IsHost)
                        {
                            index = k;
                            break;
                        }
                    }
                }

                if (index < 0) index = seat; // 对不上就按名单顺序
                bySeat[seat] = index < byIndex.Length ? byIndex[index] : -1;
            }

            return bySeat;
        }

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

        /// <summary>
        /// 玩家手动点「重连」：客户端立刻重新连一次主机（不等自动退避）。
        /// 主机侧没有可重连的对象，调用会被忽略。
        /// </summary>
        public void ReconnectNow()
        {
            if (Session == null)
            {
                Fail("还没有可重连的对局，请回到大厅重新加入。");
                return;
            }

            Session.ReconnectNow();
            StatusChanged?.Invoke("正在重新连接主机…");
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
            publishRequested = false;

            // 上一次已经开好中继、只是开局信息没写进 Lobby：直接复用现有中继码，省一次 Relay 分配。
            if (string.IsNullOrEmpty(Session.JoinCode))
                Session.Host(session.ToGameSetup(), session.PlayerCount);
        }

        /// <summary>
        /// 开局信息发布失败（网络 / UGS 超时，已自动重试过）：把发布闸门放回去，
        /// 让房主能再点一次「开始游戏」；中继已开好，重试不会再申请一次中继。
        /// </summary>
        private void OnStartPublishFailed()
        {
            waitingForRelayCode = false;
            publishRequested = false;
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
