// DiscordBot.cs
// Discord 봇의 기본 구조를 정의합니다.
using System;
using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;
using YoutubeTogether.Hanlder;

namespace YoutubeTogether.Command
{
    public class DiscordBot
    {
        private DiscordSocketClient _client;

        public DiscordBot()
        {
            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
            };
            _client = new DiscordSocketClient(config);
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

            var messages = message.Content.Split(" ");
            if(messages.Length < 1) 
                return;

            var job = JobTracker.Instance.IssueJob("CreateDiscord",
                messages[0], messages[1]);

            job.OnCompleted = result =>
            {
                if (result.IsFailed)
                {
                    _ = SendAndDeleteAsync(message, $"명령 처리 실패: {result.Error.Message}");
                    return;
                }

                _ = SendAndDeleteAsync(message, result.Result);
            };

            Hanlder.Handler.Instance.DispatchCommand(job);
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
