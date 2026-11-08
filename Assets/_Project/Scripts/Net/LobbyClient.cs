using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NomadCity.Core.Net;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace NomadCity.Net
{
    /// <summary>
    /// Lobby 房间层（准备房间）：只负责「房间码 / 名单 / 准备 / 设置 / 踢人 / 开局发布」，
    /// 不碰游戏命令，也不申请 Relay —— 中继由 <c>OnlineSessionHost</c> 持有，本类只把中继码写进 / 读出 Lobby。
    ///
    /// 分工：
    /// - 房主：登记一个私有 Lobby，展示 6 位 LobbyCode；可在房间里改人数 / 扩展包、踢人；
    ///   只有「人数满 + 所有加入玩家已准备」时才允许 <see cref="RequestStart"/>。
    /// - 客户端：用房间码加入，上报昵称与准备状态，轮询到 <c>Started = 1</c> 后拿到中继码。
    ///
    /// 额度保险（对应联机计划里的第 1/2/5/6 条）：
    /// - 懒分配 Relay：本类从头到尾不申请 Relay，房主没点开始就不产生 Relay 连接分钟；
    /// - 等待房 TTL：15 分钟没有开局 → 删房间并收摊；
    /// - 退出即释放：<see cref="Shutdown"/> 删除房间（房主）/ 退出成员（客户端）；
    /// - 建房冷却：同一客户端 10 秒内不能重复建房，避免连点刷房间。
    /// </summary>
    public sealed class LobbyClient
    {
        /// <summary>Lobby.Data 里存放 Relay 中继码的键，房主写、客户端读。</summary>
        public const string RelayJoinCodeKey = "RelayJoinCode";

        /// <summary>协议版本，防止两端线格式不一致导致静默崩溃。</summary>
        public const string ProtocolKey = "Protocol";

        /// <summary>房主设置的人数（2–4），与 <c>Lobby.MaxPlayers</c> 同步。</summary>
        public const string PlayerCountKey = "PlayerCount";

        /// <summary>企业扩展包开关（"0" / "1"）。</summary>
        public const string EnterpriseKey = "Enterprise";

        /// <summary>是否已开局（"0" / "1"）。客户端据此连 Relay。</summary>
        public const string StartedKey = "Started";

        /// <summary>玩家昵称（Player.Data）。</summary>
        public const string NicknameKey = "Nickname";

        /// <summary>玩家准备状态（Player.Data，"0" / "1"）。</summary>
        public const string ReadyKey = "Ready";

        /// <summary>玩家选定的阵营颜色（Player.Data，PlayerColor 的整数值；空 = 未选）。</summary>
        public const string ColorKey = "Color";

        /// <summary>
        /// 玩家身份令牌（Player.Data）。与 Relay 重连用的是同一个本机令牌，重启游戏后不变；
        /// 房主据此把「同一台机器重新进房」产生的重复席位并回一个，而不是越占越多。
        /// </summary>
        public const string IdentityKey = "Identity";

        /// <summary>
        /// 玩家自己的 Relay 座位号（Player.Data）。名单顺序与座位顺序不一定一致，
        /// 所有人都靠它把「谁挑了什么颜色」对到座位上。
        /// </summary>
        public const string SeatKey = "Seat";

        /// <summary>上一次进过的 6 位房间码（存本地）。重启游戏后主菜单预填，省得再手输。</summary>
        private const string LastRoomCodeKey = "NomadCity.LastRoomCode";

        /// <summary>上次进入的房间码；没有时为空串。</summary>
        public static string LastRoomCode => PlayerPrefs.GetString(LastRoomCodeKey, string.Empty);

        private static void RememberRoomCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return;

            PlayerPrefs.SetString(LastRoomCodeKey, code.Trim());
            PlayerPrefs.Save();
        }

        /// <summary>房主开局前随机排出的行动顺序（Lobby.Data，"座位号,座位号,…"；空 = 按座位自然顺序）。</summary>
        public const string OrderKey = "Order";

        private const int MaxNicknameLength = 12;

        /// <summary>Host 维护房间存活的心跳间隔；低于 15 秒 Lobby 会超时回收。</summary>
        private const float HeartbeatIntervalSeconds = 15f;

        /// <summary>等待房 TTL：创建后这么久还没开局，就自动关房（额度保险第 2 条）。</summary>
        private const float WaitingRoomTimeoutSeconds = 15f * 60f;

        /// <summary>
        /// 刷新房间信息（名单 / 设置 / 开局标记 / 准备状态的间隔）。
        /// 1 秒是 UGS Lobby「Get Lobby」的官方限流上限（每玩家每秒 1 次）；
        /// 之前压到 0.5 秒就是 Console 里成串 request_rate_limited 的来源，反而更慢。
        /// 真被限流时还会按 <see cref="refreshBackoff"/> 自动退避。
        /// </summary>
        private const float RefreshIntervalSeconds = 1f;

        /// <summary>被 UGS 限流后临时拉长的刷新间隔，成功一次就清零。</summary>
        private float refreshBackoff;

        /// <summary>牌局已经建好，房间信息不再需要刷新（见 <see cref="StopRefreshing"/>）。</summary>
        private bool refreshStopped;

        /// <summary>牌局已开打（房主侧）：刷新降频到 <see cref="SlowRefreshIntervalSeconds"/>。</summary>
        private bool refreshSlow;

        /// <summary>
        /// 牌局开打后房主的刷新间隔。不能停死：掉线玩家重启后要重新进房，
        /// 同身份的旧席位必须继续被清掉，否则房间满员会把他挡在 Lobby 层外面。
        /// </summary>
        private const float SlowRefreshIntervalSeconds = 5f;

        /// <summary>建房冷却：同一客户端 10 秒内不能重复建房（额度保险第 6 条）。</summary>
        private const float CreateCooldownSeconds = 10f;

        /// <summary>客户端连续这么多次刷不到房间，才判定「房主已关闭房间」，避免网络抖动误报。</summary>
        private const int RoomGoneThreshold = 3;

        private static DateTime lastCreateUtc = DateTime.MinValue;

        private Unity.Services.Lobbies.Models.Lobby lobby;
        private bool isHost;
        private bool shutdown;

        /// <summary>房主已经点了开始（之后不再接受名单 / 设置变化，也不再计等待房 TTL）。</summary>
        private bool startRequested;

        /// <summary>客户端已经交接过中继码（只交一次）。</summary>
        private bool startPublishedHandled;

        /// <summary>同一条 Lobby 请求还没回来时不再发第二条，避免并发打满限流。</summary>
        private bool requestInFlight;

        private bool playerUpdateInFlight;
        private bool settingsUpdateInFlight;
        private bool publishInFlight;

        /// <summary>房主正在按身份回收重复席位（同一条名单刷新里不重复发请求）。</summary>
        private bool duplicateCleanupInFlight;

        /// <summary>刷新失败只提示一次，避免每 2 秒刷一条警告。</summary>
        private bool refreshFailureLogged;

        /// <summary>客户端连续刷新失败的次数；成功一次就清零。</summary>
        private int failedRefreshes;

        private float heartbeatTimer;
        private float waitingTimer;
        private float refreshTimer;

        private readonly List<LobbyMemberInfo> members = new List<LobbyMemberInfo>();
        private int playerCountSetting = 4;
        private bool enterpriseSetting;

        /// <summary>本局随机排出的行动顺序（座位号，下标 0 = 起始玩家）；开局前由房主生成并随 Started 发布。</summary>
        private int[] seatOrder = System.Array.Empty<int>();

        private bool canStart;
        private string lastRosterSignature = string.Empty;
        private string lastSettingsSignature = string.Empty;

        /// <summary>房主展示给玩家的 6 位房间码；房间还没建好时为 null。</summary>
        public string LobbyCode => lobby != null ? lobby.LobbyCode : null;

        /// <summary>房间是否已经能加入（房主已登记 Lobby）。</summary>
        public bool RoomReady => lobby != null;

        public bool IsHost => isHost;

        /// <summary>当前成员（按加入时间排序；房主在前）。</summary>
        public IReadOnlyList<LobbyMemberInfo> Members => members;

        /// <summary>房主设置的人数（2–4）。</summary>
        public int PlayerCountSetting => playerCountSetting;

        /// <summary>房主设置的企业扩展包。</summary>
        public bool EnterpriseSetting => enterpriseSetting;

        /// <summary>
        /// 本局行动顺序（座位号，下标 0 = 起始玩家）。房主开局时随机生成并写进 Lobby，
        /// 所有玩家据此排座与倒序挑选企业家；空数组表示按座位自然顺序。
        /// </summary>
        public int[] SeatOrder => seatOrder;

        public LobbySettings CurrentSettings => new LobbySettings(playerCountSetting, enterpriseSetting);

        /// <summary>房主现在是否可以开局：人数满 + 除房主外全部准备 + 房间未锁定。</summary>
        public bool CanStart => canStart;

        /// <summary>本机的 UGS 玩家 ID。</summary>
        public string SelfPlayerId
        {
            get
            {
                try
                {
                    return AuthenticationService.Instance.IsSignedIn
                        ? AuthenticationService.Instance.PlayerId
                        : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>进度提示（可直接显示在准备房间的状态栏）。</summary>
        public event Action<string> StatusChanged;

        /// <summary>错误（房间码无效、服务不可达、设置非法等）。</summary>
        public event Action<string> ErrorArrived;

        /// <summary>房主的 6 位房间码已生成。</summary>
        public event Action<string> RoomCodeReady;

        /// <summary>成员名单变化（只在内容真的变了时才抛）。</summary>
        public event Action<IReadOnlyList<LobbyMemberInfo>> RosterChanged;

        /// <summary>人数 / 企业扩展变化（只在内容真的变了时才抛）。</summary>
        public event Action<int, bool> SettingsChanged;

        /// <summary>房主的「开始游戏」按钮是否可点，值变化时抛。</summary>
        public event Action<bool> StartEligibilityChanged;

        /// <summary>房主点了开始：上层应去申请 Relay。</summary>
        public event Action StartRequested;

        /// <summary>客户端：读到 <c>Started = 1</c> 与中继码，可以去连 Relay 了。</summary>
        public event Action<string> StartPublished;

        /// <summary>房主：开局信息（中继码 / Started / 锁定）已成功写入 Lobby，可以切 Game 场景了。</summary>
        public event Action HostStartPublished;

        /// <summary>
        /// 房主：重试若干次后仍没把开局信息写进 Lobby（网络或 UGS 超时）。
        /// 上层收到后应把「开始游戏」按钮放回可用状态，让房主再点一次。
        /// </summary>
        public event Action StartPublishFailed;

        /// <summary>客户端：自己已经不在名单里（被房主踢出）。</summary>
        public event Action Kicked;

        /// <summary>客户端：房间已不存在（房主关房 / 掉线被回收）。</summary>
        public event Action<string> RoomClosed;

        /// <summary>房主登记房间。此时**不**申请 Relay。</summary>
        public void HostRoom(int playerCount, bool enterprise, string nickname)
        {
            if (shutdown) return;

            if (isHost)
            {
                Fail("已经开过房间了。");
                return;
            }

            if ((DateTime.UtcNow - lastCreateUtc).TotalSeconds < CreateCooldownSeconds)
            {
                Fail("建房太频繁，请等 10 秒再试（避免重复创建房间占用额度）。");
                return;
            }

            isHost = true;
            playerCountSetting = Mathf.Clamp(playerCount, 2, 4);
            enterpriseSetting = enterprise;
            waitingTimer = 0f;
            CreateRoomAsync(playerCountSetting, enterpriseSetting, NormalizeNickname(nickname, "房主"));
        }

        /// <summary>客户端用 6 位房间码加入。</summary>
        public void JoinRoom(string lobbyCode, string nickname)
        {
            if (shutdown || isHost) return;

            if (string.IsNullOrWhiteSpace(lobbyCode))
            {
                Fail("房间码为空。");
                return;
            }

            JoinRoomAsync(lobbyCode.Trim(), NormalizeNickname(nickname, "玩家"));
        }

        /// <summary>客户端切换准备状态。</summary>
        public void SetReady(bool ready)
        {
            if (shutdown || isHost || lobby == null) return;
            SetReadyAsync(ready);
        }

        /// <summary>
        /// 开局前选阵营颜色（房主与客户端都可调用）：写进自己的 Player.Data，
        /// 其他人靠名单刷新看到「哪些颜色已被占」，房主据此决定何时可以开始建局。
        /// </summary>
        public void SetColor(int color, int seat = -1)
        {
            if (shutdown || lobby == null) return;
            if (color < 0 || color > 3) return;

            SetColorAsync(color, seat);
        }

        /// <summary>按名单顺序的已选颜色，未选为 -1（按座位对齐请用 OnlineSessionHost.ColorsBySeat）。</summary>
        public int[] SeatColors
        {
            get
            {
                var result = new int[members.Count];
                for (int i = 0; i < members.Count; i++) result[i] = members[i].Color;
                return result;
            }
        }

        /// <summary>房主修改人数 / 企业扩展包，实时同步给所有客户端。</summary>
        public void SetSettings(int playerCount, bool enterprise)
        {
            if (shutdown || !isHost || lobby == null || startRequested) return;

            int next = Mathf.Clamp(playerCount, 2, 4);
            if (next < members.Count)
            {
                Fail("人数不能少于已经在房间里的玩家数。");
                return;
            }

            if (next == playerCountSetting && enterprise == enterpriseSetting) return;

            SetSettingsAsync(next, enterprise);
        }

        /// <summary>房主踢人。</summary>
        public void Kick(string playerId)
        {
            if (shutdown || !isHost || lobby == null || startRequested) return;

            if (string.IsNullOrEmpty(playerId)) return;

            if (playerId == SelfPlayerId)
            {
                Fail("不能把自己踢出房间。");
                return;
            }

            KickAsync(playerId);
        }

        /// <summary>房主点「开始游戏」：先校验门槛，再让上层去申请 Relay。</summary>
        public void RequestStart()
        {
            if (shutdown || !isHost || startRequested) return;

            // 2 秒轮询可能还没回来，这里按当前名单再算一次，避免按钮刚变亮就点导致误判。
            RefreshCanStart();
            if (!canStart)
            {
                Fail("需要人数满，且所有加入的玩家都点了准备。");
                return;
            }

            startRequested = true;

            // 行动顺序在开局这一刻随机排出：人已经满了，下标即座位号。所有玩家共享这一份。
            seatOrder = ShuffledSeats(playerCountSetting);

            StatusChanged?.Invoke("正在申请 Relay 中继…");
            StartRequested?.Invoke();
        }

        /// <summary>房主：中继码已就绪，把开局信息一次性写进 Lobby（同时锁定房间）。</summary>
        public void PublishStarted(string relayJoinCode)
        {
            if (shutdown || !isHost || lobby == null) return;
            PublishStartedAsync(relayJoinCode);
        }

        /// <summary>每帧驱动：心跳、刷新房间、维护名单 / 设置 / 开局标记。</summary>
        public void Poll(float deltaTime)
        {
            if (shutdown || lobby == null) return;

            UpdateHeartbeat(deltaTime);

            if (isHost) PollHost(deltaTime);
            else PollClient(deltaTime);
        }

        /// <summary>
        /// 牌局已经建好：停掉房间信息轮询（心跳照旧，房间继续留着给掉线玩家回来拿中继码）。
        /// 企业家挑选阶段**不能**调用，那时还要靠名单刷新同步各自选的颜色。
        /// </summary>
        public void StopRefreshing() => refreshStopped = true;

        /// <summary>
        /// 牌局已经建好（房主侧）：刷新降频到 5 秒一次，而不是彻底停掉 ——
        /// 掉线玩家重启后重新进房时，同身份的旧席位还要靠这条刷新清理，否则满员会把他挡在外面。
        /// </summary>
        public void SlowDownRefreshing() => refreshSlow = true;

        /// <summary>关房：房主删 Lobby，客户端退出成员。之后不再发任何请求。</summary>
        public void Shutdown()
        {
            if (shutdown) return;
            shutdown = true;

            if (lobby == null) return;

            string lobbyId = lobby.Id;
            bool wasHost = isHost;
            lobby = null;

            if (wasHost) DeleteRoomAsync(lobbyId);
            else RemoveSelfAsync(lobbyId);
        }

        // —— 房主 ——

        private async void CreateRoomAsync(int maxPlayers, bool enterprise, string nickname)
        {
            try
            {
                await EnsureSignedInAsync();
                if (shutdown) return;

                StatusChanged?.Invoke("正在登记房间…");

                var options = new CreateLobbyOptions
                {
                    IsPrivate = true,
                    IsLocked = false,
                    Player = new Player { Data = BuildPlayerData(nickname, ready: true) },
                    Data = new Dictionary<string, DataObject>
                    {
                        [ProtocolKey] = MakeData(NetProtocol.Version.ToString()),
                        [PlayerCountKey] = MakeData(maxPlayers.ToString()),
                        [EnterpriseKey] = MakeData(enterprise ? "1" : "0"),
                        [StartedKey] = MakeData("0"),
                        [RelayJoinCodeKey] = MakeData(string.Empty),
                    },
                };

                Unity.Services.Lobbies.Models.Lobby created = await LobbyService.Instance.CreateLobbyAsync(
                    "Nomad City", maxPlayers, options);

                if (shutdown)
                {
                    DeleteRoomAsync(created.Id);
                    return;
                }

                lastCreateUtc = DateTime.UtcNow;
                lobby = created;

                playerCountSetting = Mathf.Clamp(created.MaxPlayers, 2, 4);
                enterpriseSetting = enterprise;
                ApplyLobby(created);
                RefreshCanStart();

                RoomCodeReady?.Invoke(created.LobbyCode);
                StatusChanged?.Invoke($"房间已登记，房间码：{created.LobbyCode}（等待玩家加入；此时不占用 Relay 额度）");
            }
            catch (Exception ex)
            {
                if (!shutdown) Fail($"创建房间失败：{ex.Message}");
            }
        }

        private void PollHost(float deltaTime)
        {
            // 注意：开局「发布」之后**不能**停止轮询。企业家挑选是写进 Lobby 的 Player.Data 再靠
            // 名单刷新同步的，房主要靠这一路轮询才知道别人挑了哪张；停掉就会永远卡在「等待 xxx 挑选」。
            // 真正该停的时机是牌局已经建好（<see cref="StopRefreshing"/>）。
            if (refreshStopped) return;

            // 等待房 TTL：还没开局就一直计时（开局后还在等企业家挑选，不能再按等待房超时关房）。
            if (!startRequested)
            {
                waitingTimer += deltaTime;
                if (waitingTimer >= WaitingRoomTimeoutSeconds)
                {
                    Fail("房间 15 分钟没有开局，已自动关闭（节省额度）。");
                    Shutdown();
                    return;
                }
            }

            refreshTimer += deltaTime;
            float interval = (refreshSlow ? SlowRefreshIntervalSeconds : RefreshIntervalSeconds) + refreshBackoff;
            if (refreshTimer < interval) return;

            refreshTimer = 0f;
            RefreshHostAsync();
        }

        private async void RefreshHostAsync()
        {
            if (requestInFlight || lobby == null || shutdown) return;

            requestInFlight = true;
            try
            {
                Unity.Services.Lobbies.Models.Lobby fresh = await LobbyService.Instance.GetLobbyAsync(lobby.Id);
                if (shutdown) return;

                refreshBackoff = 0f;
                refreshFailureLogged = false;
                ApplyLobby(fresh);
                RefreshCanStart();
            }
            catch (Exception ex)
            {
                if (!shutdown && !refreshFailureLogged)
                {
                    refreshFailureLogged = true;
                    Debug.LogWarning("[Lobby] 刷新房间失败：" + ex.Message);
                }

                // 被 UGS 限流（request_rate_limited）时拉长间隔；真恢复了一次就清零。
                if (!shutdown) refreshBackoff = Mathf.Min(refreshBackoff + 0.5f, 2f);
            }
            finally
            {
                requestInFlight = false;
            }
        }

        private async void SetSettingsAsync(int playerCount, bool enterprise)
        {
            if (settingsUpdateInFlight || lobby == null || shutdown) return;

            settingsUpdateInFlight = true;
            try
            {
                var options = new UpdateLobbyOptions
                {
                    MaxPlayers = playerCount,
                    Data = new Dictionary<string, DataObject>
                    {
                        [PlayerCountKey] = MakeData(playerCount.ToString()),
                        [EnterpriseKey] = MakeData(enterprise ? "1" : "0"),
                    },
                };

                lobby = await LobbyService.Instance.UpdateLobbyAsync(lobby.Id, options);
                if (shutdown) return;

                playerCountSetting = playerCount;
                enterpriseSetting = enterprise;
                ApplyLobby(lobby);
                RefreshCanStart();
                StatusChanged?.Invoke($"房间设置已更新：{playerCount} 人，企业扩展{(enterprise ? "已启用" : "未启用")}");
            }
            catch (Exception ex)
            {
                if (!shutdown) Fail($"更新房间设置失败：{ex.Message}");
            }
            finally
            {
                settingsUpdateInFlight = false;
            }
        }

        private async void KickAsync(string playerId)
        {
            if (requestInFlight || lobby == null || shutdown) return;

            requestInFlight = true;
            try
            {
                await LobbyService.Instance.RemovePlayerAsync(lobby.Id, playerId);
                if (shutdown) return;

                Unity.Services.Lobbies.Models.Lobby fresh = await LobbyService.Instance.GetLobbyAsync(lobby.Id);
                if (shutdown) return;

                ApplyLobby(fresh);
                RefreshCanStart();
                StatusChanged?.Invoke("已把该玩家移出房间。");
            }
            catch (Exception ex)
            {
                if (!shutdown) Fail($"踢人失败：{ex.Message}");
            }
            finally
            {
                requestInFlight = false;
            }
        }

        private async void PublishStartedAsync(string relayJoinCode)
        {
            if (publishInFlight || lobby == null || shutdown) return;

            publishInFlight = true;
            try
            {
                var options = new UpdateLobbyOptions
                {
                    // 不锁房：锁了之后「关掉游戏重开」的玩家就再也进不来，也就拿不到中继码回原座位。
                    // 房间是私有房（要 6 位房间码），且 Relay 只认本机身份令牌 —— 陌生玩家进来也占不到别人的座位。
                    Data = new Dictionary<string, DataObject>
                    {
                        [StartedKey] = MakeData("1"),
                        [RelayJoinCodeKey] = MakeData(relayJoinCode),
                        [PlayerCountKey] = MakeData(playerCountSetting.ToString()),
                        [EnterpriseKey] = MakeData(enterpriseSetting ? "1" : "0"),
                        [OrderKey] = MakeData(EncodeOrder(seatOrder)),
                    },
                };

                // 写库这一步一旦失败，房主就卡在准备房间里（「开始游戏」已经锁死），
                // 所以这里自己重试 3 次；全都失败才把按钮还给房主，让他手动重来。
                const int maxAttempts = 3;
                Exception lastError = null;
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    if (shutdown) return;

                    try
                    {
                        lobby = await LobbyService.Instance.UpdateLobbyAsync(lobby.Id, options);
                        if (shutdown) return;

                        StatusChanged?.Invoke("开局信息已发布，等待玩家接入…");
                        HostStartPublished?.Invoke();
                        return;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        Debug.LogWarning($"[Lobby] 发布开局信息失败（第 {attempt}/{maxAttempts} 次）：{ex.Message}");
                        if (attempt < maxAttempts)
                        {
                            StatusChanged?.Invoke($"开局信息发布失败，2 秒后重试（{attempt}/{maxAttempts}）…");
                            await Task.Delay(2000);
                        }
                    }
                }

                if (shutdown) return;

                // 中继已经开好了，重试时不需要重新申请：把「开始游戏」解禁即可。
                startRequested = false;
                RefreshCanStart();
                StartPublishFailed?.Invoke();
                Fail($"发布开局信息失败：{lastError?.Message}（请再点一次「开始游戏」重试）");
            }
            catch (Exception ex)
            {
                if (!shutdown) Fail($"发布开局信息失败：{ex.Message}");
            }
            finally
            {
                publishInFlight = false;
            }
        }

        // —— 客户端 ——

        private async void JoinRoomAsync(string lobbyCode, string nickname, int attempt = 0)
        {
            try
            {
                await EnsureSignedInAsync();
                if (shutdown) return;

                StatusChanged?.Invoke($"正在加入房间 {lobbyCode}…");

                var options = new JoinLobbyByCodeOptions
                {
                    Player = new Player { Data = BuildPlayerData(nickname, ready: false) },
                };

                Unity.Services.Lobbies.Models.Lobby joined =
                    await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode, options);

                if (shutdown)
                {
                    RemoveSelfAsync(joined.Id);
                    return;
                }

                lobby = joined;
                RememberRoomCode(joined.LobbyCode);

                string protocol = ReadLobbyValue(joined, ProtocolKey);
                if (!string.IsNullOrEmpty(protocol) && protocol != NetProtocol.Version.ToString())
                {
                    Fail("房主与你的游戏版本不一致，请使用同一版本游戏后再加入。");
                    Shutdown();
                    return;
                }

                // 已经开局的房间也允许加入：进对局后由 Relay 凭本机保存的身份令牌恢复原座位
                // （重启游戏后回到原座位走的就是这条路）；不认识的玩家会拿到空闲座位或被告知人满。
                bool started = IsStarted(joined);

                ApplyLobby(joined);
                StatusChanged?.Invoke(started
                    ? $"该房间已开局，正在凭本机身份恢复座位（{joined.LobbyCode}）…"
                    : $"已进入房间 {joined.LobbyCode}，等待房主开局…");

                // 重连场景（关掉游戏重开、再凭房间码进来）要**当场**接入：不能等下一次轮询 ——
                // 轮询可能被限流拖慢，玩家就会一直停在准备房间里「等房主再点一次开始游戏」。
                if (started && !startPublishedHandled)
                {
                    string relayCode = ReadLobbyValue(joined, RelayJoinCodeKey);
                    if (!string.IsNullOrEmpty(relayCode))
                    {
                        startPublishedHandled = true;
                        StartPublished?.Invoke(relayCode);
                    }
                    else
                    {
                        StatusChanged?.Invoke("房间已开局，但还没同步到中继信息，正在重试…");
                    }
                }
            }
            catch (LobbyServiceException ex) when (ex.Reason == LobbyExceptionReason.LobbyFull)
            {
                // 名额可能被「已经掉线、席位还没被 UGS 回收」的玩家占着（约半分钟后自动释放）。
                // 重启游戏回来时最常见，这里自动重试，不用玩家自己反复点。
                await RetryJoinWhenFullAsync(lobbyCode, nickname, attempt);
            }
            catch (Exception ex)
            {
                if (!shutdown) Fail($"加入房间失败：{ex.Message}");
            }
        }

        /// <summary>房间满员时的自动重试：等 UGS 把掉线玩家的席位回收再进（最多约 36 秒）。</summary>
        private async Task RetryJoinWhenFullAsync(string lobbyCode, string nickname, int attempt)
        {
            const int maxAttempts = 12;
            const int waitSeconds = 3;

            if (shutdown || lobby != null) return;

            if (attempt >= maxAttempts)
            {
                Fail("房间已满：掉线玩家的席位迟迟没有释放，请稍后再试，或让房主把多余成员踢出。");
                return;
            }

            StatusChanged?.Invoke(
                $"房间暂时满员（可能有掉线玩家的席位还没释放），{waitSeconds} 秒后自动重试…");

            await Task.Delay(waitSeconds * 1000);
            if (shutdown || lobby != null) return;

            JoinRoomAsync(lobbyCode, nickname, attempt + 1);
        }

        private async void SetReadyAsync(bool ready)
        {
            if (playerUpdateInFlight || lobby == null || shutdown) return;

            playerUpdateInFlight = true;
            try
            {
                var options = new UpdatePlayerOptions
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        [ReadyKey] = new PlayerDataObject(
                            PlayerDataObject.VisibilityOptions.Member, ready ? "1" : "0"),
                    },
                };

                await LobbyService.Instance.UpdatePlayerAsync(lobby.Id, SelfPlayerId, options);
            }
            catch (Exception ex)
            {
                if (!shutdown) Fail($"更新准备状态失败：{ex.Message}");
            }
            finally
            {
                playerUpdateInFlight = false;
            }
        }

        private async void SetColorAsync(int color, int seat)
        {
            if (playerUpdateInFlight || lobby == null || shutdown) return;

            playerUpdateInFlight = true;
            try
            {
                var data = new Dictionary<string, PlayerDataObject>
                {
                    [ColorKey] = new PlayerDataObject(
                        PlayerDataObject.VisibilityOptions.Member, color.ToString()),
                };

                // 顺带把座位号写进去：名单顺序 ≠ 座位顺序时，别人靠它把你的选择对到正确的座位上。
                if (seat >= 0)
                {
                    data[SeatKey] = new PlayerDataObject(
                        PlayerDataObject.VisibilityOptions.Member, seat.ToString());
                }

                var options = new UpdatePlayerOptions { Data = data };

                // 颜色是「开局必备」：这一条写不进去，房主就会永远停在「等待 xxx 挑选」，
                // 所以被限流 / 超时时自己重试几次，而不是直接放弃。
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    if (shutdown) return;

                    try
                    {
                        await LobbyService.Instance.UpdatePlayerAsync(lobby.Id, SelfPlayerId, options);
                        return;
                    }
                    catch (Exception ex)
                    {
                        if (attempt >= 3)
                        {
                            if (!shutdown) Fail($"提交阵营颜色失败：{ex.Message}");
                            return;
                        }

                        Debug.LogWarning($"[Lobby] 提交阵营颜色失败（第 {attempt}/3 次）：{ex.Message}");
                        await Task.Delay(1500);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!shutdown) Fail($"提交阵营颜色失败：{ex.Message}");
            }
            finally
            {
                playerUpdateInFlight = false;
            }
        }

        private void PollClient(float deltaTime)
        {
            // 拿到中继码之后仍要继续刷新：企业家挑选阶段要靠名单同步「哪些颜色已被占」，
            // 停早了会选到重复颜色、或者显示错误。牌局建好后由 <see cref="StopRefreshing"/> 停。
            if (refreshStopped) return;

            refreshTimer += deltaTime;
            if (refreshTimer < RefreshIntervalSeconds + refreshBackoff) return;

            refreshTimer = 0f;
            RefreshClientAsync();
        }

        private async void RefreshClientAsync()
        {
            if (requestInFlight || lobby == null || shutdown) return;

            requestInFlight = true;
            try
            {
                Unity.Services.Lobbies.Models.Lobby fresh = await LobbyService.Instance.GetLobbyAsync(lobby.Id);
                if (shutdown) return;

                failedRefreshes = 0;
                refreshBackoff = 0f;
                refreshFailureLogged = false;
                ApplyLobby(fresh);

                if (!ContainsSelf(fresh))
                {
                    Kicked?.Invoke();
                    Shutdown();
                    return;
                }

                if (IsStarted(fresh))
                {
                    string code = ReadLobbyValue(fresh, RelayJoinCodeKey);
                    if (!startPublishedHandled && !string.IsNullOrEmpty(code))
                    {
                        startPublishedHandled = true;
                        StatusChanged?.Invoke("房间已开局，正在连接中继…");
                        StartPublished?.Invoke(code);
                    }
                }
            }
            catch (Exception ex)
            {
                if (shutdown) return;

                // 自己已被移出房间：服务端会返回 PlayerNotFound / LobbyNotFound。
                if (ex is LobbyServiceException lobbyError
                    && lobbyError.Reason == LobbyExceptionReason.PlayerNotFound)
                {
                    Kicked?.Invoke();
                    Shutdown();
                    return;
                }

                // 房间被删：连续多次才判定关房，避免网络抖动误报。
                if (ex is LobbyServiceException notFoundError
                    && notFoundError.Reason == LobbyExceptionReason.LobbyNotFound)
                {
                    failedRefreshes++;
                    if (failedRefreshes >= RoomGoneThreshold)
                    {
                        RoomClosed?.Invoke("房主已关闭房间。");
                        Shutdown();
                    }
                    return;
                }

                if (!refreshFailureLogged)
                {
                    refreshFailureLogged = true;
                    Debug.LogWarning("[Lobby] 刷新房间信息失败：" + ex.Message);
                }

                // 被 UGS 限流（request_rate_limited）时拉长间隔；真恢复了一次就清零。
                refreshBackoff = Mathf.Min(refreshBackoff + 0.5f, 2f);
            }
            finally
            {
                requestInFlight = false;
            }
        }

        // —— 公共 ——

        /// <summary>把最新 Lobby 数据落到本地：名单 / 人数 / 扩展包 / 开始条件。</summary>
        private void ApplyLobby(Unity.Services.Lobbies.Models.Lobby fresh)
        {
            lobby = fresh;
            playerCountSetting = Mathf.Clamp(fresh.MaxPlayers, 2, 4);

            string enterprise = ReadLobbyValue(fresh, EnterpriseKey);
            if (!string.IsNullOrEmpty(enterprise)) enterpriseSetting = enterprise == "1";

            // 行动顺序只在「已开局」后读取：房主自己刚生成的顺序不能被开局前的轮询用旧值盖掉。
            if (IsStarted(fresh)) seatOrder = DecodeOrder(ReadLobbyValue(fresh, OrderKey), playerCountSetting);

            RefreshMembers();
            RefreshSettings();
        }

        private void RefreshMembers()
        {
            var list = new List<LobbyMemberInfo>();
            if (lobby.Players != null)
            {
                foreach (Player player in lobby.Players.OrderBy(p => p.Joined))
                {
                    list.Add(new LobbyMemberInfo(
                        player.Id,
                        ReadPlayerValue(player, NicknameKey),
                        player.Id == lobby.HostId,
                        ReadPlayerValue(player, ReadyKey) == "1",
                        ParseColor(ReadPlayerValue(player, ColorKey)),
                        ReadPlayerValue(player, IdentityKey),
                        ParseSeat(ReadPlayerValue(player, SeatKey))));
                }
            }

            members.Clear();
            members.AddRange(list);

            if (isHost) RemoveDuplicateSeats();

            string signature = BuildRosterSignature();
            if (signature == lastRosterSignature) return;

            lastRosterSignature = signature;
            RosterChanged?.Invoke(members.ToArray());
        }

        private void RefreshSettings()
        {
            string signature = $"{playerCountSetting}|{enterpriseSetting}";
            if (signature == lastSettingsSignature) return;

            lastSettingsSignature = signature;
            SettingsChanged?.Invoke(playerCountSetting, enterpriseSetting);
        }

        private void RefreshCanStart()
        {
            bool next = lobby != null
                        && !startRequested
                        && !lobby.IsLocked
                        && members.Count == playerCountSetting
                        && members.Where(member => !member.IsHost).All(member => member.Ready);

            if (next == canStart) return;

            canStart = next;
            StartEligibilityChanged?.Invoke(next);
        }

        /// <summary>
        /// 按身份复用 Lobby 席位：同一台机器重启游戏后重新进房，UGS 会给它一个新的 PlayerId，
        /// 旧席位仍占着名额。房主按身份令牌把多余的旧席位清掉，一个玩家就不会占两份名额。
        /// </summary>
        private void RemoveDuplicateSeats()
        {
            if (!isHost || duplicateCleanupInFlight || lobby?.Players == null || lobby.Players.Count < 2) return;

            var stale = lobby.Players
                // 房主自己永远不参与：同一台机器上开两个客户端测试时两端令牌相同，
                // 若把房主也并进去，房主会把正在测试的客户端误踢掉。
                .Where(player => player.Id != lobby.HostId)
                .Where(player => !string.IsNullOrEmpty(ReadPlayerValue(player, IdentityKey)))
                .GroupBy(player => ReadPlayerValue(player, IdentityKey))
                .Where(group => group.Count() > 1)
                // 保留最近加入的那条（玩家当前真正在用的连接），清掉更早的重复席位。
                .SelectMany(group => group.OrderByDescending(player => player.Joined).Skip(1))
                .Select(player => player.Id)
                .ToList();

            if (stale.Count == 0) return;

            duplicateCleanupInFlight = true;
            RemoveSeatsAsync(lobby.Id, stale);
        }

        private async void RemoveSeatsAsync(string lobbyId, List<string> playerIds)
        {
            try
            {
                foreach (string playerId in playerIds)
                {
                    try
                    {
                        await LobbyService.Instance.RemovePlayerAsync(lobbyId, playerId);
                        Debug.Log("[Lobby] 已按身份回收重复席位：" + playerId);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[Lobby] 回收重复席位失败：" + ex.Message);
                    }
                }
            }
            finally
            {
                duplicateCleanupInFlight = false;
            }
        }

        private string BuildRosterSignature()
        {
            var builder = new System.Text.StringBuilder();
            foreach (LobbyMemberInfo member in members)
            {
                builder.Append(member.PlayerId).Append(':')
                    .Append(member.Nickname).Append(':')
                    .Append(member.IsHost ? '1' : '0')
                    .Append(member.Ready ? '1' : '0')
                    .Append(':').Append(member.Color)
                    .Append('|');
            }
            return builder.ToString();
        }

        private bool ContainsSelf(Unity.Services.Lobbies.Models.Lobby fresh)
        {
            string self = SelfPlayerId;
            if (string.IsNullOrEmpty(self) || fresh.Players == null) return true;
            return fresh.Players.Any(player => player.Id == self);
        }

        private static bool IsStarted(Unity.Services.Lobbies.Models.Lobby fresh) =>
            ReadLobbyValue(fresh, StartedKey) == "1";

        private static string ReadLobbyValue(Unity.Services.Lobbies.Models.Lobby source, string key)
        {
            if (source?.Data == null) return null;
            return source.Data.TryGetValue(key, out DataObject entry) && entry != null ? entry.Value : null;
        }

        private static string ReadPlayerValue(Player player, string key)
        {
            if (player?.Data == null) return null;
            return player.Data.TryGetValue(key, out PlayerDataObject entry) && entry != null ? entry.Value : null;
        }

        /// <summary>颜色值解析失败（没写过 / 空串）一律当作「未选」，即 -1。</summary>
        private static int ParseColor(string value) =>
            int.TryParse(value, out int color) ? color : -1;

        /// <summary>座位号：没写过 / 写坏了都当「未知」（-1）。</summary>
        private static int ParseSeat(string value) =>
            int.TryParse(value, out int seat) && seat >= 0 && seat <= 3 ? seat : -1;

        /// <summary>随机排出行顺序：0..count-1 的一次洗牌。</summary>
        private static int[] ShuffledSeats(int count)
        {
            var order = new int[Mathf.Max(0, count)];
            for (int i = 0; i < order.Length; i++) order[i] = i;

            var rng = new System.Random(Environment.TickCount);
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int temp = order[i];
                order[i] = order[j];
                order[j] = temp;
            }

            return order;
        }

        private static string EncodeOrder(int[] order) =>
            order == null || order.Length == 0 ? string.Empty : string.Join(",", order);

        /// <summary>解析行动顺序；缺失或不合法（长度不对 / 越界 / 重复）时退回按座位自然顺序。</summary>
        private static int[] DecodeOrder(string text, int count)
        {
            count = Mathf.Max(0, count);
            var natural = new int[count];
            for (int i = 0; i < count; i++) natural[i] = i;

            if (string.IsNullOrWhiteSpace(text)) return natural;

            string[] parts = text.Split(',');
            if (parts.Length != count) return natural;

            var seen = new bool[count];
            var order = new int[count];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out int seat)) return natural;
                if (seat < 0 || seat >= count || seen[seat]) return natural;
                seen[seat] = true;
                order[i] = seat;
            }

            return order;
        }

        private static Dictionary<string, PlayerDataObject> BuildPlayerData(string nickname, bool ready, int color = -1) =>
            new Dictionary<string, PlayerDataObject>
            {
                [NicknameKey] = new PlayerDataObject(
                    PlayerDataObject.VisibilityOptions.Member, nickname),
                [ReadyKey] = new PlayerDataObject(
                    PlayerDataObject.VisibilityOptions.Member, ready ? "1" : "0"),
                [ColorKey] = new PlayerDataObject(
                    PlayerDataObject.VisibilityOptions.Member, color >= 0 ? color.ToString() : string.Empty),
                [IdentityKey] = new PlayerDataObject(
                    PlayerDataObject.VisibilityOptions.Member, RelayTransport.LocalIdentity),
            };

        private static DataObject MakeData(string value) =>
            new DataObject(DataObject.VisibilityOptions.Member, value ?? string.Empty);

        private static string NormalizeNickname(string nickname, string fallback)
        {
            string value = (nickname ?? string.Empty).Trim();
            if (value.Length == 0) value = fallback;
            return value.Length > MaxNicknameLength ? value.Substring(0, MaxNicknameLength) : value;
        }

        private void UpdateHeartbeat(float deltaTime)
        {
            if (!isHost) return; // 心跳只有房主需要发

            heartbeatTimer += deltaTime;
            if (heartbeatTimer < HeartbeatIntervalSeconds) return;

            heartbeatTimer = 0f;
            SendHeartbeatAsync();
        }

        private async void SendHeartbeatAsync()
        {
            if (lobby == null || shutdown) return;

            try
            {
                await LobbyService.Instance.SendHeartbeatPingAsync(lobby.Id);
            }
            catch (Exception ex)
            {
                if (!shutdown) Debug.LogWarning("[Lobby] 心跳失败：" + ex.Message);
            }
        }

        private static async void DeleteRoomAsync(string lobbyId)
        {
            try
            {
                await LobbyService.Instance.DeleteLobbyAsync(lobbyId);
                Debug.Log("[Lobby] 房间已删除（释放额度）。");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Lobby] 删除房间失败：" + ex.Message);
            }
        }

        private static async void RemoveSelfAsync(string lobbyId)
        {
            try
            {
                if (!AuthenticationService.Instance.IsSignedIn) return;
                await LobbyService.Instance.RemovePlayerAsync(lobbyId, AuthenticationService.Instance.PlayerId);
                Debug.Log("[Lobby] 已退出房间。");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Lobby] 退出房间失败：" + ex.Message);
            }
        }

        private static async Task EnsureSignedInAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        private void Fail(string message)
        {
            Debug.LogError("[Lobby] " + message);
            ErrorArrived?.Invoke(message);
        }
    }
}
