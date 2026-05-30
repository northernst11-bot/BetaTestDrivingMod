using Game;
using Game.Common;
using Game.Objects;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ObjectTransform = Game.Objects.Transform;

namespace BetaTestDrivingMod
{
    public sealed partial class ReliableDriveFollowSystem : GameSystemBase
    {
        private const float kMpsToMph = 2.23693629f;

        protected override void OnUpdate()
        {
            Entity entity = ReliableDriveRuntime.PossessedEntity;
            if (entity == Entity.Null)
                return;

            if (!EntityManager.Exists(entity) ||
                !EntityManager.HasComponent<ObjectTransform>(entity) ||
                !EntityManager.HasComponent<Moving>(entity) ||
                EntityManager.HasComponent<Deleted>(entity) ||
                EntityManager.HasComponent<Temp>(entity))
            {
                ReliableDriveRuntime.RequestRelease();
                return;
            }

            ObjectTransform transform = EntityManager.GetComponentData<ObjectTransform>(entity);
            Moving moving = EntityManager.GetComponentData<Moving>(entity);
            ReliableDriveRuntime.SyncPose(
                ToUnityVector(transform.m_Position),
                ToUnityQuaternion(transform.m_Rotation),
                math.length(moving.m_Velocity) * kMpsToMph);
        }

        private static Vector3 ToUnityVector(float3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        private static Quaternion ToUnityQuaternion(quaternion value)
        {
            return new Quaternion(value.value.x, value.value.y, value.value.z, value.value.w);
        }
    }
}
