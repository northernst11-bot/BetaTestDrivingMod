using Game;
using Game.Rendering;
using Unity.Entities;
using UnityEngine;

namespace BetaTestDrivingMod
{
    public sealed partial class ReliableDriveCameraSystem : GameSystemBase
    {
        private bool m_CameraWasOverridden;

        protected override void OnUpdate()
        {
            CameraUpdateSystem cameraUpdate = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            if (!ReliableDriveRuntime.IsDriving || !ReliableDriveRuntime.DriverCamera)
            {
                RestoreCamera(cameraUpdate);
                return;
            }

            Camera camera = cameraUpdate.activeCamera != null ? cameraUpdate.activeCamera : Camera.main;
            if (camera == null)
                return;

            if (cameraUpdate.activeCameraController != null)
            {
                cameraUpdate.activeCameraController = null;
                m_CameraWasOverridden = true;
            }

            ReliableDriveRuntime.UpdateCamera(camera);
        }

        private void RestoreCamera(CameraUpdateSystem cameraUpdate)
        {
            if (!m_CameraWasOverridden || cameraUpdate == null)
                return;

            if (cameraUpdate.gamePlayController != null)
                cameraUpdate.activeCameraController = cameraUpdate.gamePlayController;

            m_CameraWasOverridden = false;
        }
    }
}
