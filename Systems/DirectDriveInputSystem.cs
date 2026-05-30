using Game;

namespace BetaTestDrivingMod
{
    public sealed partial class DirectDriveInputSystem : GameSystemBase
    {
        protected override void OnUpdate()
        {
            DirectDriveRuntime.SampleUnityInput();
        }
    }
}
