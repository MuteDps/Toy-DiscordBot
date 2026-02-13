// Program.cs
// 메인 엔트리 포인트
using System;
using System.Threading.Tasks;
using YoutubeTogether.Command;

namespace YoutubeTogether
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var cli = new CLICommandExecutor();
            var bot = new DiscordBot();

            string token = "디스코드 토큰 봇"; // 실제 토큰으로 교체

            // Discord 봇을 백그라운드로 시작
            var botTask = Task.Run(async () =>
            {
                try
                {
                    await bot.StartAsync(token);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Discord bot 시작 오류: {ex.Message}");
                }
            });

            // CLI 입력 루프 (메인 스레드)
            Console.WriteLine("CLI 모드 시작. 명령어를 입력하세요 (예: !play <YouTube URL>, !skip, !queue, !remove <번호>, !clear, !stop, !help). 종료는 exit 입력");
            while (true)
            {
                var input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input)) continue;
                if (input.Trim().ToLower() == "exit") break;
                // 접두사 '!' 제거 허용
                if (input.StartsWith("!")) input = input.Substring(1);
                var parts = input.Split(' ', 2);
                var command = parts[0];
                var argument = parts.Length > 1 ? parts[1] : null;
                try
                {
                    cli.Execute(command, argument);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"명령 실행 중 오류: {ex.Message}");
                }
            }

            // 종료 시 Discord 봇 정리
            try { await bot.StopAsync(); } catch { }
            // 기다려서 백그라운드 작업이 끝나도록 함
            try { await botTask; } catch { }
        }
    }
}
