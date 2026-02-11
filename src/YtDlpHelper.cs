// YtDlpHelper.cs
// yt-dlp를 이용해 Youtube 영상/오디오 스트림 URL 추출
using System;
using System.Diagnostics;
using System.Linq;

namespace YoutubeTogether
{
    public static class YtDlpHelper
    {
        // yt-dlp -g <url> 명령 실행, 결과를 (video, audio) 튜플로 비동기 반환
        public static async System.Threading.Tasks.Task<(string videoUrl, string audioUrl)> GetYoutubeStreamUrlsAsync(string youtubeUrl)
        {
            var exe = System.IO.Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe");
            var file = System.IO.File.Exists(exe) ? exe : "yt-dlp";
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = $"-f \"bestvideo[height>=720]+bestaudio/best[height>=720]/best\" -g \"{youtubeUrl}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var process = Process.Start(psi))
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                var lines = output.Split('\n', '\r').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                if (lines.Count >= 2)
                    return (lines[0], lines[1]);
                else if (lines.Count == 1)
                    return (lines[0], null);
                else
                    return (null, null);
            }
        }

        // yt-dlp --flat-playlist -j <playlist_url> 으로 플레이리스트 항목들의 id/webpage_url을 한 줄씩 JSON으로 받아서
        // 각 entry의 웹페이지 URL을 반환합니다. 실패 시 빈 리스트 반환.
        public static async System.Threading.Tasks.Task<System.Collections.Generic.List<string>> GetYoutubePlaylistVideoUrlsAsync(string playlistUrl)
        {
            var result = new System.Collections.Generic.List<string>();
            try
            {
                var exe = System.IO.Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe");
                var file = System.IO.File.Exists(exe) ? exe : "yt-dlp";
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = $"--flat-playlist -j \"{playlistUrl}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var process = Process.Start(psi))
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    if (string.IsNullOrWhiteSpace(output)) return result;
                    var lines = output.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        try
                        {
                            using (var doc = System.Text.Json.JsonDocument.Parse(line))
                            {
                                var root = doc.RootElement;
                                string url = null;
                                if (root.TryGetProperty("webpage_url", out var w)) url = w.GetString();
                                else if (root.TryGetProperty("url", out var u)) url = u.GetString();
                                else if (root.TryGetProperty("id", out var id))
                                {
                                    var vid = id.GetString();
                                    if (!string.IsNullOrEmpty(vid)) url = $"https://www.youtube.com/watch?v={vid}";
                                }
                                if (!string.IsNullOrEmpty(url)) result.Add(url);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return result;
        }

        // yt-dlp -j <url> 명령 실행, 결과 JSON에서 title과 duration(초)을 추출
        public static async System.Threading.Tasks.Task<(string title, double? duration)> GetYoutubeMetadataAsync(string youtubeUrl)
        {
            try
            {
                var exe = System.IO.Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe");
                var file = System.IO.File.Exists(exe) ? exe : "yt-dlp";
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = $"-j \"{youtubeUrl}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var process = Process.Start(psi))
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    if (string.IsNullOrWhiteSpace(output)) return (null, null);
                    return ParseJsonMetadata(output);
                }
            }
            catch { return (null, null); }
        }

        private static (string title, double? duration) ParseJsonMetadata(string json)
        {
            try
            {
                using (var doc = System.Text.Json.JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    string title = null;
                    double? duration = null;
                    if (root.TryGetProperty("title", out var t)) title = t.GetString();
                    if (root.TryGetProperty("duration", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.Number)
                        duration = d.GetDouble();
                    return (title, duration);
                }
            }
            catch { return (null, null); }
        }

        public static async System.Threading.Tasks.Task GetYoutubePlaylistDetailsAsync(string playlistUrl, System.Action<string, string, string, double?> onEntryFound)
        {
            try
            {
                var exe = System.IO.Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe");
                var file = System.IO.File.Exists(exe) ? exe : "yt-dlp";
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    // Use -j for JSON output, -f for format selection. 
                    // This outputs one JSON object per playlist item as it is processed.
                    Arguments = $"-j -f \"bestvideo[height>=720]+bestaudio/best[height>=720]/best\" \"{playlistUrl}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var process = Process.Start(psi))
                {
                    while (!process.StandardOutput.EndOfStream)
                    {
                        var line = await process.StandardOutput.ReadLineAsync();
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            using (var doc = System.Text.Json.JsonDocument.Parse(line))
                            {
                                var root = doc.RootElement;
                                string title = null;
                                double? duration = null;
                                string videoUrl = null;
                                string audioUrl = null;

                                if (root.TryGetProperty("title", out var t)) title = t.GetString();
                                if (root.TryGetProperty("duration", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.Number)
                                    duration = d.GetDouble();

                                // For non-flat extraction, formats or direct url is available
                                if (root.TryGetProperty("url", out var u)) videoUrl = u.GetString();
                                
                                // Handling cases where requested format might result in multiple streams (video+audio)
                                // If the root 'url' is not the final direct stream, yt-dlp sometimes puts them in 'requested_formats'
                                if (root.TryGetProperty("requested_formats", out var rf) && rf.ValueKind == System.Text.Json.JsonValueKind.Array && rf.GetArrayLength() >= 2)
                                {
                                    videoUrl = rf[0].GetProperty("url").GetString();
                                    audioUrl = rf[1].GetProperty("url").GetString();
                                }

                                if (!string.IsNullOrEmpty(videoUrl))
                                    onEntryFound?.Invoke(videoUrl, audioUrl, title, duration);
                            }
                        }
                        catch { }
                    }
                    await process.WaitForExitAsync();
                }
            }
            catch { }
        }
    }
}
