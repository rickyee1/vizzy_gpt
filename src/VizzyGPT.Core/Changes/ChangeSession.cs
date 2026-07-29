using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VizzyGPT.Core.Patching;
using VizzyGPT.Core.Programs;
using VizzyGPT.Core.Validation;

namespace VizzyGPT.Core.Changes
{
    public sealed class ChangeSession
    {
        private ChangeSession(
            string baseXml,
            string baseHash,
            PatchDocument patch,
            string resultXml,
            string resultHash,
            IReadOnlyList<string> previewLines)
        {
            BaseXml = baseXml;
            BaseHash = baseHash;
            Patch = patch;
            ResultXml = resultXml;
            ResultHash = resultHash;
            PreviewLines = new ReadOnlyCollection<string>(previewLines.ToArray());
        }

        public string BaseXml { get; }

        public string BaseHash { get; }

        public PatchDocument Patch { get; }

        public string ResultXml { get; }

        public string ResultHash { get; }

        public IReadOnlyList<string> PreviewLines { get; }

        public static ChangeSession Create(
            VizzyProgramDocument baseDocument,
            PatchDocument patch,
            PatchResult result,
            ValidationReport validation)
        {
            if (baseDocument == null)
            {
                throw new ArgumentNullException(nameof(baseDocument));
            }

            if (patch == null)
            {
                throw new ArgumentNullException(nameof(patch));
            }

            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (validation == null)
            {
                throw new ArgumentNullException(nameof(validation));
            }

            if (!validation.IsValid)
            {
                throw new InvalidOperationException("A change session cannot be created from a validation report containing errors.");
            }

            if (result.Changes.Any(change => change == null))
            {
                throw new ArgumentException("Patch result changes cannot contain null entries.", nameof(result));
            }

            var baseXml = baseDocument.ToXml();
            var baseHash = VizzyProgramHash.Compute(baseDocument);
            if (!string.Equals(patch.BaseHash, baseHash, StringComparison.Ordinal))
            {
                throw new ArgumentException("Patch base hash does not match the base document.", nameof(patch));
            }

            var resultXml = result.Document.ToXml();
            var resultHash = VizzyProgramHash.Compute(result.Document);
            var expectedResult = VizzyPatchEngine.Apply(baseDocument, patch);
            if (!string.Equals(expectedResult.Document.ToXml(), resultXml, StringComparison.Ordinal) ||
                !expectedResult.Changes.SequenceEqual(result.Changes, StringComparer.Ordinal))
            {
                throw new ArgumentException("Patch result does not match applying the patch to the base document.", nameof(result));
            }

            var previewLines = result.Changes
                .Concat(validation.Warnings.Select(warning => warning.Message))
                .ToArray();
            return new ChangeSession(
                baseXml,
                baseHash,
                patch,
                resultXml,
                resultHash,
                previewLines);
        }
    }
}
