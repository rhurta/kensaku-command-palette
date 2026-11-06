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

// Forma real de GET /search (distinta de "kensaku-cli search --json"):
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
            return string.IsNullOrEmpty(name) ? Path : name; // carpetas/raíces
        }
    }
}

// Source generation: funciona también si el proyecto se publica con AOT/trimming.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SearchResponse))]
internal sealed partial class KensakuJson : JsonSerializerContext { }

/// <summary>
/// Habla con "kensaku-cli serve" por HTTP (loopback + bearer token).
/// Si no hay servidor en el puerto, lo lanza oculto y lo mantiene vivo.
/// </summary>
internal sealed class KensakuClient : IDisposable
{
    public static KensakuClient Instance { get; } = new();

    // Ajusta aquí o léelo desde ajustes de la extensión.
    private readonly string _cliPath =
        Environment.GetEnvironmentVariable("KENSAKU_CLI") ?? "kensaku-cli";
    private readonly int _port = 8765;

    private readonly string _tokenPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "kensaku", "http.token");

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private Process? _proc; // solo si lo lanzamos nosotros

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
                continue; // el token pudo rotar: se relee y se reintenta una vez

            res.EnsureSuccessStatusCode();
            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync(
                       stream, KensakuJson.Default.SearchResponse, ct)
                   ?? new SearchResponse([], 0, 0);
        }

        throw new InvalidOperationException("Kensaku rechazó el token (401).");
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
                    ?? throw new InvalidOperationException("No se pudo lanzar kensaku-cli.");
            // Hay que consumir la salida o el proceso puede bloquearse al llenarse el búfer.
            _proc.OutputDataReceived += (_, _) => { };
            _proc.ErrorDataReceived += (_, _) => { };
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();

            for (var i = 0; i < 50; i++) // hasta ~10 s
            {
                if (_proc.HasExited)
                    throw new InvalidOperationException(
                        $"kensaku-cli serve terminó con código {_proc.ExitCode} " +
                        "(código 3 = no hay índice: abre Kensaku una vez).");
                if (await HealthAsync(ct)) return;
                await Task.Delay(200, ct);
            }

            throw new TimeoutException("kensaku-cli serve no respondió en /health.");
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
