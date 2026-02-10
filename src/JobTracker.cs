// JobTracker.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace YoutubeTogether
{
    public class JobInfo
    {
        public string Id { get; set; }
        public string Description { get; set; }
        public Task Task { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public Exception Error { get; set; }
        public double? DurationSeconds => CompletedAt.HasValue ? (CompletedAt.Value - StartedAt).TotalSeconds : (double?)null;
        public string Requester { get; set; }
    }

    public class JobTracker
    {
        private readonly ConcurrentDictionary<string, JobInfo> _jobs = new ConcurrentDictionary<string, JobInfo>();
        private readonly System.Collections.Concurrent.ConcurrentQueue<JobInfo> _completed = new System.Collections.Concurrent.ConcurrentQueue<JobInfo>();
        private readonly int _completedCap = 1000;

        public string StartJob(Func<Task> jobFunc, string description, string requester = "Admin(CLI)")
        {
            var id = Guid.NewGuid().ToString();
            var started = DateTime.UtcNow;
            var info = new JobInfo { Id = id, Description = description, StartedAt = started, Requester = requester };
            var t = Task.Run(async () =>
            {
                Exception ex = null;
                try { await jobFunc(); }
                catch (Exception e) { ex = e; }
                finally
                {
                    info.CompletedAt = DateTime.UtcNow;
                    info.Error = ex;
                    info.Task = Task.CompletedTask;
                    // remove from active
                    _jobs.TryRemove(id, out var _);
                    // enqueue into completed queue
                    _completed.Enqueue(info);
                    // cap queue
                    while (_completed.Count > _completedCap && _completed.TryDequeue(out var _)) { }
                }
            });
            info.Task = t;
            _jobs[id] = info;
            return id;
        }

        public System.Collections.Generic.ICollection<JobInfo> ListJobs()
        {
            return _jobs.Values;
        }

        public System.Collections.Generic.List<JobInfo> ListCompletedJobs()
        {
            return new System.Collections.Generic.List<JobInfo>(_completed);
        }

        public bool UpdateDescription(string id, string newDescription)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (_jobs.TryGetValue(id, out var info))
            {
                info.Description = newDescription;
                return true;
            }
            return false;
        }

        public bool TryGetJob(string id, out JobInfo info)
        {
            return _jobs.TryGetValue(id, out info);
        }
    }
}
