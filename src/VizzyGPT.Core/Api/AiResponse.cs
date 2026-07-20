using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VizzyGPT.Core.Patching;

namespace VizzyGPT.Core.Api
{
    public sealed class AiResponse
    {
        public AiResponse(
            string message,
            PatchDocument? patch,
            bool canApply,
            IReadOnlyList<string> diagnostics)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Patch = patch;
            CanApply = canApply;
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            if (diagnostics.Any(diagnostic => diagnostic == null))
            {
                throw new ArgumentException("Diagnostics cannot contain null entries.", nameof(diagnostics));
            }

            Diagnostics = new ReadOnlyCollection<string>(diagnostics.ToArray());
        }

        public string Message { get; }

        public PatchDocument? Patch { get; }

        public bool CanApply { get; }

        public IReadOnlyList<string> Diagnostics { get; }
    }
}
