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
/// F12 (ConfigurationManager) page for the server mod's settings, in Korean or English, applied live.
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

    // Stored in the .cfg, so one value carries both languages.
    private static readonly string[] LevelLabels = ["빠름 · Fastest (추천)", "균형 · Optimal", "최소 크기 · SmallestSize (바닐라)", "압축 안 함 · NoCompression"];
    private static readonly string[] LevelValues = ["Fastest", "Optimal", "SmallestSize", "NoCompression"];
    private static readonly string[] ModeLabels = ["백그라운드 · Background (추천)", "건너뛰기 · Skip", "바닐라 · Vanilla"];
    private static readonly string[] ModeValues = ["Background", "Skip", "Vanilla"];

    internal const string LanguageKorean = "한국어";
    internal const string LanguageEnglish = "English";

    /// <summary>The single instance, for <see cref="StatusBridge"/>.</summary>
    internal static ServerSettingsMenu? Instance { get; private set; }

    /// <summary>English UI (F12 texts and the status lines).</summary>
    internal static bool En;

    internal static string L(string korean, string english) => En ? english : korean;

    private readonly ManualLogSource _log;
    private readonly ConcurrentQueue<Action> _mainThread = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<Texts> _texts = new();

    /// <summary>What F12 shows for one entry, in both languages.</summary>
    private sealed class Texts
    {
        public ConfigurationManagerAttributes Attributes = null!;
        public Cat Category;
        public string NameKo = "", NameEn = "", DescKo = "", DescEn = "";
    }

    private readonly struct Cat(string ko, string en)
    {
        public readonly string Ko = ko;
        public readonly string En = en;
    }

    private readonly ConfigEntry<string> _language;
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
    private ServerSettingsResponse? _lastReply;
    private string? _error;
    private bool _missing;
    private string _lastResult = "";

    public ServerSettingsMenu(ConfigFile config, ManualLogSource log)
    {
        _log = log;
        Instance = this;

        var c0 = new Cat("00. 상태 · 언어", "00. Status · language");
        Status(config, c0);
        _language = Bind(config, "Language", "Language", c0, LanguageKorean, 90,
            "언어 (Language)", "Language",
            "F12 화면과 상태 줄의 언어입니다. F12는 창을 닫았다 다시 열면 바뀐 언어로 보입니다.\n" +
            "RAM 클리너(zzap)를 같이 쓰면 RAM 클리너의 언어를 따라갑니다.",
            "Language of this F12 page and its status lines. F12 shows the new language after you close and reopen it.\n" +
            "With RAM Cleaner (zzap) installed, it follows RAM Cleaner's language.",
            new AcceptableValueList<string>(LanguageKorean, LanguageEnglish), sendsToServer: false);

        _s16Enabled = Bind(config, "S16 PostRaidCleanup", "Enabled", true);
        _s16Delay = Bind(config, "S16 PostRaidCleanup", "DelaySeconds", 30, new AcceptableValueRange<int>(0, 600));
        _s16Quiet = Bind(config, "S16 PostRaidCleanup", "QuietSeconds", 3, new AcceptableValueRange<int>(0, 60));
        _s16MaxWait = Bind(config, "S16 PostRaidCleanup", "MaxWaitSeconds", 120, new AcceptableValueRange<int>(0, 1800));
        _s16MinMb = Bind(config, "S16 PostRaidCleanup", "MinCommittedMb", 512, new AcceptableValueRange<int>(0, 16384));
        _s15Enabled = Bind(config, "S15 RaidStartGc", "Enabled", true);
        _s15Mode = Bind(config, "S15 RaidStartGc", "Mode", ModeLabels[0], new AcceptableValueList<string>(ModeLabels));
        _s8Enabled = Bind(config, "S8 RagfairCalmUpdates", "Enabled", true);
        _s9Enabled = Bind(config, "S9 FastCompression", "Enabled", true);
        _s9Level = Bind(config, "S9 FastCompression", "Level", LevelLabels[0], new AcceptableValueList<string>(LevelLabels));
        _s12Enabled = Bind(config, "S12 IsolatedBotRandomisation", "Enabled", true);
        _s13Enabled = Bind(config, "S13 CalmNotifier", "Enabled", true);
        _s11Enabled = Bind(config, "S11 SaveDirtyTracking", "Enabled", false);
        _s11Interval = Bind(config, "S11 SaveDirtyTracking", "ForceSaveIntervalSeconds", 300, new AcceptableValueRange<int>(90, 1800));
        _debugEnabled = Bind(config, "Debug", "Enabled", false);
        _debugSummary = Bind(config, "Debug", "SummaryIntervalMinutes", 5, new AcceptableValueRange<int>(0, 60));
        _debugSlowMs = Bind(config, "Debug", "SlowRequestMs", 250, new AcceptableValueRange<int>(50, 5000));
        _debugKeep = Bind(config, "Debug", "KeepFiles", 10, new AcceptableValueRange<int>(1, 50));

        ApplyLanguage();
        _language.SettingChanged += (_, _) => ApplyLanguage();
    }

    // ---------------------------------------------------------------- binding helpers

    /// <summary>A server setting: names, descriptions and category come from the shared <see cref="SettingsCatalog"/>.</summary>
    private ConfigEntry<T> Bind<T>(ConfigFile config, string section, string key, T defaultValue, AcceptableValueBase? range = null)
    {
        var info = SettingsCatalog.Get(section, key);
        return Bind(config, section, key, new Cat(info.CategoryKo, info.CategoryEn), defaultValue, info.Order,
            info.NameKo, info.NameEn, info.DescriptionKo, info.DescriptionEn, range);
    }

    private ConfigEntry<T> Bind<T>(ConfigFile config, string section, string key, Cat category, T defaultValue, int order,
        string nameKo, string nameEn, string descKo, string descEn, AcceptableValueBase? range = null, bool sendsToServer = true)
    {
        var attributes = new ConfigurationManagerAttributes { Category = category.Ko, DispName = nameKo, Description = descKo, Order = order };
        _texts.Add(new Texts { Attributes = attributes, Category = category, NameKo = nameKo, NameEn = nameEn, DescKo = descKo, DescEn = descEn });
        var entry = config.Bind(section, key, defaultValue, new ConfigDescription(descKo, range, attributes));
        if (sendsToServer)
        {
            entry.SettingChanged += (_, _) =>
            {
                if (!_suppress)
                {
                    _dirtyAt = Now;
                }
            };
        }

        return entry;
    }

    private void Status(ConfigFile config, Cat category)
    {
        const string ko = "서버와 연결됐는지, 서버 메모리, 마지막 레이드 후 정리 결과입니다. 이 페이지의 설정은 바꾸는 즉시(0.8초 뒤) 서버에 적용되고 서버의 config.json에도 저장됩니다.";
        const string en = "Whether the server is connected, its memory and the last post-raid cleanup. Settings on this page apply on the server right away (0.8 s later) and are saved to its config.json.";
        var attributes = new ConfigurationManagerAttributes
        {
            Category = category.Ko,
            DispName = "서버 상태",
            Description = ko,
            Order = 100,
            HideDefaultButton = true,
            CustomDrawer = DrawStatus,
        };
        _texts.Add(new Texts { Attributes = attributes, Category = category, NameKo = "서버 상태", NameEn = "Server status", DescKo = ko, DescEn = en });
        config.Bind("Status", "Server", "", new ConfigDescription(ko, null, attributes));
    }

    /// <summary>Rewrites what F12 shows for the chosen language (F12 picks it up when reopened).</summary>
    private void ApplyLanguage()
    {
        En = _language.Value == LanguageEnglish;
        foreach (var t in _texts)
        {
            t.Attributes.Category = En ? t.Category.En : t.Category.Ko;
            t.Attributes.DispName = En ? t.NameEn : t.NameKo;
            t.Attributes.Description = En ? t.DescEn : t.DescKo;
        }
    }

    private void DrawStatus(ConfigEntryBase _)
    {
        GUILayout.BeginVertical();
        GUILayout.Label(StatusText);
        if (LastResult.Length > 0)
        {
            GUILayout.Label(LastResult);
        }

        if (GUILayout.Button(L("서버에서 다시 읽기", "Read again from the server"), GUILayout.ExpandWidth(false)))
        {
            RequestRefresh();
        }

        GUILayout.EndVertical();
    }

    internal void RequestRefresh() => _nextFetchAt = 0;

    internal bool Connected => _loaded && _error is null;

    /// <summary>Status lines in the current language (built when read, so a language switch shows at once).</summary>
    internal string StatusText =>
        _error is not null
            ? _missing
                ? L("서버에 CompoundingPerf 서버 모드(2.2.0 이상)가 없습니다 — 이 F12 화면은 서버 모드와 같이 써야 합니다 (5분마다 다시 확인, '서버에서 다시 읽기'로 바로 확인)",
                    "The server has no CompoundingPerf server mod (2.2.0 or newer) — this F12 page needs it (checking again every 5 min; 'Read again from the server' checks now)")
                : L($"서버 연결 실패: {_error} — {RetrySeconds:0}초 뒤 다시 시도", $"could not reach the server: {_error} — retrying in {RetrySeconds:0} s")
            : _lastReply is null
                ? L("서버에 연결하는 중…", "connecting to the server…")
                : BuildStatus(_lastReply);

    internal string LastResult => _lastResult;

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
                    error = reply?.Error ?? L("빈 응답", "empty reply");
                }
            }
            catch (Exception ex)
            {
                missing = ex is System.Net.WebException { Response: System.Net.HttpWebResponse { StatusCode: System.Net.HttpStatusCode.NotFound } };
                error = missing ? "no CompoundingPerf route on the server" : ex.Message;
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
            _error = error ?? "?";
            _missing = missing;
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
        _error = null;
        _missing = false;
        _lastReply = reply;
        if (isSet)
        {
            _lastResult = string.IsNullOrEmpty(reply.Changes)
                ? L($"{DateTime.Now:HH:mm:ss} 바뀐 것 없음", $"{DateTime.Now:HH:mm:ss} nothing changed")
                : L($"{DateTime.Now:HH:mm:ss} 적용됨{(reply.Saved ? " · config.json 저장" : " · ⚠ config.json 저장 실패(재시작하면 원래대로)")}: {reply.Changes}",
                    $"{DateTime.Now:HH:mm:ss} applied{(reply.Saved ? " · saved to config.json" : " · ⚠ config.json NOT saved (reverts on restart)")}: {reply.Changes}");
            _log.LogInfo($"[F12] applied{(reply.Saved ? ", saved" : ", NOT saved")}: {reply.Changes}");
        }
    }

    private static string BuildStatus(ServerSettingsResponse reply)
    {
        var sb = new StringBuilder();
        sb.Append(L($"연결됨 · 서버 모드 {reply.ServerVersion} · 서버 메모리 {reply.ProcessMb:N0} MB (GC 사용 {reply.CommittedMb:N0} MB)",
                    $"connected · server mod {reply.ServerVersion} · server memory {reply.ProcessMb:N0} MB (GC {reply.CommittedMb:N0} MB)"));
        if (!reply.MasterEnabled)
        {
            sb.Append(L("\n⚠ config.json의 MasterEnabled가 false — 패치가 설치되지 않아 아래 설정은 효과가 없습니다 (true로 바꾸고 서버 재시작)",
                        "\n⚠ MasterEnabled is false in config.json — no patches are installed, so these settings do nothing (set it to true and restart the server)"));
        }

        // Older server mods only send the Korean line.
        var cleanup = En ? reply.LastCleanupEn ?? reply.LastCleanup : reply.LastCleanup;
        sb.Append(L("\n마지막 레이드 후 정리: ", "\nlast post-raid cleanup: ")).Append(cleanup ?? L("아직 없음", "none yet"));
        if (reply.DebugLogPath is not null)
        {
            sb.Append(L("\n디버그 로그: ", "\ndebug log: ")).Append(reply.DebugLogPath);
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
