using System.Threading;
using System.Threading.Tasks;
using ZeniqaDownloadManager.Models;

namespace ZeniqaDownloadManager.Services
{
    public interface IDownloader
    {
        Task DownloadAsync(DownloadJob job, CancellationToken cancellationToken);
    }
} 