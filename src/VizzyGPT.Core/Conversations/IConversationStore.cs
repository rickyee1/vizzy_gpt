using System.Threading;
using System.Threading.Tasks;

namespace VizzyGPT.Core.Conversations
{
    public interface IConversationStore
    {
        Task<ConversationHistory> LoadOrCreateAsync(
            string? programHash,
            CancellationToken cancellationToken = default);

        Task SaveAsync(
            ConversationHistory history,
            CancellationToken cancellationToken = default);

        Task LinkProgramHashAsync(
            string conversationId,
            string programHash,
            CancellationToken cancellationToken = default);

        Task ClearAsync(
            string conversationId,
            CancellationToken cancellationToken = default);
    }
}
