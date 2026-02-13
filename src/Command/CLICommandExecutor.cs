// CLICommandExecutor.cs
// CLI 명령 실행 구현 (데코레이터 패턴 기반)
using System;
using YoutubeTogether.Hanlder;

namespace YoutubeTogether.Command
{
    public class CLICommandExecutor
    {
        public CLICommandExecutor()
        {
        }

        public void Execute(string command, string argument = null)
        {
            var job = JobTracker.Instance.IssueJob("CreateCLI", command, argument);

            job.OnComplete = new System.Threading.Tasks.Task(() =>
            {
                Console.WriteLine($"CLI: 명령 '{command}' 완료");
                if (job.Error != null)
                {
                    Console.WriteLine($"CLI: 명령 '{command}' 실행 중 오류 발생: {job.Error.Message}");
                }
            });

            Handler.Instance.DispatchCommand(job);
        }
    }
}
