using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace KensakuCmdPal;

// Actual shape of GET /search (different from "kensaku-cli search --json"):
// { as_of, mode, results:[{match_type,modified,path,score,size,snippet}], returned, total }
internal sealed record SearchResponse(List<Hit> Results, int Total, int Returned);

internal sealed record Hit(
    string Path, string? Snippet, double Score, long Size, long Modified, string MatchType)
{
    [JsonIgnore]
    public string Filename
    {
        get
        {
            var name = System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(name) ? Path : name; // folders / drive roots
        }
    }
}

// Source generation: also works if the project is published with AOT/trimming.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SearchResponse))]
internal sealed partial class KensakuJson : JsonSerializerContext { }

/// <summary>
/// Talks to "kensaku-cli serve" over HTTP (loopback + bearer token).
/// If nothing is listening on the port, it launches the server hidden and keeps it alive.
/// </summary>
internal sealed class KensakuClient : IDisposable
{
    public static KensakuClient Instance { get; } = new();

    // Adjust here or read from the extension settings.
    private readonly string _cliPath =
        Environment.GetEnvironmentVariable("KENSAKU_CLI") ?? "kensaku-cli";
    private readonly int _port = 8765;

    private readonly string _tokenPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "kensaku", "http.token");

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private Process? _proc; // only set if we launched the server ourselves

    private Uri Url(string pathAndQuery) =>
        new($"http://127.0.0.1:{_port}{pathAndQuery}");

    public async Task<SearchResponse> SearchAsync(string query, int limit, CancellationToken ct)
    {
        await EnsureServerAsync(ct);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                Url($"/search?q={Uri.EscapeDataString(query)}&limit={limit}"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ReadToken());

            using var res = await _http.SendAsync(req, ct);
            if (res.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                continue; // the token may have rotated: re-read it and retry once

            res.EnsureSuccessStatusCode();
            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync(
                       stream, KensakuJson.Default.SearchResponse, ct)
                   ?? new SearchResponse([], 0, 0);
        }

        throw new InvalidOperationException(Loc.Get("ErrUnauthorized"));
    }

    private string ReadToken() => File.ReadAllText(_tokenPath).Trim();

    private async Task<bool> HealthAsync(CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, Url("/health"));
            if (File.Exists(_tokenPath))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ReadToken());
            using var res = await _http.SendAsync(req, ct);
            return res.IsSuccessStatusCode;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task EnsureServerAsync(CancellationToken ct)
    {
        if (await HealthAsync(ct)) return;

        await _startLock.WaitAsync(ct);
        try
        {
            if (await HealthAsync(ct)) return;

            var psi = new ProcessStartInfo(_cliPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("serve");
            psi.ArgumentList.Add("--port");
            psi.ArgumentList.Add(_port.ToString());

            _proc = Process.Start(psi)
                    ?? throw new InvalidOperationException(Loc.Get("ErrLaunch"));
            // The output must be consumed or the process can block when the buffer fills up.
            _proc.OutputDataReceived += (_, _) => { };
            _proc.ErrorDataReceived += (_, _) => { };
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();

            for (var i = 0; i < 50; i++) // up to ~10 s
            {
                if (_proc.HasExited)
                    throw new InvalidOperationException(
                        Loc.Format("ErrServeExited", _proc.ExitCode));
                if (await HealthAsync(ct)) return;
                await Task.Delay(200, ct);
            }

            throw new TimeoutException(Loc.Get("ErrHealthTimeout"));
        }
        finally
        {
            _startLock.Release();
        }
    }

    public void Dispose()
    {
        try { if (_proc is { HasExited: false }) _proc.Kill(true); } catch { }
        _http.Dispose();
    }
}
