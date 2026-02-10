// Logger.cs
// 간단한 파일 및 콘솔 로그 클래스
using System;
using System.IO;

namespace YoutubeTogether
{
    public static class Logger
    {
        private static readonly string LogFile = "log.txt";

        public static void Log(string message)
        {
            string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            Console.WriteLine(logEntry);
            File.AppendAllText(LogFile, logEntry + Environment.NewLine);
        }
    }
}
