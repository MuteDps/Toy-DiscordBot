// ICommandExecutor.cs
// 명령 실행 인터페이스
namespace YoutubeTogether
{
    public interface ICommandExecutor
    {
        void Execute(string command, string argument = null);
    }
}
