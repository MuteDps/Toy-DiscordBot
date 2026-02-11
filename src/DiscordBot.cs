// DiscordBot.cs
// Discord 봇의 기본 구조를 정의합니다.
using System;
using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;
using YoutubeTogether;

namespace YoutubeTogether
{
    public class DiscordBot
    {
        private DiscordSocketClient _client;
        private ICommandExecutor _executor;
        private IPlaybackController _playback;

        public DiscordBot()
        {
            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
            };
            _client = new DiscordSocketClient(config);
            _executor = new VLCCommandExecutor();
            _playback = _executor as IPlaybackController ?? new VLCCommandExecutor();
            _client.MessageReceived += OnMessageReceivedAsync;
        }

        public DiscordBot(ICommandExecutor executor, IPlaybackController playback = null)
        {
            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
            };
            _client = new DiscordSocketClient(config);
            _executor = executor ?? new VLCCommandExecutor();
            _playback = playback ?? (_executor as IPlaybackController) ?? new VLCCommandExecutor();
            _client.MessageReceived += OnMessageReceivedAsync;
        }

        public async Task StartAsync(string token)
        {
            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();
        }

        private async Task OnMessageReceivedAsync(SocketMessage message)
        {
            Logger.Log($"명령 수신: {message.Content} (from {message.Author.Username})");
            if (message.Author.IsBot) return;

            if (message.Content.StartsWith("!play "))
            {
                string url = message.Content.Substring(6).Trim();
                Logger.Log($"영상 재생 요청: {url}");
                try
                {
                    var requester = message.Author?.Username ?? "DiscordUser";
                    await _playback.EnqueueAsync(url, requester);
                    await SendAndDeleteAsync(message, $"대기열에 추가됨: {url} (요청자: {requester})");
                }
                catch (Exception ex)
                {
                    await SendAndDeleteAsync(message, $"대기열 추가 실패: {ex.Message}");
                }
            }
            else if (message.Content.StartsWith("!skip"))
            {
                Logger.Log("스킵 요청");
                try
                {
                    await _playback.SkipAsync();
                    await SendAndDeleteAsync(message, "현재 영상 건너뛰기 요청됨");
                }
                catch (Exception ex)
                {
                    await SendAndDeleteAsync(message, $"스킵 요청 실패: {ex.Message}");
                }
            }
            else if (message.Content.StartsWith("!queue"))
            {
                Logger.Log("대기열 보기 요청");
                try
                {
                    var list = await _playback.GetQueueAsync();
                    if (list != null && list.Count > 0)
                    {
                        // If queue is too long, Discord might reject it (2000 chars limit). 
                        // We will truncate it and show the count.
                        var fullText = "대기열:\n" + string.Join("\n", list);
                        if (fullText.Length > 1900) fullText = fullText.Substring(0, 1890) + "\n... (대기열이 너무 길어 생략됨)";
                        await SendAndDeleteAsync(message, fullText);
                    }
                    else
                    {
                        await SendAndDeleteAsync(message, "대기열이 비어 있습니다.");
                    }
                }
                catch (Exception ex)
                {
                    await SendAndDeleteAsync(message, $"대기열 조회 실패: {ex.Message}");
                }
            }
            else if (message.Content.StartsWith("!jobs"))
            {
                Logger.Log("백그라운드 작업 목록 요청");
                try
                {
                    var jobs = await _playback.GetJobsAsync();
                    if (jobs != null && jobs.Count > 0)
                        await SendAndDeleteAsync(message, "작업 목록:\n" + string.Join("\n", jobs));
                    else
                        await SendAndDeleteAsync(message, "실행 중인 작업이 없습니다.");
                }
                catch (Exception ex)
                {
                    await SendAndDeleteAsync(message, $"작업 목록 조회 실패: {ex.Message}");
                }
            }
            else if (message.Content.StartsWith("!stats"))
            {
                Logger.Log("작업 통계 요청");
                try
                {
                    var stats = await _playback.GetStatsAsync();
                    if (stats != null && stats.Count > 0)
                        await SendAndDeleteAsync(message, string.Join("\n", stats));
                    else
                        await SendAndDeleteAsync(message, "통계 데이터가 없습니다.");
                }
                catch (Exception ex)
                {
                    await SendAndDeleteAsync(message, $"통계 조회 실패: {ex.Message}");
                }
            }
            else if (message.Content.StartsWith("!remove "))
            {
                var arg = message.Content.Substring(8).Trim();
                Logger.Log($"대기열 항목 삭제 요청: {arg}");
                if (int.TryParse(arg, out int idx))
                {
                    try
                    {
                        var success = await _playback.RemoveAtAsync(idx - 1);
                        await SendAndDeleteAsync(message, success ? $"{idx}번 항목 삭제됨" : "삭제 실패");
                    }
                    catch (Exception ex)
                    {
                        await SendAndDeleteAsync(message, $"삭제 실패: {ex.Message}");
                    }
                }
                else
                {
                    await SendAndDeleteAsync(message, "!remove <번호> 형식으로 입력하세요.");
                }
            }
            else if (message.Content.StartsWith("!jump "))
            {
                var arg = message.Content.Substring(6).Trim();
                Logger.Log($"점프 요청: {arg}");
                _executor.Execute("jump", arg);
                await SendAndDeleteAsync(message, $"{arg}번 항목으로 점프 요청됨");
            }
            else if (message.Content.StartsWith("!clear"))
            {
                Logger.Log("대기열 전체 삭제 요청");
                try
                {
                    await _playback.ClearQueueAsync();
                    await SendAndDeleteAsync(message, "대기열 전체 삭제 요청 완료");
                }
                catch (Exception ex)
                {
                    await SendAndDeleteAsync(message, $"대기열 전체 삭제 실패: {ex.Message}");
                }
            }
            else if (message.Content.StartsWith("!stop"))
            {
                Logger.Log("영상 정지 요청");
                try
                {
                    await _playback.StopAsync();
                    await SendAndDeleteAsync(message, "VLC 영상 정지됨");
                }
                catch (Exception ex)
                {
                    await SendAndDeleteAsync(message, $"정지 실패: {ex.Message}");
                }
            }
            else if (message.Content.StartsWith("!help"))
            {
                await SendAndDeleteAsync(message, "사용 가능한 명령어:\n!play <URL>\n!skip\n!jump <번호>\n!queue\n!remove <번호>\n!clear\n!stop\n!jobs\n!stats\n!help");
            }
        }

        private async Task SendAndDeleteAsync(SocketMessage original, string responseText, int delayMs = 30000)
        {
            try
            {
                var response = await original.Channel.SendMessageAsync(responseText);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(delayMs);
                        await response.DeleteAsync();
                        await original.DeleteAsync();
                    }
                    catch { /* Ignore if already deleted */ }
                });
            }
            catch (Exception ex) { Logger.Log($"메세지 전송/삭제 중 오류: {ex.Message}"); }
        }
        // DiscordBot no longer directly calls VLC HTTP — uses IPlaybackController

        public async Task StopAsync()
        {
            try
            {
                await _client.StopAsync();
                await _client.LogoutAsync();
            }
            catch { }
        }
    }
}
