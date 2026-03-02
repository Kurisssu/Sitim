using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Sitim.Core.Models;

namespace Sitim.Web.Services;

/// <summary>
/// Typed HTTP client that wraps all SITIM API calls.
/// Token is attached from circuit-scoped <see cref="AuthTokenStore"/>.
/// </summary>
public sealed class SitimApiClient
{
    private readonly HttpClient _http;
    private readonly AuthTokenStore _store;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SitimApiClient(HttpClient http, AuthTokenStore store)
    {
        _http = http;
        _store = store;
    }

    /// <summary>
    /// Ensures the current JWT token (if any) is set on the HttpClient before making a request.
    /// </summary>
    private void AttachToken()
    {
        var token = _store.Token;
        if (!string.IsNullOrWhiteSpace(token))
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        else
            _http.DefaultRequestHeaders.Authorization = null;
    }

    // ── Auth ──────────────────────────────────────────────

    public async Task<LoginResult?> LoginAsync(string email, string password)
    {
        var resp = await _http.PostAsJsonAsync("api/auth/login", new { email, password });
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<LoginResult>(JsonOpts);
    }

    public async Task<MeResult?> GetMeAsync()
    {
        AttachToken();
        var resp = await _http.GetAsync("api/auth/me");
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<MeResult>(JsonOpts);
    }

    // ── Worklist (local DB) ──────────────────────────────

    public async Task<List<StudySummary>> GetLocalStudiesAsync()
    {
        AttachToken();
        var resp = await _http.GetAsync("api/local/studies");
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<StudySummary>>(JsonOpts) ?? [];
    }

    public async Task<StudyDetails?> GetLocalStudyAsync(string orthancStudyId)
    {
        AttachToken();
        var resp = await _http.GetAsync($"api/local/studies/{orthancStudyId}");
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<StudyDetails>(JsonOpts);
    }

    public async Task<StudyDetails?> SyncStudyAsync(string orthancStudyId)
    {
        AttachToken();
        var resp = await _http.GetAsync($"api/local/studies/{orthancStudyId}/sync");
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<StudyDetails>(JsonOpts);
    }

    public async Task<int> SyncAllStudiesAsync()
    {
        AttachToken();
        var resp = await _http.PostAsync("api/local/studies/sync-all", null);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<SyncAllResult>(JsonOpts);
        return result?.Synced ?? 0;
    }

    // ── Orthanc studies (viewer link) ────────────────────

    public async Task<string?> GetViewerLinkAsync(string orthancStudyId)
    {
        AttachToken();
        var resp = await _http.GetAsync($"api/studies/{orthancStudyId}/viewer-link");
        if (!resp.IsSuccessStatusCode) return null;
        var result = await resp.Content.ReadFromJsonAsync<ViewerLinkResult>(JsonOpts);
        return result?.Url;
    }

    // ── Analyses ─────────────────────────────────────────

    public async Task<AnalysisJobDto?> CreateAnalysisAsync(string orthancStudyId, string? modelKey = null)
    {
        AttachToken();
        var resp = await _http.PostAsJsonAsync("api/analyses", new CreateAnalysisRequest(orthancStudyId, modelKey));
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<AnalysisJobDto>(JsonOpts);
    }

    public async Task<AnalysisJobDto?> GetAnalysisAsync(Guid id)
    {
        AttachToken();
        var resp = await _http.GetAsync($"api/analyses/{id}");
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<AnalysisJobDto>(JsonOpts);
    }

    public async Task<List<AnalysisJobDto>> GetAnalysesByStudyAsync(string orthancStudyId)
    {
        AttachToken();
        var resp = await _http.GetAsync($"api/analyses/by-study/{orthancStudyId}");
        if (!resp.IsSuccessStatusCode) return [];
        return await resp.Content.ReadFromJsonAsync<List<AnalysisJobDto>>(JsonOpts) ?? [];
    }

    public async Task<string?> GetAnalysisResultAsync(Guid id)
    {
        AttachToken();
        var resp = await _http.GetAsync($"api/analyses/{id}/result");
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadAsStringAsync();
    }

    public async Task<bool> ReenqueueAnalysisAsync(Guid id)
    {
        AttachToken();
        var resp = await _http.PostAsync($"api/analyses/{id}/enqueue", null);
        return resp.IsSuccessStatusCode;
    }

    public async Task<int> EnqueueUnfinishedAsync()
    {
        AttachToken();
        var resp = await _http.PostAsync("api/analyses/enqueue-unfinished", null);
        if (!resp.IsSuccessStatusCode) return 0;
        return await resp.Content.ReadFromJsonAsync<int>(JsonOpts);
    }

    // ── Import ───────────────────────────────────────────

    public async Task<ImportResult?> ImportArchiveAsync(Stream fileStream, string fileName)
    {
        AttachToken();
        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(streamContent, "Archive", fileName);

        var resp = await _http.PostAsync("api/studies/import", content);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<ImportResult>(JsonOpts);
    }

    public async Task<ImportResult?> ImportFilesAsync(IEnumerable<(Stream stream, string name)> files)
    {
        AttachToken();
        using var content = new MultipartFormDataContent();
        foreach (var (stream, name) in files)
        {
            var sc = new StreamContent(stream);
            sc.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(sc, "Files", name);
        }

        var resp = await _http.PostAsync("api/studies/import", content);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<ImportResult>(JsonOpts);
    }

    // ── Health ────────────────────────────────────────────

    public async Task<bool> HealthCheckAsync()
    {
        try
        {
            var resp = await _http.GetAsync("api/health");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ── DTOs locale ──────────────────────────────────────

    public sealed record LoginResult(string AccessToken, int ExpiresInSeconds);
    public sealed record MeResult(Guid UserId, string Email, List<string> Roles);
    public sealed record SyncAllResult(int Synced);
    public sealed record ViewerLinkResult(string Url);
    public sealed record ImportResult(int UploadedInstances, List<string> OrthancStudyIds, int SyncedStudies, List<string> Errors);
}
