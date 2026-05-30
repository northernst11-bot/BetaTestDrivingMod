using Game;
using UnityEngine;

namespace BetaTestDrivingMod
{
    public sealed partial class ReliableDriveInputSystem : GameSystemBase
    {
        protected override void OnUpdate()
        {
            ReliableDriveRuntime.SampleUnityInput();
        }
    }
}
