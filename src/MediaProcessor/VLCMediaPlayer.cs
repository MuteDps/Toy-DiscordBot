using LibVLCSharp.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YoutubeTogether.MediaProcessor
{
    public class VlcMediaController
    {
        private LibVLC _libVlc;
        private MediaPlayer _mediaPlayer;

        // 플레이리스트 (영상 정보 보관용)
        private List<VideoItem> _playlist = new List<VideoItem>();

        // 현재 재생 중인 인덱스
        private int _currentIndex = -1;

        // yt-dlp 실행 파일 경로 (실행 파일과 같은 폴더에 있다고 가정)
        private const string YtDlpPath = "yt-dlp.exe";

        public bool IsPlaying => _mediaPlayer.IsPlaying;

        private const int ItemsPerPage = 10;

        public VlcMediaController()
        {
            // LibVLC 초기화
            Core.Initialize();
            _libVlc = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVlc);

            // 현재 영상이 끝나면 자동으로 다음 곡 재생 (비동기 처리)
            _mediaPlayer.EndReached += (s, e) =>
            {
                Task.Run(() => Next());
            };
        }

        // ════════════════════════════════════════════════════════════
        // 1. 영상/플레이리스트 추가 (핵심 기능)
        // ════════════════════════════════════════════════════════════
        /// <summary>
        /// 유튜브 URL(단일 또는 플레이리스트)을 대기열에 추가합니다.
        /// 플레이리스트인 경우 개별 영상으로 분해하여 추가합니다.
        /// </summary>
        public async Task<int> AddAsync(string url)
        {
            if (!File.Exists(YtDlpPath))
            {
                Console.WriteLine($"[오류] {YtDlpPath}를 찾을 수 없습니다.");
                return 0;
            }

            Console.WriteLine("[정보] 메타데이터 분석 중...");

            // yt-dlp를 사용해 메타데이터(URL, 제목)만 빠르게 추출 (--flat-playlist)
            // 다운로드하지 않으므로 매우 빠름
            var items = await ExtractMetadataAsync(url);

            if (items.Count == 0)
            {
                Console.WriteLine("[오류] 영상을 찾을 수 없습니다.");
                return 0;
            }

            lock (_playlist)
            {
                _playlist.AddRange(items);
            }

            Console.WriteLine($"[추가됨] {items.Count}개의 영상이 대기열에 들어갔습니다.");

            // 재생 중이 아니고, 리스트가 방금 채워졌다면(첫 곡) 자동 시작
            if (!_mediaPlayer.IsPlaying && _currentIndex == -1)
            {
                // UI 스레드 차단 방지용
                _ = Task.Run(() => PlayIndex(0));
            }

            return items.Count;
        }

        // ════════════════════════════════════════════════════════════
        // 2. 재생 제어 (재생, 일시정지, 건너뛰기, 정지)
        // ════════════════════════════════════════════════════════════
        public void PlayPause()
        {
            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Pause();
            }
            else
            {
                if (_mediaPlayer.State == VLCState.Paused)
                {
                    _mediaPlayer.Play();
                }
                else if (_currentIndex >= 0 && _currentIndex < _playlist.Count)
                {
                    // 멈춰있던 상태라면 현재 곡 다시 로드
                    PlayIndex(_currentIndex);
                }
            }
        }

        public void Next(int skipCount = 1)
        {
            lock (_playlist)
            {
                if (_playlist.Count == 0) return;

                // 현재 위치에서 skipCount만큼 더함
                int nextIndex = _currentIndex + skipCount;

                // 유효한 범위인지 확인
                if (nextIndex < _playlist.Count)
                {
                    Console.WriteLine($"[스킵] {skipCount}곡을 건너뜁니다. ({_currentIndex} -> {nextIndex})");

                    // 내부에서 PlayIndex를 호출하면 비동기로 재생됨 (UI 블로킹 없음)
                    // PlayIndex 내부에서 _currentIndex = nextIndex 로 갱신됨
                    // Task.Run으로 감싸서 호출 (VLC 스레드 안전성 확보)
                    Task.Run(() => PlayIndex(nextIndex));
                }
                else
                {
                    // 범위를 벗어나면 재생 종료
                    Stop();
                    _currentIndex = -1; // 리셋
                    Console.WriteLine("[정보] 재생목록의 끝에 도달하여 정지했습니다.");
                }
            }
        }

        public void Stop()
        {
            _mediaPlayer.Stop();
        }

        // ════════════════════════════════════════════════════════════
        // 3. 플레이리스트 관리 (조회, 삭제, 전체삭제)
        // ════════════════════════════════════════════════════════════
        public List<string> GetPlaylist()
        {
            var result = new List<string>();
            lock (_playlist)
            {
                for (int i = 0; i < _playlist.Count; i++)
                {
                    string marker = (i == _currentIndex) ? "▶ " : "  ";
                    // 유튜브 플레이리스트를 넣었을 때 분해된 개별 영상들이 여기서 출력됨
                    result.Add($"{marker}[{i}] {_playlist[i].Title}");
                }
            }
            return result;
        }

        public void RemoveAt(int index)
        {
            lock (_playlist)
            {
                if (index < 0 || index >= _playlist.Count) return;

                var removed = _playlist[index];
                _playlist.RemoveAt(index);
                Console.WriteLine($"[삭제] {removed.Title}");

                // 인덱스 보정 로직
                if (index < _currentIndex)
                {
                    _currentIndex--;
                }
                else if (index == _currentIndex)
                {
                    // 현재 재생 중인 곡을 지웠다면 다음 곡 재생
                    if (_currentIndex < _playlist.Count)
                    {
                        PlayIndex(_currentIndex);
                    }
                    else
                    {
                        Stop();
                        _currentIndex = -1;
                    }
                }
            }
        }

        public void Clear()
        {
            Stop();
            lock (_playlist)
            {
                _playlist.Clear();
            }
            _currentIndex = -1;
        }

        // ════════════════════════════════════════════════════════════
        // 4. 내부 로직 (JIT 변환 및 yt-dlp 실행)
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 특정 인덱스의 영상을 재생 (재생 직전에 주소 변환 수행)
        /// </summary>
        private async void PlayIndex(int index)
        {
            VideoItem item;
            lock (_playlist)
            {
                if (index < 0 || index >= _playlist.Count) return;
                _currentIndex = index;
                item = _playlist[index];
            }

            Console.WriteLine($"[준비] '{item.Title}' 고화질 주소 추출 중...");

            // ★ 핵심: 비디오 URL과 오디오 URL을 따로 받아옵니다.
            var (videoUrl, audioUrl) = await GetStreamUrlsAsync(item.Url);

            if (string.IsNullOrEmpty(videoUrl))
            {
                Console.WriteLine($"[오류] 재생 실패 (스킵): {item.Title}");
                Next();
                return;
            }

            // VLC 미디어 생성
            using (var media = new Media(_libVlc, new Uri(videoUrl)))
            {
                // ★ 핵심: 오디오 트랙이 별도로 존재하면 VLC에 'input-slave'로 붙입니다.
                // 이렇게 해야 1080p 영상과 고음질 오디오가 합쳐져서 재생됩니다.
                if (!string.IsNullOrEmpty(audioUrl))
                {
                    media.AddOption(":input-slave=" + audioUrl);
                }

                _mediaPlayer.Play(media);
            }
            Console.WriteLine($"[재생] {item.Title} (1080p/Best)");
        }


        /// <summary>
        /// yt-dlp를 사용해 --flat-playlist 옵션으로 메타데이터만 빠르게 가져옵니다.
        /// </summary>
        private async Task<List<VideoItem>> ExtractMetadataAsync(string url)
        {
            var results = new List<VideoItem>();
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = YtDlpPath,
                    // --flat-playlist: 영상 다운로드 안 함, 리스트만 가져옴 (빠름)
                    // --print: 원하는 포맷으로 출력 (제목 | URL)
                    Arguments = $"--flat-playlist --print \"%(title)s | %(url)s\" \"{url}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using (var process = Process.Start(startInfo))
                {
                    while (!process.StandardOutput.EndOfStream)
                    {
                        string line = await process.StandardOutput.ReadLineAsync();
                        if (!string.IsNullOrWhiteSpace(line) && line.Contains("|"))
                        {
                            var parts = line.Split(new[] { '|' }, 2);
                            if (parts.Length == 2)
                            {
                                results.Add(new VideoItem
                                {
                                    Title = parts[0].Trim(),
                                    Url = parts[1].Trim()
                                });
                            }
                        }
                    }
                    await process.WaitForExitAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[오류] 메타데이터 추출 실패: {ex.Message}");
            }
            return results;
        }

        /// <summary>
        /// 재생 직전에 실제 스트리밍 URL(googlevideo.com/...)을 가져옵니다.
        /// </summary>
        /// <summary>
        /// yt-dlp를 사용해 비디오(최대 1080p)와 오디오 URL을 각각 가져옵니다.
        /// </summary>
        /// <returns>(VideoUrl, AudioUrl)</returns>
        private async Task<(string videoUrl, string audioUrl)> GetStreamUrlsAsync(string url)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = YtDlpPath,
                    // 포맷 지정 설명:
                    // bestvideo[height<=1080]+bestaudio : 1080p 이하 최고화질 비디오 + 최고음질 오디오 (분리됨)
                    // / best[height<=1080] : 만약 분리된 게 없으면 1080p 이하 합본
                    // / best : 그것도 없으면 그냥 됨
                    // --print urls : 추출된 URL들을 줄바꿈으로 출력
                    Arguments = $"-f \"bestvideo[height<=1080]+bestaudio/best[height<=1080]/best\" --print urls \"{url}\"",

                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8 // 한글 깨짐 방지
                };

                using (var process = Process.Start(startInfo))
                {
                    string output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    // yt-dlp는 URL을 줄바꿈(\n)으로 구분해서 뱉습니다.
                    // 보통 [0]번째 줄이 비디오, [1]번째 줄이 오디오입니다.
                    var lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                    if (lines.Length >= 2)
                    {
                        // 영상과 오디오가 분리된 경우 (1080p 이상은 보통 이 케이스)
                        return (lines[0], lines[1]);
                    }
                    else if (lines.Length == 1)
                    {
                        // 720p 이하 합본인 경우 (URL이 하나만 옴)
                        return (lines[0], null);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[오류] URL 추출 실패: {ex.Message}");
            }

            return (null, null);
        }

        /// <summary>
        /// 플레이리스트의 특정 페이지를 문자열로 반환합니다.
        /// </summary>
        /// <param name="page">요청할 페이지 번호 (1부터 시작)</param>
        /// <returns>(출력 문자열, 전체 페이지 수)</returns>
        public (string message, int totalPages) GetQueuePage(int page = 0)
        {
            lock (_playlist)
            {
                int totalItems = _playlist.Count;

                // 리스트가 비어있을 때
                if (totalItems == 0)
                {
                    return ("📭 재생목록이 비어있습니다.", 0);
                }

                // 전체 페이지 수 계산
                int totalPages = (int)Math.Ceiling((double)totalItems / ItemsPerPage);

                // 페이지 범위 보정 (1보다 작으면 1, 최대 페이지보다 크면 최대 페이지)
                if (page < 1) page = 1;
                if (page > totalPages) page = totalPages;

                // 가져올 범위 계산
                int startIndex = (page - 1) * ItemsPerPage;
                int endIndex = Math.Min(startIndex + ItemsPerPage, totalItems);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"**📋 재생목록 (페이지 {page}/{totalPages})**");
                sb.AppendLine($"전체: {totalItems}곡");
                sb.AppendLine("```"); // 코드 블록 시작 (가독성 UP)

                for (int i = startIndex; i < endIndex; i++)
                {
                    // 현재 재생 중인 곡인지 확인
                    string marker = (i == _currentIndex) ? "▶" : " ";
                    string title = _playlist[i].Title;

                    // 제목이 너무 길면 자르기 (혹시 모를 도배 방지)
                    if (title.Length > 50)
                        title = title.Substring(0, 47) + "...";

                    // 포맷: ▶ [1] 노래 제목
                    sb.AppendLine($"{marker} [{i}] {title}");
                }

                sb.AppendLine("```"); // 코드 블록 끝

                // 현재 재생 정보 추가
                if (_currentIndex >= 0 && _currentIndex < _playlist.Count)
                {
                    sb.AppendLine($"**🎵 현재 재생 중:** {_playlist[_currentIndex].Title}");
                }

                return (sb.ToString(), totalPages);
            }
        }

        /// <summary>
        /// 현재 재생 중인 영상의 정보를 반환합니다.
        /// </summary>
        /// <returns>(제목, URL, 재생 시간) 튜플</returns>
        public (string title, string url, TimeSpan currentTime, TimeSpan totalTime) GetCurrentTrack()
        {
            lock (_playlist)
            {
                // 재생 중이 아닐 때
                if (_currentIndex < 0 || _currentIndex >= _playlist.Count)
                {
                    return (null, null, TimeSpan.Zero, TimeSpan.Zero);
                }

                var currentItem = _playlist[_currentIndex];

                // 현재 재생 시간 (밀리초 -> TimeSpan 변환)
                var currentTime = TimeSpan.FromMilliseconds(_mediaPlayer.Time);

                // 총 재생 시간 (밀리초 -> TimeSpan 변환)
                var totalTime = TimeSpan.FromMilliseconds(_mediaPlayer.Length);

                return (currentItem.Title, currentItem.Url, currentTime, totalTime);
            }
        }
        // 내부 데이터 클래스
        private class VideoItem
        {
            public string Title { get; set; }
            public string Url { get; set; }
        }
    }
}