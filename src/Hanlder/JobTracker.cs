// JobTracker.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace YoutubeTogether.Hanlder
{
    public class JobInfo
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Description { get; set; }
        public DateTime StartedAt { get; set; } = DateTime.Now;
        public DateTime? CompletedAt { get; set; }
        public Exception Error { get; set; }
        public double? DurationSeconds => CompletedAt.HasValue
            ? (CompletedAt.Value - StartedAt).TotalSeconds
            : null;

        public string Requester { get; set; }
        public string Command { get; set; }      // command → Command (C# 관례)
        public string Argument { get; set; }

        public Task Task { get; set; }

        // 핵심: 결과를 받는 콜백!
        public Action<JobInfo> OnCompleted { get; set; }

        // Handler가 나중에 채워줄 결과들
        public string Result { get; set; }
        public bool IsSuccess => Error == null && CompletedAt.HasValue;
        public bool IsFailed => Error != null;
    }

    public sealed class JobTracker
    {
        private static readonly Lazy<JobTracker> _instance = new Lazy<JobTracker>(() => new JobTracker());
        public static JobTracker Instance => _instance.Value;

        private readonly ConcurrentDictionary<string, JobInfo> _jobs = new ConcurrentDictionary<string, JobInfo>();
        private readonly ConcurrentQueue<JobInfo> _completed = new ConcurrentQueue<JobInfo>();
        private readonly int _completedCap = 1000;

        private JobTracker()
        {
        }

        public JobInfo IssueJob(string description, string command, string argument, string requester = "Admin(CLI)")
        {
            var id = Guid.NewGuid().ToString();
            var started = DateTime.UtcNow;
            var info = new JobInfo { Id = id, Description = description, StartedAt = started, Requester = requester, Command = command, Argument = argument };
            var Complete = new Task(async () =>
            {
                info.Description = "Complete";

                info.CompletedAt = DateTime.UtcNow;
                // remove from active
                _jobs.TryRemove(id, out var _);
                // enqueue into completed queue
                _completed.Enqueue(info);
                // cap queue
                while (_completed.Count > _completedCap && _completed.TryDequeue(out var _)) { }

                info.OnCompleted?.Invoke(info);
            });
            info.Task = Complete;
            _jobs[id] = info;
            return info;
        }

        public ICollection<JobInfo> ListJobs()
        {
            return _jobs.Values;
        }

        public List<JobInfo> ListCompletedJobs()
        {
            return new List<JobInfo>(_completed);
        }

    }
}
