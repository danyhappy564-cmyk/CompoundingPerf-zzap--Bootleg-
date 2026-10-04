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

        var c16 = new Cat("01. 레이드 후 서버 정리 (S16)", "01. Post-raid server cleanup (S16)");
        _s16Enabled = Bind(config, "S16 PostRaidCleanup", "Enabled", c16, true, 50, "사용", "Enabled",
            "레이드가 끝나고 서버가 한가해지면(결과 화면을 보는 동안) 서버 메모리를 정리해 윈도우에 돌려줍니다.\n" +
            "바닐라는 같은 정리를 레이드 '시작' 때 로딩 화면에서 했습니다. 끄면 레이드 사이에 서버 메모리가 그대로 남습니다.",
            "After a raid, once the server is idle (while you look at the results screen), cleans up the server's memory and gives it back to Windows.\n" +
            "Vanilla did the same cleanup at raid start, on the loading screen. Off = the server's memory stays as it is between raids.");
        _s16Delay = Bind(config, "S16 PostRaidCleanup", "DelaySeconds", c16, 30, 40, "레이드 끝나고 기다릴 시간 (초)", "Wait after the raid (s)",
            "레이드 종료 후 이만큼 기다린 다음 정리를 시도합니다.\n" +
            "RAM 클리너도 같이 쓰면: RAM 클리너는 레이드 후 20초(기본)에 게임 쪽을 정리합니다. 둘이 겹치지 않게 이 값을 RAM 클리너보다 10초 이상 길게 두세요.",
            "Waits this long after the raid ends before trying to clean up.\n" +
            "With RAM Cleaner: it cleans up the game 20 s (default) after a raid. Keep this at least 10 s longer so the two don't overlap.",
            new AcceptableValueRange<int>(0, 600));
        _s16Quiet = Bind(config, "S16 PostRaidCleanup", "QuietSeconds", c16, 3, 30, "조용해야 하는 시간 (초)", "Quiet time needed (s)",
            "이 시간 동안 플레이어 요청(메뉴 이동, 상점 등)이 없을 때만 정리합니다. 정리하는 순간 서버가 잠깐 멈추기 때문입니다.\n" +
            "핑·알림 확인 같은 자동 요청은 세지 않습니다.",
            "Cleans up only when there were no player requests (menus, traders, ...) for this long, because the server pauses briefly while cleaning.\n" +
            "Automatic requests such as pings and notification checks don't count.",
            new AcceptableValueRange<int>(0, 60));
        _s16MaxWait = Bind(config, "S16 PostRaidCleanup", "MaxWaitSeconds", c16, 120, 20, "최대 대기 (초)", "Longest wait (s)",
            "계속 바빠서 조용해지지 않으면 이 시간 뒤에는 그냥 정리합니다.",
            "If the server never goes quiet, cleans up anyway after this long.",
            new AcceptableValueRange<int>(0, 1800));
        _s16MinMb = Bind(config, "S16 PostRaidCleanup", "MinCommittedMb", c16, 512, 10, "최소 서버 메모리 (MB)", "Minimum server memory (MB)",
            "서버가 쥔 메모리가 이보다 작으면 정리할 게 없으니 건너뜁니다.",
            "Skips the cleanup when the server holds less memory than this (nothing worth cleaning).",
            new AcceptableValueRange<int>(0, 16384));

        var c15 = new Cat("02. 레이드 시작 GC (S15)", "02. Raid start GC (S15)");
        _s15Enabled = Bind(config, "S15 RaidStartGc", "Enabled", c15, true, 20, "사용", "Enabled",
            "레이드 시작 응답 직전에 바닐라가 서버 메모리 전체를 압축하는 무거운 정리를 하는데, 그동안 로딩 화면이 기다립니다.\n" +
            "켜면 아래 '방식'대로 바꿉니다. 끄면 바닐라와 같습니다.",
            "Right before answering 'start raid', vanilla runs a heavy cleanup that compacts all server memory, and the loading screen waits for it.\n" +
            "On = do it the 'Mode' way below. Off = vanilla.");
        _s15Mode = Bind(config, "S15 RaidStartGc", "Mode", c15, ModeLabels[0], 10, "방식", "Mode",
            "백그라운드: 정리는 하되 로딩 화면을 붙잡지 않음 (추천)\n건너뛰기: 아예 안 함\n바닐라: 원래대로 (로딩이 그만큼 길어짐)",
            "Background: still cleans up, without holding the loading screen (recommended)\nSkip: don't do it at all\nVanilla: as before (loading takes that much longer)",
            new AcceptableValueList<string>(ModeLabels));

        var c8 = new Cat("03. 플리마켓 강제 GC 제거 (S8)", "03. No forced GC on flea market updates (S8)");
        _s8Enabled = Bind(config, "S8 RagfairCalmUpdates", "Enabled", c8, true, 10, "사용", "Enabled",
            "플리마켓 매물이 만료될 때마다 바닐라가 서버를 잠깐 멈추는 강제 메모리 정리를 합니다. 그 한 번만 뺍니다.",
            "Every time flea market offers expire, vanilla forces a memory cleanup that pauses the server for a moment. This removes just that.");

        var c9 = new Cat("04. 응답 압축 (S9)", "04. Response compression (S9)");
        _s9Enabled = Bind(config, "S9 FastCompression", "Enabled", c9, true, 20, "사용", "Enabled",
            "서버 응답을 가장 느린 압축(바닐라)으로 보내는 것을 아래 수준으로 바꿉니다. 같은 PC 안에서만 오가니 크기는 거의 상관없습니다.",
            "Vanilla sends server replies with the slowest compression; this switches to the level below. The data stays on your own PC, so size hardly matters.");
        _s9Level = Bind(config, "S9 FastCompression", "Level", c9, LevelLabels[0], 10, "압축 수준", "Level",
            "빠름: CPU를 가장 적게 씀 (추천)\n최소 크기: 바닐라와 같음 (가장 느림)",
            "Fastest: least CPU (recommended)\nSmallestSize: same as vanilla (slowest)",
            new AcceptableValueList<string>(LevelLabels));

        var c12 = new Cat("05. 봇 장비 버그 수정 (S12)", "05. Bot gear bug fix (S12)");
        _s12Enabled = Bind(config, "S12 IsolatedBotRandomisation", "Enabled", c12, true, 10, "사용", "Enabled",
            "성능이 아니라 바닐라 버그 수정입니다. 야간 레이드 장비 보정이 공용 설정에 계속 누적되는 문제를 막습니다.",
            "A vanilla bug fix, not a speed-up: stops the night-raid gear adjustment from piling up in the shared settings.");

        var c13 = new Cat("06. 알림 대기 (S13)", "06. Notification wait (S13)");
        _s13Enabled = Bind(config, "S13 CalmNotifier", "Enabled", c13, true, 10, "사용", "Enabled",
            "알림 확인 요청이 서버 스레드를 붙잡고 자는 대신, 기다리는 동안 스레드를 놓아줍니다. FIKA 호스트에서 효과가 큽니다.",
            "Notification checks release the server thread while they wait instead of sleeping on it. Helps most on a FIKA host.");

        var c11 = new Cat("07. 저장 건너뛰기 (S11 · 위험)", "07. Skip saves (S11 · risky)");
        _s11Enabled = Bind(config, "S11 SaveDirtyTracking", "Enabled", c11, false, 20, "사용", "Enabled",
            "메뉴에서 아무것도 안 바뀌었을 때 프로필 저장을 건너뜁니다. 이 모드에서 유일하게 저장을 '막을 수 있는' 기능이라 기본 꺼짐입니다.\n" +
            "이득은 메뉴에 가만히 있을 때뿐이라 작습니다.",
            "Skips the profile save when nothing changed in the menus. The only feature of this mod that can stop a save, so it is off by default.\n" +
            "The gain is small (only while idling in the menus).");
        _s11Interval = Bind(config, "S11 SaveDirtyTracking", "ForceSaveIntervalSeconds", c11, 300, 10, "그래도 저장하는 간격 (초)", "Save anyway every (s)",
            "변화가 없어도 이 간격마다 한 번은 진짜로 저장합니다(은신처 제작 진행 등). 60초보다 커야 효과가 있습니다.",
            "Saves for real once per this interval even without changes (hideout crafting progress etc.). Must be above 60 s to have any effect.",
            new AcceptableValueRange<int>(90, 1800));

        var cd = new Cat("08. 디버그 로그", "08. Debug log");
        _debugEnabled = Bind(config, "Debug", "Enabled", cd, false, 40, "사용", "Enabled",
            "켜면 서버의 SPT_Runtime\\user\\logs\\CompoundingPerf\\ 에 확인용 로그 파일이 새로 생깁니다.\n" +
            "기능이 제대로 걸렸는지, 레이드마다 서버 메모리·느린 요청·봇 생성 시간이 기록됩니다. 확인이 끝나면 꺼 두세요.",
            "On = a new check log file in the server's SPT_Runtime\\user\\logs\\CompoundingPerf\\.\n" +
            "It records whether each feature hooked in, and per raid the server memory, slow requests and bot generation time. Turn it off when done.");
        _debugSummary = Bind(config, "Debug", "SummaryIntervalMinutes", cd, 5, 30, "요약 간격 (분)", "Summary interval (min)",
            "이 간격마다 서버 상태 요약 한 줄. 0이면 요약 안 함(레이드 시작/끝 요약은 계속 남음).",
            "One server summary line per this interval. 0 = none (raid start/end summaries are still written).",
            new AcceptableValueRange<int>(0, 60));
        _debugSlowMs = Bind(config, "Debug", "SlowRequestMs", cd, 250, 20, "느린 요청 기준 (ms)", "Slow request threshold (ms)",
            "이보다 오래 걸린 서버 요청은 하나하나 기록합니다.",
            "Server requests that take longer than this are logged one by one.",
            new AcceptableValueRange<int>(50, 5000));
        _debugKeep = Bind(config, "Debug", "KeepFiles", cd, 10, 10, "보관할 로그 파일 수", "Log files to keep",
            "로그를 새로 켤 때마다 파일이 하나 생기고, 최근 이만큼만 남깁니다.",
            "Each time the log is turned on a new file starts; only this many recent files are kept.",
            new AcceptableValueRange<int>(1, 50));

        ApplyLanguage();
        _language.SettingChanged += (_, _) => ApplyLanguage();
    }

    // ---------------------------------------------------------------- binding helpers

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
