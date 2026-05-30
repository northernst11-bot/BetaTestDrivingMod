using Game;
using Game.Common;
using Game.Objects;
using Game.Tools;
using Game.Vehicles;
using System;
using Unity.Entities;
using ObjectTransform = Game.Objects.Transform;

namespace BetaTestDrivingMod
{
    public sealed partial class DirectDriveFreezeSystem : GameSystemBase
    {
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
                navigation.m_TargetPosition = transform.m_Position;
                navigation.m_TargetRotation = default;
                navigation.m_MaxSpeed = 0f;
                EntityManager.SetComponentData(car, navigation);
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
    }
}
