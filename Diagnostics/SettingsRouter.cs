using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace CompoundingPerf.Diagnostics;

/// <summary>Body of <c>/compoundingperf/config/set</c>: the sections the F12 menu edits.</summary>
public record SetSettingsRequest : IRequestData
{
    public ServerToggles? Server { get; set; }
    public DebugOptions? Debug { get; set; }
}

/// <summary>
/// The client F12 menu's way in. <c>get</c> returns the live settings plus a little server
/// state for the status line; <c>set</c> applies a change immediately and saves it.
/// Both answer plain JSON (the client sends <c>responsecompressed: 0</c>).
/// </summary>
[Injectable]
public class SettingsRouter(JsonUtil jsonUtil) : StaticRouter(jsonUtil,
[
    new RouteAction<EmptyRequestData>(GetUrl, (_, _, _, _, _) => new ValueTask<string>(Respond(null, false))),
    new RouteAction<SetSettingsRequest>(SetUrl, (_, request, _, _, _) =>
    {
        try
        {
            var (changes, saved) = LiveConfig.Update(request?.Server, request?.Debug);
            return new ValueTask<string>(Respond(changes, saved));
        }
        catch (Exception ex)
        {
            return new ValueTask<string>(JsonSerializer.Serialize(new ServerSettingsResponse { Ok = false, Error = ex.Message }));
        }
    }),
])
{
    public const string GetUrl = "/compoundingperf/config/get";
    public const string SetUrl = "/compoundingperf/config/set";

    private static string Respond(string? changes, bool saved)
    {
        var config = LiveConfig.Current;
        var stats = ServerStats.Take();
        return JsonSerializer.Serialize(new ServerSettingsResponse
        {
            ServerVersion = new ModMetadata().Version.ToString(),
            MasterEnabled = config.MasterEnabled,
            Server = config.Server,
            Debug = config.Debug,
            Changes = changes,
            Saved = saved,
            HeapMb = ServerStats.Mb(stats.HeapBytes),
            CommittedMb = ServerStats.Mb(stats.CommittedBytes),
            ProcessMb = ServerStats.Mb(stats.WorkingSetBytes),
            LastCleanup = Features.PostRaidCleanup.LastResult,
            LastCleanupEn = Features.PostRaidCleanup.LastResultEn,
            DebugLogPath = DebugLog.Enabled ? DebugLog.FilePath : null,
        });
    }
}
