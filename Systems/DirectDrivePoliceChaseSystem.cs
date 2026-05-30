using Colossal.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Pathfind;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ObjectTransform = Game.Objects.Transform;
using VehiclePoliceCar = Game.Vehicles.PoliceCar;

namespace BetaTestDrivingMod
{
    public sealed partial class DirectDrivePoliceChaseSystem : GameSystemBase
    {
        private const float kArmProgress = 0.55f;
        private const float kViolationProgress = 0.92f;
        private const float kStopSpeedMps = 1.1f;
        private const float kViolationSpeedMps = 3.5f;
        private const float kChaseSeconds = 150f;
        private const float kPulledOverSeconds = 8f;
        private const float kPoliceDispatchRadiusSq = 760f * 760f;
        private const float kMovedEventDistanceSq = 6f * 6f;
        private const uint kMovedEventIntervalFrames = 30u;
        private const uint kDispatchRefreshFrames = 60u;
        private const uint kViolationCooldownFrames = 240u;
        private const float kCrimeLevel = 10000f;

        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_PoliceCarQuery;

        private Entity m_WatchedLane = Entity.Null;
        private bool m_RedSignalArmed;
        private bool m_StoppedAtSignal;
        private float m_LastLaneProgress;
        private uint m_LastViolationFrame;

        private Entity m_ChaseTarget = Entity.Null;
        private Entity m_ChaseRequest = Entity.Null;
        private bool m_TargetHadCrimeProducer;
        private CrimeProducer m_TargetOriginalCrimeProducer;
        private uint m_ChaseUntilFrame;
        private uint m_StoppedSinceFrame;
        private uint m_LastDispatchFrame;
        private uint m_LastPoliceRepathFrame;
        private uint m_LastMovedEventFrame;
        private float3 m_LastMovedEventPosition;
        private int m_AssignedUnits;
        private int m_LogCooldown;
        private int m_SignalWatchLogCooldown;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_PoliceCarQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadWrite<VehiclePoliceCar>(),
                    ComponentType.ReadWrite<Car>(),
                    ComponentType.ReadWrite<CarNavigation>(),
                    ComponentType.ReadWrite<CarCurrentLane>(),
                    ComponentType.ReadWrite<Target>(),
                    ComponentType.ReadWrite<PathOwner>(),
                    ComponentType.ReadWrite<ServiceDispatch>(),
                    ComponentType.ReadOnly<ObjectTransform>(),
                    ComponentType.ReadOnly<Moving>(),
                    ComponentType.ReadOnly<PrefabRef>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<TripSource>(),
                    ComponentType.ReadOnly<ParkedCar>(),
                    ComponentType.ReadOnly<Unspawned>(),
                    ComponentType.ReadOnly<Bicycle>(),
                    ComponentType.ReadOnly<OutOfControl>()
                }
            });
        }

        protected override void OnUpdate()
        {
            try
            {
                OnUpdateSafe();
            }
            catch (Exception ex)
            {
                if (m_LogCooldown-- <= 0)
                {
                    Mod.log.Warn($"Direct Drive police chase safety disabled chase after {ex.GetType().Name}: {ex.Message}");
                    m_LogCooldown = 180;
                }

                DirectDriveRuntime.PoliceChaseEnabled = false;
                DirectDriveRuntime.SetPoliceChase(false, "Police chase paused after safety guard", 0);
                SafeEndChase("Police chase safety guard");
                ResetSignalWatch();
            }
        }

        private void OnUpdateSafe()
        {
            if (!DirectDriveRuntime.PoliceChaseEnabled)
            {
                EndChase("Police chase disabled");
                ResetSignalWatch();
                DirectDriveRuntime.SetPoliceChase(false, "Police chase off", 0);
                return;
            }

            Entity target = DirectDriveRuntime.PossessedEntity;
            if (!DirectDriveRuntime.IsDriving ||
                target == Entity.Null ||
                !EntityManager.Exists(target) ||
                !EntityManager.TryGetComponent(target, out ObjectTransform transform) ||
                !EntityManager.TryGetComponent(target, out CarCurrentLane currentLane) ||
                !EntityManager.TryGetComponent(target, out Moving moving))
            {
                EndChase("No possessed vehicle");
                ResetSignalWatch();
                DirectDriveRuntime.SetPoliceChase(false, "Police chase armed", 0);
                return;
            }

            float speedMps = math.length(moving.m_Velocity);
            uint frame = m_SimulationSystem != null ? m_SimulationSystem.frameIndex : 0u;

            if (m_ChaseTarget != Entity.Null && m_ChaseTarget != target)
                EndChase("Possessed vehicle changed");

            if (DirectDriveRuntime.ConsumePoliceChaseTestRequest())
                StartOrRefreshChase(target, transform.m_Position, frame, "Manual chase test");

            if (DirectDriveRuntime.PoliceChaseActive)
                MaintainChase(target, transform.m_Position, speedMps, frame);

            WatchRedLight(target, currentLane, transform.m_Position, speedMps, frame);
        }

        private void SafeEndChase(string reason)
        {
            try
            {
                EndChase(reason);
            }
            catch (Exception cleanupEx)
            {
                if (m_LogCooldown-- <= 0)
                {
                    Mod.log.Warn($"Direct Drive police chase cleanup guard skipped after {cleanupEx.GetType().Name}: {cleanupEx.Message}");
                    m_LogCooldown = 180;
                }

                m_ChaseRequest = Entity.Null;
                m_ChaseTarget = Entity.Null;
                m_ChaseUntilFrame = 0u;
                m_StoppedSinceFrame = 0u;
                m_LastDispatchFrame = 0u;
                m_LastPoliceRepathFrame = 0u;
                m_AssignedUnits = 0;
            }
        }

        private void WatchRedLight(Entity target, CarCurrentLane currentLane, float3 position, float speedMps, uint frame)
        {
            Entity lane = currentLane.m_Lane;
            float progress = GetTravelProgress(currentLane);

            if (lane != m_WatchedLane)
            {
                if (m_RedSignalArmed && !m_StoppedAtSignal && m_LastLaneProgress > 0.74f && speedMps >= kViolationSpeedMps)
                    TriggerViolation(target, position, frame, "Ran a red while entering the junction");

                m_WatchedLane = lane;
                m_RedSignalArmed = false;
                m_StoppedAtSignal = false;
                m_LastLaneProgress = progress;
            }

            if (!TryGetWatchedLaneSignal(currentLane, out lane, out LaneSignal laneSignal))
            {
                if (m_RedSignalArmed &&
                    !m_StoppedAtSignal &&
                    m_LastLaneProgress > kArmProgress &&
                    speedMps >= kViolationSpeedMps)
                {
                    TriggerViolation(target, position, frame, "Ran a red after leaving the signal lane");
                    m_RedSignalArmed = false;
                    m_StoppedAtSignal = false;
                }

                m_LastLaneProgress = progress;
                SetSignalWatchStatus("Police chase armed; no signal lane detected", lane, hasSignal: false, laneSignal, progress, speedMps);
                return;
            }

            bool stopSignal = laneSignal.m_Signal == LaneSignalType.Stop || laneSignal.m_Signal == LaneSignalType.SafeStop;
            if (!stopSignal)
            {
                m_RedSignalArmed = false;
                m_StoppedAtSignal = false;
                m_LastLaneProgress = progress;
                SetSignalWatchStatus("Police chase armed; signal is clear", lane, hasSignal: true, laneSignal, progress, speedMps);
                return;
            }

            if (speedMps <= kStopSpeedMps && progress < kViolationProgress)
                m_StoppedAtSignal = true;

            if (progress >= kArmProgress && progress < kViolationProgress && !m_StoppedAtSignal)
                m_RedSignalArmed = true;

            if (m_RedSignalArmed && progress >= kViolationProgress && !m_StoppedAtSignal && speedMps >= kViolationSpeedMps)
            {
                TriggerViolation(target, position, frame, "Ran a red without stopping");
                m_RedSignalArmed = false;
            }
            else if (!DirectDriveRuntime.PoliceChaseActive)
            {
                DirectDriveRuntime.SetPoliceChase(false, m_RedSignalArmed ? "Red light ahead - stop or chase starts" : "Police chase armed", 0);
            }

            SetSignalWatchStatus(m_RedSignalArmed ? "Red signal armed" : "Red signal watched", lane, hasSignal: true, laneSignal, progress, speedMps);
            m_LastLaneProgress = progress;
        }

        private bool TryGetWatchedLaneSignal(CarCurrentLane currentLane, out Entity lane, out LaneSignal laneSignal)
        {
            lane = currentLane.m_Lane;
            if (lane != Entity.Null && EntityManager.TryGetComponent(lane, out laneSignal))
                return true;

            lane = currentLane.m_ChangeLane;
            if (lane != Entity.Null && EntityManager.TryGetComponent(lane, out laneSignal))
                return true;

            lane = Entity.Null;
            laneSignal = default;
            return false;
        }

        private void SetSignalWatchStatus(string status, Entity lane, bool hasSignal, LaneSignal laneSignal, float progress, float speedMps)
        {
            if (DirectDriveRuntime.PoliceChaseActive)
                return;

            DirectDriveRuntime.SetPoliceChase(false, status, 0);

            if (m_SignalWatchLogCooldown-- > 0)
                return;

            m_SignalWatchLogCooldown = 240;
            string signalText = hasSignal ? laneSignal.m_Signal.ToString() : "none";
            Mod.log.Info($"Direct Drive police signal watch: {status}; lane={lane}; signal={signalText}; progress={progress:0.00}; speed={speedMps:0.0}m/s.");
        }

        private void TriggerViolation(Entity target, float3 position, uint frame, string reason)
        {
            if (frame != 0u && m_LastViolationFrame != 0u && frame - m_LastViolationFrame < kViolationCooldownFrames)
                return;

            m_LastViolationFrame = frame;
            DirectDriveRuntime.RecordRedLightViolation();
            StartOrRefreshChase(target, position, frame, reason);
        }

        private void StartOrRefreshChase(Entity target, float3 position, uint frame, string reason)
        {
            if (m_ChaseTarget != target)
            {
                EndChase("New chase target");
                m_ChaseTarget = target;
                m_TargetHadCrimeProducer = EntityManager.TryGetComponent(target, out m_TargetOriginalCrimeProducer);
                m_LastMovedEventPosition = position;
                m_LastMovedEventFrame = frame;
            }

            m_ChaseUntilFrame = frame + (uint)(kChaseSeconds * 60f);
            m_StoppedSinceFrame = 0u;
            EnsureChaseRequest(target);
            MarkTargetAsCrimeProducer(target);
            m_AssignedUnits = DispatchNearbyPolice(target, position, forceRepath: true);
            m_LastDispatchFrame = frame;
            m_LastPoliceRepathFrame = frame;
            RaiseTargetMovedEvent(target, position, frame, force: true);

            string status = m_AssignedUnits > 0
                ? $"{reason}; police pursuing"
                : $"{reason}; requesting police";
            DirectDriveRuntime.SetPoliceChase(true, status, m_AssignedUnits);
            Mod.log.Info($"Direct Drive police chase started for {target}: {reason}. Assigned nearby units={m_AssignedUnits}.");
        }

        private void MaintainChase(Entity target, float3 position, float speedMps, uint frame)
        {
            if (m_ChaseUntilFrame != 0u && frame >= m_ChaseUntilFrame)
            {
                EndChase("Police chase expired");
                DirectDriveRuntime.SetPoliceChase(false, "Police chase armed", 0);
                return;
            }

            EnsureChaseRequest(target);
            MarkTargetAsCrimeProducer(target);

            bool forceRepath = m_LastPoliceRepathFrame == 0u ||
                frame - m_LastPoliceRepathFrame >= kMovedEventIntervalFrames ||
                math.distancesq(position, m_LastMovedEventPosition) >= kMovedEventDistanceSq;

            if (m_LastDispatchFrame == 0u || frame - m_LastDispatchFrame >= kDispatchRefreshFrames)
            {
                m_AssignedUnits = DispatchNearbyPolice(target, position, forceRepath);
                m_LastDispatchFrame = frame;
            }
            else
            {
                m_AssignedUnits = RefreshAssignedPoliceUnits(target, forceRepath);
            }

            if (forceRepath)
                m_LastPoliceRepathFrame = frame;

            RaiseTargetMovedEvent(target, position, frame, force: false);

            if (speedMps <= kStopSpeedMps)
            {
                if (m_StoppedSinceFrame == 0u)
                    m_StoppedSinceFrame = frame;
                else if (frame - m_StoppedSinceFrame >= (uint)(kPulledOverSeconds * 60f))
                {
                    EndChase("Pulled over");
                    DirectDriveRuntime.SetPoliceChase(false, "Pulled over; chase cleared", 0);
                    return;
                }
            }
            else
            {
                m_StoppedSinceFrame = 0u;
            }

            string status = m_AssignedUnits > 0 ? "Police chase active" : "Police chase active; waiting for units";
            DirectDriveRuntime.SetPoliceChase(true, status, m_AssignedUnits);
        }

        private void EnsureChaseRequest(Entity target)
        {
            if (m_ChaseRequest != Entity.Null &&
                EntityManager.Exists(m_ChaseRequest) &&
                EntityManager.HasComponent<PolicePatrolRequest>(m_ChaseRequest))
            {
                PolicePatrolRequest request = EntityManager.GetComponentData<PolicePatrolRequest>(m_ChaseRequest);
                request.m_Target = target;
                request.m_Priority = kCrimeLevel;
                EntityManager.SetComponentData(m_ChaseRequest, request);
                return;
            }

            m_ChaseRequest = EntityManager.CreateEntity();
            ServiceRequest serviceRequest = new ServiceRequest(reversed: false);
            serviceRequest.m_Flags |= ServiceRequestFlags.SkipCooldown;
            EntityManager.AddComponentData(m_ChaseRequest, serviceRequest);
            EntityManager.AddComponentData(m_ChaseRequest, new PolicePatrolRequest(target, kCrimeLevel));
            EntityManager.AddComponentData(m_ChaseRequest, new RequestGroup(32u));
        }

        private void MarkTargetAsCrimeProducer(Entity target)
        {
            CrimeProducer crimeProducer;
            if (EntityManager.TryGetComponent(target, out crimeProducer))
            {
                crimeProducer.m_PatrolRequest = m_ChaseRequest;
                crimeProducer.m_Crime = math.max(crimeProducer.m_Crime, kCrimeLevel);
                EntityManager.SetComponentData(target, crimeProducer);
                return;
            }

            EntityManager.AddComponentData(target, new CrimeProducer
            {
                m_PatrolRequest = m_ChaseRequest,
                m_Crime = kCrimeLevel,
                m_DispatchIndex = 0
            });
        }

        private int DispatchNearbyPolice(Entity target, float3 targetPosition, bool forceRepath)
        {
            if (m_ChaseRequest == Entity.Null || !EntityManager.Exists(m_ChaseRequest) || m_PoliceCarQuery == default || m_PoliceCarQuery.IsEmptyIgnoreFilter)
                return 0;

            Entity[] bestCars = { Entity.Null, Entity.Null, Entity.Null };
            float[] bestScores = { float.MaxValue, float.MaxValue, float.MaxValue };
            NativeArray<Entity> cars = m_PoliceCarQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < cars.Length; i++)
                {
                    Entity candidate = cars[i];
                    if (candidate == target ||
                        !EntityManager.TryGetComponent(candidate, out VehiclePoliceCar policeCar) ||
                        (policeCar.m_State & (PoliceCarFlags.ShiftEnded | PoliceCarFlags.Disabled)) != 0 ||
                        !EntityManager.TryGetComponent(candidate, out ObjectTransform transform))
                    {
                        continue;
                    }

                    float distanceSq = math.distancesq(transform.m_Position, targetPosition);
                    if (distanceSq > kPoliceDispatchRadiusSq)
                        continue;

                    float score = distanceSq + ((policeCar.m_State & PoliceCarFlags.Returning) != 0 ? 9000f : 0f);
                    for (int slot = 0; slot < bestCars.Length; slot++)
                    {
                        if (score >= bestScores[slot])
                            continue;

                        for (int move = bestCars.Length - 1; move > slot; move--)
                        {
                            bestCars[move] = bestCars[move - 1];
                            bestScores[move] = bestScores[move - 1];
                        }

                        bestCars[slot] = candidate;
                        bestScores[slot] = score;
                        break;
                    }
                }
            }
            finally
            {
                cars.Dispose();
            }

            int assigned = 0;
            for (int i = 0; i < bestCars.Length; i++)
            {
                if (bestCars[i] == Entity.Null)
                    continue;

                if (AssignPoliceUnit(bestCars[i], target, forceRepath))
                    assigned++;
            }

            return math.max(assigned, RefreshAssignedPoliceUnits(target, forceRepath));
        }

        private bool AssignPoliceUnit(Entity policeUnit, Entity target, bool forceRepath)
        {
            if (!EntityManager.HasBuffer<ServiceDispatch>(policeUnit))
                return false;

            DynamicBuffer<ServiceDispatch> dispatches = EntityManager.GetBuffer<ServiceDispatch>(policeUnit);
            bool alreadyAssigned = false;
            for (int i = 0; i < dispatches.Length; i++)
            {
                if (dispatches[i].m_Request == m_ChaseRequest)
                {
                    alreadyAssigned = true;
                    break;
                }
            }

            if (!alreadyAssigned)
                dispatches.Add(new ServiceDispatch(m_ChaseRequest));

            ApplyChaseToPoliceUnit(policeUnit, target, forceRepath || !alreadyAssigned);
            return true;
        }

        private int RefreshAssignedPoliceUnits(Entity target, bool forceRepath)
        {
            if (m_ChaseRequest == Entity.Null || !EntityManager.Exists(m_ChaseRequest) || m_PoliceCarQuery == default || m_PoliceCarQuery.IsEmptyIgnoreFilter)
                return 0;

            int assigned = 0;
            NativeArray<Entity> cars = m_PoliceCarQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < cars.Length; i++)
                {
                    Entity policeUnit = cars[i];
                    if (!HasChaseDispatch(policeUnit))
                        continue;

                    ApplyChaseToPoliceUnit(policeUnit, target, forceRepath);
                    assigned++;
                }
            }
            finally
            {
                cars.Dispose();
            }

            return assigned;
        }

        private bool HasChaseDispatch(Entity policeUnit)
        {
            if (!EntityManager.HasBuffer<ServiceDispatch>(policeUnit))
                return false;

            DynamicBuffer<ServiceDispatch> dispatches = EntityManager.GetBuffer<ServiceDispatch>(policeUnit);
            for (int i = 0; i < dispatches.Length; i++)
            {
                if (dispatches[i].m_Request == m_ChaseRequest)
                    return true;
            }

            return false;
        }

        private void ApplyChaseToPoliceUnit(Entity policeUnit, Entity target, bool forceRepath)
        {
            if (EntityManager.TryGetComponent(policeUnit, out Target targetData))
            {
                bool targetChanged = targetData.m_Target != target;
                targetData.m_Target = target;
                EntityManager.SetComponentData(policeUnit, targetData);
                forceRepath |= targetChanged;
            }

            if (EntityManager.TryGetComponent(policeUnit, out PathOwner pathOwner))
            {
                if (forceRepath)
                {
                    pathOwner.m_State &= ~(PathFlags.Failed | PathFlags.Stuck);
                    pathOwner.m_State |= PathFlags.Obsolete;
                }
                EntityManager.SetComponentData(policeUnit, pathOwner);
            }

            if (EntityManager.TryGetComponent(policeUnit, out Car car))
            {
                car.m_Flags &= ~CarFlags.AnyLaneTarget;
                car.m_Flags |= CarFlags.Emergency | CarFlags.Warning | CarFlags.StayOnRoad | CarFlags.UsePublicTransportLanes;
                EntityManager.SetComponentData(policeUnit, car);
            }

            if (EntityManager.TryGetComponent(policeUnit, out VehiclePoliceCar policeCar))
            {
                policeCar.m_TargetRequest = m_ChaseRequest;
                policeCar.m_State &= ~(PoliceCarFlags.Returning | PoliceCarFlags.AtTarget | PoliceCarFlags.Cancelled | PoliceCarFlags.Disembarking);
                EntityManager.SetComponentData(policeUnit, policeCar);
            }

            if (!EntityManager.HasComponent<EffectsUpdated>(policeUnit))
                EntityManager.AddComponent<EffectsUpdated>(policeUnit);
        }

        private void RaiseTargetMovedEvent(Entity target, float3 position, uint frame, bool force)
        {
            if (!force &&
                frame - m_LastMovedEventFrame < kMovedEventIntervalFrames &&
                math.distancesq(position, m_LastMovedEventPosition) < kMovedEventDistanceSq)
            {
                return;
            }

            Entity e = EntityManager.CreateEntity();
            EntityManager.AddComponent<Game.Common.Event>(e);
            EntityManager.AddComponentData(e, new PathTargetMoved(target, m_LastMovedEventPosition, position));
            m_LastMovedEventFrame = frame;
            m_LastMovedEventPosition = position;
        }

        private void EndChase(string reason)
        {
            Entity request = m_ChaseRequest;
            RemoveChaseRequestFromPolice(request);

            if (request != Entity.Null && EntityManager.Exists(request))
                EntityManager.DestroyEntity(request);

            RestoreTargetCrimeProducer();
            m_ChaseRequest = Entity.Null;
            m_ChaseTarget = Entity.Null;
            m_ChaseUntilFrame = 0u;
            m_StoppedSinceFrame = 0u;
            m_LastDispatchFrame = 0u;
            m_LastPoliceRepathFrame = 0u;
            m_AssignedUnits = 0;

            if (DirectDriveRuntime.PoliceChaseActive)
                Mod.log.Info($"Direct Drive police chase ended: {reason}");
        }

        private void RemoveChaseRequestFromPolice(Entity request)
        {
            if (request == Entity.Null || m_PoliceCarQuery == default || m_PoliceCarQuery.IsEmptyIgnoreFilter)
                return;

            NativeArray<Entity> cars = m_PoliceCarQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < cars.Length; i++)
                {
                    Entity car = cars[i];
                    if (!EntityManager.HasBuffer<ServiceDispatch>(car))
                        continue;

                    DynamicBuffer<ServiceDispatch> dispatches = EntityManager.GetBuffer<ServiceDispatch>(car);
                    bool removed = false;
                    for (int j = dispatches.Length - 1; j >= 0; j--)
                    {
                        if (dispatches[j].m_Request == request)
                        {
                            dispatches.RemoveAt(j);
                            removed = true;
                        }
                    }

                    if (removed)
                        ReleasePoliceUnitFromChase(car, request, dispatches);
                }
            }
            catch (Exception ex)
            {
                if (m_LogCooldown-- <= 0)
                {
                    Mod.log.Warn($"Direct Drive police chase cleanup skipped after {ex.GetType().Name}: {ex.Message}");
                    m_LogCooldown = 180;
                }
            }
            finally
            {
                cars.Dispose();
            }
        }

        private void ReleasePoliceUnitFromChase(Entity policeUnit, Entity request, DynamicBuffer<ServiceDispatch> remainingDispatches)
        {
            if (EntityManager.TryGetComponent(policeUnit, out VehiclePoliceCar policeCar))
            {
                if (policeCar.m_TargetRequest == request)
                    policeCar.m_TargetRequest = Entity.Null;
                policeCar.m_State &= ~(PoliceCarFlags.AtTarget | PoliceCarFlags.Cancelled | PoliceCarFlags.Disembarking);
                EntityManager.SetComponentData(policeUnit, policeCar);
            }

            bool hasEmergencyDispatch = false;
            for (int i = 0; i < remainingDispatches.Length; i++)
            {
                if (EntityManager.HasComponent<PoliceEmergencyRequest>(remainingDispatches[i].m_Request))
                {
                    hasEmergencyDispatch = true;
                    break;
                }
            }

            if (EntityManager.TryGetComponent(policeUnit, out Car car))
            {
                car.m_Flags &= ~CarFlags.Warning;
                if (!hasEmergencyDispatch)
                    car.m_Flags &= ~CarFlags.Emergency;
                EntityManager.SetComponentData(policeUnit, car);
            }

            if (!EntityManager.HasComponent<EffectsUpdated>(policeUnit))
                EntityManager.AddComponent<EffectsUpdated>(policeUnit);
        }

        private void RestoreTargetCrimeProducer()
        {
            if (m_ChaseTarget == Entity.Null || !EntityManager.Exists(m_ChaseTarget))
                return;

            if (m_TargetHadCrimeProducer)
            {
                EntityManager.SetComponentData(m_ChaseTarget, m_TargetOriginalCrimeProducer);
            }
            else if (EntityManager.HasComponent<CrimeProducer>(m_ChaseTarget))
            {
                EntityManager.RemoveComponent<CrimeProducer>(m_ChaseTarget);
            }

            m_TargetHadCrimeProducer = false;
            m_TargetOriginalCrimeProducer = default;
        }

        private void ResetSignalWatch()
        {
            m_WatchedLane = Entity.Null;
            m_RedSignalArmed = false;
            m_StoppedAtSignal = false;
            m_LastLaneProgress = 0f;
        }

        private static float GetTravelProgress(CarCurrentLane currentLane)
        {
            float curveT = math.saturate(currentLane.m_CurvePosition.x);
            float curveSign = currentLane.m_CurvePosition.z < currentLane.m_CurvePosition.x ? -1f : 1f;
            return curveSign < 0f ? 1f - curveT : curveT;
        }
    }
}
