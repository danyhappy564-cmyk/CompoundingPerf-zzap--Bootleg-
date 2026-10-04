using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace CompoundingPerf.Client;

/// <summary>
/// Plain HTTP to the SPT server's CompoundingPerf settings route. No spt-common needed:
/// the backend address comes from the game's own <c>-config={"BackendUrl":...}</c> launch
/// argument, and the <c>requestcompressed: 0</c> / <c>responsecompressed: 0</c> headers
/// make <c>SptHttpListener</c> exchange plain JSON instead of zlib. Blocking — call it
/// from a thread-pool thread, never the main thread.
/// </summary>
internal static class ServerLink
{
    private static string? _backend;

    public static string? BackendUrl => _backend ??= FindBackend();

    private static string? FindBackend()
    {
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (!arg.StartsWith("-config=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = Regex.Match(arg, "\"BackendUrl\"\\s*:\\s*\"([^\"]+)\"");
            if (match.Success)
            {
                return match.Groups[1].Value.TrimEnd('/');
            }
        }

        return null;
    }

    public static string Send(string method, string path, string? json)
    {
        var backend = BackendUrl ?? throw new InvalidOperationException("no -config BackendUrl launch argument");
        var request = (HttpWebRequest)WebRequest.Create(backend + path);
        request.Method = method;
        request.Timeout = 5000;
        request.ReadWriteTimeout = 5000;
        request.Headers["requestcompressed"] = "0";
        request.Headers["responsecompressed"] = "0";

        // Mono adds "Expect: 100-continue" to POSTs and then waits for the server's go-ahead
        // before sending the body; a server that never sends one stalls us until the timeout
        // (seen against HttpListener in testing). The body is tiny - just send it.
        request.ServicePoint.Expect100Continue = false;

        // SPT serves a self-signed certificate; trust it for this request only.
        request.ServerCertificateValidationCallback = (_, _, _, _) => true;

        if (json is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            request.ContentType = "application/json";
            request.ContentLength = bytes.Length;
            using var body = request.GetRequestStream();
            body.Write(bytes, 0, bytes.Length);
        }

        using var response = (HttpWebResponse)request.GetResponse();
        using var reader = new StreamReader(response.GetResponseStream()!, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
