using Sitim.Core.Models;

namespace Sitim.Core.Services;

public interface IOrthancClient
{
    Task<IReadOnlyList<string>> GetStudyIdsAsync(CancellationToken ct);
    Task<OrthancStudyDetails> GetStudyAsync(string orthancStudyId, CancellationToken ct);
}
