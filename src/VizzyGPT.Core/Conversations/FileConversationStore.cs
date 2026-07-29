using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace VizzyGPT.Core.Conversations
{
    public sealed class FileConversationStore : IConversationStore
    {
        private const int CurrentSchemaVersion = 1;
        private const int MessageRetentionCount = 50;
        private const string GeneralConversationId = "general";

        private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
            MissingMemberHandling = MissingMemberHandling.Error
        };

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> RootGates =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        private static readonly Regex AuthorizationHeaderPattern = new Regex(
            @"\bAuthorization\s*:\s*Bearer\s+\S+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex BearerCredentialPattern = new Regex(
            @"\bBearer\s+(?<credential>\S+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex ApiKeyPattern = new Regex(
            @"\bsk-[A-Za-z0-9_-]{8,}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly string rootDirectory;
        private readonly string rootPrefix;
        private readonly Action<string> warningSink;
        private readonly SemaphoreSlim gate;

        public FileConversationStore(string rootDirectory, Action<string>? warningSink = null)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException(
                    "Conversation store root directory must be non-whitespace.",
                    nameof(rootDirectory));
            }

            this.rootDirectory = new DirectoryInfo(Path.GetFullPath(rootDirectory)).FullName;
            rootPrefix = this.rootDirectory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                this.rootDirectory.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? this.rootDirectory
                : this.rootDirectory + Path.DirectorySeparatorChar;
            this.warningSink = warningSink ?? (_ => { });
            gate = RootGates.GetOrAdd(this.rootDirectory, _ => new SemaphoreSlim(1, 1));
        }

        public async Task<ConversationHistory> LoadOrCreateAsync(
            string? programHash,
            CancellationToken cancellationToken = default)
        {
            ValidateOptionalProgramHash(programHash);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (programHash == null)
                {
                    return await LoadHistoryOrEmptyAsync(
                            GeneralConversationId,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                var indexResult = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
                if (!indexResult.IsValid)
                {
                    return EmptyHistory(Guid.NewGuid().ToString("N"));
                }

                if (!indexResult.Index.Aliases.TryGetValue(programHash, out var conversationId))
                {
                    conversationId = Guid.NewGuid().ToString("N");
                    indexResult.Index.Aliases.Add(programHash, conversationId);
                    await SaveIndexAsync(indexResult.Index, cancellationToken).ConfigureAwait(false);
                }

                return await LoadHistoryOrEmptyAsync(conversationId, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task SaveAsync(
            ConversationHistory history,
            CancellationToken cancellationToken = default)
        {
            if (history == null)
            {
                throw new ArgumentNullException(nameof(history));
            }

            ValidateConversationId(history.ConversationId);
            cancellationToken.ThrowIfCancellationRequested();
            var bounded = new ConversationHistory(
                CurrentSchemaVersion,
                history.ConversationId,
                history.Messages
                    .Skip(Math.Max(0, history.Messages.Count - MessageRetentionCount))
                    .Select(SanitizeMessageForPersistence)
                    .ToArray());
            var json = JsonConvert.SerializeObject(bounded, Formatting.None, JsonSettings);

            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await WriteAtomicAsync(HistoryPath(history.ConversationId), json, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task LinkProgramHashAsync(
            string conversationId,
            string programHash,
            CancellationToken cancellationToken = default)
        {
            ValidateLinkableConversationId(conversationId);
            ValidateProgramHash(programHash);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var indexResult = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
                if (!indexResult.IsValid)
                {
                    return;
                }

                indexResult.Index.Aliases[programHash] = conversationId;
                await SaveIndexAsync(indexResult.Index, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task ClearAsync(
            string conversationId,
            CancellationToken cancellationToken = default)
        {
            ValidateConversationId(conversationId);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Delete(HistoryPath(conversationId));
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<ConversationHistory> LoadHistoryOrEmptyAsync(
            string conversationId,
            CancellationToken cancellationToken)
        {
            string path;
            try
            {
                path = HistoryPath(conversationId);
            }
            catch (ArgumentException)
            {
                WarnHistory();
                return EmptyHistory(Guid.NewGuid().ToString("N"));
            }

            if (!File.Exists(path))
            {
                return EmptyHistory(conversationId);
            }

            try
            {
                var json = await File.ReadAllTextAsync(path, Utf8WithoutBom, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var history = JsonConvert.DeserializeObject<ConversationHistory>(json, JsonSettings);
                if (history == null ||
                    history.SchemaVersion != CurrentSchemaVersion ||
                    !string.Equals(history.ConversationId, conversationId, StringComparison.Ordinal) ||
                    history.Messages.Count > MessageRetentionCount)
                {
                    WarnHistory();
                    return EmptyHistory(conversationId);
                }

                return history;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (IsRecoverableReadFailure(exception))
            {
                WarnHistory();
                return EmptyHistory(conversationId);
            }
        }

        private async Task<IndexLoadResult> LoadIndexAsync(CancellationToken cancellationToken)
        {
            var path = IndexPath();
            if (!File.Exists(path))
            {
                return new IndexLoadResult(new ConversationIndex(), isValid: true);
            }

            try
            {
                var json = await File.ReadAllTextAsync(path, Utf8WithoutBom, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var index = JsonConvert.DeserializeObject<ConversationIndex>(json, JsonSettings);
                if (index == null ||
                    index.SchemaVersion != CurrentSchemaVersion ||
                    index.Aliases == null ||
                    index.Aliases.Any(alias =>
                        string.IsNullOrWhiteSpace(alias.Key) ||
                        !IsValidConversationId(alias.Value, allowGeneral: false)))
                {
                    WarnIndex();
                    return new IndexLoadResult(new ConversationIndex(), isValid: false);
                }

                return new IndexLoadResult(index, isValid: true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (IsRecoverableReadFailure(exception))
            {
                WarnIndex();
                return new IndexLoadResult(new ConversationIndex(), isValid: false);
            }
        }

        private Task SaveIndexAsync(
            ConversationIndex index,
            CancellationToken cancellationToken)
        {
            var json = JsonConvert.SerializeObject(index, Formatting.None, JsonSettings);
            return WriteAtomicAsync(IndexPath(), json, cancellationToken);
        }

        private async Task WriteAtomicAsync(
            string targetPath,
            string contents,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.GetDirectoryName(targetPath)
                ?? throw new IOException("Conversation store target has no parent directory.");
            Directory.CreateDirectory(directory);

            var temporaryPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(
                        temporaryPath,
                        contents,
                        Utf8WithoutBom,
                        cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(targetPath))
                {
                    File.Replace(temporaryPath, targetPath, null);
                }
                else
                {
                    File.Move(temporaryPath, targetPath);
                }
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }

        private string IndexPath()
        {
            return ContainedPath("Conversations", "index.json");
        }

        private string HistoryPath(string conversationId)
        {
            ValidateConversationId(conversationId);
            var fileName = string.Equals(conversationId, GeneralConversationId, StringComparison.Ordinal)
                ? "general.json"
                : conversationId + ".json";
            return ContainedPath("Conversations", fileName);
        }

        private string ContainedPath(params string[] segments)
        {
            var path = rootDirectory;
            foreach (var segment in segments)
            {
                path = Path.Combine(path, segment);
            }

            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Conversation store path escaped its configured root.");
            }

            return fullPath;
        }

        private static ConversationHistory EmptyHistory(string conversationId)
        {
            return new ConversationHistory(
                CurrentSchemaVersion,
                conversationId,
                Array.Empty<ConversationMessage>());
        }

        private static ConversationMessage SanitizeMessageForPersistence(ConversationMessage message)
        {
            return new ConversationMessage(
                SanitizePersistedText(message.Id),
                message.Role,
                message.Kind,
                message.Mode,
                SanitizePersistedText(message.Text),
                message.ReasoningSummary == null
                    ? null
                    : SanitizePersistedText(message.ReasoningSummary),
                message.Stages
                    .Select(
                        stage => new ConversationStageTiming(
                            SanitizePersistedText(stage.Stage),
                            stage.ElapsedSeconds))
                    .ToArray(),
                message.ElapsedSeconds,
                message.Error == null ? null : SanitizeErrorForPersistence(message.Error),
                message.CreatedUtc);
        }

        private static ConversationError SanitizeErrorForPersistence(ConversationError error)
        {
            return new ConversationError(
                SanitizePersistedText(error.Code),
                SanitizePersistedText(error.Stage),
                SanitizePersistedText(error.Summary),
                SanitizePersistedText(error.TechnicalDetails),
                error.Path == null ? null : SanitizePersistedText(error.Path));
        }

        private static string SanitizePersistedText(string value)
        {
            if (IsCompleteProgramXml(value))
            {
                return "[redacted program XML]";
            }

            if (IsPatchOperationsPayload(value))
            {
                return "[redacted patch payload]";
            }

            var sanitized = AuthorizationHeaderPattern.Replace(value, "[redacted authorization]");
            sanitized = BearerCredentialPattern.Replace(
                sanitized,
                match => IsCredentialShaped(match.Groups["credential"].Value)
                    ? "[redacted authorization]"
                    : match.Value);
            return ApiKeyPattern.Replace(sanitized, "[redacted API key]");
        }

        private static bool IsCompleteProgramXml(string value)
        {
            try
            {
                var document = XDocument.Parse(value, LoadOptions.None);
                return string.Equals(
                    document.Root?.Name.LocalName,
                    "Program",
                    StringComparison.Ordinal);
            }
            catch (Exception exception) when (
                exception is System.Xml.XmlException ||
                exception is ArgumentException)
            {
                return false;
            }
        }

        private static bool IsPatchOperationsPayload(string value)
        {
            try
            {
                return ContainsOperationsProperty(JToken.Parse(value));
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool ContainsOperationsProperty(JToken token)
        {
            if (token is JObject jsonObject &&
                jsonObject.Properties().Any(
                    property => string.Equals(
                        property.Name,
                        "operations",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return token.Children().Any(ContainsOperationsProperty);
        }

        private static bool IsCredentialShaped(string value)
        {
            return value.Length >= 16 ||
                value.Any(char.IsDigit) ||
                value.IndexOfAny(new[] { '-', '_', '.', '=', '+', '/' }) >= 0;
        }

        private static bool IsRecoverableReadFailure(Exception exception)
        {
            return exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is JsonException ||
                exception is ArgumentException;
        }

        private static void ValidateOptionalProgramHash(string? programHash)
        {
            if (programHash != null)
            {
                ValidateProgramHash(programHash);
            }
        }

        private static void ValidateProgramHash(string programHash)
        {
            if (string.IsNullOrWhiteSpace(programHash))
            {
                throw new ArgumentException("Program hash must be non-whitespace.", nameof(programHash));
            }
        }

        private static void ValidateConversationId(string conversationId)
        {
            if (!IsValidConversationId(conversationId, allowGeneral: true))
            {
                throw new ArgumentException("Conversation ID is invalid.", nameof(conversationId));
            }
        }

        private static void ValidateLinkableConversationId(string conversationId)
        {
            if (!IsValidConversationId(conversationId, allowGeneral: false))
            {
                throw new ArgumentException(
                    "Only program conversation IDs can be linked to program hashes.",
                    nameof(conversationId));
            }
        }

        private static bool IsValidConversationId(string? conversationId, bool allowGeneral)
        {
            if (allowGeneral &&
                string.Equals(conversationId, GeneralConversationId, StringComparison.Ordinal))
            {
                return true;
            }

            return conversationId != null &&
                Guid.TryParseExact(conversationId, "N", out _);
        }

        private void WarnIndex()
        {
            TryWarn("Conversation index could not be read and was ignored.");
        }

        private void WarnHistory()
        {
            TryWarn("Conversation history could not be read and was ignored.");
        }

        private void TryWarn(string warning)
        {
            try
            {
                warningSink(warning);
            }
            catch (Exception)
            {
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private sealed class ConversationIndex
        {
            public ConversationIndex()
            {
                SchemaVersion = CurrentSchemaVersion;
                Aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            [JsonConstructor]
            public ConversationIndex(int schemaVersion, Dictionary<string, string> aliases)
            {
                SchemaVersion = schemaVersion;
                Aliases = aliases;
            }

            public int SchemaVersion { get; }

            public Dictionary<string, string> Aliases { get; }
        }

        private sealed class IndexLoadResult
        {
            public IndexLoadResult(ConversationIndex index, bool isValid)
            {
                Index = index;
                IsValid = isValid;
            }

            public ConversationIndex Index { get; }

            public bool IsValid { get; }
        }
    }
}
