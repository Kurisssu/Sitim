using System.Net.Http.Json;
using System.Text.Json;
using Sitim.Core.Models;
using Sitim.Core.Services;

namespace Sitim.Infrastructure.Orthanc;

/// <summary>
/// Very small Orthanc REST client (MVP):
/// - GET /studies
/// - GET /studies/{id}
/// </summary>
public sealed class OrthancClient : IOrthancClient
{
    private readonly HttpClient _http;

    public OrthancClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<string>> GetStudyIdsAsync(CancellationToken ct)
    {
        // Orthanc returns JSON array of strings
        var ids = await _http.GetFromJsonAsync<List<string>>("studies", cancellationToken: ct);
        return ids ?? new List<string>();
    }

    public async Task<OrthancStudyDetails> GetStudyAsync(string orthancStudyId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orthancStudyId))
            throw new ArgumentException("orthancStudyId is required", nameof(orthancStudyId));

        using var resp = await _http.GetAsync($"studies/{orthancStudyId}", ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? GetTag(JsonElement parent, string property, string tagName)
        {
            if (parent.TryGetProperty(property, out var tags) &&
                tags.ValueKind == JsonValueKind.Object &&
                tags.TryGetProperty(tagName, out var v))
            {
                return v.GetString();
            }
            return null;
        }

        var studyInstanceUid = GetTag(root, "MainDicomTags", "StudyInstanceUID");
        var studyDate = GetTag(root, "MainDicomTags", "StudyDate");

        var patientId = GetTag(root, "PatientMainDicomTags", "PatientID");
        var patientName = GetTag(root, "PatientMainDicomTags", "PatientName");

        var modalities = new List<string>();
        if (root.TryGetProperty("ModalitiesInStudy", out var mods) && mods.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in mods.EnumerateArray())
            {
                var s = m.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    modalities.Add(s);
            }
        }

        var seriesIds = new List<string>();
        if (root.TryGetProperty("Series", out var series) && series.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in series.EnumerateArray())
            {
                var id = s.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                    seriesIds.Add(id);
            }
        }

        return new OrthancStudyDetails(
            OrthancStudyId: orthancStudyId,
            StudyInstanceUid: studyInstanceUid,
            StudyDate: studyDate,
            PatientId: patientId,
            PatientName: patientName,
            ModalitiesInStudy: modalities,
            SeriesOrthancIds: seriesIds
        );
    }
}
