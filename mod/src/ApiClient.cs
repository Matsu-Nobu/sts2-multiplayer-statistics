using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace StsStats;

/// <summary>
/// docs/spec/api.md のエンドポイントを叩く HTTP クライアント。
/// 失敗時は false / null を返し、例外は内部でログして握る。
/// </summary>
internal interface IApiClient
{
    Task<CreateSessionResult?> CreateSessionAsync(CreateSessionRequest req);
    Task<bool> PostEventsAsync(string sessionId, string writeToken, IReadOnlyList<EventRecord> events);
}

/// <summary>セーブデータの履歴の取り込みに使う呼び出し (docs/spec/api.md「POST /imports/check」)。</summary>
internal interface IImportApi
{
    /// <summary>サーバにまだ無いランの番号。失敗したら null。</summary>
    Task<int[]?> CheckImportsAsync(IReadOnlyList<ImportCheckRun> runs);
    /// <summary>1 ランのセッションと記録をまとめて送る (docs/spec/api.md「POST /imports/run」)。</summary>
    Task<ImportRunStatus> ImportRunAsync(ImportSession session, IReadOnlyList<EventRecord> events);
}

internal sealed class ApiClient : IApiClient, IImportApi
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

    public async Task<int[]?> CheckImportsAsync(IReadOnlyList<ImportCheckRun> runs)
    {
        try
        {
            using var content = JsonContent(new { runs });
            using var res = await _http.PostAsync(_baseUrl + "/imports/check", content);
            if (!res.IsSuccessStatusCode)
            {
                MegaCrit.Sts2.Core.Logging.Log.Error($"[StsStats] CheckImports failed: {res.StatusCode}");
                return null;
            }
            string body = await res.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<ImportCheckResult>(body, PayloadJson.Options)?.Missing;
        }
        catch (System.Exception ex)
        {
            MegaCrit.Sts2.Core.Logging.Log.Error($"[StsStats] CheckImports error: {ex.Message}");
            return null;
        }
    }

    public async Task<ImportRunStatus> ImportRunAsync(ImportSession session, IReadOnlyList<EventRecord> events)
    {
        try
        {
            using var content = JsonContent(new { session, events = events.Select(PayloadJson.BuildEventBody).ToArray() });
            using var res = await _http.PostAsync(_baseUrl + "/imports/run", content);
            switch (res.StatusCode)
            {
                case System.Net.HttpStatusCode.Created:         return ImportRunStatus.Created;
                case System.Net.HttpStatusCode.Conflict:        return ImportRunStatus.AlreadyExists;
                case System.Net.HttpStatusCode.TooManyRequests: return ImportRunStatus.RateLimited;
            }
            MegaCrit.Sts2.Core.Logging.Log.Error($"[StsStats] ImportRun failed: {res.StatusCode} {await res.Content.ReadAsStringAsync()}");
            return ImportRunStatus.Failed;
        }
        catch (System.Exception ex)
        {
            MegaCrit.Sts2.Core.Logging.Log.Error($"[StsStats] ImportRun error: {ex.Message}");
            return ImportRunStatus.Failed;
        }
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
    string? Seed        = null,
    string? GameVersion = null
);

/// <summary>POST /imports/run の session。</summary>
internal record ImportSession(
    string? HostName,
    string  HostSteamId,
    string? CharacterId,
    int     Ascension,
    string  Seed,
    string  GameVersion,
    string  StartedAt,
    IReadOnlyList<string> PlayerIds
);

internal enum ImportRunStatus { Created, AlreadyExists, RateLimited, Failed }

/// <summary>POST /imports/check の runs の要素。</summary>
internal record ImportCheckRun(
    string Seed,
    string StartedAt,
    IReadOnlyList<string> PlayerIds,
    string Outcome,
    int FinalFloor
);

internal record ImportCheckResult(int[] Missing);


/// <summary>POST /sessions のレスポンス。</summary>
internal record CreateSessionResult(
    string SessionId,
    string WriteToken,
    string ShareUrl
);
