using Colossal.Entities;
using Game;
using Game.Common;
using Game.Objects;
using Game.Pathfind;
using Game.Tools;
using Game.Vehicles;
using System;
using Unity.Entities;
using Unity.Mathematics;
using ObjectTransform = Game.Objects.Transform;

namespace BetaTestDrivingMod
{
    public sealed partial class DirectDriveFreezeSystem : GameSystemBase
    {
        private const float kLivePathTargetLeadMeters = 0.05f;
        private int m_LogCooldown;

        protected override void OnUpdate()
        {
            try
            {
                if (!DirectDriveRuntime.IsDriving || !DirectDriveRuntime.FreezeVanillaNavigation)
                    return;

                Entity car = DirectDriveRuntime.PossessedEntity;
                if (car == Entity.Null ||
                    !EntityManager.Exists(car) ||
                    !EntityManager.HasComponent<CarNavigation>(car) ||
                    !EntityManager.HasComponent<ObjectTransform>(car) ||
                    EntityManager.HasComponent<Deleted>(car) ||
                    EntityManager.HasComponent<Temp>(car))
                {
                    return;
                }

                ObjectTransform transform = EntityManager.GetComponentData<ObjectTransform>(car);
                CarNavigation navigation = EntityManager.GetComponentData<CarNavigation>(car);
                float3 forward = math.forward(transform.m_Rotation);
                navigation.m_TargetPosition = transform.m_Position + forward * kLivePathTargetLeadMeters;
                navigation.m_TargetRotation = default;
                navigation.m_MaxSpeed = 0f;
                EntityManager.SetComponentData(car, navigation);
                ParkLivePathfinding(car);
            }
            catch (Exception ex)
            {
                if (m_LogCooldown-- <= 0)
                {
                    Mod.log.Warn($"Direct Drive freeze safety skipped after {ex.GetType().Name}: {ex.Message}");
                    m_LogCooldown = 180;
                }
            }
        }

        private void ParkLivePathfinding(Entity car)
        {
            if (EntityManager.HasBuffer<PathElement>(car))
            {
                DynamicBuffer<PathElement> pathElements = EntityManager.GetBuffer<PathElement>(car);
                if (pathElements.Length > 0)
                    pathElements.Clear();
            }

            if (!EntityManager.TryGetComponent(car, out PathOwner pathOwner))
                return;

            pathOwner.m_ElementIndex = 0;
            pathOwner.m_State &= ~(PathFlags.Failed | PathFlags.Stuck | PathFlags.Scheduled | PathFlags.Append | PathFlags.Updated | PathFlags.Obsolete | PathFlags.Divert | PathFlags.DivertObsolete | PathFlags.CachedObsolete);
            pathOwner.m_State |= PathFlags.Pending;
            EntityManager.SetComponentData(car, pathOwner);
        }
    }
}
