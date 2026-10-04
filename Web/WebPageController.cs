using System.Reflection;
using System.Text.Json;
using CompoundingPerf.Diagnostics;
using CompoundingPerf.Features;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompoundingPerf.Web;

/// <summary>
/// The launcher's / SPT web panel's "mod page" for this mod (<see cref="ModMetadata.HomePage"/>): one HTML page
/// plus two JSON endpoints, served by the SPT server itself, so it works without the game running.
/// SPT maps controllers of mods that implement <c>IModBlazorMetadata</c>. Viewing needs the web panel's login
/// rules (local requests pass by default); changing settings needs an administrator, like SPT's own config editor.
/// Changes go through <see cref="LiveConfig.Update"/> — the same path as the client's F12 page.
/// </summary>
[Route("compoundingperf")]
[Authorize]
[IgnoreAntiforgeryToken]
public sealed class WebPageController : ControllerBase
{
    /// <summary>Sent by the page's own script; a form or fetch from another site cannot add it without CORS.</summary>
    private const string ChangeHeader = "X-CompoundingPerf";

    private static readonly Lazy<string> PageHtml = new(() =>
    {
        using var stream = typeof(WebPageController).Assembly.GetManifestResourceStream("CompoundingPerf.Web.page.html")
                           ?? throw new InvalidOperationException("page.html is not embedded");
        return new StreamReader(stream).ReadToEnd();
    });

    [HttpGet("")]
    public IActionResult Page()
    {
        Response.Headers.CacheControl = "no-store";
        return Content(PageHtml.Value, "text/html; charset=utf-8");
    }

    [HttpGet("api/state")]
    public IActionResult State()
    {
        Response.Headers.CacheControl = "no-store";
        return Content(JsonSerializer.Serialize(BuildState()), "application/json; charset=utf-8");
    }

    [HttpPost("api/set")]
    [Authorize(Policy = "Administrator")]
    public async Task<IActionResult> Set()
    {
        if (Request.Headers[ChangeHeader] != "1")
        {
            return StatusCode(403, "Changes are only accepted from the CompoundingPerf page.");
        }

        var form = await Request.ReadFormAsync();
        string id = form["id"].ToString(), value = form["value"].ToString();
        if (!SettingsCatalog.All.Any(s => s.Id == id))
        {
            return Json(new { ok = false, error = "unknown setting" });
        }

        var current = LiveConfig.Current;
        try
        {
            var (server, debug) = SettingAccess.Set(current.Server, current.Debug, id, value);
            var (changes, saved) = LiveConfig.Update(server, debug);
            var after = LiveConfig.Current;
            return Json(new { ok = true, v = SettingAccess.Get(after.Server, after.Debug, id), changes, saved });
        }
        catch (FormatException ex)
        {
            return Json(new { ok = false, error = ex.Message, v = SettingAccess.Get(current.Server, current.Debug, id) });
        }
    }

    private ContentResult Json(object value)
    {
        Response.Headers.CacheControl = "no-store";
        return Content(JsonSerializer.Serialize(value), "application/json; charset=utf-8");
    }

    private static object BuildState()
    {
        var config = LiveConfig.Current;
        var stats = ServerStats.Take();
        var defaults = (Server: LiveConfig.Sanitize(new ServerToggles()), Debug: LiveConfig.Sanitize(new DebugOptions()));
        return new
        {
            version = new ModMetadata().Version.ToString(),
            masterEnabled = config.MasterEnabled,
            lang = DefaultLanguage(),
            processMb = ServerStats.Mb(stats.WorkingSetBytes),
            committedMb = ServerStats.Mb(stats.CommittedBytes),
            lastCleanupKo = PostRaidCleanup.LastResult,
            lastCleanupEn = PostRaidCleanup.LastResultEn,
            debugLog = DebugLog.Enabled ? DebugLog.FilePath : null,
            settings = SettingsCatalog.All.Select(s => new
            {
                id = s.Id,
                catKo = s.CategoryKo,
                catEn = s.CategoryEn,
                nameKo = s.NameKo,
                nameEn = s.NameEn,
                descKo = s.DescriptionKo,
                descEn = s.DescriptionEn,
                kind = s.Kind,
                order = s.Order,
                min = s.Min,
                max = s.Max,
                options = s.Options,
                optionsKo = s.OptionsKo,
                optionsEn = s.OptionsEn,
                v = SettingAccess.Get(config.Server, config.Debug, s.Id),
                def = SettingAccess.Get(defaults.Server, defaults.Debug, s.Id),
            }),
        };
    }

    /// <summary>
    /// The page's starting language: the client F12 plugin's "Language" setting when the game is on this PC
    /// (BepInEx\config next to SPT_Runtime), else Korean. The page's own switch overrides it per browser.
    /// </summary>
    private static string DefaultLanguage()
    {
        try
        {
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            for (var i = 0; i < 6 && dir is not null; i++, dir = Path.GetDirectoryName(dir))
            {
                var cfg = Path.Combine(dir, "BepInEx", "config", "com.echostarz.compoundingperf.client.cfg");
                if (System.IO.File.Exists(cfg))
                {
                    return System.IO.File.ReadLines(cfg).Any(l => l.Replace(" ", "") == "Language=English") ? "en" : "ko";
                }
            }
        }
        catch (Exception)
        {
            // fall back to Korean
        }

        return "ko";
    }
}
