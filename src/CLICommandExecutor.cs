// CLICommandExecutor.cs
// CLI 명령 실행 구현 (데코레이터 패턴 기반)
using System;

namespace YoutubeTogether
{
    public class CLICommandExecutor : ICommandExecutor
    {
        private readonly ICommandExecutor _inner;

        public CLICommandExecutor(ICommandExecutor inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void Execute(string command, string argument = null)
        {
            switch (command.ToLower())
            {
                case "play":
                    Console.WriteLine($"CLI: 영상 재생: {argument}");
                    // mark CLI as requester — inner.Execute will start jobs with Admin(CLI) by default
                    _inner.Execute("play", argument);
                    break;
                case "stop":
                    Console.WriteLine("CLI: 영상 정지");
                    _inner.Execute("stop");
                    break;
                case "skip":
                    Console.WriteLine("CLI: 현재 영상 건너뛰기");
                    _inner.Execute("skip");
                    break;
                case "queue":
                    Console.WriteLine("CLI: 대기열 보기");
                    _inner.Execute("queue");
                    break;
                case "jobs":
                    Console.WriteLine("CLI: 백그라운드 작업 목록 요청");
                    _inner.Execute("jobs");
                    break;
                case "stats":
                    Console.WriteLine("CLI: 작업 통계 요청");
                    _inner.Execute("stats");
                    break;
                case "remove":
                    Console.WriteLine($"CLI: 대기열 항목 삭제: {argument}");
                    _inner.Execute("remove", argument);
                    break;
                case "clear":
                    Console.WriteLine("CLI: 대기열 전체 삭제");
                    _inner.Execute("clear");
                    break;
                case "help":
                    Console.WriteLine("CLI: 도움말 요청");
                    _inner.Execute("help");
                    break;
                default:
                    Console.WriteLine($"알 수 없는 명령: {command}");
                    break;
            }
        }
    }
}
