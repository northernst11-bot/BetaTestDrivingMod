using System;
using Game;
using Game.Rendering;
using Unity.Entities;
using UnityEngine;

namespace BetaTestDrivingMod
{
    public sealed partial class DirectDriveCameraSystem : GameSystemBase
    {
        private const float kPositionSharpness = 9.5f;
        private const float kLookSharpness = 13f;
        private const float kMaxDeltaTime = 0.05f;

        private CameraUpdateSystem m_CameraUpdateSystem;
        private IGameCameraController m_PreviousController;
        private Entity m_Target = Entity.Null;
        private Vector3 m_Position;
        private Vector3 m_LookPoint;
        private bool m_Active;
        private bool m_ControllerCrashguardLogged;
        private bool m_FirstPoseLogged;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_CameraUpdateSystem = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
        }

        protected override void OnUpdate()
        {
            try
            {
                if (!DirectDriveRuntime.IsDriving ||
                    !DirectDriveRuntime.ChaseCameraEnabled ||
                    DirectDriveRuntime.PossessedEntity == Entity.Null ||
                    !EntityManager.Exists(DirectDriveRuntime.PossessedEntity))
                {
                    RestoreCamera("Chase camera standby");
                    return;
                }

                Camera camera = ResolveCamera();
                if (camera == null)
                {
                    DirectDriveRuntime.SetChaseCameraStatus("Chase camera waiting for game camera");
                    return;
                }

                Entity target = DirectDriveRuntime.PossessedEntity;
                if (!m_Active || m_Target != target)
                    ActivateCamera(camera, target);

                KeepSafeCameraController(target);
                ApplyChaseCamera(camera);
            }
            catch (Exception ex)
            {
                DirectDriveRuntime.SetChaseCameraStatus($"Chase camera error: {ex.GetType().Name}");
                Mod.log.Warn($"Direct Drive chase camera failed: {ex.GetType().Name}: {ex.Message}");
                RestoreCamera("Chase camera paused after safety guard");
            }
        }

        private Camera ResolveCamera()
        {
            if (m_CameraUpdateSystem != null && m_CameraUpdateSystem.activeCamera != null)
                return m_CameraUpdateSystem.activeCamera;

            Camera mainCamera = Camera.main;
            if (mainCamera != null && m_CameraUpdateSystem != null && m_CameraUpdateSystem.activeCamera == null)
                m_CameraUpdateSystem.activeCamera = mainCamera;

            return mainCamera;
        }

        private void ActivateCamera(Camera camera, Entity target)
        {
            m_Target = target;
            m_PreviousController = m_CameraUpdateSystem != null ? m_CameraUpdateSystem.activeCameraController : null;

            if (m_CameraUpdateSystem != null && m_CameraUpdateSystem.orbitCameraController != null)
            {
                m_CameraUpdateSystem.orbitCameraController.followedEntity = Entity.Null;
                if (m_PreviousController != null)
                    m_CameraUpdateSystem.orbitCameraController.TryMatchPosition(m_PreviousController);
            }

            KeepSafeCameraController(target);

            m_Position = camera.transform.position;
            m_LookPoint = DirectDriveRuntime.PosePosition + DirectDriveRuntime.PoseRotation * Vector3.forward * 8f;
            m_Active = true;
            DirectDriveRuntime.SetChaseCameraStatus("Chase camera attached after game camera");
            Mod.log.Info($"Direct Drive chase camera attached to {target} after CameraUpdateSystem.");
        }

        private void KeepSafeCameraController(Entity target)
        {
            if (m_CameraUpdateSystem == null)
                return;

            if (m_CameraUpdateSystem.orbitCameraController != null)
            {
                m_CameraUpdateSystem.orbitCameraController.followedEntity = Entity.Null;
                m_CameraUpdateSystem.activeCameraController = m_CameraUpdateSystem.orbitCameraController;
            }
            else if (m_CameraUpdateSystem.activeCameraController == null && m_CameraUpdateSystem.gamePlayController != null)
            {
                m_CameraUpdateSystem.activeCameraController = m_CameraUpdateSystem.gamePlayController;
            }

            if (!m_ControllerCrashguardLogged && m_CameraUpdateSystem.activeCameraController != null)
            {
                Mod.log.Info("Direct Drive chase camera keeping a live game camera controller while applying the custom chase pose.");
                m_ControllerCrashguardLogged = true;
            }
        }

        private void ApplyChaseCamera(Camera camera)
        {
            Vector3 carPosition = DirectDriveRuntime.PosePosition;
            Quaternion carRotation = DirectDriveRuntime.PoseRotation;
            Vector3 forward = Vector3.ProjectOnPlane(carRotation * Vector3.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            forward.Normalize();

            float speedBlend = Mathf.InverseLerp(18f, 75f, Mathf.Abs(DirectDriveRuntime.SpeedMph));
            float distance = DirectDriveRuntime.ChaseCameraDistance + speedBlend * 2.75f;
            float height = DirectDriveRuntime.ChaseCameraHeight + speedBlend * 0.75f;
            float lookAhead = DirectDriveRuntime.ChaseCameraLookAhead + speedBlend * 5f;

            Vector3 desiredLook = carPosition + Vector3.up * 1.15f + forward * lookAhead;
            Vector3 desiredPosition = carPosition - forward * distance + Vector3.up * height;

            float frameDelta = UnityEngine.Time.unscaledDeltaTime > 0f ? UnityEngine.Time.unscaledDeltaTime : UnityEngine.Time.deltaTime;
            float dt = Mathf.Min(frameDelta, kMaxDeltaTime);
            float posBlend = 1f - Mathf.Exp(-kPositionSharpness * dt);
            float lookBlend = 1f - Mathf.Exp(-kLookSharpness * dt);

            m_Position = Vector3.Lerp(m_Position, desiredPosition, posBlend);
            m_LookPoint = Vector3.Lerp(m_LookPoint, desiredLook, lookBlend);

            Vector3 viewDirection = m_LookPoint - m_Position;
            if (viewDirection.sqrMagnitude < 0.001f)
                viewDirection = forward;

            if (!IsFinite(m_Position) || !IsFinite(viewDirection))
                return;

            camera.transform.SetPositionAndRotation(m_Position, Quaternion.LookRotation(viewDirection.normalized, Vector3.up));
            if (!m_FirstPoseLogged)
            {
                Mod.log.Info("Direct Drive chase camera first custom pose applied.");
                m_FirstPoseLogged = true;
            }

            DirectDriveRuntime.SetChaseCameraStatus("Chase camera attached after game camera");
        }

        private static bool IsFinite(Vector3 value)
        {
            return !(float.IsNaN(value.x) || float.IsInfinity(value.x) ||
                     float.IsNaN(value.y) || float.IsInfinity(value.y) ||
                     float.IsNaN(value.z) || float.IsInfinity(value.z));
        }

        private void RestoreCamera(string status)
        {
            if (!m_Active)
            {
                DirectDriveRuntime.SetChaseCameraStatus(status);
                return;
            }

            if (m_CameraUpdateSystem != null)
            {
                if (m_CameraUpdateSystem.orbitCameraController != null &&
                    (m_Target == Entity.Null || m_CameraUpdateSystem.orbitCameraController.followedEntity == m_Target))
                {
                    m_CameraUpdateSystem.orbitCameraController.followedEntity = Entity.Null;
                }

                if (m_PreviousController != null)
                    m_CameraUpdateSystem.activeCameraController = m_PreviousController;
                else if (m_CameraUpdateSystem.gamePlayController != null)
                    m_CameraUpdateSystem.activeCameraController = m_CameraUpdateSystem.gamePlayController;
            }

            m_PreviousController = null;
            m_Target = Entity.Null;
            m_Active = false;
            DirectDriveRuntime.SetChaseCameraStatus(status);
        }
    }
}
