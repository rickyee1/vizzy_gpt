#nullable enable

using ModApi.Ui;
using NUnit.Framework;
using System.Threading.Tasks;
using UnityEngine;
using VizzyGPT.Runtime;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class VizzyGptUiLifecycleTests
    {
        [Test]
        public void Staged_panel_mount_is_limited_to_vizzy_and_flight_scene_ui_ids()
        {
            Assert.That(VizzyGptBehaviour.IsSupportedUserInterfaceId(UserInterfaceIds.Vizzy), Is.True);
            Assert.That(
                VizzyGptBehaviour.IsSupportedUserInterfaceId(UserInterfaceIds.Flight.FlightSceneUI),
                Is.True);
            Assert.That(VizzyGptBehaviour.IsSupportedUserInterfaceId("VizzyGPT.UnrelatedUi"), Is.False);
        }

        [Test]
        public void Panel_mount_uses_the_loaded_ui_root_instead_of_the_global_fallback()
        {
            var loadedUiRoot = new GameObject("Flight UI Root");
            var fallbackRoot = new GameObject("Global UI Root");
            try
            {
                Assert.That(
                    VizzyGptBehaviour.ResolvePanelParent(loadedUiRoot, fallbackRoot.transform),
                    Is.SameAs(loadedUiRoot.transform));
                Assert.That(
                    VizzyGptBehaviour.ResolvePanelParent(null, fallbackRoot.transform),
                    Is.SameAs(fallbackRoot.transform));
            }
            finally
            {
                Object.DestroyImmediate(loadedUiRoot);
                Object.DestroyImmediate(fallbackRoot);
            }
        }

        [Test]
        public void Runtime_pending_fingerprint_is_derived_consistently_from_the_program_hash()
        {
            Assert.That(
                VizzyGptBehaviour.ResolveProgramFingerprint("abc123"),
                Is.EqualTo("program-abc123"));
        }

        [Test]
        public void Pending_restore_waits_until_the_editor_program_is_initialized()
        {
            var reads = 0;

            var ready = VizzyGptBehaviour.WaitForEditorProgramAsync(
                () => ++reads >= 3,
                () => Task.CompletedTask,
                3).GetAwaiter().GetResult();

            Assert.That(ready, Is.True);
            Assert.That(reads, Is.EqualTo(3));
        }
    }
}
