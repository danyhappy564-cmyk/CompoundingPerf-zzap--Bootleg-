using System.Diagnostics;
using HarmonyLib;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Bot;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace CompoundingPerf.Diagnostics;

/// <summary>
/// Debug log only: how long the server takes to build each batch of bots the client asks
/// for (<c>BotController.Generate</c>, behind <c>/client/game/bot/generate</c>). A slow
/// batch here is a late spawn in raid; heavy bot mods (gear variety, big presets) show up
/// as rising per-bot times. Always installed; does nothing while the debug log is off.
/// </summary>
internal static class BotGenerationTracker
{
    private static long _batches;
    private static long _bots;
    private static long _totalTicks;
    private static long _maxTicks;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, int> Roles = new(StringComparer.OrdinalIgnoreCase);

    public static string Status { get; private set; } = "not installed";

    public static void Apply(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(BotController), nameof(BotController.Generate));
        if (target is null)
        {
            Status = "inactive (BotController.Generate not found)";
            return;
        }

        harmony.Patch(
            target,
            prefix: new HarmonyMethod(AccessTools.Method(typeof(BotGenerationTracker), nameof(Prefix))),
            postfix: new HarmonyMethod(AccessTools.Method(typeof(BotGenerationTracker), nameof(Postfix))));
        Status = "ok";
    }

    private static void Prefix(out long __state) => __state = Stopwatch.GetTimestamp();

    // Observes the returned Task rather than replacing it: nothing downstream changes.
    private static void Postfix(GenerateBotsRequestData request, long __state, Task<IEnumerable<BotBase?>> __result)
    {
        if (!DebugLog.Enabled)
        {
            return;
        }

        string roles;
        try
        {
            roles = request?.Conditions is { } conditions
                ? string.Join(", ", conditions.Select(c => $"{c.Role ?? "?"} x{c.Limit}"))
                : "?";
        }
        catch
        {
            roles = "?";
        }

        __result?.ContinueWith(task => Record(task, roles, __state), TaskContinuationOptions.ExecuteSynchronously);
    }

    private static void Record(Task<IEnumerable<BotBase?>> task, string roles, long start)
    {
        try
        {
            var ticks = Stopwatch.GetTimestamp() - start;
            var ms = RequestTracker.ToMs(ticks);
            if (task.Status != TaskStatus.RanToCompletion)
            {
                DebugLog.Write("bots", $"generation FAILED after {ms:0} ms ({roles}): {task.Exception?.GetBaseException().Message}");
                return;
            }

            // SelectMany over already-built arrays - enumerating it again is cheap and
            // does not regenerate anything.
            var count = task.Result?.Count(b => b is not null) ?? 0;

            lock (Gate)
            {
                _batches++;
                _bots += count;
                _totalTicks += ticks;
                _maxTicks = Math.Max(_maxTicks, ticks);
                foreach (var bot in task.Result ?? [])
                {
                    var role = bot?.Info?.Settings?.Role;
                    if (role is not null)
                    {
                        Roles[role] = Roles.GetValueOrDefault(role) + 1;
                    }
                }
            }

            DebugLog.Write("bots", $"{count} bot(s) in {ms:0} ms" + (count > 0 ? $" ({ms / count:0} ms each)" : string.Empty) + $" — asked for: {roles}");
        }
        catch (Exception ex)
        {
            DebugLog.Write("bots", $"could not record a generation batch: {ex.Message}");
        }
    }

    /// <summary>Writes the totals for the period (normally one raid) and starts a new one.</summary>
    public static void DumpPeriod(string label)
    {
        long batches, bots, total, max;
        string roles;
        lock (Gate)
        {
            batches = _batches;
            bots = _bots;
            total = _totalTicks;
            max = _maxTicks;
            roles = string.Join(", ", Roles.OrderByDescending(kv => kv.Value).Take(10).Select(kv => $"{kv.Key} {kv.Value}"));
            _batches = _bots = _totalTicks = _maxTicks = 0;
            Roles.Clear();
        }

        if (batches == 0)
        {
            DebugLog.Write("bots", $"{label}: no bot generation requests");
            return;
        }

        var totalMs = RequestTracker.ToMs(total);
        DebugLog.Write("bots", $"{label}: {bots} bots in {batches} batches, {totalMs / 1000.0:0.0} s total, " +
                               $"avg {totalMs / Math.Max(1, bots):0} ms per bot, slowest batch {RequestTracker.ToMs(max):0} ms | roles: {roles}");
    }
}
