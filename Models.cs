// Compile-included by both the server project (net10.0) and the client project
// (netstandard2.1). Avoid `required` so both target frameworks accept it — use defaults.

namespace CompoundingPerf;

public record CompoundingPerfConfig
{
    /// <summary>One-switch A/B toggle: when false, EVERY optimization is disabled (server
    /// and client) regardless of individual flags — but the frame-stats recorder keeps
    /// running, so with/without benchmark runs are a single config flip apart.</summary>
    public bool MasterEnabled { get; set; } = true;

    public ServerToggles Server { get; set; } = new();
    public ClientToggles Client { get; set; } = new();
    public DebugOptions Debug { get; set; } = new();
    public TelemetryOptions Telemetry { get; set; } = new();
    public CompatOptions Compat { get; set; } = new();
}

public record ServerToggles
{
    public RagfairCalmUpdatesOptions        RagfairCalmUpdates       { get; set; } = new();
    public FastCompressionOptions           FastCompression          { get; set; } = new();
    public SaveDirtyTrackingOptions         SaveDirtyTracking        { get; set; } = new();
    public IsolatedBotRandomisationOptions  IsolatedBotRandomisation { get; set; } = new();
    public CalmNotifierOptions              CalmNotifier             { get; set; } = new();
    public RaidStartGcOptions               RaidStartGc              { get; set; } = new();
    public PostRaidCleanupOptions           PostRaidCleanup          { get; set; } = new();

    // Retired in 2.0 because SPT 4.1 does the job itself, verified against the 4.1.5
    // server assembly rather than assumed:
    //   ProfileSaveDebouncer (S1) - SaveProfileAsync now takes a per-profile SemaphoreSlim,
    //                               so saves for one profile no longer overlap.
    //   ResponseCache (S2)        - the heavy endpoints (items, globals, handbook,
    //                               customization, hideout areas/recipes) now return
    //                               StreamedJsonBody and serialize straight to the response
    //                               stream, so there is no longer a big string to cache.
    //   ThreadSafeRandom (S6)     - RandomUtil no longer holds a shared System.Random; it
    //                               uses RandomNumberGenerator, which is thread-safe.
    //   ResponseSanitizer (S7)    - ClearString is already a single SearchValues scan over
    //                               a pooled buffer.
    //   ThreadSafeCaches (S10)    - ItemBaseClassService now guards its cache with a Lock,
    //                               HandbookHelper's lazy init is benign, and nothing inside
    //                               SPT calls ItemFilterService's blacklist mutators at all.
    // S14 (FastRouteDispatch) was REMOVED before release: memoizing url→router
    // resolution changed the /launcher/server/connect response under FIKA (raw
    // 0.0.0.0 backendUrl → game cannot connect). Root cause not fully explained,
    // which by itself disqualifies a behavior-neutrality-critical feature.
}

public record CalmNotifierOptions
{
    // S13: the /notify long-poll releases its thread between checks instead of pinning
    // one thread-pool thread per connected client for its full 15s budget. The websocket
    // half of S13 retired in 2.0 - 4.1 serializes once per message and gates each socket
    // individually on its own.
    public bool Enabled { get; set; } = true;
}

public record IsolatedBotRandomisationOptions
{
    // S12: fixes a verified vanilla bug — night-raid equipment modifiers are written
    // into SHARED bot config: they compound per generated bot, persist across raids
    // until restart, and race across vanilla's parallel bot generation. The fix gives
    // every caller a private clone, so the modifier applies exactly once per bot.
    public bool Enabled { get; set; } = true;
}


public record SaveDirtyTrackingOptions
{
    // S11: skips the periodic profile save entirely when the session is provably clean.
    // Vanilla serializes + MD5-hashes the FULL profile every tick just to discover
    // nothing changed; with this on, an idle session costs nothing. Any request that
    // isn't in a small known-pure whitelist marks the session dirty, so player-driven
    // changes can never be skipped.
    //
    // OFF BY DEFAULT since 2.0. It is the only feature in the mod that suppresses a
    // vanilla call rather than changing a value, so it is the only one whose failure mode
    // is "a profile change was not written" instead of "an optimization did nothing".
    // The payoff is also the smallest of the five: it only helps a session sitting idle in
    // the menu, because anything happening in a raid marks the session dirty anyway.
    // Worst risk, least reward - opt in deliberately if you want it.
    public bool Enabled { get; set; } = false;

    /// <summary>A clean session still gets a real save this often, to persist
    /// server-internal changes that bypass HTTP (hideout production progress).
    /// This is the worst-case persistence window for purely passive changes.</summary>
    public int ForceSaveIntervalSeconds { get; set; } = 300;
}


public record RagfairCalmUpdatesOptions
{
    // S8: vanilla's flea-offer expiry pass ends with a forced, blocking, compacting
    // full GC — a recurring multi-hundred-ms stall on large heaps. The forced collect
    // is the only thing removed; the runtime's server GC reclaims the memory on its
    // own schedule.
    public bool Enabled { get; set; } = true;
}

public record RaidStartGcOptions
{
    // S15: StartLocalRaidAsync ends with
    //   GC.Collect(MaxGeneration, Aggressive, blocking: true, compacting: true)
    // - the most expensive collection .NET offers - and it runs inside the request path,
    // so the player waits on the loading screen while the server compacts its whole heap.
    // Present in 4.0 too; the original mod covered the ragfair collect and missed this one.
    public bool Enabled { get; set; } = true;

    /// <summary>One of: Background, Skip, Vanilla. Unrecognized values fall back to
    /// Background — still a gen-2 collection, but non-blocking and non-compacting, so the
    /// raid-start response is not held up by it. Skip drops the collect entirely; Vanilla
    /// forwards it untouched (same as Enabled: false).</summary>
    public string Mode { get; set; } = "Background";
}

public record PostRaidCleanupOptions
{
    // S16 (new in 2.1): vanilla's raid-start collect (see S15) is Aggressive, which also
    // decommits free heap back to the OS. Making it background (S15's default) keeps the
    // loading screen fast but means the server no longer hands memory back between raids.
    // This puts that one aggressive collect back at a moment nobody is waiting on: after a
    // raid ends, once the server has gone quiet (the player is reading the post-raid
    // screens), and only when the heap is big enough to be worth it.
    public bool Enabled { get; set; } = true;

    /// <summary>Seconds after the raid-end request before the cleanup is considered.</summary>
    public int DelaySeconds { get; set; } = 30;

    /// <summary>The server must have had no player requests (pings and notifier polls do not
    /// count) for this long before the collect runs, so it does not stall a menu action.</summary>
    public int QuietSeconds { get; set; } = 3;

    /// <summary>Give up waiting for a quiet moment after this long and run anyway — vanilla
    /// paid the same collect with the player on a loading screen, so this is never worse.</summary>
    public int MaxWaitSeconds { get; set; } = 120;

    /// <summary>Skip the collect when the server has committed less than this; a small
    /// heap has nothing worth returning.</summary>
    public int MinCommittedMb { get; set; } = 512;
}

public record DebugOptions
{
    // New in 2.1: a separate verification log, user/logs/CompoundingPerf/CompoundingPerf-debug-*.log.
    // Startup self-check (did each patch land), every forced collect it touched, raid
    // start/end timing, slow requests, bot generation timing, and a periodic memory/GC/
    // feature-counter summary. Off by default; it adds one timestamp and one task
    // continuation per HTTP request while on.
    public bool Enabled { get; set; } = false;

    /// <summary>Minutes between periodic summary lines. 0 turns the periodic summary off
    /// (raid start/end summaries are still written).</summary>
    public int SummaryIntervalMinutes { get; set; } = 5;

    /// <summary>Requests that take at least this long (receive + route + compress + send)
    /// are logged individually.</summary>
    public int SlowRequestMs { get; set; } = 250;

    /// <summary>How many debug log files to keep; older ones are deleted at startup.</summary>
    public int KeepFiles { get; set; } = 10;
}

public record FastCompressionOptions
{
    // S9: vanilla compresses every JSON response at CompressionLevel.SmallestSize —
    // zlib's slowest setting — over what is almost always a localhost connection.
    // Fastest cuts response-compression CPU several-fold for a few percent more bytes.
    public bool Enabled { get; set; } = true;

    /// <summary>One of: Fastest, Optimal, SmallestSize, NoCompression. Unrecognized
    /// values fall back to Fastest.</summary>
    public string Level { get; set; } = "Fastest";
}

public record ClientToggles
{
    // The in-raid log suppressor (C4) was removed in 1.3: its benefit never survived
    // measurement on real setups, and suppressing other mods' in-raid logs breaks the
    // ecosystem's debugging currency. FrameStats is BENCH-dev-build-only.
    public FrameStatsOptions FrameStats { get; set; } = new();
}

public record FrameStatsOptions
{
    // C6: per-raid frame-time benchmark recorder. One float write per frame while in
    // raid (no allocation, no measurable cost); on raid end, appends a stats line
    // (avg FPS, 1%/0.1% lows, hitch counts, worst spike) to
    // SPT/user/logs/CompoundingPerf-framestats.jsonl. Deliberately NOT gated by
    // MasterEnabled so with/without comparisons measure both sides.
    public bool Enabled { get; set; } = true;

    /// <summary>Leading seconds of each raid discarded from the stats — the spawn-in /
    /// asset-streaming window produces multi-second frames that say nothing about
    /// gameplay performance and would dominate the lows.</summary>
    public double WarmupSkipSeconds { get; set; } = 20;
}




/// <summary>Retired in 2.1. The 2.0 port kept these keys but nothing ever read them except
/// <see cref="TimingEnabled"/>, so turning telemetry on did nothing. <see cref="DebugOptions"/>
/// replaces it. Kept so an old config.json still deserializes.</summary>
public record TelemetryOptions
{
    public bool Enabled { get; set; } = false;
    public int DumpEveryNRaids { get; set; } = 10;
    public bool DumpOnRaidEnd { get; set; } = true;
    public bool DumpOnServerShutdown { get; set; } = true;

    /// <summary>Stopwatch timing on hot paths is opt-in because the measurement itself
    /// has overhead. Counters are always cheap.</summary>
    public bool TimingEnabled { get; set; } = false;
}

public record CompatOptions
{
    public bool AutoDisableOnConflict { get; set; } = true;
    public bool Verbose { get; set; } = true;
}
