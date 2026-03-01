namespace SubtitleGuardian.Application.Jobs;

public enum JobStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Canceled = 4
}

public sealed record JobProgress(int Percent, string? Message = null);

public sealed record JobSnapshot(
    Guid JobId,
    string Title,
    JobStatus Status,
    int Percent,
    string? Message,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt
);

public sealed record JobCompletion(
    Guid JobId,
    JobStatus Status,
    object? Result,
    Exception? Exception
);

public sealed record JobHandle(
    Guid JobId,
    Func<bool> Cancel,
    Task<JobCompletion> Completion
);

public sealed record JobContext(
    Guid JobId,
    CancellationToken CancellationToken,
    IProgress<JobProgress> Progress
);

