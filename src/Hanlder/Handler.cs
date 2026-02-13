using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YoutubeTogether.MediaProcessor;

namespace YoutubeTogether.Hanlder
{
    internal sealed class Handler
    {
        private static readonly System.Lazy<Handler> _instance = new System.Lazy<Handler>(() => new Handler());
        public static Handler Instance => _instance.Value;

        private readonly ConcurrentQueue<JobInfo> _commandQueue = new ConcurrentQueue<JobInfo>();

        private CancellationTokenSource? _cts;
        private Task? _processingTask;

        private Handler()
        {
            StartProcessing();
        }

        public void DispatchCommand(JobInfo job)
        {
            _commandQueue.Enqueue(job);
        }

        private void StartProcessing()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
                return;

            _cts = new CancellationTokenSource();
            _processingTask = Task.Run(() => ProcessLoopAsync(_cts.Token));
        }

        public void StopProcessing()
        {
            if (_cts == null)
                return;

            _cts.Cancel();
            try
            {
                _processingTask?.Wait();
            }
            catch { }
            finally
            {
                _cts.Dispose();
                _cts = null;
                _processingTask = null;
            }
        }

        private async Task ProcessLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (_commandQueue.TryDequeue(out var cmd))
                    {
                        try
                        {
                            await ProcessCommandAsync(cmd);
                            cmd.Task.Start();
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"명령 처리 중 오류: {ex.Message}");
                        }

                        await Task.Yield();
                    }
                    else
                    {
                        await Task.Delay(100, ct);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.Log($"Handler processing loop error: {ex}");
            }
        }

        private Task ProcessCommandAsync(JobInfo cmd)
        {
            string c = cmd.Command.Trim();
            if (c.StartsWith("!")) c = c.Substring(1);
            c = c.ToLowerInvariant();

            switch (c)
            {
                case "play":
                    MediaController.Instance.Play(cmd.Argument);
                    return Task.CompletedTask;
                case "stop":
                    MediaController.Instance.Stop();
                    return Task.CompletedTask;
                case "skip":
                    
                    if (int.TryParse(cmd.Argument, out var n) && n > 0)
                        MediaController.Instance.Skip(n);
                    else
                        MediaController.Instance.Skip(1);
                    
                    return Task.CompletedTask;
                case "queue":
                    int page = 1;
                    if (!string.IsNullOrEmpty(cmd.Argument) && int.TryParse(cmd.Argument, out var p) && p > 0) page = p;
                    cmd.Result = MediaController.Instance.GetQueuePage(page);
                    
                    return Task.CompletedTask;
                case "remove":
                    if (int.TryParse(cmd.Argument, out var idx))
                    {
                        MediaController.Instance.RemoveAt(idx);
                        cmd.Result = $"{idx}번 항목 삭제 요청";
                    }
                    else cmd.Result = "삭제할 인덱스가 필요합니다.";
                    return Task.CompletedTask;
                case "clear":
                    MediaController.Instance.ClearQueue();
                    cmd.Result = "대기열 전체 삭제 요청";
                    return Task.CompletedTask;
                case "jobs":
                    cmd.Result = "백그라운드 작업 목록 요청";
                    var jobs = JobTracker.Instance.ListJobs();
                    foreach (var j in jobs)
                    {
                        cmd.Result = $"- {j.Id}: {j.Description} (by: {j.Requester}, started: {j.StartedAt:u})";
                    }
                    return Task.CompletedTask;
                case "stats":
                    cmd.Result = "작업 통계 요청";
                    var completed = JobTracker.Instance.ListCompletedJobs();
                    foreach (var g in completed.GroupBy(x => x.Description))
                    {
                        var times = g.Where(x => x.DurationSeconds.HasValue).Select(x => x.DurationSeconds.Value).ToList();
                        if (times.Count == 0) continue;
                        var avg = TimeSpan.FromSeconds(times.Average()).TotalSeconds;
                        var max = TimeSpan.FromSeconds(times.Max()).TotalSeconds;
                        cmd.Result = $"{g.Key}: count={times.Count}, avg={avg:F1}s, max={max:F1}s";
                    }
                    return Task.CompletedTask;
                case "help":    
                    cmd.Result = "사용 가능한 명령어:\n!play <URL>\n!skip\n!queue\n!remove <번호>\n!clear\n!stop\n!jobs\n!stats\n!help";
                    return Task.CompletedTask;
                default:
                    cmd.Result = $"알 수 없는 명령: {cmd.Argument}";
                    return Task.CompletedTask;
            }
        }
    }
}
