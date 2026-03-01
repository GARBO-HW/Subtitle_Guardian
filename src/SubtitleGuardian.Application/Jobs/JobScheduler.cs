using System.Collections.Concurrent;
using System.Threading.Channels;

namespace SubtitleGuardian.Application.Jobs;

public sealed class JobScheduler : IDisposable
{
    private readonly Channel<JobWorkItem> _queue;
    private readonly ConcurrentDictionary<Guid, JobState> _states;
    private readonly CancellationTokenSource _shutdownCts;
    private readonly Task _worker;

    public JobScheduler()
    {
        _queue = Channel.CreateUnbounded<JobWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        _states = new ConcurrentDictionary<Guid, JobState>();
        _shutdownCts = new CancellationTokenSource();
        _worker = Task.Run(WorkerLoopAsync);
    }

    public event Action<JobSnapshot>? JobUpdated;

    public IReadOnlyList<JobSnapshot> GetSnapshot()
    {
        return _states.Values.Select(s => s.Snapshot).OrderByDescending(s => s.CreatedAt).ToArray();
    }

    public JobHandle Enqueue(string title, Func<JobContext, Task<object?>> work)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("title is required", nameof(title));
        }

        Guid jobId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);

        var state = new JobState(
            cts,
            new JobSnapshot(
                jobId,
                title,
                JobStatus.Pending,
                0,
                null,
                null,
                now,
                null,
                null
            )
        );

        if (!_states.TryAdd(jobId, state))
        {
            throw new InvalidOperationException("failed to register job");
        }

        Emit(state.Snapshot);

        var item = new JobWorkItem(jobId, work);
        if (!_queue.Writer.TryWrite(item))
        {
            _states.TryRemove(jobId, out _);
            throw new InvalidOperationException("failed to enqueue job");
        }

        return new JobHandle(
            jobId,
            () => TryCancel(jobId),
            state.CompletionTcs.Task
        );
    }

    public bool TryCancel(Guid jobId)
    {
        if (!_states.TryGetValue(jobId, out JobState? state))
        {
            return false;
        }

        if (state.MarkCancellationRequested())
        {
            state.Cts.Cancel();
            UpdateSnapshot(state, s => s with { Status = s.Status == JobStatus.Pending ? JobStatus.Canceled : s.Status });
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        _shutdownCts.Cancel();
        _queue.Writer.TryComplete();
        try
        {
            _worker.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
        _shutdownCts.Dispose();
    }

    private async Task WorkerLoopAsync()
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(_shutdownCts.Token).ConfigureAwait(false))
            {
                while (_queue.Reader.TryRead(out JobWorkItem? item))
                {
                    if (item is null)
                    {
                        continue;
                    }

                    if (!_states.TryGetValue(item.JobId, out JobState? state))
                    {
                        continue;
                    }

                    if (state.Cts.IsCancellationRequested)
                    {
                        CompleteCanceled(state);
                        continue;
                    }

                    DateTimeOffset start = DateTimeOffset.UtcNow;
                    UpdateSnapshot(state, s => s with { Status = JobStatus.Running, StartedAt = start });

                    var progress = new Progress<JobProgress>(p =>
                    {
                        int percent = Math.Clamp(p.Percent, 0, 100);
                        UpdateSnapshot(state, s => s with { Percent = percent, Message = p.Message });
                    });

                    var ctx = new JobContext(state.Snapshot.JobId, state.Cts.Token, progress);

                    try
                    {
                        object? result = await item.Work(ctx).ConfigureAwait(false);
                        DateTimeOffset end = DateTimeOffset.UtcNow;
                        UpdateSnapshot(state, s => s with { Status = JobStatus.Completed, Percent = 100, FinishedAt = end });
                        state.CompletionTcs.TrySetResult(new JobCompletion(state.Snapshot.JobId, JobStatus.Completed, result, null));
                    }
                    catch (OperationCanceledException oce) when (state.Cts.IsCancellationRequested)
                    {
                        CompleteCanceled(state, oce);
                    }
                    catch (Exception ex)
                    {
                        DateTimeOffset end = DateTimeOffset.UtcNow;
                        UpdateSnapshot(state, s => s with { Status = JobStatus.Failed, ErrorMessage = ex.Message, FinishedAt = end });
                        state.CompletionTcs.TrySetResult(new JobCompletion(state.Snapshot.JobId, JobStatus.Failed, null, ex));
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CompleteCanceled(JobState state, Exception? ex = null)
    {
        DateTimeOffset end = DateTimeOffset.UtcNow;
        UpdateSnapshot(state, s => s with { Status = JobStatus.Canceled, FinishedAt = end });
        state.CompletionTcs.TrySetResult(new JobCompletion(state.Snapshot.JobId, JobStatus.Canceled, null, ex));
    }

    private void UpdateSnapshot(JobState state, Func<JobSnapshot, JobSnapshot> update)
    {
        JobSnapshot next;
        lock (state.Gate)
        {
            next = update(state.Snapshot);
            state.Snapshot = next;
        }
        Emit(next);
    }

    private void Emit(JobSnapshot snapshot)
    {
        JobUpdated?.Invoke(snapshot);
    }

    private sealed record JobWorkItem(Guid JobId, Func<JobContext, Task<object?>> Work);

    private sealed class JobState
    {
        private int _cancelRequested;

        public JobState(CancellationTokenSource cts, JobSnapshot snapshot)
        {
            Cts = cts;
            Snapshot = snapshot;
            CompletionTcs = new TaskCompletionSource<JobCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public object Gate { get; } = new object();
        public CancellationTokenSource Cts { get; }
        public TaskCompletionSource<JobCompletion> CompletionTcs { get; }
        public JobSnapshot Snapshot { get; set; }

        public bool MarkCancellationRequested()
        {
            return Interlocked.Exchange(ref _cancelRequested, 1) == 0;
        }
    }
}
