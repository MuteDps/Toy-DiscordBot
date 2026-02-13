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

            job.OnCompleted = result =>
            {
                if (result.IsFailed)
                {
                    Logger.Log ($"명령 처리 실패: {result.Error.Message}");
                    return;
                }

                Logger.Log(result.Result);
            };

            Handler.Instance.DispatchCommand(job);
        }
    }
}
