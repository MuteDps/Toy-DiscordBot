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
                        var status = j.Task == null ? "unknown" : j.Task.Status.ToString();
                        var requester = string.IsNullOrEmpty(j.Requester) ? "<unknown>" : j.Requester;
                        outList.Add($"{j.Id}: {j.Description} (by: {requester}, started: {j.StartedAt:u}, status: {status})");
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
            // track enqueue jobs
            _jobs.StartJob(async () =>
            {
                await System.Threading.Tasks.Task.Run(() => EnqueueVlcUrl(url));
            }, $"Enqueue: {url}", requester);
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
                string playUrl = url;
                string audioUrl = null;
                string metaTitle = null;
                double? metaDuration = null;
                if (IsYoutubeUrl(url))
                {
                    // First try to detect playlist entries
                    var playlistUrls = await YtDlpHelper.GetYoutubePlaylistVideoUrlsAsync(url);
                    if (playlistUrls != null && playlistUrls.Count > 0)
                    {
                            // Only allow playlists with fewer than 100 items
                            if (playlistUrls.Count >= 100)
                            {
                                System.Console.WriteLine("플레이리스트는 100개 미만만 지원합니다.");
                                return;
                            }

                            // Ensure VLC running
                            var startedP = await _vlc.EnsureRunningAsync();
                            if (!startedP) _vlc.StartProcess();

                            // Process first item immediately so playback can start as soon as it's ready
                            string firstUrl = playlistUrls[0];
                            string firstPlayUrl = firstUrl;
                            try
                            {
                                var firstMeta = await YtDlpHelper.GetYoutubeMetadataAsync(firstUrl);
                                var firstStreams = await YtDlpHelper.GetYoutubeStreamUrlsAsync(firstUrl);
                                if (!string.IsNullOrWhiteSpace(firstStreams.videoUrl)) firstPlayUrl = firstStreams.videoUrl;
                                var firstId = await _vlc.EnqueueAsync(firstPlayUrl);
                                if (!string.IsNullOrEmpty(firstId) && (firstMeta.title != null || firstMeta.duration.HasValue))
                                    _metaById[firstId] = (firstMeta.title, firstMeta.duration);
                                var status = await _vlc.SendRequestAsync("/requests/status.xml");
                                if (string.IsNullOrEmpty(status) || status.Contains("stopped") || status.Contains("idle"))
                                {
                                    if (!string.IsNullOrEmpty(firstId)) await _vlc.PlayByIdAsync(firstId);
                                }
                            }
                            catch { }

                            // Enqueue remaining items as tracked jobs (one job per item for visibility)
                            for (int i = 1; i < playlistUrls.Count; i++)
                            {
                                var entryUrl = playlistUrls[i];
                                var idx = i + 1;
                                var descr = $"Playlist enqueue #{idx}: {entryUrl}";
                                string pjobId = null;
                                pjobId = _jobs.StartJob(async () =>
                                {
                                    try
                                    {
                                        _jobs.UpdateDescription(pjobId, $"Fetching metadata: {entryUrl}");
                                        var meta = await YtDlpHelper.GetYoutubeMetadataAsync(entryUrl);
                                        var streams = await YtDlpHelper.GetYoutubeStreamUrlsAsync(entryUrl);
                                        var entryPlayUrl = !string.IsNullOrWhiteSpace(streams.videoUrl) ? streams.videoUrl : entryUrl;
                                        if (!string.IsNullOrEmpty(meta.title))
                                            _jobs.UpdateDescription(pjobId, $"Enqueue: {meta.title}");
                                        var lastId = await _vlc.EnqueueAsync(entryPlayUrl);
                                        if (!string.IsNullOrEmpty(lastId) && (!string.IsNullOrEmpty(meta.title) || meta.duration.HasValue))
                                            _metaById[lastId] = (meta.title, meta.duration);
                                    }
                                    catch { _jobs.UpdateDescription(pjobId, "Failed"); }
                                }, descr, requester);
                            }
                            return;
                    }

                    // Not a playlist: single video handling
                    var metaSingle = await YtDlpHelper.GetYoutubeMetadataAsync(url);
                    metaTitle = metaSingle.title;
                    metaDuration = metaSingle.duration;
                    var yt = await YtDlpHelper.GetYoutubeStreamUrlsAsync(url);
                    if (!string.IsNullOrWhiteSpace(yt.videoUrl))
                    {
                        playUrl = yt.videoUrl;
                        audioUrl = yt.audioUrl;
                    }
                    else
                    {
                        System.Console.WriteLine("yt-dlp로 Youtube 스트림 URL 추출 실패");
                        return;
                    }
                }
                // If VLC is running and we just want to enqueue, use controller
                if (enqueueOnly && _vlc.IsRunning)
                {
                    var lastId = await _vlc.EnqueueAsync(playUrl);
                    if (!string.IsNullOrEmpty(lastId) && (!string.IsNullOrEmpty(metaTitle) || metaDuration.HasValue))
                        _metaById[lastId] = (metaTitle, metaDuration);
                    // if idle, try to play
                    var status = await _vlc.SendRequestAsync("/requests/status.xml");
                    if (string.IsNullOrEmpty(status) || status.Contains("stopped") || status.Contains("idle"))
                    {
                        if (!string.IsNullOrEmpty(lastId)) await _vlc.PlayByIdAsync(lastId);
                    }
                    return;
                }

                // Ensure VLC is running (start if needed) then start with URL
                var started = await _vlc.EnsureRunningAsync();
                if (!started)
                {
                    // fallback: start process directly
                    _vlc.StartProcess(playUrl, audioUrl);
                }
                else
                {
                    // if process started and we have playUrl, enqueue then play
                    var lastId = await _vlc.EnqueueAsync(playUrl);
                    if (!string.IsNullOrEmpty(lastId) && (!string.IsNullOrEmpty(metaTitle) || metaDuration.HasValue))
                        _metaById[lastId] = (metaTitle, metaDuration);
                    // if idle, play
                    var status = await _vlc.SendRequestAsync("/requests/status.xml");
                    if (string.IsNullOrEmpty(status) || status.Contains("stopped") || status.Contains("idle"))
                    {
                        if (!string.IsNullOrEmpty(lastId)) await _vlc.PlayByIdAsync(lastId);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[VLCCommandExecutor] 영상 재생 중 오류: {ex.Message}");
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
                    var status = j.Task == null ? "unknown" : j.Task.Status.ToString();
                    var requester = string.IsNullOrEmpty(j.Requester) ? "<unknown>" : j.Requester;
                    System.Console.WriteLine($"- {j.Id}: {j.Description} (by: {requester}, started: {j.StartedAt:u}, status: {status})");
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
            if (!string.IsNullOrEmpty(currentId))
            {
                var idx = ids.IndexOf(currentId);
                if (idx >= 0) startIndex = idx;
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
            System.Console.WriteLine("  - 플레이리스트 URL을 넣으면 첫 항목의 스트림이 준비되는 즉시 재생을 시작하고, 나머지 항목은 백그라운드에서 순차 처리됩니다.");
            System.Console.WriteLine("  - 플레이리스트 동기화(메타/스트림 추출)는 시간이 걸릴 수 있으니 !jobs로 진행 상태를 확인하세요.");
            System.Console.WriteLine("!skip - 현재 영상 건너뛰기");
            System.Console.WriteLine("!queue - 대기열 확인 (VLC의 현재/다음 항목만 표시)");
            System.Console.WriteLine("!remove <번호> - 대기열에서 특정 항목 삭제");
            System.Console.WriteLine("!clear - 대기열 전체 삭제");
            System.Console.WriteLine("!stop - 재생 중지");
            System.Console.WriteLine("!jobs - 백그라운드에서 진행 중인 작업 목록(메타/스트림 추출, 플레이리스트 항목 처리 등)");
            System.Console.WriteLine("!stats - 완료된 작업 통계(어떤 작업이 오래 걸리는지 확인 가능)");
            System.Console.WriteLine("!help - 도움말 (플레이리스트 동기화는 시간이 걸릴 수 있음)");
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

        private void EnqueueVlcUrl(string url, string title = null, double? duration = null, string requester = "Admin(CLI)")
        {
            // Create a tracked job for enqueue + metadata extraction
            var shortUrl = url.Length > 60 ? url.Substring(0, 57) + "..." : url;
            var jobDesc = $"Enqueue: {shortUrl}";
            string jobId = null;
            jobId = _jobs.StartJob(async () =>
            {
                try
                {
                    // update description to indicate metadata fetch
                    _jobs.UpdateDescription(jobId, $"Fetching metadata: {shortUrl}");
                    if (string.IsNullOrEmpty(title) && !duration.HasValue && IsYoutubeUrl(url))
                    {
                        try
                        {
                            var meta = await YtDlpHelper.GetYoutubeMetadataAsync(url);
                            title = meta.title;
                            duration = meta.duration;
                            if (!string.IsNullOrEmpty(title))
                                _jobs.UpdateDescription(jobId, $"Enqueue: {title}");
                        }
                        catch { }
                    }

                    _jobs.UpdateDescription(jobId, $"Resolving stream: {shortUrl}");
                    var streams = await YtDlpHelper.GetYoutubeStreamUrlsAsync(url);
                    var streamUrl = !string.IsNullOrWhiteSpace(streams.videoUrl) ? streams.videoUrl : url;
                    _jobs.UpdateDescription(jobId, $"Enqueuing: {(string.IsNullOrEmpty(title) ? shortUrl : title)}");
                    var lastId = await _vlc.EnqueueAsync(streamUrl);
                    if (!string.IsNullOrEmpty(lastId))
                    {
                        if (!string.IsNullOrEmpty(title) || duration.HasValue)
                        {
                            _metaById[lastId] = (title, duration);
                        }
                        var statusXml = await _vlc.SendRequestAsync("/requests/status.xml");
                        var sdoc = new System.Xml.XmlDocument();
                        sdoc.LoadXml(statusXml);
                        var state = sdoc.SelectSingleNode("//state")?.InnerText;
                        if (string.IsNullOrEmpty(state) || state == "stopped" || state == "idle")
                        {
                            await _vlc.PlayByIdAsync(lastId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"VLC 대기열 추가 실패: {ex.Message}");
                    _jobs.UpdateDescription(jobId, $"Failed: {ex.Message}");
                }
            }, jobDesc, requester);
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

    }
}
