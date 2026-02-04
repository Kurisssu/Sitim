using System;
using System.Collections.Generic;
using System.Text;

namespace Sitim.Core.Services;

public interface IOrthancClient
{
    Task<IReadOnlyList<string>> GetStudyIdsAsync(CancellationToken ct);
    Task<OrthancStudyDetails> GetStudyAsync(string orthancStudyId, CancellationToken ct);
}

