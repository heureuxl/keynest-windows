using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using KeyNestForWin.Models;

namespace KeyNestForWin.Services;

/// <summary>本机 HTTP 桥，与 macOS KeyNest / Chrome 扩展约定一致（127.0.0.1:17373）。</summary>
public sealed class LocalBridgeServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly VaultService _vault;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public LocalBridgeServer(VaultService vault)
    {
        _vault = vault;
    }

    public bool IsRunning => _listener?.IsListening == true;

    public void Start(int port = 17373)
    {
        Stop();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"KeyNest bridge failed to start: {ex}");
            listener.Close();
            return;
        }
        _listener = listener;
        _cts = new CancellationTokenSource();
        _ = ListenAsync(_cts.Token);
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        _cts?.Dispose();
        _cts = null;
        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch { /* ignore */ }
        _listener = null;
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        var listener = _listener;
        if (listener == null) return;
        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync().WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch { continue; }
            _ = Task.Run(() => HandleRequest(ctx), ct);
        }
    }

    private static T RunOnUiThread<T>(Func<T> action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
            return action();
        return dispatcher.Invoke(action, DispatcherPriority.Normal);
    }

    private void HandleRequest(HttpListenerContext ctx)
    {
        try
        {
            var req = ctx.Request;
            var res = ctx.Response;
            AddCors(res);
            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = 204;
                res.Close();
                return;
            }

            var path = req.Url?.AbsolutePath ?? "";
            if (req.HttpMethod == "GET" &&
                (path == "/api/credentials" || path.EndsWith("/api/credentials", StringComparison.Ordinal)))
            {
                var q = req.QueryString["url"];
                if (string.IsNullOrEmpty(q))
                {
                    WriteJson(res, 400, """{"error":"missing url query"}""");
                    return;
                }
                if (!RunOnUiThread(() => _vault.IsUnlocked))
                {
                    WriteJson(res, 503, """{"error":"vault locked"}""");
                    return;
                }
                var matches = RunOnUiThread(() => _vault.MatchCredentialsForPage(q));
                var payload = matches.Select(x => new BridgeCredentialDto
                {
                    Username = x.Username,
                    Password = x.Password,
                    Title = x.Title
                }).ToList();
                var json = JsonSerializer.Serialize(payload, JsonOptions);
                WriteJson(res, 200, json);
                return;
            }

            if (req.HttpMethod == "GET" &&
                (path == "/api/site-limit-check" || path.EndsWith("/api/site-limit-check", StringComparison.Ordinal)))
            {
                var url = req.QueryString["url"];
                var username = req.QueryString["username"] ?? "";
                if (string.IsNullOrEmpty(url))
                {
                    WriteJson(res, 400, """{"error":"missing url query"}""");
                    return;
                }
                if (!RunOnUiThread(() => _vault.IsUnlocked))
                {
                    WriteJson(res, 503, """{"error":"vault locked"}""");
                    return;
                }
                var probe = new PasswordItemDto
                {
                    Id = Guid.NewGuid(),
                    Username = username,
                    Url = url
                };
                var check = RunOnUiThread(() => _vault.GetBridgeSiteLimitCheck(url, username));
                WriteJson(res, 200, JsonSerializer.Serialize(check, JsonOptions));
                return;
            }

            if (req.HttpMethod == "POST" &&
                (path == "/api/save" || path.EndsWith("/api/save", StringComparison.Ordinal)))
            {
                if (!RunOnUiThread(() => _vault.IsUnlocked))
                {
                    WriteJson(res, 503, """{"error":"vault locked"}""");
                    return;
                }
                string body;
                using (var sr = new StreamReader(req.InputStream, req.ContentEncoding))
                    body = sr.ReadToEnd();
                BridgeSavePayloadDto? payload;
                try
                {
                    payload = JsonSerializer.Deserialize<BridgeSavePayloadDto>(body, JsonOptions);
                }
                catch
                {
                    WriteJson(res, 400, """{"error":"invalid json"}""");
                    return;
                }
                if (payload == null || string.IsNullOrEmpty(payload.Password))
                {
                    WriteJson(res, 400, """{"error":"empty password"}""");
                    return;
                }
                var title = payload.Title.Trim();
                var urlStr = payload.Url.Trim();
                if (RunOnUiThread(() => _vault.ShouldSkipBridgeSave(urlStr, payload.Username, payload.Password)))
                {
                    WriteJson(res, 200, """{"ok":true,"unchanged":true}""");
                    return;
                }
                var displayTitle = string.IsNullOrEmpty(title)
                    ? (Uri.TryCreate(urlStr, UriKind.Absolute, out var u) ? u.Host : "未命名")
                    : title;
                var item = new PasswordItemDto
                {
                    Id = Guid.NewGuid(),
                    Title = displayTitle,
                    Username = payload.Username,
                    Password = payload.Password,
                    Url = urlStr,
                    Notes = ""
                };
                try
                {
                    // 扩展已在浏览器内完成保存/站点上限确认；桥接路径始终允许驱逐最旧条目
                    var saved = RunOnUiThread(() =>
                        _vault.AddOrUpdateItemAsync(item, urlStr, allowEvictOldest: true).GetAwaiter().GetResult());
                    if (!saved)
                    {
                        WriteJson(res, 500, """{"error":"save rejected at site limit"}""");
                        return;
                    }
                    WriteJson(res, 200, """{"ok":true}""");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"KeyNest bridge save failed: {ex}");
                    WriteJson(res, 500, """{"error":"save failed"}""");
                }
                return;
            }

            WriteJson(res, 404, """{"error":"unknown path"}""");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"KeyNest bridge request failed: {ex}");
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { /* ignore */ }
        }
    }

    private static void AddCors(HttpListenerResponse res)
    {
        res.Headers.Add("Access-Control-Allow-Origin", "*");
        res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        res.Headers.Add("Access-Control-Allow-Headers", "*");
    }

    private static void WriteJson(HttpListenerResponse res, int status, string jsonBody)
    {
        res.StatusCode = status;
        res.ContentType = "application/json; charset=utf-8";
        var buf = Encoding.UTF8.GetBytes(jsonBody);
        res.ContentLength64 = buf.Length;
        res.OutputStream.Write(buf, 0, buf.Length);
        res.Close();
    }

    public void Dispose() => Stop();
}
