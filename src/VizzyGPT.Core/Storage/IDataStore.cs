using System;
using System.Threading;
using System.Threading.Tasks;
using VizzyGPT.Core.Changes;

namespace VizzyGPT.Core.Storage
{
    public interface IDataStore
    {
        Task SaveBackupAsync(
            string programFingerprint,
            string xml,
            DateTime createdUtc,
            CancellationToken cancellationToken = default);

        Task SavePendingAsync(PendingChange pending, CancellationToken cancellationToken = default);

        Task<PendingChange?> LoadPendingAsync(
            string programFingerprint,
            CancellationToken cancellationToken = default);

        Task DeletePendingAsync(string programFingerprint, CancellationToken cancellationToken = default);
    }
}
