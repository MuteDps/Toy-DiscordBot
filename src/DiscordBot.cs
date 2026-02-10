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
            _client = new DiscordSocketClient();
            _executor = new VLCCommandExecutor();
            _playback = _executor as IPlaybackController ?? new VLCCommandExecutor();
            _client.MessageReceived += OnMessageReceivedAsync;
        }

        public DiscordBot(ICommandExecutor executor, IPlaybackController playback = null)
        {
            _client = new DiscordSocketClient();
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
                    await message.Channel.SendMessageAsync($"대기열에 추가됨: {url} (요청자: {requester})");
                }
                catch (Exception ex)
                {
                    await message.Channel.SendMessageAsync($"대기열 추가 실패: {ex.Message}");
                }
            }
            else if (message.Content.StartsWith("!skip"))
            {
                Logger.Log("스킵 요청");
                try
                {
                    await _playback.SkipAsync();
                    await message.Channel.SendMessageAsync("현재 영상 건너뛰기 요청됨");
                }
                catch (Exception ex)
                {
                    await message.Channel.SendMessageAsync($"스킵 요청 실패: {ex.Message}");
                }
            }
            else if (message.Content.StartsWith("!queue"))
            {
                Logger.Log("대기열 보기 요청");
                try
                {
                    var list = await _playback.GetQueueAsync();
                    if (list != null && list.Count > 0)
                        await message.Channel.SendMessageAsync("대기열:\n" + string.Join("\n", list));
                    else
                        await message.Channel.SendMessageAsync("대기열이 비어 있습니다.");
                }
                catch (Exception ex)
                {
                    await message.Channel.SendMessageAsync($"대기열 조회 실패: {ex.Message}");
                }
            }
            else if (message.Content.StartsWith("!jobs"))
            {
                Logger.Log("백그라운드 작업 목록 요청");
                try
                {
                    var jobs = await _playback.GetJobsAsync();
                    if (jobs != null && jobs.Count > 0)
                        await message.Channel.SendMessageAsync("작업 목록:\n" + string.Join("\n", jobs));
                    else
                        await message.Channel.SendMessageAsync("실행 중인 작업이 없습니다.");
                }
                catch (Exception ex)
                {
                    await message.Channel.SendMessageAsync($"작업 목록 조회 실패: {ex.Message}");
                }
            }
            else if (message.Content.StartsWith("!stats"))
            {
                Logger.Log("작업 통계 요청");
                try
                {
                    var stats = await _playback.GetStatsAsync();
                    if (stats != null && stats.Count > 0)
                        await message.Channel.SendMessageAsync(string.Join("\n", stats));
                    else
                        await message.Channel.SendMessageAsync("통계 데이터가 없습니다.");
                }
                catch (Exception ex)
                {
                    await message.Channel.SendMessageAsync($"통계 조회 실패: {ex.Message}");
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
                        await message.Channel.SendMessageAsync(success ? $"{idx}번 항목 삭제됨" : "삭제 실패");
                    }
                    catch (Exception ex)
                    {
                        await message.Channel.SendMessageAsync($"삭제 실패: {ex.Message}");
                    }
                }
                else
                {
                    await message.Channel.SendMessageAsync("!remove <번호> 형식으로 입력하세요.");
                }
            }
            else if (message.Content.StartsWith("!clear"))
            {
                Logger.Log("대기열 전체 삭제 요청");
                try
                {
                    await _playback.ClearQueueAsync();
                    await message.Channel.SendMessageAsync("대기열 전체 삭제 요청 완료");
                }
                catch (Exception ex)
                {
                    await message.Channel.SendMessageAsync($"대기열 전체 삭제 실패: {ex.Message}");
                }
            }
            else if (message.Content.StartsWith("!stop"))
            {
                Logger.Log("영상 정지 요청");
                try
                {
                    await _playback.StopAsync();
                    await message.Channel.SendMessageAsync("VLC 영상 정지됨");
                }
                catch (Exception ex)
                {
                    await message.Channel.SendMessageAsync($"정지 실패: {ex.Message}");
                }
            }
            else if (message.Content.StartsWith("!help"))
            {
                await message.Channel.SendMessageAsync("사용 가능한 명령어:\n!play <YouTube URL>\n!skip\n!queue\n!remove <번호>\n!clear\n!stop\n!help");
            }
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
