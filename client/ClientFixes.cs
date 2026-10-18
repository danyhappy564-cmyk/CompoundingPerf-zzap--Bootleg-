using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT.Airdrop;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Video;

namespace CompoundingPerf.Client;

/// <summary>
/// "09. Game client CPU waste" (C1, C2): two spots where the game keeps the CPU busy for nothing during a raid. Unlike
/// everything else on this F12 page they run in the game, not on the server, and are not sent anywhere. C1 needs no
/// patch (a check every 2 s); C2 is patched once at start and checks its setting. A missing target (another game
/// build) is skipped and logged. Inspired by Smoothie (TominoCZ); written for this mod from the 4.1 client's own code.
/// </summary>
internal static class ClientFixes
{
    internal static ConfigEntry<bool> MenuVideo = null!;
    internal static ConfigEntry<bool> AirdropPhysics = null!;

    private static ManualLogSource? _log;

    internal static void Apply(ManualLogSource log)
    {
        _log = log;
        var harmony = new Harmony(Plugin.ModGuid + ".clientfixes");
        Patch(harmony, "C2 airdrop physics (landed)", AccessTools.Method(typeof(ServerAirDrop), nameof(ServerAirDrop.ManualUpdate)), nameof(AirdropUpdatePostfix));
        Patch(harmony, "C2 airdrop physics (reused)", AccessTools.Method(typeof(ServerAirDrop), nameof(ServerAirDrop.Init)), nameof(AirdropInitPostfix));
    }

    private static void Patch(Harmony harmony, string name, System.Reflection.MethodInfo? target, string postfix)
    {
        try
        {
            if (target is null) throw new MissingMethodException("target not found");
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(ClientFixes), postfix));
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[{name}] skipped, the game code is not as expected: {ex.Message}");
        }
    }

    private static readonly HashSet<string> Failed = new();

    private static void Fail(string what, Exception ex)
    {
        if (Failed.Add(what)) _log?.LogWarning($"[{what}] failed (logged once, the game carries on without the fix): {ex}");
    }

    // ---------------------------------------------------------------- C1 menu background video

    // Checked in Assembly-CSharp 4.1: VideoEnvironmentUIRoot.Init re-parents its public _videoHolderGameObject (the
    // VideoPlayers) next to the environment root, and EnvironmentUI.ShowEnvironment(false) only deactivates the root,
    // so the menu videos keep decoding through the raid. No patch here: every 2 s the plugin's Update looks at the
    // menu's public IsActive and pauses the holder's players while the menu background is hidden, then plays exactly
    // those again once it is shown (or the setting is turned off). A player that starts late is caught on the next look.
    private static readonly AccessTools.FieldRef<EnvironmentUI, EnvironmentUIRoot> CurrentEnvironment =
        AccessTools.FieldRefAccess<EnvironmentUI, EnvironmentUIRoot>("_currentEnvironment");

    private const float VideoCheckSeconds = 2f;
    private static float _nextVideoCheck;
    private static VideoEnvironmentUIRoot? _videoRoot;
    private static VideoPlayer[] _videoPlayers = Array.Empty<VideoPlayer>();
    private static readonly HashSet<VideoPlayer> PausedByUs = new();

    internal static void Tick()
    {
        if (Time.unscaledTime < _nextVideoCheck) return;
        _nextVideoCheck = Time.unscaledTime + VideoCheckSeconds;
        try
        {
            CheckMenuVideos();
        }
        catch (Exception ex)
        {
            Fail("C1 menu video", ex);
        }
    }

    private static void CheckMenuVideos()
    {
        if (!MonoBehaviourSingleton<EnvironmentUI>.Instantiated)
        {
            PlayPausedAgain();
            return;
        }

        var ui = MonoBehaviourSingleton<EnvironmentUI>.Instance;
        var root = CurrentEnvironment(ui) as VideoEnvironmentUIRoot;
        if (!ReferenceEquals(root, _videoRoot))
        {
            // The menu switched backgrounds: the old players are gone with the old root.
            PlayPausedAgain();
            _videoRoot = root;
            var holder = root != null ? root._videoHolderGameObject : null;
            _videoPlayers = holder != null ? holder.GetComponentsInChildren<VideoPlayer>(true) : Array.Empty<VideoPlayer>();
        }

        if (!MenuVideo.Value || root == null || ui.IsActive)
        {
            PlayPausedAgain();
            return;
        }

        foreach (var player in _videoPlayers)
        {
            if (player != null && player.isPlaying && PausedByUs.Add(player)) player.Pause();
        }
    }

    private static void PlayPausedAgain()
    {
        if (PausedByUs.Count == 0) return;
        foreach (var player in PausedByUs)
        {
            if (player != null && player.isPaused) player.Play();
        }

        PausedByUs.Clear();
    }

    // ---------------------------------------------------------------- C2 landed airdrop physics

    // Checked in Assembly-CSharp 4.1: ServerAirDrop.Init calls PhysicsExtensions.UpdateController.SupportRigidbody, and
    // nothing ever calls UnsupportRigidbody for it. UpdateController.CheckRigidbodiesDone keeps Simulation.UpdateEnabled
    // on while any listed body is active in the hierarchy, so after one airdrop Physics.Simulate ran every frame until the
    // raid ended. The crate only reaches EAirdropFallingStage.Landed after RotateAfterDrop has finished, and from then on
    // ManualUpdate moves nothing, so the body is taken off the list at that point. The colliders stay in the scene.
    // ServerAirDrop exists only where the drop is simulated (solo, or the Fika host).
    private static readonly ConditionalWeakTable<ServerAirDrop, object> Released = new();
    private static readonly object Marker = new();

    private static void AirdropUpdatePostfix(ServerAirDrop __instance)
    {
        try
        {
            if (__instance._fallingStage != EAirdropFallingStage.Landed || !AirdropPhysics.Value || Released.TryGetValue(__instance, out _)) return;
            Released.Add(__instance, Marker);
            var body = __instance.SyncObjectRigidbody;
            if (body == null) return;
            PhysicsExtensions.UpdateController.UnsupportRigidbody(body);
            _log?.LogInfo("[C2] airdrop landed: its rigidbody no longer keeps the map's physics running");
        }
        catch (Exception ex)
        {
            Fail("C2 airdrop physics", ex);
        }
    }

    // A crate taken from the pool registers itself again: watch for its landing again.
    private static void AirdropInitPostfix(ServerAirDrop __instance) => Released.Remove(__instance);
}
