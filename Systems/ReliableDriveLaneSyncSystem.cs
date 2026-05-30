using Game;
using Game.Common;
using Game.Objects;
using Game.Tools;
using Game.Vehicles;
using Unity.Entities;
using ObjectTransform = Game.Objects.Transform;

namespace BetaTestDrivingMod
{
    public sealed partial class ReliableDriveLaneSyncSystem : GameSystemBase
    {
        private int m_SyncCount;

        protected override void OnUpdate()
        {
            Entity entity = ReliableDriveRuntime.PossessedEntity;
            if (entity == Entity.Null)
                return;

            if (!EntityManager.Exists(entity) ||
                !EntityManager.HasComponent<CarCurrentLane>(entity) ||
                !EntityManager.HasComponent<ObjectTransform>(entity) ||
                EntityManager.HasComponent<Deleted>(entity) ||
                EntityManager.HasComponent<Temp>(entity))
            {
                return;
            }

            CarCurrentLane currentLane = EntityManager.GetComponentData<CarCurrentLane>(entity);
            bool hasTurnQueue = ReliableDriveRuntime.TurnQueueEntity == entity &&
                                ReliableDriveRuntime.TurnConnectionLane != Entity.Null &&
                                EntityManager.HasBuffer<CarNavigationLane>(entity);

            if (hasTurnQueue)
            {
                DynamicBuffer<CarNavigationLane> navigationLanes = EntityManager.GetBuffer<CarNavigationLane>(entity);
                navigationLanes.Clear();
                navigationLanes.Add(new CarNavigationLane
                {
                    m_Lane = ReliableDriveRuntime.TurnConnectionLane,
                    m_CurvePosition = ReliableDriveRuntime.TurnConnectionCurvePosition,
                    m_Flags = ReliableDriveRuntime.TurnConnectionFlags
                });

                if (ReliableDriveRuntime.TurnExitLane != Entity.Null)
                {
                    navigationLanes.Add(new CarNavigationLane
                    {
                        m_Lane = ReliableDriveRuntime.TurnExitLane,
                        m_CurvePosition = ReliableDriveRuntime.TurnExitCurvePosition,
                        m_Flags = ReliableDriveRuntime.TurnExitFlags
                    });
                }

                currentLane.m_LaneFlags &= ~CarLaneFlags.Obsolete;
                currentLane.m_LaneFlags |= CarLaneFlags.UpdateOptimalLane;
            }
            else
            {
                currentLane.m_LaneFlags |= CarLaneFlags.Obsolete;
            }
            currentLane.m_ChangeLane = Entity.Null;

            // Let vanilla relocate normal driving, but keep queued turn connector lanes alive long enough to be consumed.
            EntityManager.SetComponentData(entity, currentLane);

            if (EntityManager.HasComponent<Blocker>(entity))
            {
                Blocker blocker = EntityManager.GetComponentData<Blocker>(entity);
                blocker.m_Blocker = Entity.Null;
                blocker.m_Type = BlockerType.None;
                blocker.m_MaxSpeed = byte.MaxValue;
                EntityManager.SetComponentData(entity, blocker);
            }

            if (++m_SyncCount == 1 || m_SyncCount % 120 == 0)
                Mod.log.Info($"Reliable Drive lane sync refreshed possessed car lane registration entity={entity} count={m_SyncCount} turnQueue={hasTurnQueue}");
        }
    }
}
