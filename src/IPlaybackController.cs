// IPlaybackController.cs
using System.Collections.Generic;
using System.Threading.Tasks;

namespace YoutubeTogether
{
    public interface IPlaybackController
    {
        Task<List<string>> GetQueueAsync();
        Task<bool> RemoveAtAsync(int index);
        Task ClearQueueAsync();
        Task EnqueueAsync(string url);
        Task EnqueueAsync(string url, string requester);
        Task StopAsync();
        Task SkipAsync();
        Task<System.Collections.Generic.List<string>> GetJobsAsync();
        Task<System.Collections.Generic.List<string>> GetStatsAsync();
    }
}
