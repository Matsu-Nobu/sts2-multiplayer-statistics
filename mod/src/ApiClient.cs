using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace StsStats;

/// <summary>
/// docs/API.md のエンドポイントを叩く HTTP クライアント。
/// 失敗時は false / null を返し、例外は内部でログして握る。
/// </summary>
internal interface IApiClient
{
    Task<CreateSessionResult?> CreateSessionAsync(CreateSessionRequest req);
    Task<bool> PostTurnAsync(string sessionId, string writeToken, TurnPayload payload);
    Task<bool> PostEventsAsync(string sessionId, string writeToken, IReadOnlyList<EventRecord> events);
}

internal sealed class ApiClient : IApiClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public ApiClient(string baseUrl, HttpClient? http = null)
    {
        _baseUrl = baseUrl?.TrimEnd('/') ?? "";
        _http = http ?? new HttpClient { Timeout = System.TimeSpan.FromSeconds(10) };
    }

    public async Task<CreateSessionResult?> CreateSessionAsync(CreateSessionRequest req)
    {
        try
        {
            using var content = JsonContent(req);
            using var res = await _http.PostAsync(_baseUrl + "/sessions", content);
            if (!res.IsSuccessStatusCode)
            {
                MegaCrit.Sts2.Core.Logging.Log.Error($"[StsStats] CreateSession failed: {res.StatusCode}");
                return null;
            }
            string body = await res.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<CreateSessionResult>(body, PayloadJson.Options);
        }
        catch (System.Exception ex)
        {
            MegaCrit.Sts2.Core.Logging.Log.Error($"[StsStats] CreateSession error: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> PostTurnAsync(string sessionId, string writeToken, TurnPayload payload)
    {
        string url = $"{_baseUrl}/sessions/{sessionId}/turns";
        var body = PayloadJson.BuildTurnBody(payload);
        return await PostJsonAsync(url, writeToken, body);
    }

    public async Task<bool> PostEventsAsync(string sessionId, string writeToken, IReadOnlyList<EventRecord> events)
    {
        if (events.Count == 0) return true;
        string url = $"{_baseUrl}/sessions/{sessionId}/events";
        var body = events.Select(PayloadJson.BuildEventBody).ToArray();
        return await PostJsonAsync(url, writeToken, body);
    }

    private async Task<bool> PostJsonAsync(string url, string? bearerToken, object body)
    {
        try
        {
            using var content = JsonContent(body);
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            if (!string.IsNullOrEmpty(bearerToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            using var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode)
            {
                MegaCrit.Sts2.Core.Logging.Log.Error($"[StsStats] POST {url} failed: {res.StatusCode}");
                return false;
            }
            return true;
        }
        catch (System.Exception ex)
        {
            MegaCrit.Sts2.Core.Logging.Log.Error($"[StsStats] POST {url} error: {ex.Message}");
            return false;
        }
    }

    private static StringContent JsonContent(object body)
    {
        string json = JsonSerializer.Serialize(body, PayloadJson.Options);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }
}

/// <summary>POST /sessions のリクエスト。</summary>
internal record CreateSessionRequest(
    string? HostName    = null,
    string? HostSteamId = null,
    string? CharacterId = null,
    int?    Ascension   = null,
    string? Seed        = null
);

/// <summary>POST /sessions のレスポンス。</summary>
internal record CreateSessionResult(
    string SessionId,
    string WriteToken,
    string ShareUrl
);
