using System;
using System.Threading;
using System.Threading.Tasks;
using VizzyGPT.Core.Changes;

namespace VizzyGPT.Core.Storage
{
    public interface IDataStore
    {
        Task SaveNonSecretSettingsAsync(
            NonSecretSettings settings,
            CancellationToken cancellationToken = default);

        Task<NonSecretSettings?> LoadNonSecretSettingsAsync(
            CancellationToken cancellationToken = default);

        Task SaveProtectedApiKeyAsync(
            string protectedApiKey,
            CancellationToken cancellationToken = default);

        Task<string?> LoadProtectedApiKeyAsync(
            CancellationToken cancellationToken = default);

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
