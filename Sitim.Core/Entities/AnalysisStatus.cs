namespace Sitim.Core.Entities
{
    /// <summary>
    /// Lifecycle for an analysis job.
    /// </summary>
    public enum AnalysisStatus
    {
        Queued = 0,
        Running = 1,
        Succeeded = 2,
        Failed = 3
    }
}
