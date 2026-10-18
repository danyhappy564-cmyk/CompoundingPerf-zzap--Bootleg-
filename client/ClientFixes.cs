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
/// everything else on this F12 page they run in the game, not on the server, and are not sent anywhere. Patched once at
/// start; each patch checks its setting. A target that is missing (another game build) is skipped and logged.
/// Ideas from Smoothie (TominoCZ), written anew and checked against the 4.1 client.
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
        Patch(harmony, "C1 menu video (shown/hidden)", AccessTools.Method(typeof(EnvironmentUI), nameof(EnvironmentUI.ShowEnvironment)), nameof(ShowEnvironmentPostfix));
        Patch(harmony, "C1 menu video (loaded)", AccessTools.Method(typeof(VideoEnvironmentUIRoot), nameof(VideoEnvironmentUIRoot.Init)), nameof(VideoInitPostfix));
        Patch(harmony, "C2 airdrop physics (landed)", AccessTools.Method(typeof(ServerAirDrop), nameof(ServerAirDrop.ManualUpdate)), nameof(AirdropUpdatePostfix));
        Patch(harmony, "C2 airdrop physics (reused)", AccessTools.Method(typeof(ServerAirDrop), nameof(ServerAirDrop.Init)), nameof(AirdropInitPostfix));
        MenuVideo.SettingChanged += (_, _) => ResyncMenuVideos();
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

    // One main-menu background (VideoEnvironmentUIRoot) is four 2048x1024 60 fps video loops. Its Init moves the video
    // players out of the background object, and for a raid the menu hides only the background object
    // (EnvironmentUI.ShowEnvironment(false)), so the videos kept decoding for the whole raid. Paused while the background
    // is hidden, played again when it is shown. Only players this code paused are resumed.
    private static readonly AccessTools.FieldRef<EnvironmentUI, EnvironmentUIRoot> CurrentEnvironment =
        AccessTools.FieldRefAccess<EnvironmentUI, EnvironmentUIRoot>("_currentEnvironment");

    private static readonly AccessTools.FieldRef<EnvironmentUI, bool> EnvironmentShown =
        AccessTools.FieldRefAccess<EnvironmentUI, bool>("_lastVisibleStateEnvironment");

    private static readonly AccessTools.FieldRef<VideoEnvironmentUIRoot, VideoPlayer[]> VideoPlayers =
        AccessTools.FieldRefAccess<VideoEnvironmentUIRoot, VideoPlayer[]>("_videoPlayers");

    private static readonly List<VideoPlayer> PausedVideos = new();
    private static float _nextResync;

    private static void ShowEnvironmentPostfix(EnvironmentUI __instance, bool value)
    {
        try
        {
            SyncMenuVideos(CurrentEnvironment(__instance), value);
        }
        catch (Exception ex)
        {
            Fail("C1 menu video", ex);
        }
    }

    // A background loads asynchronously and can finish (Init collects its players) while it is already hidden.
    private static void VideoInitPostfix(VideoEnvironmentUIRoot __instance)
    {
        try
        {
            if (!__instance.gameObject.activeSelf) SyncMenuVideos(__instance, false);
        }
        catch (Exception ex)
        {
            Fail("C1 menu video", ex);
        }
    }

    private static void SyncMenuVideos(EnvironmentUIRoot? environment, bool shown)
    {
        if (shown || !MenuVideo.Value)
        {
            foreach (var player in PausedVideos)
            {
                if (player != null && player.isPaused) player.Play();
            }

            PausedVideos.Clear();
            return;
        }

        if (environment is not VideoEnvironmentUIRoot root || VideoPlayers(root) is not { } players) return;
        foreach (var player in players)
        {
            // Playing, or about to start by itself (still preparing with play-on-awake). A stopped one is left alone,
            // so resuming never starts a video that was not playing.
            if (player == null || !(player.isPlaying || player.playOnAwake && player.isActiveAndEnabled && !player.isPaused && !player.isPrepared)) continue;
            player.Pause();
            if (!PausedVideos.Contains(player)) PausedVideos.Add(player);
        }
    }

    /// <summary>After a settings change, and every 5 s from the plugin's Update for a video that started late: the
    /// background is not shown or hidden again until the raid ends, so apply its current state.</summary>
    internal static void ResyncMenuVideos()
    {
        try
        {
            if (!MonoBehaviourSingleton<EnvironmentUI>.Instantiated) return;
            var ui = MonoBehaviourSingleton<EnvironmentUI>.Instance;
            SyncMenuVideos(CurrentEnvironment(ui), EnvironmentShown(ui));
        }
        catch (Exception ex)
        {
            Fail("C1 menu video", ex);
        }
    }

    internal static void Tick()
    {
        if (Time.unscaledTime < _nextResync) return;
        _nextResync = Time.unscaledTime + 5f;
        if (MenuVideo.Value || PausedVideos.Count > 0) ResyncMenuVideos();
    }

    // ---------------------------------------------------------------- C2 landed airdrop physics

    // A falling crate registers its rigidbody with EFT's physics controller (ServerAirDrop.Init) and never unregisters it.
    // While any registered body is active the controller steps the physics of the whole map every frame, so one airdrop
    // kept that running for the rest of the raid. Once the crate has landed (stage Landed comes after it has finished
    // tipping over) nothing moves it again, so its body is unregistered then; its colliders stay, players still stand on
    // it and it is still lootable. Runs only where the airdrop is simulated (solo, or the Fika host).
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
