using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using VizzyGPT.Core.Changes;

namespace VizzyGPT.Core.Storage
{
    public sealed class FileDataStore : IDataStore
    {
        private const int BackupRetentionCount = 20;

        private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind
        };

        private readonly string rootDirectory;
        private readonly string rootPrefix;

        public FileDataStore(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("Data store root directory must be non-whitespace.", nameof(rootDirectory));
            }

            this.rootDirectory = Path.GetFullPath(rootDirectory);
            rootPrefix = this.rootDirectory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                this.rootDirectory.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? this.rootDirectory
                : this.rootDirectory + Path.DirectorySeparatorChar;
        }

        public async Task SaveBackupAsync(
            string programFingerprint,
            string xml,
            DateTime createdUtc,
            CancellationToken cancellationToken = default)
        {
            ValidateFingerprint(programFingerprint);
            if (xml == null)
            {
                throw new ArgumentNullException(nameof(xml));
            }

            if (createdUtc.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("Backup timestamp must be UTC.", nameof(createdUtc));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var fingerprintDirectory = SanitizeFingerprint(programFingerprint);
            if (string.Equals(fingerprintDirectory, ".", StringComparison.Ordinal) ||
                string.Equals(fingerprintDirectory, "..", StringComparison.Ordinal))
            {
                fingerprintDirectory = fingerprintDirectory.Replace('.', '_');
            }

            var backupDirectory = ContainedPath("Backups", fingerprintDirectory);
            var timestamp = createdUtc.ToString("yyyyMMdd'T'HHmmss.fffffff'Z'", CultureInfo.InvariantCulture);
            var fileName = timestamp + "-" + Guid.NewGuid().ToString("N") + ".xml";
            var backupPath = ContainedPath("Backups", fingerprintDirectory, fileName);

            await WriteAtomicAsync(backupPath, xml, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            RetainNewestBackups(backupDirectory);
        }

        public Task SavePendingAsync(PendingChange pending, CancellationToken cancellationToken = default)
        {
            if (pending == null)
            {
                throw new ArgumentNullException(nameof(pending));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var path = PendingPath(pending.ProgramFingerprint);
            var json = JsonConvert.SerializeObject(pending, Formatting.None, JsonSettings);
            return WriteAtomicAsync(path, json, cancellationToken);
        }

        public async Task<PendingChange?> LoadPendingAsync(
            string programFingerprint,
            CancellationToken cancellationToken = default)
        {
            ValidateFingerprint(programFingerprint);
            cancellationToken.ThrowIfCancellationRequested();
            var path = PendingPath(programFingerprint);
            if (!File.Exists(path))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(path, Utf8WithoutBom, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return JsonConvert.DeserializeObject<PendingChange>(json, JsonSettings)
                ?? throw new JsonSerializationException("Pending change JSON deserialized to null.");
        }

        public Task DeletePendingAsync(
            string programFingerprint,
            CancellationToken cancellationToken = default)
        {
            ValidateFingerprint(programFingerprint);
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(PendingPath(programFingerprint));
            return Task.CompletedTask;
        }

        private async Task WriteAtomicAsync(
            string targetPath,
            string contents,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.GetDirectoryName(targetPath)
                ?? throw new IOException("Data store target has no parent directory.");
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

        private static void RetainNewestBackups(string backupDirectory)
        {
            var backups = Directory.GetFiles(backupDirectory, "*.xml", SearchOption.TopDirectoryOnly)
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToArray();
            var deleteCount = backups.Length - BackupRetentionCount;
            for (var index = 0; index < deleteCount; index++)
            {
                File.Delete(backups[index]);
            }
        }

        private string PendingPath(string programFingerprint)
        {
            ValidateFingerprint(programFingerprint);
            return ContainedPath("Pending", SanitizeFingerprint(programFingerprint) + ".json");
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
                throw new ArgumentException("Data store path escaped the configured root directory.");
            }

            return fullPath;
        }

        private static string SanitizeFingerprint(string programFingerprint)
        {
            var invalidCharacters = new HashSet<char>(Path.GetInvalidFileNameChars());
            return new string(
                programFingerprint.Select(
                    character => invalidCharacters.Contains(character) ? '_' : character).ToArray());
        }

        private static void ValidateFingerprint(string programFingerprint)
        {
            if (string.IsNullOrWhiteSpace(programFingerprint))
            {
                throw new ArgumentException("Program fingerprint must be non-whitespace.", nameof(programFingerprint));
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
    }
}
