#nullable enable annotations

using VizzyGPT.Core.Validation;

namespace VizzyGPT.Runtime.Adapters
{
    public interface IVizzyRuntimeAdapter
    {
        bool IsEditorAvailable { get; }

        bool IsFlightAvailable { get; }

        bool TryGetEditorProgramXml(out string xml, out string error);

        bool TrySetEditorProgramXml(string xml, out string error);

        bool TryGetFlightProgramXml(out string xml, out string error);

        ValidationIssue? ValidateWithProgramSerializer(string xml);
    }
}
