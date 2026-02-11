// VLCCommandExecutor.cs
// VLC 제어 명령 실행 구현 (데코레이터 패턴 기반)
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace YoutubeTogether
{
    public class VLCCommandExecutor : ICommandExecutor, IPlaybackController
    {
        private readonly VlcController _vlc;
        private readonly JobTracker _jobs = new JobTracker();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string title, double? duration)> _metaById = new System.Collections.Concurrent.ConcurrentDictionary<string, (string, double?)>();

        public VLCCommandExecutor(VlcController vlc = null)
        {
            _vlc = vlc ?? new VlcController();
        }

        public void Execute(string command, string argument = null)
        {
            switch (command.ToLower())
            {
                case "play":
                    if (string.IsNullOrWhiteSpace(argument))
                    {
                        System.Console.WriteLine("!play <YouTube URL> 형식으로 입력하세요.");
                        break;
                    }
                    // Start play as tracked background job (CLI requester)
                    var jobId = _jobs.StartJob(async () => await PlayAsync(argument, true, true, "Admin(CLI)"), $"Play: {argument}", "Admin(CLI)");
                    System.Console.WriteLine($"VLC 대기열에 추가됨: {argument} (job {jobId})");
                    break;
                case "skip":
                    PlayNext();
                    break;
                case "queue":
                    ShowQueue();
                    break;
                case "jobs":
                    ShowJobs();
                    break;
                case "stats":
                    ShowStats();
                    break;
                case "remove":
                    RemoveFromQueue(argument);
                    break;
                case "clear":
                    ClearQueue();
                    break;
                case "stop":
                    StopVLC();
                    break;
                case "jump":
                    JumpToItem(argument);
                    break;
                case "help":
                    ShowHelp();
                    break;
                default:
                    System.Console.WriteLine("알 수 없는 명령입니다. !help를 입력하세요.");
                    break;
            }
        }

                // IPlaybackController implementation
        public async Task<List<string>> GetQueueAsync()
        {
            return await System.Threading.Tasks.Task.Run(() => GetVlcPlaylist());
        }

        public async Task<System.Collections.Generic.List<string>> GetJobsAsync()
        {
            return await System.Threading.Tasks.Task.Run(() =>
            {
                var outList = new System.Collections.Generic.List<string>();
                try
                {
                    var jobs = _jobs.ListJobs();
                    foreach (var j in jobs)
                    {
                        var status = j.CompletedAt.HasValue ? "Done" : "Running";
                        var requester = string.IsNullOrEmpty(j.Requester) ? "unknown" : j.Requester;
                        outList.Add($"- [{j.StartedAt:HH:mm:ss}] {j.Description} (by {requester}) [{status}]");
                    }
                }
                catch { }
                return outList;
            });
        }

        public async Task<System.Collections.Generic.List<string>> GetStatsAsync()
        {
            return await System.Threading.Tasks.Task.Run(() =>
            {
                var outList = new System.Collections.Generic.List<string>();
                try
                {
                    var completed = _jobs.ListCompletedJobs();
                    if (completed == null || completed.Count == 0)
                    {
                        outList.Add("통계 데이터가 없습니다.");
                        return outList;
                    }
                    // aggregate by description + requester so we can see who requested long jobs
                    var map = new System.Collections.Generic.Dictionary<string, (int count, double totalSeconds, double maxSeconds)>();
                    foreach (var j in completed)
                    {
                        var who = string.IsNullOrEmpty(j.Requester) ? "<unknown>" : j.Requester;
                        var key = $"{j.Description} (by: {who})";
                        var dur = j.DurationSeconds ?? 0.0;
                        if (!map.TryGetValue(key, out var v)) v = (0, 0.0, 0.0);
                        v.count += 1;
                        v.totalSeconds += dur;
                        if (dur > v.maxSeconds) v.maxSeconds = dur;
                        map[key] = v;
                    }
                    outList.Add("작업 통계(완료된 최근 작업)");
                    foreach (var kv in map)
                    {
                        var avg = kv.Value.count > 0 ? (kv.Value.totalSeconds / kv.Value.count) : 0.0;
                        outList.Add($"{kv.Key}: count={kv.Value.count}, avg={avg:F1}s, max={kv.Value.maxSeconds:F1}s");
                    }
                }
                catch { outList.Add("통계 집계 중 오류"); }
                return outList;
            });
        }

        public async Task<bool> RemoveAtAsync(int index)
        {
            return await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var ids = GetVlcPlaylistIds();
                    if (index < 0 || index >= ids.Count) return false;
                    var id = ids[index];
                    _vlc.DeleteByIdAsync(id).GetAwaiter().GetResult();
                    return true;
                }
                catch { return false; }
            });
        }

        public async Task ClearQueueAsync()
        {
            await System.Threading.Tasks.Task.Run(() => ClearVlcPlaylist());
        }

        public async Task EnqueueAsync(string url, string requester = "Admin(CLI)")
        {
            var shortUrl = url.Length > 60 ? url.Substring(0, 57) + "..." : url;
            // Run PlayAsync in a background job to avoid blocking the caller (e.g. Discord gateway)
            // and to ensure the resolution phase is visible in !jobs.
            _jobs.StartJob(async () =>
            {
                await PlayAsync(url, enqueueIfPlaying: true, enqueueOnly: true, requester: requester);
            }, $"Resolving: {shortUrl}", requester);
            await Task.CompletedTask;
        }

        // backward-compatible wrapper
        public async Task EnqueueAsync(string url)
        {
            await EnqueueAsync(url, "Admin(CLI)");
        }

        public async Task StopAsync()
        {
            await System.Threading.Tasks.Task.Run(() => StopVLC());
        }

        public async Task SkipAsync()
        {
            await System.Threading.Tasks.Task.Run(() => SendVlcHttpCommand("pl_next"));
        }

        private async System.Threading.Tasks.Task PlayAsync(string url, bool enqueueIfPlaying = true, bool enqueueOnly = false, string requester = "Admin(CLI)")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url)) return;

                // Ensure VLC running
                var startedVlc = await _vlc.EnsureRunningAsync();
                if (!startedVlc) _vlc.StartProcess();

                if (IsYoutubeUrl(url))
                {
                    // FAST check if it's a playlist (flat)
                    var playlistUrls = await YtDlpHelper.GetYoutubePlaylistVideoUrlsAsync(url);
                    if (playlistUrls != null && playlistUrls.Count > 1)
                    {
                        // Massive Optimization: Use a single yt-dlp process to resolve everything in the playlist.
                        string playlistJobId = null;
                        playlistJobId = _jobs.StartJob(async () =>
                        {
                            int processedCount = 0;
                            await YtDlpHelper.GetYoutubePlaylistDetailsAsync(url, async (vUrl, aUrl, title, duration) =>
                            {
                                processedCount++;
                                _jobs.UpdateDescription(playlistJobId, $"Playlist [{processedCount}] - Enqueuing: {(string.IsNullOrEmpty(title) ? "unknown" : title)}");

                                var lastId = await _vlc.EnqueueAsync(vUrl, aUrl);
                                if (!string.IsNullOrEmpty(lastId))
                                {
                                    if (!string.IsNullOrEmpty(title) || duration.HasValue)
                                        _metaById[lastId] = (title, duration);

                                    // Play if idle
                                    var statusXml = await _vlc.SendRequestAsync("/requests/status.xml");
                                    if (statusXml.Contains("stopped") || statusXml.Contains("idle"))
                                        await _vlc.PlayByIdAsync(lastId);
                                }
                            });
                             _jobs.UpdateDescription(playlistJobId, $"Playlist [{processedCount}] - Completed");
                        }, $"Playlist: {url}", requester);
                        return;
                    }
                    
                    // Single Video handling (or 1-item playlist)
                    var targetUrl = (playlistUrls != null && playlistUrls.Count == 1) ? playlistUrls[0] : url;
                    var meta = await YtDlpHelper.GetYoutubeMetadataAsync(targetUrl);
                    var streams = await YtDlpHelper.GetYoutubeStreamUrlsAsync(targetUrl);
                    string playUrl = !string.IsNullOrWhiteSpace(streams.videoUrl) ? streams.videoUrl : targetUrl;

                    var lastId = await _vlc.EnqueueAsync(playUrl, streams.audioUrl);
                    if (!string.IsNullOrEmpty(lastId))
                    {
                        if (!string.IsNullOrEmpty(meta.title) || meta.duration.HasValue)
                            _metaById[lastId] = (meta.title, meta.duration);
                        
                        var statusXml = await _vlc.SendRequestAsync("/requests/status.xml");
                        if (statusXml.Contains("stopped") || statusXml.Contains("idle"))
                            await _vlc.PlayByIdAsync(lastId);
                    }
                }
                else
                {
                    // Non-Youtube direct URL
                    var lastId = await _vlc.EnqueueAsync(url);
                    var statusXml = await _vlc.SendRequestAsync("/requests/status.xml");
                    if (statusXml.Contains("stopped") || statusXml.Contains("idle"))
                        await _vlc.PlayByIdAsync(lastId);
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[VLCCommandExecutor] PlayAsync 오류: {ex.Message}");
            }
        }

        private void PlayNext()
        {
            try
            {
                // VLC HTTP API로 next 명령 전송
                SendVlcHttpCommand("pl_next");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[VLCCommandExecutor] 다음 영상 재생 중 오류: {ex.Message}");
            }
        }

        private void ShowQueue()
        {
            try
            {
                var playlist = GetVlcPlaylist();
                if (playlist == null || playlist.Count == 0)
                {
                    System.Console.WriteLine("대기열이 비어 있습니다.");
                    return;
                }
                System.Console.WriteLine("VLC 대기열:");
                int idx = 1;
                foreach (var item in playlist)
                {
                    System.Console.WriteLine($"{idx++}. {item}");
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"대기열 조회 오류: {ex.Message}");
            }
        }

        private void ShowJobs()
        {
            try
            {
                var jobs = _jobs.ListJobs();
                if (jobs == null)
                {
                    System.Console.WriteLine("실행 중인 잡이 없습니다.");
                    return;
                }
                System.Console.WriteLine("실행 중인 백그라운드 작업:");
                foreach (var j in jobs)
                {
                    var status = j.CompletedAt.HasValue ? "Done" : "Running";
                    var requester = string.IsNullOrEmpty(j.Requester) ? "unknown" : j.Requester;
                    System.Console.WriteLine($"- [{j.StartedAt:HH:mm:ss}] {j.Description} (by {requester}) [{status}]");
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"잡 조회 오류: {ex.Message}");
            }
        }

        private void ShowStats()
        {
            try
            {
                var completed = _jobs.ListCompletedJobs();
                if (completed == null || completed.Count == 0)
                {
                    System.Console.WriteLine("통계 데이터가 없습니다.");
                    return;
                }
                // aggregate by description (exact)
                var map = new System.Collections.Generic.Dictionary<string, (int count, double totalSeconds, double maxSeconds)>();
                foreach (var j in completed)
                {
                    var key = j.Description ?? "<unknown>";
                    var dur = j.DurationSeconds ?? 0.0;
                    if (!map.TryGetValue(key, out var v)) v = (0, 0.0, 0.0);
                    v.count += 1;
                    v.totalSeconds += dur;
                    if (dur > v.maxSeconds) v.maxSeconds = dur;
                    map[key] = v;
                }
                System.Console.WriteLine("작업 통계(완료된 최근 작업)");
                foreach (var kv in map)
                {
                    var avg = kv.Value.count > 0 ? (kv.Value.totalSeconds / kv.Value.count) : 0.0;
                    System.Console.WriteLine($"- {kv.Key}: count={kv.Value.count}, avg={avg:F1}s, max={kv.Value.maxSeconds:F1}s");
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"통계 조회 오류: {ex.Message}");
            }
        }

        private void RemoveFromQueue(string arg)
        {
            if (string.IsNullOrWhiteSpace(arg) || !int.TryParse(arg, out int idx))
            {
                System.Console.WriteLine("!remove <번호> 형식으로 입력하세요.");
                return;
            }
            try
            {
                var playlist = GetVlcPlaylist();
                if (playlist == null || idx < 1 || idx > playlist.Count)
                {
                    System.Console.WriteLine("잘못된 번호입니다.");
                    return;
                }
                RemoveVlcPlaylistItem(idx - 1);
                System.Console.WriteLine($"{idx}번 항목 삭제 요청 완료");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"대기열 항목 삭제 오류: {ex.Message}");
            }
        }

        private void ClearQueue()
        {
            try
            {
                ClearVlcPlaylist();
                System.Console.WriteLine("대기열 전체 삭제 요청 완료");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"대기열 전체 삭제 오류: {ex.Message}");
            }
        }

        private void RemoveVlcPlaylistItem(int index)
        {
            var ids = GetVlcPlaylistIds();
            if (index < 0 || index >= ids.Count) return;
            var id = ids[index];
            _vlc.DeleteByIdAsync(id).GetAwaiter().GetResult();
        }

        private void ClearVlcPlaylist()
        {
            var ids = GetVlcPlaylistIds();
            foreach (var id in ids)
            {
                _vlc.DeleteByIdAsync(id).GetAwaiter().GetResult();
            }
        }

        private System.Collections.Generic.List<string> GetVlcPlaylistIds()
        {
            var result = new System.Collections.Generic.List<string>();
            try
            {
                result = _vlc.GetPlaylistIdsAsync().GetAwaiter().GetResult();
            }
            catch { }
            return result;
        }

        private bool IsVlcRunning()
        {
            return _vlc.IsRunning;
        }

        private void SendVlcHttpCommand(string command)
        {
            var url = $"/requests/status.xml?command={command}";
            _vlc.SendRequest(url);
        }

        private System.Collections.Generic.List<string> GetVlcPlaylist()
        {
            var result = new System.Collections.Generic.List<string>();
            var xml = _vlc.GetPlaylistXmlAsync().GetAwaiter().GetResult();
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(xml);
            var nodes = doc.SelectNodes("//leaf");

            // Build id->node map
            var nodeById = new System.Collections.Generic.Dictionary<string, System.Xml.XmlNode>();
            foreach (System.Xml.XmlNode node in nodes)
            {
                var id = node.Attributes["id"]?.Value;
                if (!string.IsNullOrEmpty(id) && !nodeById.ContainsKey(id)) nodeById[id] = node;
            }

            var ids = GetVlcPlaylistIds();
            var currentId = _vlc.GetCurrentPlayingIdAsync().GetAwaiter().GetResult();
            int startIndex = 0;
            if (!string.IsNullOrEmpty(currentId) && currentId != "-1")
            {
                var idx = ids.IndexOf(currentId);
                if (idx > 0)
                {
                    startIndex = idx;
                    // Background cleanup of finished items
                    CleanupPlayedItemsBackground(ids.GetRange(0, idx));
                }
                else if (idx == 0)
                {
                    startIndex = 0;
                }
            }

            for (int i = startIndex; i < ids.Count; i++)
            {
                var id = ids[i];
                if (!nodeById.TryGetValue(id, out var node)) continue;
                var name = node.Attributes["name"]?.Value;
                string display = null;
                if (!string.IsNullOrEmpty(id) && _metaById.TryGetValue(id, out var meta))
                {
                    var title = !string.IsNullOrEmpty(meta.title) ? meta.title : name;
                    var dur = meta.duration.HasValue ? FormatDuration(meta.duration.Value) : null;
                    display = dur != null ? $"{title} ({dur})" : title;
                }
                else
                {
                    display = name;
                }
                if (!string.IsNullOrEmpty(display)) result.Add(display);
            }
            return result;
        }

        private void JumpToItem(string arg)
        {
            if (string.IsNullOrWhiteSpace(arg) || !int.TryParse(arg, out int idx))
            {
                System.Console.WriteLine("!jump <번호> 형식으로 입력하세요.");
                return;
            }
            try
            {
                var ids = GetVlcPlaylistIds();
                if (idx < 1 || idx > ids.Count)
                {
                    System.Console.WriteLine("잘못된 번호입니다.");
                    return;
                }
                _vlc.PlayByIdAsync(ids[idx - 1]).GetAwaiter().GetResult();
                System.Console.WriteLine($"{idx}번 항목으로 점프 완료");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"점프 오류: {ex.Message}");
            }
        }

        private string FormatDuration(double seconds)
        {
            try
            {
                var ts = System.TimeSpan.FromSeconds(seconds);
                if (ts.Hours > 0)
                    return string.Format("{0:D2}:{1:D2}:{2:D2}", ts.Hours, ts.Minutes, ts.Seconds);
                else
                    return string.Format("{0:D2}:{1:D2}", ts.Minutes, ts.Seconds);
            }
            catch
            {
                return null;
            }
        }

        private void ShowHelp()
        {
            System.Console.WriteLine("사용 가능한 명령어:");
            System.Console.WriteLine("!play <YouTube URL> - 영상 재생 (송출PC 전체화면 자동 실행)");
            System.Console.WriteLine("!skip - 현재 영상 건너뛰기");
            System.Console.WriteLine("!jump <번호> - 특정 번호의 음악으로 즉시 이동 (대기열이 길 때 유용)");
            System.Console.WriteLine("!queue - 대기열 확인 (VLC의 현재/다음 항목만 표시, 전송후 메시지 자동 삭제)");
            System.Console.WriteLine("!remove <번호> - 대기열에서 특정 항목 삭제");
            System.Console.WriteLine("!clear - 대기열 전체 삭제");
            System.Console.WriteLine("!stop - 재생 중지");
            System.Console.WriteLine("!jobs - 백그라운드 작업 목록 확인");
            System.Console.WriteLine("!help - 도움말 확인");
            System.Console.WriteLine("\n* 참고: 디스코드 요청 및 결과 메시지는 30초 후 자동으로 삭제됩니다.");
        }

        private bool IsYoutubeUrl(string url)
        {
            return url != null && (url.Contains("youtube.com") || url.Contains("youtu.be"));
        }

        private void StartVLC(string url, string audioUrl = null, bool enqueueOnly = false, string title = null, double? duration = null, string requester = "Admin(CLI)")
        {
            // If enqueueOnly and VLC is running, use controller enqueue
            if (enqueueOnly && _vlc.IsRunning)
            {
                // tracked enqueue job
                _jobs.StartJob(async () =>
                {
                    try
                    {
                        var lastId = await _vlc.EnqueueAsync(url);
                        if (!string.IsNullOrEmpty(lastId) && (!string.IsNullOrEmpty(title) || duration.HasValue))
                            _metaById[lastId] = (title, duration);
                    }
                    catch { }
                }, $"Enqueue (startvlc): {url}", requester);
                return;
            }

            // Start VLC process with the provided URL
            _vlc.StartProcess(url, audioUrl);

            // try to map metadata asynchronously (tracked)
            if (!string.IsNullOrEmpty(title) || duration.HasValue)
            {
                _jobs.StartJob(async () =>
                {
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        try
                        {
                            await System.Threading.Tasks.Task.Delay(500);
                            var xml = await _vlc.GetPlaylistXmlAsync();
                            var doc = new System.Xml.XmlDocument();
                            doc.LoadXml(xml);
                            var nodes = doc.SelectNodes("//leaf");
                            foreach (System.Xml.XmlNode node in nodes)
                            {
                                var id = node.Attributes["id"]?.Value;
                                var name = node.Attributes["name"]?.Value;
                                var uriNode = node.SelectSingleNode("uri");
                                var uri = uriNode?.InnerText;
                                if (!string.IsNullOrEmpty(id))
                                {
                                    if ((!string.IsNullOrEmpty(title) && name != null && name.Contains(title)) || (!string.IsNullOrEmpty(uri) && uri.Contains(url)))
                                    {
                                        _metaById[id] = (title, duration);
                                        return;
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }, $"MapMeta: {title ?? url}", requester);
            }
        }

        private async Task DownloadAndEnqueueInternalAsync(string url, string title, double? duration, string requester, string jobId, string progressPrefix = "")
        {
            var shortUrl = url.Length > 60 ? url.Substring(0, 57) + "..." : url;
            try
            {
                if (jobId != null) _jobs.UpdateDescription(jobId, $"{progressPrefix}Fetching metadata: {shortUrl}");
                if (string.IsNullOrEmpty(title) && !duration.HasValue && IsYoutubeUrl(url))
                {
                    var meta = await YtDlpHelper.GetYoutubeMetadataAsync(url);
                    title = meta.title;
                    duration = meta.duration;
                }

                if (jobId != null) _jobs.UpdateDescription(jobId, $"{progressPrefix}Resolving stream: {(string.IsNullOrEmpty(title) ? shortUrl : title)}");
                var streams = await YtDlpHelper.GetYoutubeStreamUrlsAsync(url);
                var streamUrl = !string.IsNullOrWhiteSpace(streams.videoUrl) ? streams.videoUrl : url;

                if (jobId != null) _jobs.UpdateDescription(jobId, $"{progressPrefix}Enqueuing: {(string.IsNullOrEmpty(title) ? shortUrl : title)}");
                var lastId = await _vlc.EnqueueAsync(streamUrl, streams.audioUrl);
                if (!string.IsNullOrEmpty(lastId))
                {
                    if (!string.IsNullOrEmpty(title) || duration.HasValue)
                        _metaById[lastId] = (title, duration);

                    var statusXml = await _vlc.SendRequestAsync("/requests/status.xml");
                    if (statusXml.Contains("stopped") || statusXml.Contains("idle"))
                        await _vlc.PlayByIdAsync(lastId);
                }
            }
            catch (Exception ex)
            {
                if (jobId != null) _jobs.UpdateDescription(jobId, $"{progressPrefix}Failed: {ex.Message}");
                throw;
            }
        }

        private async System.Threading.Tasks.Task<System.Collections.Generic.List<string>> GetVlcPlaylistIdsAsync()
        {
            var result = new System.Collections.Generic.List<string>();
            try
            {
                result = await _vlc.GetPlaylistIdsAsync();
            }
            catch { }
            return result;
        }

        private void StopVLC()
        {
            // Stop via controller
            try { _vlc.Stop(); } catch { }
        }

        private void CleanupPlayedItemsBackground(System.Collections.Generic.List<string> idsToCleanup)
        {
            if (idsToCleanup == null || idsToCleanup.Count == 0) return;
            System.Threading.Tasks.Task.Run(async () =>
            {
                foreach (var id in idsToCleanup)
                {
                    try
                    {
                        await _vlc.DeleteByIdAsync(id);
                        _metaById.TryRemove(id, out _);
                    }
                    catch { }
                }
            });
        }

    }
}
