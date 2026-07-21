#nullable enable

using ModApi.Ui;
using NUnit.Framework;
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
    }
}
