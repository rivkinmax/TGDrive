using System.Threading;
using System.Threading.Tasks;

namespace TGDrive.Core;

public interface IStore
{
    Task<bool> SaveRemoteMessageAsync(ChannelRecord record, CancellationToken ct = default);
    Task<string?> GetCurrentVersionIdAsync(string fileId, CancellationToken ct = default);
    Task EnqueueAsync(string versionId, string path, string type, CancellationToken ct = default);
}
