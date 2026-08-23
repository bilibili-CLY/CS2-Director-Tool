using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CS2_Director_Tool.App.Models;
using Newtonsoft.Json.Linq;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 通过 HTTP 游戏状态集成（GSI）监控 CS2 游戏状态。
/// 在 http://localhost:3000/ 上监听来自 CS2 的 POST 请求。
/// </summary>
public class GsiService : IGsiService, IDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    private readonly object _stateLock = new object();

    private string? _previousRoundPhase;
    private string? _previousMapPhase;
    private int? _previousMapRound;
    private bool _isGamePaused;

    private static readonly HashSet<string> PausePhases = new HashSet<string>(StringComparer.Ordinal)
    {
        "paused", "timeout", "timeout_ct", "timeout_t"
    };

    private string? _lastPayloadSummary;
    private string? _lastParseErrorSignature;
    private DateTime _lastParseErrorTime;

    private readonly Dictionary<string, int> _playerHealth = new Dictionary<string, int>();
    private readonly Dictionary<string, int> _playerKills = new Dictionary<string, int>();
    private Dictionary<string, string> _playerNames = new Dictionary<string, string>();

    private static readonly TimeSpan KillcamMatchWindow = TimeSpan.FromSeconds(1.5);
    private readonly List<PendingKill> _pendingKills = new List<PendingKill>();

    private string? _lastSpectatorTarget;
    private string? _lastObserverTargetName;

    public bool IsRunning { get; private set; }

    public event EventHandler<GsiKillEventArgs>? OnKill;
    public event EventHandler? OnRoundStarted;
    public event EventHandler? OnMatchStarted;
    public event EventHandler? OnRoundEnded;
    public event EventHandler? OnGamePaused;
    public event EventHandler? OnGameResumed;
    public event EventHandler<string>? OnLog;

    private void Log(string message) => Dispatcher.UIThread.Post(() => OnLog?.Invoke(this, message));

    /// <summary>在 http://localhost:3000/ 上启动 GSI HTTP 监听器。</summary>
    public void Start()
    {
        if (IsRunning)
            return;

        _listener = new HttpListener();
        _listener.Prefixes.Add("http://localhost:3000/");

        try
        {
            _listener.Start();
            IsRunning = true;

            _cts = new CancellationTokenSource();
            _listenTask = Task.Run(() => ListenLoop(_cts.Token));
        }
        catch
        {
            IsRunning = false;
            _listener = null;
            throw;
        }
    }

    public void Stop()
    {
        if (!IsRunning)
            return;

        try
        {
            _cts?.Cancel();
            _listener?.Stop();
        }
        catch
        {
            // 关闭过程中忽略异常。
        }
        finally
        {
            IsRunning = false;
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        (_listener as IDisposable)?.Dispose();
    }

    private async Task ListenLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var context = await _listener!.GetContextAsync().ConfigureAwait(false);
                ProcessRequest(context);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"监听异常: {ex.Message}");
            }
        }
    }

    private void ProcessRequest(HttpListenerContext context)
    {
        try
        {
            string body;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                body = reader.ReadToEnd();
            }

            // 立即以 200 OK 响应 —— CS2 不要求响应正文。
            var response = context.Response;
            response.StatusCode = 200;
            response.Close();

            if (!string.IsNullOrEmpty(body))
                ProcessPayload(body);
        }
        catch
        {
            try
            {
                context.Response.StatusCode = 200;
                context.Response.Close();
            }
            catch
            {
                // 忽略二次失败。
            }
        }
    }

    private void ProcessPayload(string json)
    {
        var result = GsiPayloadParser.Parse(json);

        if (result.IsFullyParsed)
        {
            LogPayloadSummary(result.Data!);
            ProcessPlayerDeaths(result.Data!);
            ProcessPlayerNames(result.Data!);
            ProcessMapPhase(result.Data!);
            ProcessRoundPhase(result.Data!);
            ProcessPausePhase(result.Data!);
            return;
        }

        if (result.IsRecovered)
        {
            ProcessMapPhase(result.Data!);
            ProcessRoundPhase(result.Data!);
            ProcessPausePhase(result.Data!);
            return;
        }

        if (ShouldLogParseError(result.ErrorMessage))
            Log($"GSI JSON 解析失败: {result.ErrorMessage} | body 片段: {result.ContextSnippet}");
    }

    private bool ShouldLogParseError(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
            return true;

        var now = DateTime.UtcNow;
        lock (_stateLock)
        {
            if (_lastParseErrorSignature == errorMessage &&
                now - _lastParseErrorTime < TimeSpan.FromSeconds(5))
            {
                return false;
            }

            _lastParseErrorSignature = errorMessage;
            _lastParseErrorTime = now;
            return true;
        }
    }

    private void LogPayloadSummary(JObject data)
    {
        var mapToken = data["map"];
        var roundToken = data["round"];
        var playerToken = data["player"];

        var mapPhase = mapToken?["phase"]?.ToString() ?? "-";
        var mapRound = mapToken?["round"]?.ToString() ?? "-";
        var roundPhase = roundToken?["phase"]?.ToString() ?? "(无 round 段)";
        var activity = playerToken?["activity"]?.ToString() ?? "-";

        var summary = $"map.phase={mapPhase} map.round={mapRound} | round.phase={roundPhase} | player.activity={activity}";
        if (summary == _lastPayloadSummary)
            return;

        _lastPayloadSummary = summary;
        Log($"GSI 数据: {summary}");
    }

    private void ProcessMapPhase(JObject data)
    {
        var mapToken = data["map"];
        if (mapToken is null)
            return;

        lock (_stateLock)
        {
            var phase = mapToken["phase"]?.ToString();
            if (!string.IsNullOrEmpty(phase) && _previousMapPhase != phase)
            {
                Log($"map.phase: {_previousMapPhase ?? "(首包)"} -> {phase}");
                _previousMapPhase = phase;
                if (phase == "live")
                {
                    Dispatcher.UIThread.Post(() => OnMatchStarted?.Invoke(this, EventArgs.Empty));
                    _previousRoundPhase = null;
                    _previousMapRound = null;
                }
            }

            var roundNumber = mapToken["round"]?.Value<int>();
            if (roundNumber.HasValue)
            {
                if (_previousMapRound.HasValue && roundNumber.Value > _previousMapRound.Value)
                {
                    Log($"map.round 递增: {_previousMapRound.Value} -> {roundNumber.Value}（触发 OnRoundEnded）");
                    Dispatcher.UIThread.Post(() => OnRoundEnded?.Invoke(this, EventArgs.Empty));
                }

                _previousMapRound = roundNumber.Value;
            }
        }
    }

    private void ProcessRoundPhase(JObject data)
    {
        var roundToken = data["round"];
        if (roundToken is null)
            return;

        var phase = roundToken["phase"]?.ToString();
        if (string.IsNullOrEmpty(phase))
            return;

        lock (_stateLock)
        {
            if (_previousRoundPhase is null)
            {
                _previousRoundPhase = phase;
                if (phase == "freezetime" || phase == "live")
                {
                    Log($"首包 round.phase={phase}（触发 OnRoundStarted）");
                    Dispatcher.UIThread.Post(() => OnRoundStarted?.Invoke(this, EventArgs.Empty));
                }
                else
                {
                    Log($"首包 round.phase={phase}（无事件）");
                }

                return;
            }

            if (_previousRoundPhase == phase)
                return;

            Log($"round.phase: {_previousRoundPhase} -> {phase}");
            if (phase == "freezetime" || phase == "live")
                Dispatcher.UIThread.Post(() => OnRoundStarted?.Invoke(this, EventArgs.Empty));
            else if (phase == "over")
                Dispatcher.UIThread.Post(() => OnRoundEnded?.Invoke(this, EventArgs.Empty));

            _previousRoundPhase = phase;
        }
    }

    private void ProcessPausePhase(JObject data)
    {
        var phase = data["phase_countdowns"]?["phase"]?.ToString();
        if (string.IsNullOrEmpty(phase))
            return;

        lock (_stateLock)
        {
            var isPaused = PausePhases.Contains(phase);
            if (isPaused && !_isGamePaused)
            {
                _isGamePaused = true;
                Log($"游戏暂停（phase_countdowns.phase={phase}），触发 OnGamePaused");
                Dispatcher.UIThread.Post(() => OnGamePaused?.Invoke(this, EventArgs.Empty));
            }
            else if (!isPaused && _isGamePaused)
            {
                _isGamePaused = false;
                Log($"游戏暂停结束（phase_countdowns.phase={phase}），触发 OnGameResumed");
                Dispatcher.UIThread.Post(() => OnGameResumed?.Invoke(this, EventArgs.Empty));
            }
        }
    }

    private void ProcessPlayerNames(JObject data)
    {
        var allPlayers = data["allplayers"] as JObject;
        if (allPlayers is null)
            return;

        var snapshot = new Dictionary<string, string>();
        foreach (var player in allPlayers.Properties())
        {
            snapshot[player.Name] = player.Value["name"]?.ToString() ?? player.Name;
        }

        lock (_stateLock)
        {
            _playerNames = snapshot;
        }
    }

    /// <summary>返回当前已知玩家的快照，仅保留有效的 Steam 64 位 ID。</summary>
    public IReadOnlyList<GsiPlayerInfo> GetCurrentPlayers()
    {
        lock (_stateLock)
        {
            var result = new List<GsiPlayerInfo>();
            foreach (var entry in _playerNames)
            {
                if (IsSteam64Id(entry.Key))
                    result.Add(new GsiPlayerInfo { SteamId = entry.Key, Name = entry.Value });
            }

            return result;
        }
    }

    private static bool IsSteam64Id(string value)
    {
        return !string.IsNullOrEmpty(value)
               && value.Length == 17
               && value.StartsWith("7656119", StringComparison.Ordinal)
               && long.TryParse(value, out _);
    }

    private void ProcessPlayerDeaths(JObject data)
    {
        var allPlayersToken = data["allplayers"] as JObject;
        if (allPlayersToken is null)
            return;

        lock (_stateLock)
        {
            var preDeathSpectatorTarget = _lastSpectatorTarget;
            var preDeathObserverTargetName = _lastObserverTargetName;

            var spectatorTarget = GetSpectatorTarget(data);
            var observerTargetName = GetObserverTargetName(data);

            // 第一遍：检测死亡（生命值从 >0 变为 <=0）。
            foreach (var player in allPlayersToken.Properties())
            {
                var steamId = player.Name;
                var health = player.Value["state"]?["health"]?.Value<int>();
                if (health is null)
                    continue;

                if (_playerHealth.TryGetValue(steamId, out var prevHealth))
                {
                    if (prevHealth > 0 && health <= 0)
                    {
                        var victimName = player.Value["name"]?.ToString() ?? steamId;
                        var killerKey = FindKiller(allPlayersToken, steamId);
                        string? killerName = null;
                        if (killerKey is not null)
                            killerName = allPlayersToken[killerKey]?["name"]?.ToString() ?? killerKey;

                        var matched = IsTargetOnKiller(preDeathSpectatorTarget, killerKey, killerName, allPlayersToken);
                        if (matched)
                        {
                            Log($"检测到死亡: {victimName} 被 {killerName} 击杀（观战视角=True）");
                            FireKill(victimName, killerName ?? "Unknown", true, preDeathObserverTargetName, DateTime.Now);
                        }
                        else
                        {
                            _pendingKills.Add(new PendingKill
                            {
                                VictimKey = steamId,
                                VictimName = victimName,
                                KillerKey = killerKey,
                                KillerName = killerName,
                                DetectedAt = DateTime.Now,
                                PreDeathSpectatorTarget = preDeathSpectatorTarget,
                                PreDeathObserverTargetName = preDeathObserverTargetName,
                            });
                        }
                    }
                }

                _playerHealth[steamId] = health.Value;
            }

            // 第二遍：解析被保留的击杀。
            ResolvePendingKills(allPlayersToken, spectatorTarget, observerTargetName);

            // 第三遍：更新追踪的击杀数。
            foreach (var player in allPlayersToken.Properties())
            {
                var steamId = player.Name;
                var kills = player.Value["match_stats"]?["kills"]?.Value<int>();
                if (kills.HasValue)
                    _playerKills[steamId] = kills.Value;
            }

            _lastSpectatorTarget = spectatorTarget;
            _lastObserverTargetName = observerTargetName;
        }
    }

    private void ResolvePendingKills(JObject allPlayers, string? spectatorTarget, string? observerTargetName)
    {
        var now = DateTime.Now;
        for (var i = _pendingKills.Count - 1; i >= 0; i--)
        {
            var pending = _pendingKills[i];

            if (pending.KillerKey is null)
            {
                pending.KillerKey = FindKiller(allPlayers, pending.VictimKey);
                if (pending.KillerKey is not null)
                    pending.KillerName = allPlayers[pending.KillerKey]?["name"]?.ToString() ?? pending.KillerKey;
            }

            if (pending.KillerKey is not null &&
                IsTargetOnKiller(pending.PreDeathSpectatorTarget, pending.KillerKey, pending.KillerName, allPlayers))
            {
                _pendingKills.RemoveAt(i);
                var delayMs = (now - pending.DetectedAt).TotalMilliseconds;
                Log($"击杀视角已确认: {pending.VictimName} 被 {pending.KillerName} 击杀（延迟 {delayMs:F0}ms）");
                FireKill(pending.VictimName, pending.KillerName ?? "Unknown", true, pending.PreDeathObserverTargetName, pending.DetectedAt);
                continue;
            }

            if (now - pending.DetectedAt > KillcamMatchWindow)
            {
                _pendingKills.RemoveAt(i);
                Log($"死亡诊断(非击杀者视角): spectatorTarget={spectatorTarget ?? "-"}, killerKey={pending.KillerKey ?? "-"}, killerName={pending.KillerName ?? "-"}");
                FireKill(pending.VictimName, pending.KillerName ?? "Unknown", false, observerTargetName, pending.DetectedAt);
            }
        }
    }

    private void FireKill(string victimName, string killerName, bool isObserverOnKiller,
        string? observerTargetName, DateTime detectedAt)
    {
        Dispatcher.UIThread.Post(() => OnKill?.Invoke(this, new GsiKillEventArgs
        {
            KillerName = killerName,
            VictimName = victimName,
            IsObserverOnKiller = isObserverOnKiller,
            ObserverTargetName = observerTargetName,
            KillDetectedAt = detectedAt,
        }));
    }

    private string? FindKiller(JObject allPlayers, string victimSteamId)
    {
        string? candidate = null;
        var candidateCount = 0;
        foreach (var player in allPlayers.Properties())
        {
            var steamId = player.Name;
            if (steamId == victimSteamId)
                continue;

            var currentKills = player.Value["match_stats"]?["kills"]?.Value<int>();
            if (currentKills.HasValue &&
                _playerKills.TryGetValue(steamId, out var prevKills) &&
                currentKills > prevKills)
            {
                candidate = steamId;
                candidateCount++;
            }
        }

        return candidateCount == 1 ? candidate : null;
    }

    private static bool IsTargetOnKiller(string? spectatorTarget, string? killerKey, string? killerName, JObject allPlayers)
    {
        if (string.IsNullOrEmpty(spectatorTarget))
            return false;

        if (!string.IsNullOrEmpty(killerKey) &&
            string.Equals(killerKey, spectatorTarget, StringComparison.Ordinal))
            return true;

        if (string.IsNullOrEmpty(killerName) || allPlayers is null)
            return false;

        foreach (var player in allPlayers.Properties())
        {
            var name = player.Value["name"]?.ToString();
            if (string.Equals(name, killerName, StringComparison.OrdinalIgnoreCase))
            {
                return player.Name == spectatorTarget
                       || string.Equals(name, spectatorTarget, StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    private static string? GetSpectatorTarget(JObject data)
    {
        var player = data["player"];
        var target = player?["spectarget"]?.ToString();
        if (string.IsNullOrEmpty(target))
        {
            var observer = data["observer"];
            target = observer?["steamid"]?.ToString() ?? observer?["target"]?.ToString();
        }

        if (string.IsNullOrEmpty(target) ||
            string.Equals(target, "0", StringComparison.Ordinal) ||
            string.Equals(target, "free", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return target;
    }

    private static string? GetObserverTargetName(JObject data)
    {
        var target = GetSpectatorTarget(data);
        if (string.IsNullOrEmpty(target))
            return null;

        var allPlayers = data["allplayers"] as JObject;
        if (allPlayers is null)
            return null;

        var player = allPlayers[target];
        if (player is not null)
            return player["name"]?.ToString() ?? target;

        foreach (var entry in allPlayers.Properties())
        {
            var name = entry.Value["name"]?.ToString();
            if (string.Equals(name, target, StringComparison.OrdinalIgnoreCase))
                return name;
        }

        return target;
    }

    private class PendingKill
    {
        public string VictimKey { get; set; } = string.Empty;
        public string VictimName { get; set; } = string.Empty;
        public string? KillerKey { get; set; }
        public string? KillerName { get; set; }
        public DateTime DetectedAt { get; set; }
        public string? PreDeathSpectatorTarget { get; set; }
        public string? PreDeathObserverTargetName { get; set; }
    }
}
