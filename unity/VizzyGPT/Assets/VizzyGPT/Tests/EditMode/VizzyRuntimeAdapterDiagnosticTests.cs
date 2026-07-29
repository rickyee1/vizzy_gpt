using System;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using ModApi.Craft.Program;
using NUnit.Framework;
using UnityEngine;
using VizzyGPT.Runtime.Adapters;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class VizzyRuntimeAdapterDiagnosticTests
    {
        [Test]
        public void Adapter_exposes_an_internal_runtime_validation_constructor()
        {
            var constructor = typeof(VizzyRuntimeAdapter).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(Func<string, Exception>) },
                null);

            Assert.That(constructor, Is.Not.Null);
        }

        [Test]
        public void Serializer_validation_unwraps_runtime_exceptions_into_staged_diagnostics()
        {
            var adapter = CreateAdapter(_ => new TargetInvocationException(
                new InvalidOperationException("Bad style.")));

            var issue = adapter.ValidateWithProgramSerializer("<Program>");

            Assert.That(issue, Is.Not.Null);
            Assert.That(issue.Message, Does.StartWith("Program XML is not accepted by the current runtime: "));
            Assert.That(issue.Message, Does.Contain("Bad style."));
            Assert.That(issue.Message, Does.Contain("RuntimeValidation"));
            Assert.That(issue.Message, Does.Not.Contain("target of an invocation"));
        }

        [Test]
        public void Serialization_errors_use_unwrapped_sanitized_diagnostics()
        {
            var formatter = typeof(VizzyRuntimeAdapter).GetMethod(
                "FormatDiagnostic",
                BindingFlags.Static | BindingFlags.NonPublic);
            var wrapped = new TargetInvocationException(new InvalidOperationException(
                "Authorization: Basic editor-secret; Body: {\"input\":\"sensitive body\"}"));

            Assert.That(formatter, Is.Not.Null);

            var error = (string)formatter.Invoke(
                null,
                new object[]
                {
                    "Unable to serialize the flight program: ",
                    wrapped,
                    "FlightProgramSerialization"
                });

            Assert.That(error, Does.StartWith("Unable to serialize the flight program: "));
            Assert.That(error, Does.Contain("FlightProgramSerialization"));
            Assert.That(error, Does.Contain("[REDACTED STRUCTURED PAYLOAD]"));
            Assert.That(error, Does.Not.Contain("editor-secret"));
            Assert.That(error, Does.Not.Contain("sensitive body"));
            Assert.That(error, Does.Not.Contain("target of an invocation"));
        }

        [Test]
        public void Flight_program_xml_sanitizes_public_scene_resolution_failures()
        {
            var adapter = CreateAdapter(
                _ => null,
                () => throw new TargetInvocationException(new InvalidOperationException(
                    "Authorization=Basic public-secret; Body: plaintext public response")),
                () => null);

            var found = adapter.TryGetFlightProgramXml(out _, out var error);

            Assert.That(found, Is.False);
            Assert.That(error, Does.StartWith("Unable to access the public flight scene: "));
            Assert.That(error, Does.Contain("PublicFlightProgramAccess"));
            Assert.That(error, Does.Not.Contain("public-secret"));
            Assert.That(error, Does.Not.Contain("plaintext public response"));
            Assert.That(error, Does.Not.Contain("target of an invocation"));
        }

        [Test]
        public void Flight_program_xml_sanitizes_fallback_reflection_failures()
        {
            var adapter = CreateAdapter(
                _ => null,
                () => null,
                () => throw new TargetInvocationException(new InvalidOperationException(
                    "Authorization=Basic fallback-secret; Body: plaintext fallback response")));

            var found = adapter.TryGetFlightProgramXml(out _, out var error);

            Assert.That(found, Is.False);
            Assert.That(error, Does.StartWith("Unable to read the resolved flight program: "));
            Assert.That(error, Does.Contain("FallbackFlightProgramAccess"));
            Assert.That(error, Does.Not.Contain("fallback-secret"));
            Assert.That(error, Does.Not.Contain("plaintext fallback response"));
            Assert.That(error, Does.Not.Contain("target of an invocation"));
        }

        [Test]
        public void Editor_program_xml_sanitizes_serializer_failures_at_the_public_boundary()
        {
            var host = new GameObject("Diagnostic Serialization Editor");
            try
            {
                var editor = host.AddComponent<DiagnosticSerializationEditor>();
                editor.FlightProgram = ValidFlightProgram();
                var adapter = CreateAdapter(
                    _ => null,
                    () => null,
                    () => null,
                    _ => throw SerializationException("editor-serializer-secret"));
                SetEditorContract(adapter, editor);

                var found = adapter.TryGetEditorProgramXml(out _, out var error);

                Assert.That(found, Is.False);
                Assert.That(error, Does.StartWith("Unable to read the Vizzy editor program: "));
                Assert.That(error, Does.Contain("EditorProgramSerialization"));
                AssertThatSanitizedSerializationError(error, "editor-serializer-secret");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Flight_program_xml_sanitizes_serializer_failures_at_the_public_boundary()
        {
            var adapter = CreateAdapter(
                _ => null,
                ValidFlightProgram,
                () => null,
                _ => throw SerializationException("flight-serializer-secret"));

            var found = adapter.TryGetFlightProgramXml(out _, out var error);

            Assert.That(found, Is.False);
            Assert.That(error, Does.StartWith("Unable to serialize the flight program: "));
            Assert.That(error, Does.Contain("FlightProgramSerialization"));
            AssertThatSanitizedSerializationError(error, "flight-serializer-secret");
        }

        private static VizzyRuntimeAdapter CreateAdapter(Func<string, Exception> runtimeValidation)
        {
            var constructor = typeof(VizzyRuntimeAdapter).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(Func<string, Exception>) },
                null);

            return (VizzyRuntimeAdapter)constructor.Invoke(new object[] { runtimeValidation });
        }

        private static VizzyRuntimeAdapter CreateAdapter(
            Func<string, Exception> runtimeValidation,
            Func<FlightProgram> publicFlightProgram,
            Func<FlightProgram> fallbackFlightProgram)
        {
            var constructor = typeof(VizzyRuntimeAdapter).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[]
                {
                    typeof(Func<string, Exception>),
                    typeof(Func<FlightProgram>),
                    typeof(Func<FlightProgram>)
                },
                null);

            Assert.That(constructor, Is.Not.Null);
            return (VizzyRuntimeAdapter)constructor.Invoke(
                new object[] { runtimeValidation, publicFlightProgram, fallbackFlightProgram });
        }

        private static VizzyRuntimeAdapter CreateAdapter(
            Func<string, Exception> runtimeValidation,
            Func<FlightProgram> publicFlightProgram,
            Func<FlightProgram> fallbackFlightProgram,
            Func<FlightProgram, string> serializeFlightProgram)
        {
            var constructor = typeof(VizzyRuntimeAdapter)
                .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .SingleOrDefault(candidate => candidate.GetParameters().Length == 4);

            Assert.That(constructor, Is.Not.Null);
            return (VizzyRuntimeAdapter)constructor.Invoke(
                new object[] { runtimeValidation, publicFlightProgram, fallbackFlightProgram, serializeFlightProgram });
        }

        private static FlightProgram ValidFlightProgram()
        {
            return new ProgramSerializer().DeserializeFlightProgram(
                XElement.Parse("<Program><Variables /><Instructions /><Expressions /></Program>"));
        }

        private static Exception SerializationException(string secret)
        {
            return new TargetInvocationException(new InvalidOperationException(
                "Authorization=Basic " + secret + "; Body: plaintext serialization response"));
        }

        private static void AssertThatSanitizedSerializationError(string error, string secret)
        {
            Assert.That(error, Does.Not.Contain(secret));
            Assert.That(error, Does.Not.Contain("plaintext serialization response"));
            Assert.That(error, Does.Not.Contain("target of an invocation"));
        }

        private static void SetEditorContract(VizzyRuntimeAdapter adapter, DiagnosticSerializationEditor editor)
        {
            var contractType = typeof(VizzyRuntimeAdapter).Assembly.GetType(
                "VizzyGPT.Runtime.Adapters.RuntimeContract");
            var constructor = contractType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(UnityEngine.Object), typeof(MemberInfo), typeof(object), typeof(MethodInfo) },
                null);
            var contract = constructor.Invoke(
                new object[]
                {
                    editor,
                    typeof(DiagnosticSerializationEditor).GetProperty("FlightProgram"),
                    editor,
                    typeof(DiagnosticSerializationEditor).GetMethod("RefreshUI")
                });
            var field = typeof(VizzyRuntimeAdapter).GetField(
                "editorContract",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(contractType, Is.Not.Null);
            Assert.That(constructor, Is.Not.Null);
            Assert.That(field, Is.Not.Null);
            field.SetValue(adapter, contract);
        }

        public sealed class DiagnosticSerializationEditor : MonoBehaviour
        {
            public FlightProgram FlightProgram { get; set; }

            public void RefreshUI()
            {
            }
        }
    }
}
