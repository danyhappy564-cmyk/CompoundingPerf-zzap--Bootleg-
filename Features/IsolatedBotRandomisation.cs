using CompoundingPerf.Telemetry;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Helpers.Bot;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Utils.Cloners;

namespace CompoundingPerf.Features;

/// <summary>
/// S12 — fixes a vanilla bug rather than an inefficiency. <c>GetBotRandomizationDetails</c>
/// hands back the live <c>RandomisationDetails</c> record out of the shared
/// <c>BotConfig</c>, and <c>BotInventoryGenerator.GenerateAndAddEquipmentToBot</c> applies
/// night-raid equipment modifiers by writing straight back into it. Three consequences:
///
/// <list type="number">
///   <item><b>Compounding</b> — the modifier is added once per generated bot
///     (<c>newWeight = modifier + currentValue</c>), so chances drift toward the 0/100
///     clamp bounds as a raid generates more bots.</item>
///   <item><b>Persistence</b> — the mutation is never reverted, so one night raid leaves
///     the modifiers baked into config for every later raid, day or night, until the
///     server restarts.</item>
///   <item><b>Data race</b> — bots are generated in parallel, so those are concurrent
///     read-modify-writes on a plain <c>Dictionary</c>.</item>
/// </list>
///
/// <para>Verified still present in 4.1.5 — <c>BotInventoryGenerator</c> still does
/// <c>botRandomizationDetails.EquipmentMods[key] = Math.Clamp(...)</c> on the returned
/// object.</para>
///
/// <para>The fix hands every caller its own clone, so the nighttime adjustment applies
/// exactly once per bot to that bot's private copy — the evident intent of the code —
/// nothing persists across raids, and there is no shared object to race on.</para>
///
/// <para><b>Behaviour note</b>: one downstream reader (<c>BotEquipmentModGenerator</c>)
/// used to observe the leaked, progressively compounded values; with isolation it reads
/// pristine config. That is deliberate — what it read before was corrupted by accident,
/// not by design.</para>
///
/// <para><b>2.2.1 — clone only where the write happens.</b> The only writer is vanilla
/// <c>BotInventoryGenerator.GenerateAndAddEquipmentToBot</c> (synchronous, and only on night
/// raids). But <c>GetBotRandomizationDetails</c> is also called by
/// <c>BotEquipmentModGenerator.GenerateModsForWeapon</c>, which recurses once per attachment
/// slot, and by APBS's own generators (<c>CustomBotInventoryGenerator</c>,
/// <c>CustomBotEquipmentModGenerator</c>) — all read-only. Cloning on every call made dozens
/// of throwaway copies per bot. Now a thread-static scope around the vanilla equipment step
/// decides: inside it the caller gets a private clone, everywhere else the shared object,
/// which nothing writes any more. Same safety (no shared write, so nothing compounds,
/// persists or races), one clone per vanilla bot, none for APBS-managed bots. If the
/// equipment step cannot be found the old clone-everything behaviour is kept.</para>
///
/// <para><b>4.0 → 4.1</b>: was a DI <c>TypeOverride</c> subclass of <c>BotHelper</c>.
/// 4.1 left <c>BotHelper</c> unsealed but made the method non-virtual, so an override is
/// no longer dispatched to. Same one-line behaviour, delivered as a postfix.</para>
/// </summary>
internal static class IsolatedBotRandomisation
{
    /// <summary>Kill-switch. While false the shared reference is returned, as in vanilla.</summary>
    public static volatile bool IsEnabled;

    private static ICloner? _cloner;

    /// <summary>Depth of vanilla equipment-generation calls on this thread; &gt; 0 means the
    /// caller is the one that writes.</summary>
    [ThreadStatic]
    private static int _writerDepth;

    private static bool _scoped;

    public static string Status { get; private set; } = "not installed";

    public static void Apply(Harmony harmony, ICloner cloner, ISptLogger<CompoundingPerfMod> logger)
    {
        _cloner = cloner;

        var target = AccessTools.Method(typeof(BotHelper), nameof(BotHelper.GetBotRandomizationDetails));
        if (target is null)
        {
            Status = "inactive (GetBotRandomizationDetails not found)";
            logger.Warning("[CompoundingPerf/S12] BotHelper.GetBotRandomizationDetails not found — SPT internals moved. Feature inactive.");
            return;
        }

        harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(IsolatedBotRandomisation), nameof(ClonePostfix))));

        var writer = AccessTools.Method(typeof(BotInventoryGenerator), nameof(BotInventoryGenerator.GenerateAndAddEquipmentToBot));
        if (writer is null)
        {
            Status = "ok (clone on every call — equipment step not found)";
            logger.Warning("[CompoundingPerf/S12] BotInventoryGenerator.GenerateAndAddEquipmentToBot not found — falling back to cloning on every call.");
            return;
        }

        harmony.Patch(writer,
            prefix: new HarmonyMethod(AccessTools.Method(typeof(IsolatedBotRandomisation), nameof(EnterWriter))),
            finalizer: new HarmonyMethod(AccessTools.Method(typeof(IsolatedBotRandomisation), nameof(LeaveWriter))));
        _scoped = true;
        Status = "ok (clone only in vanilla equipment step)";
    }

    private static void EnterWriter() => _writerDepth++;

    private static Exception? LeaveWriter(Exception? __exception)
    {
        _writerDepth--;
        return __exception;
    }

    private static void ClonePostfix(ref RandomisationDetails? __result)
    {
        if (!IsEnabled || __result is null || _cloner is null)
        {
            return;
        }

        // Read-only callers (weapon mod generation, APBS) share the pristine object.
        if (_scoped && _writerDepth <= 0)
        {
            return;
        }

        TelemetryHub.Increment("s12.randomisation.clones");
        __result = _cloner.Clone(__result);
    }

    public static void Configure(IsolatedBotRandomisationOptions options, ISptLogger<CompoundingPerfMod>? logger)
    {
        IsEnabled = options.Enabled;
        if (options.Enabled)
        {
            logger?.Success("[CompoundingPerf/S12] isolated bot randomisation ACTIVE — nighttime modifiers no longer compound, persist, or race on shared config");
        }
        else
        {
            logger?.Info("[CompoundingPerf/S12] isolated bot randomisation disabled in config");
        }
    }
}
