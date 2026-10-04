using System.Collections.Generic;

namespace CompoundingPerf;

/// <summary>
/// Names and descriptions (Korean / English) of every live setting, shared by the client's F12
/// page and the server's web page (the launcher's mod page), so the two always say the same.
/// The values themselves live in <see cref="ServerToggles"/> / <see cref="DebugOptions"/>; ranges
/// here must match <c>LiveConfig.Sanitize</c>.
/// </summary>
public sealed class SettingInfo
{
    public string Section = "";
    public string Key = "";
    public string CategoryKo = "";
    public string CategoryEn = "";
    public int Order;
    public string NameKo = "";
    public string NameEn = "";
    public string DescriptionKo = "";
    public string DescriptionEn = "";

    /// <summary>"bool", "int" (with <see cref="Min"/>/<see cref="Max"/>) or "list" (<see cref="Options"/>).</summary>
    public string Kind = "bool";
    public int Min;
    public int Max;
    public string[] Options = [];
    public string[] OptionsKo = [];
    public string[] OptionsEn = [];

    public string Id => Section + "|" + Key;
}

public static class SettingsCatalog
{
    public static readonly SettingInfo[] All =
    [
        new SettingInfo
        {
            Section = "S16 PostRaidCleanup",
            Key = "Enabled",
            CategoryKo = "01. 레이드 후 서버 정리 (S16)",
            CategoryEn = "01. Post-raid server cleanup (S16)",
            Order = 50,
            NameKo = "사용",
            NameEn = "Enabled",
            DescriptionKo = "레이드가 끝나고 서버가 한가해지면(결과 화면을 보는 동안) 서버 메모리를 정리해 윈도우에 돌려줍니다.\n바닐라는 같은 정리를 레이드 '시작' 때 로딩 화면에서 했습니다. 끄면 레이드 사이에 서버 메모리가 그대로 남습니다.",
            DescriptionEn = "After a raid, once the server is idle (while you look at the results screen), cleans up the server's memory and gives it back to Windows.\nVanilla did the same cleanup at raid start, on the loading screen. Off = the server's memory stays as it is between raids.",
            Kind = "bool",
        },
        new SettingInfo
        {
            Section = "S16 PostRaidCleanup",
            Key = "DelaySeconds",
            CategoryKo = "01. 레이드 후 서버 정리 (S16)",
            CategoryEn = "01. Post-raid server cleanup (S16)",
            Order = 40,
            NameKo = "레이드 끝나고 기다릴 시간 (초)",
            NameEn = "Wait after the raid (s)",
            DescriptionKo = "레이드 종료 후 이만큼 기다린 다음 정리를 시도합니다.\nRAM 클리너도 같이 쓰면: RAM 클리너는 레이드 후 20초(기본)에 게임 쪽을 정리합니다. 둘이 겹치지 않게 이 값을 RAM 클리너보다 10초 이상 길게 두세요.",
            DescriptionEn = "Waits this long after the raid ends before trying to clean up.\nWith RAM Cleaner: it cleans up the game 20 s (default) after a raid. Keep this at least 10 s longer so the two don't overlap.",
            Kind = "int",
            Min = 0,
            Max = 600,
        },
        new SettingInfo
        {
            Section = "S16 PostRaidCleanup",
            Key = "QuietSeconds",
            CategoryKo = "01. 레이드 후 서버 정리 (S16)",
            CategoryEn = "01. Post-raid server cleanup (S16)",
            Order = 30,
            NameKo = "조용해야 하는 시간 (초)",
            NameEn = "Quiet time needed (s)",
            DescriptionKo = "이 시간 동안 플레이어 요청(메뉴 이동, 상점 등)이 없을 때만 정리합니다. 정리하는 순간 서버가 잠깐 멈추기 때문입니다.\n핑·알림 확인 같은 자동 요청은 세지 않습니다.",
            DescriptionEn = "Cleans up only when there were no player requests (menus, traders, ...) for this long, because the server pauses briefly while cleaning.\nAutomatic requests such as pings and notification checks don't count.",
            Kind = "int",
            Min = 0,
            Max = 60,
        },
        new SettingInfo
        {
            Section = "S16 PostRaidCleanup",
            Key = "MaxWaitSeconds",
            CategoryKo = "01. 레이드 후 서버 정리 (S16)",
            CategoryEn = "01. Post-raid server cleanup (S16)",
            Order = 20,
            NameKo = "최대 대기 (초)",
            NameEn = "Longest wait (s)",
            DescriptionKo = "계속 바빠서 조용해지지 않으면 이 시간 뒤에는 그냥 정리합니다.",
            DescriptionEn = "If the server never goes quiet, cleans up anyway after this long.",
            Kind = "int",
            Min = 0,
            Max = 1800,
        },
        new SettingInfo
        {
            Section = "S16 PostRaidCleanup",
            Key = "MinCommittedMb",
            CategoryKo = "01. 레이드 후 서버 정리 (S16)",
            CategoryEn = "01. Post-raid server cleanup (S16)",
            Order = 10,
            NameKo = "최소 서버 메모리 (MB)",
            NameEn = "Minimum server memory (MB)",
            DescriptionKo = "서버가 쥔 메모리가 이보다 작으면 정리할 게 없으니 건너뜁니다.",
            DescriptionEn = "Skips the cleanup when the server holds less memory than this (nothing worth cleaning).",
            Kind = "int",
            Min = 0,
            Max = 16384,
        },
        new SettingInfo
        {
            Section = "S15 RaidStartGc",
            Key = "Enabled",
            CategoryKo = "02. 레이드 시작 GC (S15)",
            CategoryEn = "02. Raid start GC (S15)",
            Order = 20,
            NameKo = "사용",
            NameEn = "Enabled",
            DescriptionKo = "레이드 시작 응답 직전에 바닐라가 서버 메모리 전체를 압축하는 무거운 정리를 하는데, 그동안 로딩 화면이 기다립니다.\n켜면 아래 '방식'대로 바꿉니다. 끄면 바닐라와 같습니다.",
            DescriptionEn = "Right before answering 'start raid', vanilla runs a heavy cleanup that compacts all server memory, and the loading screen waits for it.\nOn = do it the 'Mode' way below. Off = vanilla.",
            Kind = "bool",
        },
        new SettingInfo
        {
            Section = "S15 RaidStartGc",
            Key = "Mode",
            CategoryKo = "02. 레이드 시작 GC (S15)",
            CategoryEn = "02. Raid start GC (S15)",
            Order = 10,
            NameKo = "방식",
            NameEn = "Mode",
            DescriptionKo = "백그라운드: 정리는 하되 로딩 화면을 붙잡지 않음 (추천)\n건너뛰기: 아예 안 함\n바닐라: 원래대로 (로딩이 그만큼 길어짐)",
            DescriptionEn = "Background: still cleans up, without holding the loading screen (recommended)\nSkip: don't do it at all\nVanilla: as before (loading takes that much longer)",
            Kind = "list",
            Options = new[] { "Background", "Skip", "Vanilla" },
            OptionsKo = new[] { "백그라운드 (추천)", "건너뛰기", "바닐라" },
            OptionsEn = new[] { "Background (recommended)", "Skip", "Vanilla" },
        },
        new SettingInfo
        {
            Section = "S8 RagfairCalmUpdates",
            Key = "Enabled",
            CategoryKo = "03. 플리마켓 강제 GC 제거 (S8)",
            CategoryEn = "03. No forced GC on flea market updates (S8)",
            Order = 10,
            NameKo = "사용",
            NameEn = "Enabled",
            DescriptionKo = "플리마켓 매물이 만료될 때마다 바닐라가 서버를 잠깐 멈추는 강제 메모리 정리를 합니다. 그 한 번만 뺍니다.",
            DescriptionEn = "Every time flea market offers expire, vanilla forces a memory cleanup that pauses the server for a moment. This removes just that.",
            Kind = "bool",
        },
        new SettingInfo
        {
            Section = "S9 FastCompression",
            Key = "Enabled",
            CategoryKo = "04. 응답 압축 (S9)",
            CategoryEn = "04. Response compression (S9)",
            Order = 20,
            NameKo = "사용",
            NameEn = "Enabled",
            DescriptionKo = "서버 응답을 가장 느린 압축(바닐라)으로 보내는 것을 아래 수준으로 바꿉니다. 같은 PC 안에서만 오가니 크기는 거의 상관없습니다.",
            DescriptionEn = "Vanilla sends server replies with the slowest compression; this switches to the level below. The data stays on your own PC, so size hardly matters.",
            Kind = "bool",
        },
        new SettingInfo
        {
            Section = "S9 FastCompression",
            Key = "Level",
            CategoryKo = "04. 응답 압축 (S9)",
            CategoryEn = "04. Response compression (S9)",
            Order = 10,
            NameKo = "압축 수준",
            NameEn = "Level",
            DescriptionKo = "빠름: CPU를 가장 적게 씀 (추천)\n최소 크기: 바닐라와 같음 (가장 느림)",
            DescriptionEn = "Fastest: least CPU (recommended)\nSmallestSize: same as vanilla (slowest)",
            Kind = "list",
            Options = new[] { "Fastest", "Optimal", "SmallestSize", "NoCompression" },
            OptionsKo = new[] { "빠름 (추천)", "균형", "최소 크기 (바닐라)", "압축 안 함" },
            OptionsEn = new[] { "Fastest (recommended)", "Optimal", "Smallest size (vanilla)", "No compression" },
        },
        new SettingInfo
        {
            Section = "S12 IsolatedBotRandomisation",
            Key = "Enabled",
            CategoryKo = "05. 봇 장비 버그 수정 (S12)",
            CategoryEn = "05. Bot gear bug fix (S12)",
            Order = 10,
            NameKo = "사용",
            NameEn = "Enabled",
            DescriptionKo = "성능이 아니라 바닐라 버그 수정입니다. 야간 레이드 장비 보정이 공용 설정에 계속 누적되는 문제를 막습니다.",
            DescriptionEn = "A vanilla bug fix, not a speed-up: stops the night-raid gear adjustment from piling up in the shared settings.",
            Kind = "bool",
        },
        new SettingInfo
        {
            Section = "S13 CalmNotifier",
            Key = "Enabled",
            CategoryKo = "06. 알림 대기 (S13)",
            CategoryEn = "06. Notification wait (S13)",
            Order = 10,
            NameKo = "사용",
            NameEn = "Enabled",
            DescriptionKo = "알림 확인 요청이 서버 스레드를 붙잡고 자는 대신, 기다리는 동안 스레드를 놓아줍니다. FIKA 호스트에서 효과가 큽니다.",
            DescriptionEn = "Notification checks release the server thread while they wait instead of sleeping on it. Helps most on a FIKA host.",
            Kind = "bool",
        },
        new SettingInfo
        {
            Section = "S11 SaveDirtyTracking",
            Key = "Enabled",
            CategoryKo = "07. 저장 건너뛰기 (S11 · 위험)",
            CategoryEn = "07. Skip saves (S11 · risky)",
            Order = 20,
            NameKo = "사용",
            NameEn = "Enabled",
            DescriptionKo = "메뉴에서 아무것도 안 바뀌었을 때 프로필 저장을 건너뜁니다. 이 모드에서 유일하게 저장을 '막을 수 있는' 기능이라 기본 꺼짐입니다.\n이득은 메뉴에 가만히 있을 때뿐이라 작습니다.",
            DescriptionEn = "Skips the profile save when nothing changed in the menus. The only feature of this mod that can stop a save, so it is off by default.\nThe gain is small (only while idling in the menus).",
            Kind = "bool",
        },
        new SettingInfo
        {
            Section = "S11 SaveDirtyTracking",
            Key = "ForceSaveIntervalSeconds",
            CategoryKo = "07. 저장 건너뛰기 (S11 · 위험)",
            CategoryEn = "07. Skip saves (S11 · risky)",
            Order = 10,
            NameKo = "그래도 저장하는 간격 (초)",
            NameEn = "Save anyway every (s)",
            DescriptionKo = "변화가 없어도 이 간격마다 한 번은 진짜로 저장합니다(은신처 제작 진행 등). 60초보다 커야 효과가 있습니다.",
            DescriptionEn = "Saves for real once per this interval even without changes (hideout crafting progress etc.). Must be above 60 s to have any effect.",
            Kind = "int",
            Min = 90,
            Max = 1800,
        },
        new SettingInfo
        {
            Section = "Debug",
            Key = "Enabled",
            CategoryKo = "08. 디버그 로그",
            CategoryEn = "08. Debug log",
            Order = 40,
            NameKo = "사용",
            NameEn = "Enabled",
            DescriptionKo = "켜면 서버의 SPT_Runtime\\user\\logs\\CompoundingPerf\\ 에 확인용 로그 파일이 새로 생깁니다.\n기능이 제대로 걸렸는지, 레이드마다 서버 메모리·느린 요청·봇 생성 시간이 기록됩니다. 확인이 끝나면 꺼 두세요.",
            DescriptionEn = "On = a new check log file in the server's SPT_Runtime\\user\\logs\\CompoundingPerf\\.\nIt records whether each feature hooked in, and per raid the server memory, slow requests and bot generation time. Turn it off when done.",
            Kind = "bool",
        },
        new SettingInfo
        {
            Section = "Debug",
            Key = "SummaryIntervalMinutes",
            CategoryKo = "08. 디버그 로그",
            CategoryEn = "08. Debug log",
            Order = 30,
            NameKo = "요약 간격 (분)",
            NameEn = "Summary interval (min)",
            DescriptionKo = "이 간격마다 서버 상태 요약 한 줄. 0이면 요약 안 함(레이드 시작/끝 요약은 계속 남음).",
            DescriptionEn = "One server summary line per this interval. 0 = none (raid start/end summaries are still written).",
            Kind = "int",
            Min = 0,
            Max = 60,
        },
        new SettingInfo
        {
            Section = "Debug",
            Key = "SlowRequestMs",
            CategoryKo = "08. 디버그 로그",
            CategoryEn = "08. Debug log",
            Order = 20,
            NameKo = "느린 요청 기준 (ms)",
            NameEn = "Slow request threshold (ms)",
            DescriptionKo = "이보다 오래 걸린 서버 요청은 하나하나 기록합니다.",
            DescriptionEn = "Server requests that take longer than this are logged one by one.",
            Kind = "int",
            Min = 50,
            Max = 5000,
        },
        new SettingInfo
        {
            Section = "Debug",
            Key = "KeepFiles",
            CategoryKo = "08. 디버그 로그",
            CategoryEn = "08. Debug log",
            Order = 10,
            NameKo = "보관할 로그 파일 수",
            NameEn = "Log files to keep",
            DescriptionKo = "로그를 새로 켤 때마다 파일이 하나 생기고, 최근 이만큼만 남깁니다.",
            DescriptionEn = "Each time the log is turned on a new file starts; only this many recent files are kept.",
            Kind = "int",
            Min = 1,
            Max = 50,
        },
    ];

    public static SettingInfo Get(string section, string key)
    {
        foreach (var info in All)
        {
            if (info.Section == section && info.Key == key)
            {
                return info;
            }
        }

        throw new KeyNotFoundException(section + "|" + key);
    }
}
