using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace VizzyGPT.Core.Conversations
{
    public sealed class ConversationHistory
    {
        [JsonConstructor]
        public ConversationHistory(
            int schemaVersion,
            string conversationId,
            IReadOnlyList<ConversationMessage> messages)
        {
            if (schemaVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            }

            SchemaVersion = schemaVersion;
            ConversationId = ConversationStageTiming.RequireText(conversationId, nameof(conversationId));
            Messages = (messages ?? throw new ArgumentNullException(nameof(messages))).ToArray();
            if (Messages.Any(message => message == null))
            {
                throw new ArgumentException("Messages must not contain null entries.", nameof(messages));
            }
        }

        public int SchemaVersion { get; }

        public string ConversationId { get; }

        public IReadOnlyList<ConversationMessage> Messages { get; }
    }
}
