using System;
using VizzyGPT.Core.Patching;
using VizzyGPT.Core.Programs;

namespace VizzyGPT.Core.Changes
{
    public enum RebaseStatus
    {
        Unchanged,
        Rebased,
        Conflict
    }

    public sealed class RebaseResult
    {
        private RebaseResult(RebaseStatus status, PatchDocument? patch, VizzyProgramDocument? document)
        {
            Status = status;
            Patch = patch;
            Document = document;
        }

        public RebaseStatus Status { get; }

        public PatchDocument? Patch { get; }

        public VizzyProgramDocument? Document { get; }

        internal static RebaseResult Unchanged(PatchDocument patch, VizzyProgramDocument document)
        {
            return new RebaseResult(RebaseStatus.Unchanged, patch, document);
        }

        internal static RebaseResult Rebased(PatchDocument patch, VizzyProgramDocument document)
        {
            return new RebaseResult(RebaseStatus.Rebased, patch, document);
        }

        internal static RebaseResult Conflict()
        {
            return new RebaseResult(RebaseStatus.Conflict, null, null);
        }
    }

    public sealed class PendingChangeRebaser
    {
        public RebaseResult TryRebase(PendingChange pending, VizzyProgramDocument current)
        {
            if (pending == null)
            {
                throw new ArgumentNullException(nameof(pending));
            }

            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            try
            {
                var currentHash = VizzyProgramHash.Compute(current);
                var storedPatch = PatchDocument.Deserialize(pending.PatchJson);
                if (!string.Equals(storedPatch.BaseHash, pending.BaseHash, StringComparison.Ordinal))
                {
                    return RebaseResult.Conflict();
                }

                if (string.Equals(currentHash, pending.BaseHash, StringComparison.Ordinal))
                {
                    var storedResult = VizzyProgramDocument.Parse(pending.ResultXml);
                    if (!string.Equals(VizzyProgramHash.Compute(storedResult), pending.ResultHash, StringComparison.Ordinal))
                    {
                        return RebaseResult.Conflict();
                    }

                    return RebaseResult.Unchanged(storedPatch, storedResult);
                }

                foreach (var target in pending.TargetFingerprints)
                {
                    var selected = ChangeFingerprintUtilities.ResolveSelector(current, target.Selector);
                    if (!string.Equals(
                            ChangeFingerprintUtilities.ComputeHash(selected),
                            target.Hash,
                            StringComparison.Ordinal))
                    {
                        return RebaseResult.Conflict();
                    }
                }

                foreach (var declaration in pending.DeclarationFingerprints)
                {
                    var selected = ChangeFingerprintUtilities.ResolveDeclaration(
                        current,
                        declaration.Kind,
                        declaration.Name);
                    if (!string.Equals(
                            ChangeFingerprintUtilities.ComputeHash(selected),
                            declaration.Hash,
                            StringComparison.Ordinal))
                    {
                        return RebaseResult.Conflict();
                    }
                }

                var rebasedPatch = new PatchDocument(currentHash, storedPatch.Summary, storedPatch.Operations);
                var result = VizzyPatchEngine.Apply(current, rebasedPatch);
                return RebaseResult.Rebased(rebasedPatch, result.Document);
            }
            catch (PatchApplyException)
            {
                return RebaseResult.Conflict();
            }
            catch (InvalidOperationException)
            {
                return RebaseResult.Conflict();
            }
            catch (ArgumentException)
            {
                return RebaseResult.Conflict();
            }
        }
    }
}
