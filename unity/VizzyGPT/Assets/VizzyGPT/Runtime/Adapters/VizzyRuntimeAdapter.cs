#nullable enable annotations

using System;
using System.Reflection;
using System.Xml.Linq;
using ModApi;
using ModApi.Craft.Program;
using UnityEngine;
using VizzyGPT.Core.Diagnostics;
using VizzyGPT.Core.Validation;

namespace VizzyGPT.Runtime.Adapters
{
    public sealed class VizzyRuntimeAdapter : IVizzyRuntimeAdapter
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly ProgramSerializer serializer = new ProgramSerializer();
        private readonly RuntimeContractProbe probe = new RuntimeContractProbe(ResolvePrivateRefreshContract);
        private readonly Func<string, Exception?> runtimeValidation;
        private RuntimeContract editorContract;
        private RuntimeCompatibilityResult editorCompatibility;

        public VizzyRuntimeAdapter()
        {
            runtimeValidation = ValidateRuntimeProgram;
        }

        internal VizzyRuntimeAdapter(Func<string, Exception?> runtimeValidation)
        {
            this.runtimeValidation = runtimeValidation ?? throw new ArgumentNullException(nameof(runtimeValidation));
        }

        public bool IsEditorAvailable => GetEditorContract() != null;

        public bool IsFlightAvailable => TryGetFlightProgram(out _, out _);

        public RuntimeCompatibilityResult Compatibility
        {
            get
            {
                GetEditorContract();
                return editorCompatibility ?? RuntimeCompatibilityResult.EditorUnavailable();
            }
        }

        public bool TryGetEditorProgramXml(out string xml, out string error)
        {
            xml = null;
            error = null;
            var contract = GetEditorContract();
            if (contract == null)
            {
                error = "Vizzy editor is not available in the current scene.";
                return false;
            }

            try
            {
                xml = Serialize(ReadFlightProgram(contract.Instance, contract.FlightProgramMember));
                return true;
            }
            catch (Exception exception)
            {
                error = FormatDiagnostic(
                    "Unable to read the Vizzy editor program: ",
                    exception,
                    "EditorProgramSerialization");
                return false;
            }
        }

        public bool TrySetEditorProgramXml(string xml, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(xml))
            {
                error = "Program XML is required.";
                return false;
            }

            FlightProgram replacement;
            try
            {
                replacement = serializer.DeserializeFlightProgram(XElement.Parse(xml));
            }
            catch (Exception exception)
            {
                error = FormatDiagnostic(
                    "Program XML is not accepted by the current runtime: ",
                    exception,
                    "EditorProgramSet");
                return false;
            }

            var contract = GetEditorContract();
            if (contract == null)
            {
                error = "Vizzy editor is not available in the current scene.";
                return false;
            }

            if (!CanWrite(contract.FlightProgramMember))
            {
                error = "Vizzy editor FlightProgram member is read-only.";
                return false;
            }

            FlightProgram previous = null;
            try
            {
                previous = ReadFlightProgram(contract.Instance, contract.FlightProgramMember);
                WriteFlightProgram(contract.Instance, contract.FlightProgramMember, replacement);
                RefreshEditor(contract);
                return true;
            }
            catch (Exception exception)
            {
                try
                {
                    if (previous != null)
                    {
                        WriteFlightProgram(contract.Instance, contract.FlightProgramMember, previous);
                        RefreshEditor(contract);
                    }
                }
                catch (Exception rollbackException)
                {
                    error = FormatDiagnostic(
                        "Vizzy editor refresh failed and rollback failed: ",
                        rollbackException,
                        "EditorRefreshRollback");
                    return false;
                }

                error = FormatDiagnostic(
                    "Vizzy editor refresh failed; the previous program was restored: ",
                    exception,
                    "EditorRefresh");
                return false;
            }
        }

        public bool TryGetFlightProgramXml(out string xml, out string error)
        {
            xml = null;
            if (!TryGetFlightProgram(out var flightProgram, out error))
            {
                return false;
            }

            try
            {
                xml = Serialize(flightProgram);
                return true;
            }
            catch (Exception exception)
            {
                error = FormatDiagnostic(
                    "Unable to serialize the flight program: ",
                    exception,
                    "FlightProgramSerialization");
                return false;
            }
        }

        public ValidationIssue? ValidateWithProgramSerializer(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return new ValidationIssue(ValidationSeverity.Error, "RuntimeSerializer", "Program XML is required.");
            }

            var exception = runtimeValidation(xml);
            if (exception != null)
            {
                var diagnostic = ExceptionDiagnostic.From(exception, "RuntimeValidation");
                return new ValidationIssue(
                    ValidationSeverity.Error,
                    "RuntimeSerializer",
                    "Program XML is not accepted by the current runtime: [" + diagnostic.Stage + "] " +
                        diagnostic.DisplayMessage);
            }

            return null;
        }

        private Exception? ValidateRuntimeProgram(string xml)
        {
            try
            {
                serializer.DeserializeFlightProgram(XElement.Parse(xml));
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static string FormatDiagnostic(string friendlyPrefix, Exception exception, string stage)
        {
            var diagnostic = ExceptionDiagnostic.From(exception, stage);
            return friendlyPrefix + "[" + diagnostic.Stage + "] " + diagnostic.DisplayMessage;
        }

        private RuntimeContract GetEditorContract()
        {
            if (editorContract == null || editorContract.Instance == null)
            {
                editorContract = probe.ProbeEditor(out editorCompatibility);
            }

            return editorContract;
        }

        private bool TryGetFlightProgram(out FlightProgram program, out string error)
        {
            program = null;
            error = null;

            try
            {
                var game = ModApi.Common.Game.Instance;
                var flightScene = game == null ? null : game.FlightScene;
                var craftScript = flightScene?.CraftNode?.CraftScript;
                if (RuntimeContractProbe.TryFindFlightProgramOnCraft(craftScript, out program))
                {
                    return true;
                }

                if (flightScene != null && RuntimeContractProbe.TryFindFlightProgramMember(flightScene.GetType(), out var member))
                {
                    program = ReadFlightProgram(flightScene, member);
                    return true;
                }
            }
            catch (Exception exception)
            {
                error = "Unable to access the public flight scene: " + exception.Message;
                return false;
            }

            if (probe.TryFindFlightProgramFallback(out var instance, out var fallbackMember))
            {
                try
                {
                    program = ReadFlightProgram(instance, fallbackMember);
                    return true;
                }
                catch (Exception exception)
                {
                    error = "Unable to read the resolved flight program: " + exception.Message;
                    return false;
                }
            }

            error = "Flight program is not available in the current scene.";
            return false;
        }

        private string Serialize(FlightProgram program)
        {
            if (program == null)
            {
                throw new InvalidOperationException("The runtime did not provide a FlightProgram.");
            }

            return serializer.SerializeFlightProgram(program).ToString(SaveOptions.DisableFormatting);
        }

        private static FlightProgram ReadFlightProgram(object instance, MemberInfo member)
        {
            var value = member is PropertyInfo property
                ? property.GetValue(instance, null)
                : ((FieldInfo)member).GetValue(instance);
            return value as FlightProgram ?? throw new InvalidOperationException("Resolved FlightProgram member returned null.");
        }

        private static bool CanWrite(MemberInfo member)
        {
            return member is PropertyInfo property ? property.CanWrite : !((FieldInfo)member).IsInitOnly;
        }

        private static void WriteFlightProgram(object instance, MemberInfo member, FlightProgram program)
        {
            if (member is PropertyInfo property)
            {
                property.SetValue(instance, program, null);
                return;
            }

            ((FieldInfo)member).SetValue(instance, program);
        }

        private static void RefreshEditor(RuntimeContract contract)
        {
            contract.RefreshMethod.Invoke(contract.RefreshTarget, null);
        }

        private static RefreshContract ResolvePrivateRefreshContract(UnityEngine.Object editor)
        {
            try
            {
                var controller = editor.GetType()
                    .GetField("_controller", PrivateInstance)
                    ?.GetValue(editor);
                if (controller == null)
                {
                    return null;
                }

                var method = RuntimeContractProbe.FindPublicRefreshMethod(controller.GetType());
                return method == null ? null : new RefreshContract(controller, method);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
