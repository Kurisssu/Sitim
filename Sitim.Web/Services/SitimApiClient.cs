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
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            return [];
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

    public async Task<bool> DeleteStudyAsync(string orthancStudyId)
    {
        AttachToken();
        var resp = await _http.DeleteAsync($"api/local/studies/{orthancStudyId}");
        return resp.IsSuccessStatusCode;
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

    public async Task<ImportResult?> ImportFilesAsync(IEnumerable<(byte[] data, string name)> files)
    {
        AttachToken();
        using var content = new MultipartFormDataContent();
        foreach (var (data, name) in files)
        {
            var sc = new ByteArrayContent(data);
            sc.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(sc, "Files", name);
        }

        var resp = await _http.PostAsync("api/studies/import", content);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<ImportResult>(JsonOpts);
    }

    // ── Users ─────────────────────────────────────────────

    public async Task<List<UserResult>> GetUsersAsync()
    {
        AttachToken();
        var resp = await _http.GetAsync("api/users");
        if (!resp.IsSuccessStatusCode) return [];
        return await resp.Content.ReadFromJsonAsync<List<UserResult>>(JsonOpts) ?? [];
    }

    public async Task<InviteUserResponse?> InviteUserAsync(
        string email, string? fullName, string role, Guid? institutionId, string webBaseUrl)
    {
        AttachToken();
        var resp = await _http.PostAsJsonAsync(
            $"api/users/invite?baseUrl={Uri.EscapeDataString(webBaseUrl)}",
            new InviteUserRequest(email, fullName, role, institutionId));
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<InviteUserResponse>(JsonOpts);
    }

    public async Task<UserResult?> UpdateUserAsync(Guid id, string? fullName, string? role, bool? isActive)
    {
        AttachToken();
        var resp = await _http.PutAsJsonAsync($"api/users/{id}",
            new UpdateUserRequest(fullName, role, isActive));
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<UserResult>(JsonOpts);
    }

    public async Task<bool> DeactivateUserAsync(Guid id)
    {
        AttachToken();
        var resp = await _http.DeleteAsync($"api/users/{id}");
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> SetPasswordAsync(Guid userId, string token, string newPassword)
    {
        var resp = await _http.PostAsJsonAsync("api/auth/set-password",
            new SetPasswordRequest(userId, token, newPassword));
        return resp.IsSuccessStatusCode;
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

    // ── Institutions (SuperAdmin) ─────────────────────────

    public async Task<List<InstitutionResult>> GetInstitutionsAsync()
    {
        AttachToken();
        var resp = await _http.GetAsync("api/institutions");
        if (!resp.IsSuccessStatusCode) return [];
        return await resp.Content.ReadFromJsonAsync<List<InstitutionResult>>(JsonOpts) ?? [];
    }

    public async Task<InstitutionResult?> CreateInstitutionAsync(string name, string slug, string orthancBaseUrl)
    {
        AttachToken();
        var resp = await _http.PostAsJsonAsync("api/institutions",
            new CreateInstitutionRequest(name, slug, orthancBaseUrl));
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<InstitutionResult>(JsonOpts);
    }

    public async Task<InstitutionResult?> UpdateInstitutionAsync(Guid id, string name, string orthancBaseUrl, bool isActive)
    {
        AttachToken();
        var resp = await _http.PutAsJsonAsync($"api/institutions/{id}",
            new UpdateInstitutionRequest(name, orthancBaseUrl, isActive));
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<InstitutionResult>(JsonOpts);
    }

    // ── Federated Learning (SuperAdmin) ───────────────────

    public async Task<List<FLSessionDto>> GetFLSessionsAsync()
    {
        AttachToken();
        var resp = await _http.GetAsync("api/fl/sessions");
        if (!resp.IsSuccessStatusCode) return [];
        return await resp.Content.ReadFromJsonAsync<List<FLSessionDto>>(JsonOpts) ?? [];
    }

    public async Task<FLSessionDetailsDto?> GetFLSessionAsync(Guid sessionId)
    {
        AttachToken();
        var resp = await _http.GetAsync($"api/fl/sessions/{sessionId}");
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<FLSessionDetailsDto>(JsonOpts);
    }

    public async Task<List<FLConnectedClientDto>> GetFLConnectedClientsAsync()
    {
        AttachToken();
        var resp = await _http.GetAsync("api/fl/clients");
        if (!resp.IsSuccessStatusCode) return [];
        return await resp.Content.ReadFromJsonAsync<List<FLConnectedClientDto>>(JsonOpts) ?? [];
    }

    public async Task<FLSessionDto?> StartFLSessionAsync(string modelKey, int totalRounds, List<Guid> institutionIds)
    {
        AttachToken();
        var request = new StartFLSessionRequest(modelKey, totalRounds, institutionIds);
        var resp = await _http.PostAsJsonAsync("api/fl/sessions", request);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<FLSessionDto>(JsonOpts);
    }

    public async Task<bool> StopFLSessionAsync(Guid sessionId)
    {
        AttachToken();
        var resp = await _http.PostAsync($"api/fl/sessions/{sessionId}/stop", null);
        return resp.IsSuccessStatusCode;
    }

    public async Task<FLPublishedModelDto?> GetFLPublishedModelAsync(Guid sessionId)
    {
        AttachToken();
        var resp = await _http.GetAsync($"api/fl/sessions/{sessionId}/published-model");
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<FLPublishedModelDto>(JsonOpts);
    }

    public async Task<FLPublishedModelDto?> ActivateFLPublishedModelAsync(Guid sessionId)
    {
        AttachToken();
        var resp = await _http.PostAsync($"api/fl/sessions/{sessionId}/activate-model", null);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<FLPublishedModelDto>(JsonOpts);
    }

    public async Task<List<ModelDefinitionDto>> GetModelRegistryAsync(bool activeOnly = false, string? task = null, string? modality = null)
    {
        AttachToken();

        var query = new List<string>();
        if (activeOnly) query.Add("activeOnly=true");
        if (!string.IsNullOrWhiteSpace(task)) query.Add($"task={Uri.EscapeDataString(task)}");
        if (!string.IsNullOrWhiteSpace(modality)) query.Add($"modality={Uri.EscapeDataString(modality)}");
        var queryString = query.Count > 0 ? "?" + string.Join("&", query) : string.Empty;

        var resp = await _http.GetAsync($"api/models{queryString}");
        if (!resp.IsSuccessStatusCode) return [];
        return await resp.Content.ReadFromJsonAsync<List<ModelDefinitionDto>>(JsonOpts) ?? [];
    }

    // ── DTOs locale ──────────────────────────────────────

    public sealed record LoginResult(string AccessToken, int ExpiresInSeconds);
    public sealed record MeResult(Guid UserId, string Email, List<string> Roles, Guid? InstitutionId, string? InstitutionName);
    public sealed record SyncAllResult(int Synced);
    public sealed record ViewerLinkResult(string Url);
    public sealed record ImportResult(int UploadedInstances, List<string> OrthancStudyIds, int SyncedStudies, List<string> Errors);
    public sealed record InstitutionResult(Guid Id, string Name, string Slug, string OrthancBaseUrl, bool IsActive, DateTime CreatedAtUtc);
    public sealed record CreateInstitutionRequest(string Name, string Slug, string OrthancBaseUrl);
    public sealed record UpdateInstitutionRequest(string Name, string OrthancBaseUrl, bool IsActive);
    public sealed record UserResult(Guid Id, string Email, string? FullName, string Role, Guid? InstitutionId, string? InstitutionName, bool IsActive, DateTime CreatedAtUtc);
    public sealed record InviteUserRequest(string Email, string? FullName, string Role, Guid? InstitutionId);
    public sealed record InviteUserResponse(Guid UserId, string Email, string InviteLink);
    public sealed record UpdateUserRequest(string? FullName, string? Role, bool? IsActive);
    public sealed record SetPasswordRequest(Guid UserId, string Token, string NewPassword);
    public sealed record StartFLSessionRequest(string ModelKey, int TotalRounds, List<Guid> InstitutionIds);
    public sealed record FLSessionDto(
        Guid Id,
        string ModelKey,
        string Status,
        int TotalRounds,
        int CurrentRound,
        int ParticipantsCount,
        DateTime CreatedAtUtc,
        DateTime? StartedAtUtc,
        DateTime? FinishedAtUtc);
    public sealed record FLParticipantDto(
        Guid InstitutionId,
        string InstitutionName,
        string Status,
        DateTime? LastHeartbeatUtc);
    public sealed record FLConnectedClientDto(
        Guid InstitutionId,
        string ClientId,
        string Status,
        DateTime? LastHeartbeatUtc,
        bool IsOnline);
    public sealed record FLRoundDto(
        int RoundNumber,
        decimal? AggregatedLoss,
        decimal? AggregatedAccuracy,
        DateTime? CompletedAtUtc);
    public sealed record FLSessionDetailsDto(
        Guid Id,
        string ModelKey,
        string Status,
        int TotalRounds,
        int CurrentRound,
        Guid CreatedByUserId,
        DateTime CreatedAtUtc,
        DateTime? StartedAtUtc,
        DateTime? FinishedAtUtc,
        string? LastError,
        string? OutputModelPath,
        List<FLParticipantDto> Participants,
        List<FLRoundDto> Rounds);
    public sealed record FLPublishedModelDto(
        Guid ModelId,
        string Name,
        string Task,
        string Version,
        string StorageFileName,
        bool IsActive);
    public sealed record ModelDefinitionDto(
        Guid Id,
        string Name,
        string Task,
        string Version,
        bool IsActive,
        string StorageFileName,
        decimal? Accuracy,
        string? TrainingSource,
        string? TargetModality,
        DateTime CreatedAt);

    // ── AI Models ─────────────────────────────────────────

    public async Task<List<AIModelDto>> GetAIModelsAsync()
    {
        AttachToken();
        var resp = await _http.GetAsync("api/ai/models");
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<AIModelDto>>(JsonOpts) ?? new();
    }

    public async Task<bool> ToggleModelStatusAsync(Guid modelId)
    {
        AttachToken();
        var resp = await _http.PatchAsync($"api/ai/models/{modelId}/toggle", null);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteModelAsync(Guid modelId)
    {
        AttachToken();
        var resp = await _http.DeleteAsync($"api/ai/models/{modelId}");
        return resp.IsSuccessStatusCode;
    }

    public async Task<List<AIModelDto>> GetActiveModelsAsync()
    {
        AttachToken();
        var resp = await _http.GetAsync("api/ai/models");
        resp.EnsureSuccessStatusCode();
        var allModels = await resp.Content.ReadFromJsonAsync<List<AIModelDto>>(JsonOpts) ?? new();
        return allModels.Where(m => m.IsActive).ToList();
    }

    public async Task<AIAnalysisResultDto?> RunAIAnalysisAsync(Guid studyId, Guid? modelId = null)
    {
        AttachToken();
        var request = new AnalyzeStudyRequest(studyId, modelId);
        var resp = await _http.PostAsJsonAsync("api/ai/analyze", request);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<AIAnalysisResultDto>(JsonOpts);
    }

    /// <summary>
    /// Get AI models compatible with a study's modality.
    /// Returns filtered list sorted by accuracy.
    /// </summary>
    public async Task<List<AIModelSelectionDto>> GetCompatibleModelsAsync(Guid studyId)
    {
        AttachToken();
        var resp = await _http.GetAsync($"api/ai/models-for-study/{studyId}");
        if (!resp.IsSuccessStatusCode) return new();
        return await resp.Content.ReadFromJsonAsync<List<AIModelSelectionDto>>(JsonOpts) ?? new();
    }

    /// <summary>
    /// Start an AI analysis job (runs in background via Hangfire).
    /// Returns job ID for polling job status.
    /// </summary>
    public async Task<Guid> StartAnalysisAsync(Guid studyId, Guid modelId)
    {
        AttachToken();
        var request = new AnalyzeStudyRequest(studyId, modelId);
        var resp = await _http.PostAsJsonAsync("api/ai/analyze", request);
        if (!resp.IsSuccessStatusCode) 
            throw new InvalidOperationException("Failed to start analysis");
        var result = await resp.Content.ReadFromJsonAsync<StartAnalysisResponseDto>(JsonOpts);
        return result?.JobId ?? throw new InvalidOperationException("No job ID returned");
    }

    /// <summary>
    /// Get latest analysis jobs (running + completed) visible in current tenant.
    /// </summary>
    public async Task<List<AIAnalysisJobListItemDto>> GetAnalysisJobsAsync(int limit = 100)
    {
        AttachToken();
        var resp = await _http.GetAsync($"api/ai/jobs?limit={limit}");
        if (!resp.IsSuccessStatusCode) return new();
        return await resp.Content.ReadFromJsonAsync<List<AIAnalysisJobListItemDto>>(JsonOpts) ?? new();
    }

    /// <summary>
    /// Get all analysis jobs for one study (running first, then latest completed/failed).
    /// </summary>
    public async Task<List<AIAnalysisJobListItemDto>> GetStudyAnalysisJobsAsync(Guid studyId)
    {
        AttachToken();
        var resp = await _http.GetAsync($"api/ai/studies/{studyId}/jobs");
        if (!resp.IsSuccessStatusCode) return new();
        return await resp.Content.ReadFromJsonAsync<List<AIAnalysisJobListItemDto>>(JsonOpts) ?? new();
    }

    /// <summary>
    /// Get AI analysis job status and results (for polling).
    /// </summary>
    public async Task<AIAnalysisJobStatusDto?> GetAnalysisJobAsync(Guid jobId)
    {
        AttachToken();
        var resp = await _http.GetAsync($"api/ai/jobs/{jobId}");
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<AIAnalysisJobStatusDto>(JsonOpts);
    }

    /// <summary>
    /// Get full analysis results with diagnosis, severity, and recommendations.
    /// </summary>
    public async Task<AIAnalysisResultDto?> GetAnalysisResultsAsync(Guid jobId)
    {
        AttachToken();
        var resp = await _http.GetAsync($"api/ai/jobs/{jobId}/results");
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<AIAnalysisResultDto>(JsonOpts);
    }

    /// <summary>
    /// Cancel a running analysis job.
    /// </summary>
    public async Task<bool> CancelAnalysisAsync(Guid jobId)
    {
        AttachToken();
        var resp = await _http.PostAsync($"api/ai/jobs/{jobId}/cancel", null);
        return resp.IsSuccessStatusCode;
    }

    /// <summary>
    /// Delete a completed analysis job record.
    /// </summary>
    public async Task<bool> DeleteAnalysisAsync(Guid jobId)
    {
        AttachToken();
        var resp = await _http.DeleteAsync($"api/ai/jobs/{jobId}");
        return resp.IsSuccessStatusCode;
    }

    /// <summary>
    /// Get analysis history for a study.
    /// </summary>
    public async Task<List<AIAnalysisResultDto>?> GetAnalysisHistoryAsync(Guid studyId)
    {
        AttachToken();
        var resp = await _http.GetAsync($"api/ai/studies/{studyId}/history");
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<List<AIAnalysisResultDto>>(JsonOpts);
    }

    public sealed record AnalyzeStudyRequest(Guid StudyId, Guid? ModelId = null);

    public sealed record AIAnalysisResultDto(
        Guid Id,
        string ModelName,
        string ModelVersion,
        int? PredictionClass,
        decimal Confidence,
        string Diagnosis,
        string Severity,
        List<string> Recommendations,
        List<ClassProbability> AllProbabilities,
        int ProcessingTimeMs,
        DateTime PerformedAt,
        string PerformedByUserName
    );

    public sealed record ClassProbability(string ClassName, decimal Probability);

    // Hangfire Job DTOs
    public sealed record StartAnalysisResponseDto(
        Guid JobId,
        string Status,
        DateTime CreatedAt
    );

    public sealed record AIAnalysisJobStatusDto(
        Guid Id,
        Guid StudyId,
        string OrthancStudyId,
        string Status,
        DateTime CreatedAt,
        DateTime? StartedAt,
        DateTime? FinishedAt,
        string? ModelName,
        int? PredictionClass,
        decimal? Confidence,
        int? ProcessingTimeMs,
        string? ErrorMessage
    );

    public sealed record AIAnalysisJobListItemDto(
        Guid Id,
        Guid StudyId,
        string OrthancStudyId,
        string? PatientName,
        string? StudyDate,
        IReadOnlyList<string> ModalitiesInStudy,
        string Status,
        DateTime CreatedAt,
        DateTime? StartedAt,
        DateTime? FinishedAt,
        string? ModelName,
        int? PredictionClass,
        decimal? Confidence,
        int? ProcessingTimeMs,
        string? ErrorMessage
    );

    public sealed record AIModelDto(
        Guid Id,
        string Name,
        string? Description,
        string Task,
        string Version,
        string StorageFileName,
        decimal? Accuracy,
        bool IsActive,
        int? NumClasses,
        string? InputShape,
        string? TrainingSource,
        DateTime CreatedAt
    );

    // Model selection DTO (lightweight, for UI)
    public sealed record AIModelSelectionDto(
        Guid Id,
        string Name,
        string Version,
        string Task,
        decimal? Accuracy,
        string? TargetModality,
        string? Description
    )
    {
        public string Label => $"{Name} (v{Version})";
    };
}
