using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using BepInEx.Configuration;
using BepInEx.Logging;
using Newtonsoft.Json;
using UnityEngine;

namespace CompoundingPerf.Client;

/// <summary>
/// F12 (ConfigurationManager) page for the server mod's settings, in Korean, applied live.
///
/// <para>The server's config.json is the source of truth: on start the menu reads the live
/// values from the server and shows them; every change is sent 0.8 s after the last edit
/// (so dragging a slider is one request), the server applies it immediately and saves it to
/// config.json, and the menu shows what the server actually kept. Network calls run on the
/// thread pool; their results are applied in <see cref="Update"/> on the main thread.</para>
///
/// <para>Setting keys stay English so the .cfg file is stable; what F12 shows comes from
/// <see cref="ConfigurationManagerAttributes.Category"/> / <see cref="ConfigurationManagerAttributes.DispName"/>.</para>
/// </summary>
internal sealed class ServerSettingsMenu
{
    private const float DebounceSeconds = 0.8f;
    private const float RetrySeconds = 10f;
    private const float RefreshSeconds = 60f;

    /// <summary>Server answered but has no CompoundingPerf route (plugin installed without the
    /// server mod, or an old server mod): nothing will change soon, so ask rarely.</summary>
    private const float MissingModRetrySeconds = 300f;

    private static readonly string[] LevelLabels = ["빠름 (Fastest) · 추천", "균형 (Optimal)", "최소 크기 (SmallestSize) · 바닐라", "압축 안 함 (NoCompression)"];
    private static readonly string[] LevelValues = ["Fastest", "Optimal", "SmallestSize", "NoCompression"];
    private static readonly string[] ModeLabels = ["백그라운드 (Background) · 추천", "건너뛰기 (Skip)", "바닐라 (Vanilla)"];
    private static readonly string[] ModeValues = ["Background", "Skip", "Vanilla"];

    private readonly ManualLogSource _log;
    private readonly ConcurrentQueue<Action> _mainThread = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private readonly ConfigEntry<bool> _s16Enabled;
    private readonly ConfigEntry<int> _s16Delay;
    private readonly ConfigEntry<int> _s16Quiet;
    private readonly ConfigEntry<int> _s16MaxWait;
    private readonly ConfigEntry<int> _s16MinMb;
    private readonly ConfigEntry<bool> _s15Enabled;
    private readonly ConfigEntry<string> _s15Mode;
    private readonly ConfigEntry<bool> _s8Enabled;
    private readonly ConfigEntry<bool> _s9Enabled;
    private readonly ConfigEntry<string> _s9Level;
    private readonly ConfigEntry<bool> _s12Enabled;
    private readonly ConfigEntry<bool> _s13Enabled;
    private readonly ConfigEntry<bool> _s11Enabled;
    private readonly ConfigEntry<int> _s11Interval;
    private readonly ConfigEntry<bool> _debugEnabled;
    private readonly ConfigEntry<int> _debugSummary;
    private readonly ConfigEntry<int> _debugSlowMs;
    private readonly ConfigEntry<int> _debugKeep;

    private bool _suppress;
    private bool _loaded;
    private bool _warned;
    private int _inFlight;
    private double _dirtyAt = -1;
    private double _nextFetchAt;
    private string _status = "서버에 연결하는 중…";
    private string _lastResult = "";

    public ServerSettingsMenu(ConfigFile config, ManualLogSource log)
    {
        _log = log;

        Status(config);

        const string c16 = "01. 레이드 후 서버 정리 (S16)";
        _s16Enabled = Server(config, "S16 PostRaidCleanup", "Enabled", c16, "사용", true, 50,
            "레이드가 끝나고 서버가 한가해지면(결과 화면을 보는 동안) 서버 메모리를 정리해 윈도우에 돌려줍니다.\n" +
            "바닐라는 같은 정리를 레이드 '시작' 때 로딩 화면에서 했습니다. 끄면 레이드 사이에 서버 메모리가 그대로 남습니다.");
        _s16Delay = Server(config, "S16 PostRaidCleanup", "DelaySeconds", c16, "레이드 끝나고 기다릴 시간 (초)", 30, 40,
            "레이드 종료 후 이만큼 기다린 다음 정리를 시도합니다.\n" +
            "RAM 클리너도 같이 쓰면: RAM 클리너는 레이드 후 20초(기본)에 게임 쪽을 정리합니다. 둘이 겹치지 않게 이 값을 RAM 클리너보다 10초 이상 길게 두세요.",
            new AcceptableValueRange<int>(0, 600));
        _s16Quiet = Server(config, "S16 PostRaidCleanup", "QuietSeconds", c16, "조용해야 하는 시간 (초)", 3, 30,
            "이 시간 동안 플레이어 요청(메뉴 이동, 상점 등)이 없을 때만 정리합니다. 정리하는 순간 서버가 잠깐 멈추기 때문입니다.\n" +
            "핑·알림 확인 같은 자동 요청은 세지 않습니다.", new AcceptableValueRange<int>(0, 60));
        _s16MaxWait = Server(config, "S16 PostRaidCleanup", "MaxWaitSeconds", c16, "최대 대기 (초)", 120, 20,
            "계속 바빠서 조용해지지 않으면 이 시간 뒤에는 그냥 정리합니다.", new AcceptableValueRange<int>(0, 1800));
        _s16MinMb = Server(config, "S16 PostRaidCleanup", "MinCommittedMb", c16, "최소 서버 메모리 (MB)", 512, 10,
            "서버가 쥔 메모리가 이보다 작으면 정리할 게 없으니 건너뜁니다.", new AcceptableValueRange<int>(0, 16384));

        const string c15 = "02. 레이드 시작 GC (S15)";
        _s15Enabled = Server(config, "S15 RaidStartGc", "Enabled", c15, "사용", true, 20,
            "레이드 시작 응답 직전에 바닐라가 서버 메모리 전체를 압축하는 무거운 정리를 하는데, 그동안 로딩 화면이 기다립니다.\n" +
            "켜면 아래 '방식'대로 바꿉니다. 끄면 바닐라와 같습니다.");
        _s15Mode = Server(config, "S15 RaidStartGc", "Mode", c15, "방식", ModeLabels[0], 10,
            "백그라운드: 정리는 하되 로딩 화면을 붙잡지 않음 (추천)\n건너뛰기: 아예 안 함\n바닐라: 원래대로 (로딩이 그만큼 길어짐)",
            new AcceptableValueList<string>(ModeLabels));

        const string c8 = "03. 플리마켓 강제 GC 제거 (S8)";
        _s8Enabled = Server(config, "S8 RagfairCalmUpdates", "Enabled", c8, "사용", true, 10,
            "플리마켓 매물이 만료될 때마다 바닐라가 서버를 잠깐 멈추는 강제 메모리 정리를 합니다. 그 한 번만 뺍니다.");

        const string c9 = "04. 응답 압축 (S9)";
        _s9Enabled = Server(config, "S9 FastCompression", "Enabled", c9, "사용", true, 20,
            "서버 응답을 가장 느린 압축(바닐라)으로 보내는 것을 아래 수준으로 바꿉니다. 같은 PC 안에서만 오가니 크기는 거의 상관없습니다.");
        _s9Level = Server(config, "S9 FastCompression", "Level", c9, "압축 수준", LevelLabels[0], 10,
            "빠름: CPU를 가장 적게 씀 (추천)\n최소 크기: 바닐라와 같음 (가장 느림)",
            new AcceptableValueList<string>(LevelLabels));

        const string c12 = "05. 봇 장비 버그 수정 (S12)";
        _s12Enabled = Server(config, "S12 IsolatedBotRandomisation", "Enabled", c12, "사용", true, 10,
            "성능이 아니라 바닐라 버그 수정입니다. 야간 레이드 장비 보정이 공용 설정에 계속 누적되는 문제를 막습니다.");

        const string c13 = "06. 알림 대기 (S13)";
        _s13Enabled = Server(config, "S13 CalmNotifier", "Enabled", c13, "사용", true, 10,
            "알림 확인 요청이 서버 스레드를 붙잡고 자는 대신, 기다리는 동안 스레드를 놓아줍니다. FIKA 호스트에서 효과가 큽니다.");

        const string c11 = "07. 저장 건너뛰기 (S11 · 위험)";
        _s11Enabled = Server(config, "S11 SaveDirtyTracking", "Enabled", c11, "사용", false, 20,
            "메뉴에서 아무것도 안 바뀌었을 때 프로필 저장을 건너뜁니다. 이 모드에서 유일하게 저장을 '막을 수 있는' 기능이라 기본 꺼짐입니다.\n" +
            "이득은 메뉴에 가만히 있을 때뿐이라 작습니다.");
        _s11Interval = Server(config, "S11 SaveDirtyTracking", "ForceSaveIntervalSeconds", c11, "그래도 저장하는 간격 (초)", 300, 10,
            "변화가 없어도 이 간격마다 한 번은 진짜로 저장합니다(은신처 제작 진행 등). 60초보다 커야 효과가 있습니다.",
            new AcceptableValueRange<int>(90, 1800));

        const string cd = "08. 디버그 로그";
        _debugEnabled = Server(config, "Debug", "Enabled", cd, "사용", false, 40,
            "켜면 서버의 SPT_Runtime\\user\\logs\\CompoundingPerf\\ 에 확인용 로그 파일이 새로 생깁니다.\n" +
            "기능이 제대로 걸렸는지, 레이드마다 서버 메모리·느린 요청·봇 생성 시간이 기록됩니다. 확인이 끝나면 꺼 두세요.");
        _debugSummary = Server(config, "Debug", "SummaryIntervalMinutes", cd, "요약 간격 (분)", 5, 30,
            "이 간격마다 서버 상태 요약 한 줄. 0이면 요약 안 함(레이드 시작/끝 요약은 계속 남음).", new AcceptableValueRange<int>(0, 60));
        _debugSlowMs = Server(config, "Debug", "SlowRequestMs", cd, "느린 요청 기준 (ms)", 250, 20,
            "이보다 오래 걸린 서버 요청은 하나하나 기록합니다.", new AcceptableValueRange<int>(50, 5000));
        _debugKeep = Server(config, "Debug", "KeepFiles", cd, "보관할 로그 파일 수", 10, 10,
            "로그를 새로 켤 때마다 파일이 하나 생기고, 최근 이만큼만 남깁니다.", new AcceptableValueRange<int>(1, 50));
    }

    // ---------------------------------------------------------------- binding helpers

    private ConfigEntry<T> Server<T>(ConfigFile config, string section, string key, string category, string name, T defaultValue,
        int order, string description, AcceptableValueBase? range = null)
    {
        var entry = config.Bind(section, key, defaultValue, new ConfigDescription(description, range, new ConfigurationManagerAttributes
        {
            Category = category,
            DispName = name,
            Order = order,
        }));
        entry.SettingChanged += (_, _) =>
        {
            if (!_suppress)
            {
                _dirtyAt = Now;
            }
        };
        return entry;
    }

    private void Status(ConfigFile config)
    {
        config.Bind("Status", "Server", "", new ConfigDescription(
            "서버와 연결됐는지, 서버 메모리, 마지막 레이드 후 정리 결과입니다. 이 페이지의 설정은 바꾸는 즉시(0.8초 뒤) 서버에 적용되고 서버의 config.json에도 저장됩니다.",
            null, new ConfigurationManagerAttributes
            {
                Category = "00. 상태",
                DispName = "서버 상태",
                Order = 100,
                HideDefaultButton = true,
                CustomDrawer = DrawStatus,
            }));
    }

    private void DrawStatus(ConfigEntryBase _)
    {
        GUILayout.BeginVertical();
        GUILayout.Label(_status);
        if (_lastResult.Length > 0)
        {
            GUILayout.Label(_lastResult);
        }

        if (GUILayout.Button("서버에서 다시 읽기", GUILayout.ExpandWidth(false)))
        {
            _nextFetchAt = 0;
        }

        GUILayout.EndVertical();
    }

    // ---------------------------------------------------------------- sync

    private double Now => _clock.Elapsed.TotalSeconds;

    /// <summary>Call every frame from the plugin's Update.</summary>
    public void Update()
    {
        while (_mainThread.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _log.LogError($"[F12] applying the server reply failed: {ex}");
            }
        }

        if (Volatile.Read(ref _inFlight) != 0)
        {
            return;
        }

        var now = Now;
        if (_loaded && _dirtyAt >= 0 && now - _dirtyAt >= DebounceSeconds)
        {
            _dirtyAt = -1;
            var json = JsonConvert.SerializeObject(new { Server = ToServer(), Debug = ToDebug() });
            Run("POST", "/compoundingperf/config/set", json, isSet: true);
        }
        else if (now >= _nextFetchAt)
        {
            _nextFetchAt = now + RefreshSeconds;
            Run("GET", "/compoundingperf/config/get", null, isSet: false);
        }
    }

    private void Run(string method, string path, string? json, bool isSet)
    {
        Interlocked.Exchange(ref _inFlight, 1);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            ServerSettingsResponse? reply = null;
            string? error = null;
            var missing = false;
            try
            {
                reply = JsonConvert.DeserializeObject<ServerSettingsResponse>(ServerLink.Send(method, path, json));
                if (reply is null || !reply.Ok)
                {
                    error = reply?.Error ?? "빈 응답";
                }
            }
            catch (Exception ex)
            {
                missing = ex is System.Net.WebException { Response: System.Net.HttpWebResponse { StatusCode: System.Net.HttpStatusCode.NotFound } };
                error = missing
                    ? "서버에 CompoundingPerf 서버 모드(2.2.0 이상)가 없습니다 — 이 F12 화면은 서버 모드와 같이 써야 합니다"
                    : ex.Message;
            }

            _mainThread.Enqueue(() => OnReply(reply, error, isSet, missing));
            Interlocked.Exchange(ref _inFlight, 0);
        });
    }

    private void OnReply(ServerSettingsResponse? reply, string? error, bool isSet, bool missing)
    {
        if (error is not null || reply is null)
        {
            var retry = missing ? MissingModRetrySeconds : RetrySeconds;
            _status = missing
                ? $"{error} (5분마다 다시 확인, `서버에서 다시 읽기` 로 바로 확인)"
                : $"서버 연결 실패: {error} — {RetrySeconds:0}초 뒤 다시 시도";
            _nextFetchAt = Now + retry;
            if (isSet)
            {
                // Keep the edit; it goes out again with the next attempt.
                _dirtyAt = Now + retry - DebounceSeconds;
            }

            // Once per outage, not every retry.
            if (!_warned || isSet)
            {
                _warned = true;
                _log.LogWarning($"[F12] {(isSet ? "sending settings" : "reading settings")} failed: {error} (retrying every {retry:0}s)");
            }

            return;
        }

        // A change made while this request was in flight is newer than the reply: keep it.
        if (_dirtyAt < 0)
        {
            FromServer(reply);
        }

        if (!_loaded || _warned)
        {
            _log.LogInfo($"[F12] connected to CompoundingPerf {reply.ServerVersion} on {ServerLink.BackendUrl} — settings loaded from the server");
        }

        _loaded = true;
        _warned = false;
        _status = BuildStatus(reply);
        if (isSet)
        {
            _lastResult = string.IsNullOrEmpty(reply.Changes)
                ? $"{DateTime.Now:HH:mm:ss} 바뀐 것 없음"
                : $"{DateTime.Now:HH:mm:ss} 적용됨{(reply.Saved ? " · config.json 저장" : " · ⚠ config.json 저장 실패(재시작하면 원래대로)")}: {reply.Changes}";
            _log.LogInfo($"[F12] {_lastResult}");
        }
    }

    private static string BuildStatus(ServerSettingsResponse reply)
    {
        var sb = new StringBuilder();
        sb.Append($"연결됨 · 서버 모드 {reply.ServerVersion} · 서버 메모리 {reply.ProcessMb:N0} MB (GC 사용 {reply.CommittedMb:N0} MB)");
        if (!reply.MasterEnabled)
        {
            sb.Append("\n⚠ config.json의 MasterEnabled가 false — 패치가 설치되지 않아 아래 설정은 효과가 없습니다 (true로 바꾸고 서버 재시작)");
        }

        sb.Append($"\n마지막 레이드 후 정리: {reply.LastCleanup ?? "아직 없음"}");
        if (reply.DebugLogPath is not null)
        {
            sb.Append($"\n디버그 로그: {reply.DebugLogPath}");
        }

        return sb.ToString();
    }

    // ---------------------------------------------------------------- mapping

    private ServerToggles ToServer() => new()
    {
        PostRaidCleanup = new PostRaidCleanupOptions
        {
            Enabled = _s16Enabled.Value,
            DelaySeconds = _s16Delay.Value,
            QuietSeconds = _s16Quiet.Value,
            MaxWaitSeconds = _s16MaxWait.Value,
            MinCommittedMb = _s16MinMb.Value,
        },
        RaidStartGc = new RaidStartGcOptions { Enabled = _s15Enabled.Value, Mode = ToValue(_s15Mode.Value, ModeLabels, ModeValues) },
        RagfairCalmUpdates = new RagfairCalmUpdatesOptions { Enabled = _s8Enabled.Value },
        FastCompression = new FastCompressionOptions { Enabled = _s9Enabled.Value, Level = ToValue(_s9Level.Value, LevelLabels, LevelValues) },
        IsolatedBotRandomisation = new IsolatedBotRandomisationOptions { Enabled = _s12Enabled.Value },
        CalmNotifier = new CalmNotifierOptions { Enabled = _s13Enabled.Value },
        SaveDirtyTracking = new SaveDirtyTrackingOptions { Enabled = _s11Enabled.Value, ForceSaveIntervalSeconds = _s11Interval.Value },
    };

    private DebugOptions ToDebug() => new()
    {
        Enabled = _debugEnabled.Value,
        SummaryIntervalMinutes = _debugSummary.Value,
        SlowRequestMs = _debugSlowMs.Value,
        KeepFiles = _debugKeep.Value,
    };

    private void FromServer(ServerSettingsResponse reply)
    {
        _suppress = true;
        try
        {
            if (reply.Server is { } s)
            {
                Set(_s16Enabled, s.PostRaidCleanup.Enabled);
                Set(_s16Delay, s.PostRaidCleanup.DelaySeconds);
                Set(_s16Quiet, s.PostRaidCleanup.QuietSeconds);
                Set(_s16MaxWait, s.PostRaidCleanup.MaxWaitSeconds);
                Set(_s16MinMb, s.PostRaidCleanup.MinCommittedMb);
                Set(_s15Enabled, s.RaidStartGc.Enabled);
                Set(_s15Mode, ToLabel(s.RaidStartGc.Mode, ModeLabels, ModeValues));
                Set(_s8Enabled, s.RagfairCalmUpdates.Enabled);
                Set(_s9Enabled, s.FastCompression.Enabled);
                Set(_s9Level, ToLabel(s.FastCompression.Level, LevelLabels, LevelValues));
                Set(_s12Enabled, s.IsolatedBotRandomisation.Enabled);
                Set(_s13Enabled, s.CalmNotifier.Enabled);
                Set(_s11Enabled, s.SaveDirtyTracking.Enabled);
                Set(_s11Interval, s.SaveDirtyTracking.ForceSaveIntervalSeconds);
            }

            if (reply.Debug is { } d)
            {
                Set(_debugEnabled, d.Enabled);
                Set(_debugSummary, d.SummaryIntervalMinutes);
                Set(_debugSlowMs, d.SlowRequestMs);
                Set(_debugKeep, d.KeepFiles);
            }
        }
        finally
        {
            _suppress = false;
        }
    }

    /// <summary>Server values may lie outside the slider range (hand-edited config.json);
    /// BepInEx clamps them on assignment, which would then read back as a change. Only
    /// assign when different, and never send these back (suppressed).</summary>
    private static void Set<T>(ConfigEntry<T> entry, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(entry.Value, value))
        {
            entry.Value = value;
        }
    }

    private static string ToValue(string label, string[] labels, string[] values)
    {
        var i = Array.IndexOf(labels, label);
        return i >= 0 ? values[i] : values[0];
    }

    private static string ToLabel(string? value, string[] labels, string[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
            {
                return labels[i];
            }
        }

        return labels[0];
    }
}
