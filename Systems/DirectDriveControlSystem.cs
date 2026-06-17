using Colossal.Collections;
using Colossal.Entities;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Objects;
using Game.Pathfind;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Game.UI.InGame;
using Game.Vehicles;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Jobs;
using UnityEngine;
using ObjectTransform = Game.Objects.Transform;

namespace BetaTestDrivingMod
{
    public sealed partial class DirectDriveControlSystem : GameSystemBase
    {
        private const float kMpsToMph = 2.23693629f;
        private const float kMphToMps = 0.44704f;
        private const float kSimulationStepSeconds = 1f / 60f;
        private const float kInterpolationFrameSeconds = 16f / 60f;
        private const float kMaxVerticalCorrectionPerTick = 0.08f;
        private const float kNearbyLaneSearchRadius = 26f;
        private const float kMaxRoadAttachDistanceSq = 400f;
        private const float kCurrentRoadPoseAcceptDistanceSq = 4f;
        private const float kRoadPoseMaxHeightDelta = 3.2f;
        private const float kTrafficPresenceMaxRoadAttachDistanceSq = 36f;
        private const float kTrafficPresenceRoadOnlyMaxLateralMeters = 3.25f;
        private const float kTrafficPresenceMaxRoadHeightDelta = 2.8f;
        private const float kTrafficPresenceHaloMaxLateralMeters = 4.35f;
        private const float kTrafficPresenceHaloMaxRoadAttachDistanceSq = 42f;
        private const float kTrafficPresenceHaloMaxHeightDelta = 2.4f;
        private const float kTrafficPresenceHaloMinForwardDot = 0.15f;
        private const float kTrafficPresenceMinForwardDot = -0.65f;
        private const float kTrafficPresenceForwardMeters = 2.0f;
        private const float kTrafficPresenceRearMeters = 1.7f;
        private const float kTrafficPresenceSpeedLeadSeconds = 0f;
        private const float kTrafficPresenceMaxForwardMeters = 2.1f;
        private const float kTrafficPresenceMaxRearMeters = 1.8f;
        private const float kTrafficPresenceMinCurveSpan = 0.0004f;
        private const float kTrafficPresenceMaxCurveSpan = 0.025f;
        private const float kTrafficPresenceCurveUpdateThresholdSq = 0.000001f;
        private const uint kNearbyRoadPoseCacheFrames = 2U;
        private const float kNearbyRoadPoseCacheReuseDistanceSq = 1.44f;
        private const float kTrafficPresenceStaleCleanupRadius = 34f;
        private const uint kTrafficPresenceNearbyCleanupFrames = 10U;
        private const uint kTurnIntentCacheFrames = 15U;
        private const int kTrafficPresenceStableFrames = 1;
        private const uint kTrafficPresenceMinSyncFrames = 1U;
        private const float kTransformFrameLagResyncSeconds = 0.12f;
        private const float kTransformFrameLagDriftResyncDistanceSq = 18f * 18f;
        private const float kTransformFrameDriftResyncDistanceSq = 64f * 64f;
        private const float kRoadGradePitchBlend = 0.72f;
        private const float kRoadGradeMaxSlopeY = 0.42f;
        private const float kLivePathTargetLeadMeters = 0.05f;

        private SelectedInfoUISystem m_SelectedInfo;
        private ToolSystem m_ToolSystem;
        private CameraUpdateSystem m_CameraUpdateSystem;
        private PrefabSystem m_PrefabSystem;
        private SimulationSystem m_SimulationSystem;
        private Game.Net.SearchSystem m_NetSearchSystem;
        private EntityQuery m_LiveCarQuery;
        private EntityQuery m_ConnectionLaneQuery;
        private Entity m_PossessedCar = Entity.Null;
        private Entity m_LastTrafficLane = Entity.Null;
        private Entity m_LastTrafficChangeLane = Entity.Null;
        private float2 m_LastTrafficCurvePosition = new float2(-1f, -1f);
        private float2 m_LastTrafficChangeCurvePosition = new float2(-1f, -1f);
        private readonly List<Entity> m_TouchedTrafficPresenceLanes = new List<Entity>(16);
        private string m_PossessedName = "";
        private float m_SpeedMps;
        private float m_RoadGroundOffsetY;
        private bool m_ReverseArmed;
        private bool m_ReverseActive;
        private int m_LogCooldown;
        private int m_TurnLogCooldown;
        private bool m_RoadLaneWriteCrashguardLogged;
        private bool m_TrafficPresenceRestoredLogged;
        private Entity m_TrafficPresenceCandidateLane = Entity.Null;
        private int m_TrafficPresenceCandidateFrames;
        private Entity m_TrafficPresenceBridgeLane = Entity.Null;
        private int m_TrafficPresenceBridgeFramesRemaining;
        private uint m_LastTrafficPresenceSyncFrame;
        private uint m_LastTrafficPresenceNearbyCleanupFrame;
        private Entity m_TurnIntentCacheLane = Entity.Null;
        private Entity m_TurnIntentCacheConnection = Entity.Null;
        private Entity m_TurnIntentCacheExit = Entity.Null;
        private int m_TurnIntentCacheSide;
        private uint m_TurnIntentCacheFrame;
        private CarLaneFlags m_TurnIntentCacheTurnFlag;
        private CarLaneFlags m_TurnIntentCacheConnectionFlags;
        private CarLaneFlags m_TurnIntentCacheExitFlags;
        private float2 m_TurnIntentCacheConnectionCurvePosition;
        private float2 m_TurnIntentCacheExitCurvePosition;
        private string m_TurnIntentCacheStatus = "";
        private Entity m_NearbyRoadPoseCacheLane = Entity.Null;
        private Vector3 m_NearbyRoadPoseCacheCenter;
        private uint m_NearbyRoadPoseCacheFrame;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SelectedInfo = World.GetOrCreateSystemManaged<SelectedInfoUISystem>();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_CameraUpdateSystem = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_NetSearchSystem = World.GetOrCreateSystemManaged<Game.Net.SearchSystem>();
            m_LiveCarQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Car>(),
                    ComponentType.ReadWrite<CarNavigation>(),
                    ComponentType.ReadWrite<CarCurrentLane>(),
                    ComponentType.ReadOnly<ObjectTransform>(),
                    ComponentType.ReadOnly<Moving>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<TransformFrame>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<TripSource>(),
                    ComponentType.ReadOnly<ParkedCar>(),
                    ComponentType.ReadOnly<Unspawned>(),
                    ComponentType.ReadOnly<Bicycle>()
                }
            });
            m_ConnectionLaneQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Net.ConnectionLane>(),
                    ComponentType.ReadOnly<Game.Net.LaneConnection>(),
                    ComponentType.ReadOnly<Game.Net.Curve>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>()
                }
            });
            DirectDriveRuntime.SetIdle(DirectDriveRuntime.ReadyStatusText);
        }

        protected override void OnUpdate()
        {
            try
            {
                DirectDriveRuntime.EnsureHud();
                OnUpdateSafe();
            }
            catch (Exception ex)
            {
                if (m_LogCooldown-- <= 0)
                {
                    Mod.log.Warn($"Direct Drive safety release after top-level control exception: {ex.GetType().Name}: {ex.Message}");
                    m_LogCooldown = 180;
                }

                Release($"Safety release after {ex.GetType().Name}");
            }
        }

        private void OnUpdateSafe()
        {
            if (DirectDriveRuntime.ConsumeReleaseRequest())
                Release("Released by user");

            if (DirectDriveRuntime.ConsumeToggleRequest())
            {
                if (m_PossessedCar != Entity.Null)
                    Release("Released by toggle");
                else
                    TryPossessBestCar();
            }

            if (m_PossessedCar == Entity.Null)
                return;

            if (!IsDriveableCar(m_PossessedCar))
            {
                Release("Possessed car disappeared or stopped being driveable");
                return;
            }

            try
            {
                ApplyDirectControl(m_PossessedCar);
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"Direct Drive released car after control exception: {ex.GetType().Name}: {ex.Message}");
                Release($"Released after {ex.GetType().Name}");
            }
        }

        private void TryPossessBestCar()
        {
            Entity selected = ResolveTransformEntity(m_SelectedInfo != null ? m_SelectedInfo.selectedEntity : Entity.Null);
            if (IsDriveableCar(selected))
            {
                Possess(selected, "selected car");
                return;
            }

            Vector3 searchPosition = ResolveCameraSearchPosition();
            if (TryFindNearestLiveCar(searchPosition, out Entity nearest))
            {
                Possess(nearest, "nearest car");
                return;
            }

            DirectDriveRuntime.SetIdle($"No driveable live car found near camera. Let traffic spawn, select a car, then press {DirectDriveRuntime.ToggleDrivingKey}.");
            if (m_LogCooldown-- <= 0)
            {
                Mod.log.Info("Direct Drive possession rejected: no live road car with Moving/CarNavigation was found near camera.");
                m_LogCooldown = 120;
            }
        }

        private void Possess(Entity car, string reason)
        {
            m_PossessedCar = car;
            m_PossessedName = GetVehicleName(car);
            m_ReverseArmed = false;
            m_ReverseActive = false;

            ObjectTransform transform = EntityManager.GetComponentData<ObjectTransform>(car);
            Moving moving = EntityManager.GetComponentData<Moving>(car);
            CarCurrentLane currentLane = EntityManager.GetComponentData<CarCurrentLane>(car);
            Vector3 position = ToUnityVector(transform.m_Position);
            Quaternion rotation = LevelRotation(ToUnityQuaternion(transform.m_Rotation));
            Vector3 forward = FlattenForward(rotation * Vector3.forward);
            m_SpeedMps = Vector3.Dot(ToUnityVector(moving.m_Velocity), forward);
            m_RoadGroundOffsetY = ResolveGroundOffset(car, position, forward, rotation * Vector3.right);
            m_LastTrafficLane = Entity.Null;
            m_LastTrafficChangeLane = Entity.Null;
            m_LastTrafficCurvePosition = new float2(-1f, -1f);
            m_LastTrafficChangeCurvePosition = new float2(-1f, -1f);
            m_TouchedTrafficPresenceLanes.Clear();
            m_TrafficPresenceCandidateLane = Entity.Null;
            m_TrafficPresenceCandidateFrames = 0;
            m_TrafficPresenceBridgeLane = Entity.Null;
            m_TrafficPresenceBridgeFramesRemaining = 0;
            m_LastTrafficPresenceSyncFrame = 0U;
            m_LastTrafficPresenceNearbyCleanupFrame = 0U;
            ClearTurnIntentCache();
            ClearNearbyRoadPoseCache();
            InvalidateVehicleCollisionCandidateCache();
            ClearPossessionFocus(car, false);
            ClearNavigationBuffer(car);
            ParkLivePathfinding(car);
            PrimeTransformFrames(car, transform, moving);

            DirectDriveRuntime.SetDriving(car, m_PossessedName, ToUnityVector(transform.m_Position), ToUnityQuaternion(transform.m_Rotation), math.length(moving.m_Velocity), false, false, "Direct control active");
            Mod.log.Info($"Direct Drive possessed {car} '{m_PossessedName}' by {reason}. Physical movement is now direct-controlled.");
        }

        private void Release(string reason)
        {
            if (m_PossessedCar != Entity.Null)
                Mod.log.Info($"Direct Drive released {m_PossessedCar} '{m_PossessedName}': {reason}");

            ClearTrafficPresence();
            PrepareReleasePathfinding(m_PossessedCar);
            ClearPossessionFocus(m_PossessedCar, true);
            m_PossessedCar = Entity.Null;
            m_LastTrafficLane = Entity.Null;
            m_LastTrafficChangeLane = Entity.Null;
            m_LastTrafficCurvePosition = new float2(-1f, -1f);
            m_LastTrafficChangeCurvePosition = new float2(-1f, -1f);
            m_TouchedTrafficPresenceLanes.Clear();
            m_TrafficPresenceCandidateLane = Entity.Null;
            m_TrafficPresenceCandidateFrames = 0;
            m_TrafficPresenceBridgeLane = Entity.Null;
            m_TrafficPresenceBridgeFramesRemaining = 0;
            m_LastTrafficPresenceSyncFrame = 0U;
            m_LastTrafficPresenceNearbyCleanupFrame = 0U;
            ClearNearbyRoadPoseCache();
            m_LastVehicleCollisionTarget = Entity.Null;
            m_LastVehicleCollisionFrame = 0U;
            m_LastVehicleCollisionLogFrame = 0U;
            ClearTurnIntentCache();
            InvalidateVehicleCollisionCandidateCache();
            m_PossessedName = "";
            m_SpeedMps = 0f;
            m_RoadGroundOffsetY = 0f;
            m_ReverseArmed = false;
            m_ReverseActive = false;
            DirectDriveRuntime.SetIdle(reason);
        }

        private void PrepareReleasePathfinding(Entity car)
        {
            if (car == Entity.Null || !EntityManager.Exists(car))
                return;

            try
            {
                if (!EntityManager.TryGetComponent(car, out ObjectTransform transform) ||
                    !EntityManager.TryGetComponent(car, out Moving moving) ||
                    !EntityManager.TryGetComponent(car, out CarNavigation navigation) ||
                    !EntityManager.TryGetComponent(car, out CarCurrentLane currentLane))
                {
                    return;
                }

                Vector3 position = ToUnityVector(transform.m_Position);
                Quaternion rotation = LevelRotation(ToUnityQuaternion(transform.m_Rotation));
                Vector3 forward = FlattenForward(rotation * Vector3.forward);
                Vector3 velocity = ToUnityVector(moving.m_Velocity);
                if (forward.sqrMagnitude < 0.001f && velocity.sqrMagnitude > 0.001f)
                    forward = FlattenForward(velocity);

                Vector3 right = GetRightFromForward(forward, rotation * Vector3.right);
                bool snappedToReleaseLane = false;
                if (TryGetRoadPose(currentLane, position, forward, right, out Entity roadLane, out _, out Vector3 laneForward, out _, out float curveT, out float curveSign, out _))
                {
                    ApplyRoadPoseToCurrentLane(ref currentLane, roadLane, curveT, curveSign);
                    currentLane.m_LaneFlags &= ~(CarLaneFlags.TurnLeft | CarLaneFlags.TurnRight);
                    currentLane.m_LaneFlags |= CarLaneFlags.Obsolete;
                    EntityManager.SetComponentData(car, currentLane);
                    if (laneForward.sqrMagnitude > 0.001f)
                        forward = FlattenForward(laneForward);
                    snappedToReleaseLane = true;
                }
                else
                {
                    currentLane.m_ChangeLane = Entity.Null;
                    currentLane.m_ChangeProgress = 0f;
                    currentLane.m_LaneFlags |= CarLaneFlags.Obsolete;
                    EntityManager.SetComponentData(car, currentLane);
                }

                if (velocity.sqrMagnitude > 0.25f)
                    forward = FlattenForward(velocity);

                float speedMps = Mathf.Max(math.length(moving.m_Velocity), Mathf.Abs(m_SpeedMps));
                float leadMeters = Mathf.Clamp(speedMps * 1.25f, 6f, 22f);
                Vector3 targetPosition = position + forward * leadMeters;
                navigation.m_TargetPosition = ToMathVector(targetPosition);
                navigation.m_TargetRotation = default;
                navigation.m_MaxSpeed = Mathf.Max(speedMps, 0.5f);
                EntityManager.SetComponentData(car, navigation);

                ClearNavigationBuffer(car);
                if (EntityManager.TryGetComponent(car, out PathOwner pathOwner))
                {
                    pathOwner.m_ElementIndex = 0;
                    pathOwner.m_State &= ~(PathFlags.Pending | PathFlags.Failed | PathFlags.Stuck | PathFlags.Scheduled | PathFlags.Append | PathFlags.Updated | PathFlags.Divert | PathFlags.DivertObsolete | PathFlags.CachedObsolete);
                    pathOwner.m_State |= PathFlags.Obsolete;
                    EntityManager.SetComponentData(car, pathOwner);
                }

                Mod.log.Info(snappedToReleaseLane
                    ? $"Direct Drive release handoff rebuilt vanilla path from current lane for {car}."
                    : $"Direct Drive release handoff requested vanilla repath from current position for {car}.");
            }
            catch (Exception ex)
            {
                if (m_LogCooldown-- <= 0)
                {
                    Mod.log.Warn($"Direct Drive release path handoff skipped after {ex.GetType().Name}: {ex.Message}");
                    m_LogCooldown = 180;
                }
            }
        }

        private void ClearTrafficPresence()
        {
            if (m_LastTrafficLane == Entity.Null &&
                m_LastTrafficChangeLane == Entity.Null &&
                m_TrafficPresenceCandidateLane == Entity.Null &&
                m_TrafficPresenceCandidateFrames == 0 &&
                m_TrafficPresenceBridgeLane == Entity.Null &&
                m_TrafficPresenceBridgeFramesRemaining == 0 &&
                m_LastTrafficPresenceSyncFrame == 0U &&
                m_TouchedTrafficPresenceLanes.Count == 0)
            {
                return;
            }

            Entity lastTrafficLane = m_LastTrafficLane;
            Entity lastTrafficChangeLane = m_LastTrafficChangeLane;
            Entity car = m_PossessedCar;

            m_LastTrafficLane = Entity.Null;
            m_LastTrafficChangeLane = Entity.Null;
            m_LastTrafficCurvePosition = new float2(-1f, -1f);
            m_LastTrafficChangeCurvePosition = new float2(-1f, -1f);
            m_TrafficPresenceCandidateLane = Entity.Null;
            m_TrafficPresenceCandidateFrames = 0;
            m_TrafficPresenceBridgeLane = Entity.Null;
            m_TrafficPresenceBridgeFramesRemaining = 0;
            m_LastTrafficPresenceSyncFrame = 0U;
            m_LastTrafficPresenceNearbyCleanupFrame = 0U;
            DirectDriveRuntime.ClearTrafficPresenceTarget();

            if (car == Entity.Null)
            {
                m_TouchedTrafficPresenceLanes.Clear();
                return;
            }

            try
            {
                if (lastTrafficLane != Entity.Null)
                    RemoveLaneObject(lastTrafficLane, car);

                if (lastTrafficChangeLane != Entity.Null && lastTrafficChangeLane != lastTrafficLane)
                    RemoveLaneObject(lastTrafficChangeLane, car);

                RemoveTouchedTrafficPresenceLanes(car, Entity.Null, Entity.Null);
            }
            catch (Exception ex)
            {
                if (m_LogCooldown-- <= 0)
                {
                    Mod.log.Warn($"Direct Drive lane presence cleanup skipped after {ex.GetType().Name}: {ex.Message}");
                    m_LogCooldown = 180;
                }
            }

            m_TouchedTrafficPresenceLanes.Clear();
        }

        private void ApplyDirectControl(Entity car)
        {
            DirectDriveRuntime.SanitizeDrivingTuning();
            DirectDriveInputFrame input = DirectDriveRuntime.ConsumeDriveInput();
            ObjectTransform transform = EntityManager.GetComponentData<ObjectTransform>(car);
            Moving moving = EntityManager.GetComponentData<Moving>(car);
            CarNavigation navigation = EntityManager.GetComponentData<CarNavigation>(car);
            CarCurrentLane currentLane = EntityManager.GetComponentData<CarCurrentLane>(car);

            Vector3 position = ToUnityVector(transform.m_Position);
            Quaternion rotation = LevelRotation(ToUnityQuaternion(transform.m_Rotation));
            Vector3 forward = FlattenForward(rotation * Vector3.forward);
            float currentForwardSpeed = Vector3.Dot(ToUnityVector(moving.m_Velocity), forward);
            if (!math.isfinite(m_SpeedMps))
                m_SpeedMps = currentForwardSpeed;

            float dt = kSimulationStepSeconds;
            Vector3 previousPosition = position;
            bool reverseCommand = UpdateReverseState(input.Throttle, input.Brake > 0.1f, input.BrakePressed, currentForwardSpeed);
            float targetSpeedMps = BuildTargetSpeed(input, reverseCommand);
            bool braking = (input.Brake > 0.1f && !reverseCommand) || Mathf.Abs(targetSpeedMps) < Mathf.Abs(m_SpeedMps) - 0.1f;

            m_SpeedMps = MoveSpeed(m_SpeedMps, targetSpeedMps, input.Throttle > 0.1f, input.Brake > 0.1f, reverseCommand, dt);
            rotation = ApplyDirectSteering(rotation, input.Steering, m_SpeedMps, dt);
            rotation = LevelRotation(rotation);
            forward = FlattenForward(rotation * Vector3.forward);
            Vector3 velocity = forward * m_SpeedMps;
            position += velocity * dt;
            Entity collisionEntity = Entity.Null;
            bool vehicleCollision = DirectDriveRuntime.VehicleCollisionEnabled &&
                TryResolveVehicleCollision(car, previousPosition, rotation, forward, ref position, ref velocity, ref m_SpeedMps, out collisionEntity);

            bool needsRoadPose = DirectDriveRuntime.RoadHeightAssist || DirectDriveRuntime.RoadIntentAssist;
            if (needsRoadPose &&
                TryGetRoadPose(currentLane, position, forward, rotation * Vector3.right, out Entity roadLane, out Vector3 lanePosition, out Vector3 laneForward, out Vector3 laneRight, out float curveT, out float curveSign, out float roadXzDistanceSq))
            {
                if (DirectDriveRuntime.RoadHeightAssist)
                {
                    float targetY = lanePosition.y + m_RoadGroundOffsetY;
                    float blendedY = Mathf.LerpUnclamped(position.y, targetY, DirectDriveRuntime.RoadHeightStickiness);
                    position.y = Mathf.MoveTowards(position.y, blendedY, kMaxVerticalCorrectionPerTick);
                    rotation = AlignRotationToRoadGrade(rotation, laneForward);
                    forward = FlattenForward(rotation * Vector3.forward);
                }

                if (!m_RoadLaneWriteCrashguardLogged)
                {
                    Mod.log.Info($"Direct Drive CarCurrentLane writes remain disabled in crashguard build; nearest road lane {roadLane} is used for height assist and lane-object traffic presence.");
                    m_RoadLaneWriteCrashguardLogged = true;
                }

                Vector3 trafficLaneForward = laneForward;
                float trafficCurveSign = curveSign;
                if (EntityManager.TryGetComponent(roadLane, out Game.Net.Curve roadCurve))
                    trafficLaneForward = GetLaneForwardClosestTo(roadCurve, curveT, forward, out trafficCurveSign);

                Vector3 laneDelta = position - lanePosition;
                laneDelta.y = 0f;
                float laneLateralMeters = Mathf.Abs(Vector3.Dot(laneDelta, laneRight.sqrMagnitude > 0.001f ? laneRight.normalized : GetRightFromForward(trafficLaneForward, rotation * Vector3.right)));
                float roadHeightDelta = Mathf.Abs(lanePosition.y - position.y);

                bool clearOffRoadPresence = DirectDriveRuntime.RoadOnlyTrafficPresence &&
                    (laneLateralMeters > kTrafficPresenceRoadOnlyMaxLateralMeters ||
                     roadHeightDelta > kTrafficPresenceMaxRoadHeightDelta);
                if (DirectDriveRuntime.RoadIntentAssist &&
                    !clearOffRoadPresence &&
                    roadHeightDelta <= kTrafficPresenceMaxRoadHeightDelta &&
                    CanSyncTrafficPresence(forward, trafficLaneForward, roadXzDistanceSq))
                {
                    CarCurrentLane trafficPresenceLane = currentLane;
                    ApplyRoadPoseToCurrentLane(ref trafficPresenceLane, roadLane, curveT, trafficCurveSign);
                    trafficPresenceLane.m_ChangeLane = ResolveTrafficPresenceHaloLane(currentLane, roadLane, position, forward, rotation * Vector3.right);
                    ApplyTrafficPresenceLookAhead(ref trafficPresenceLane, roadLane, curveT, trafficCurveSign, Mathf.Abs(m_SpeedMps), out float rearSpan, out float forwardSpan);
                    SyncTrafficPresence(car, trafficPresenceLane, curveT, trafficCurveSign, rearSpan, forwardSpan, Mathf.Abs(m_SpeedMps), Mathf.Abs(input.Steering));
                }
                else
                {
                    ClearTrafficPresence();
                }
            }
            else
            {
                ClearTrafficPresence();
            }

            string intentStatus = DirectDriveRuntime.RoadIntentAssist
                ? (m_LastTrafficLane != Entity.Null
                    ? "Traffic presence active"
                    : "Traffic presence stabilizing")
                : "Road intent assist off";
            if (vehicleCollision)
            {
                braking = true;
                intentStatus = collisionEntity != Entity.Null ? $"Vehicle collision: blocked by {collisionEntity.Index}" : "Vehicle collision";
            }

            velocity = BuildActualVelocity(previousPosition, position, dt, velocity);
            transform.m_Position = ToMathVector(position);
            transform.m_Rotation = ToMathQuaternion(rotation);
            moving.m_Velocity = ToMathVector(velocity);
            moving.m_AngularVelocity = new float3(0f, input.Steering * Mathf.Deg2Rad * DirectDriveRuntime.MaxTurnDegPerSecond, 0f);

            if (DirectDriveRuntime.FreezeVanillaNavigation)
            {
                navigation.m_TargetPosition = transform.m_Position + ToMathVector(forward * kLivePathTargetLeadMeters);
                navigation.m_TargetRotation = default;
                navigation.m_MaxSpeed = 0f;
            }
            else
            {
                navigation.m_TargetPosition = transform.m_Position + moving.m_Velocity * 0.5f;
                navigation.m_TargetRotation = default;
                navigation.m_MaxSpeed = math.abs(m_SpeedMps);
            }

            EntityManager.SetComponentData(car, transform);
            EntityManager.SetComponentData(car, moving);
            EntityManager.SetComponentData(car, navigation);
            ParkLivePathfinding(car);
            UpdateLatestTransformFrame(car, transform, moving, input, braking, reverseCommand);

            DirectDriveRuntime.SetDriving(car, m_PossessedName, position, rotation, math.abs(m_SpeedMps), braking, m_ReverseArmed || m_ReverseActive, intentStatus);
        }

        private static Vector3 BuildActualVelocity(Vector3 previousPosition, Vector3 position, float dt, Vector3 fallbackVelocity)
        {
            if (dt <= 0.0001f)
                return fallbackVelocity;

            Vector3 actualVelocity = (position - previousPosition) / dt;
            return IsFinite(actualVelocity) ? actualVelocity : fallbackVelocity;
        }

        private bool UpdateReverseState(float throttle, bool brakeHeld, bool brakePressed, float currentForwardSpeed)
        {
            bool stoppedForReverse = Mathf.Abs(currentForwardSpeed) < 0.75f && Mathf.Abs(m_SpeedMps) < 0.9f;
            if (throttle > 0.1f)
            {
                m_ReverseArmed = false;
                m_ReverseActive = false;
                return false;
            }

            if (!brakeHeld)
            {
                m_ReverseActive = false;
                if (stoppedForReverse)
                    m_ReverseArmed = true;

                return false;
            }

            if (brakePressed && stoppedForReverse && m_ReverseArmed)
                m_ReverseActive = true;

            return m_ReverseActive;
        }

        private static float BuildTargetSpeed(DirectDriveInputFrame input, bool reverseCommand)
        {
            if (input.Throttle > 0.1f)
                return DirectDriveRuntime.TargetSpeedMph * kMphToMps * input.Throttle;

            if (input.Brake > 0.1f && reverseCommand)
                return -DirectDriveRuntime.ReverseSpeedMph * kMphToMps;

            return 0f;
        }

        private static float MoveSpeed(float current, float target, bool throttleHeld, bool brakeHeld, bool reverseCommand, float dt)
        {
            float rate;
            if (brakeHeld && !reverseCommand)
                rate = DirectDriveRuntime.BrakeMps2;
            else if (reverseCommand)
                rate = DirectDriveRuntime.ReverseAccelerationMps2;
            else if (throttleHeld && Mathf.Abs(target) > Mathf.Abs(current))
                rate = DirectDriveRuntime.AccelerationMps2;
            else
                rate = DirectDriveRuntime.CoastMps2;

            if (throttleHeld && target > 1.2f && current >= 0f && current < 1.25f)
                current = 1.25f;

            float result = Mathf.MoveTowards(current, target, rate * dt);
            if (Mathf.Abs(target) < 0.1f && Mathf.Abs(result) < 0.2f)
                result = 0f;

            return result;
        }

        private static Quaternion ApplyDirectSteering(Quaternion rotation, float steering, float speedMps, float dt)
        {
            if (Mathf.Abs(steering) < 0.001f)
                return rotation;

            float speedBlend = Mathf.Clamp01(Mathf.Abs(speedMps) / 14f);
            float lowSpeedBoost = DirectDriveRuntime.LowSpeedTurnBoost;
            float turnScale = Mathf.Lerp(lowSpeedBoost, 1f, speedBlend);
            float reverseSign = speedMps < -0.1f ? -1f : 1f;
            float yaw = steering * reverseSign * DirectDriveRuntime.MaxTurnDegPerSecond * turnScale * dt;
            return Quaternion.AngleAxis(yaw, Vector3.up) * rotation;
        }

        private string ApplyRoadIntent(Entity car, ref CarCurrentLane currentLane, Vector3 position, Vector3 forward, DirectDriveInputFrame input)
        {
            const CarLaneFlags turnMask = CarLaneFlags.TurnLeft | CarLaneFlags.TurnRight;
            currentLane.m_LaneFlags &= ~turnMask;
            currentLane.m_ChangeLane = Entity.Null;

            if (!DirectDriveRuntime.RoadIntentAssist)
            {
                ClearNavigationBuffer(car);
                return DirectDriveRuntime.FreezeVanillaNavigation ? "Vanilla physical path driving disabled; road intent assist off" : "Road intent assist off";
            }

            if (Mathf.Abs(input.Steering) < 0.28f)
            {
                ClearNavigationBuffer(car);
                return DirectDriveRuntime.FreezeVanillaNavigation ? "Physical path driving disabled; AI turn intent standby" : "AI turn intent standby";
            }

            int side = input.Steering > 0f ? 1 : -1;
            if (!TryQueueTurnIntent(car, currentLane, position, forward, side, out CarLaneFlags turnFlag, out string intentStatus))
            {
                currentLane.m_LaneFlags |= side < 0 ? CarLaneFlags.TurnLeft : CarLaneFlags.TurnRight;
                return side < 0 ? "AI turn intent: left requested, no path connection yet" : "AI turn intent: right requested, no path connection yet";
            }

            currentLane.m_LaneFlags |= turnFlag;
            return intentStatus;
        }

        private bool TryQueueTurnIntent(Entity car, CarCurrentLane currentLane, Vector3 position, Vector3 forward, int side, out CarLaneFlags turnFlag, out string status)
        {
            turnFlag = side < 0 ? CarLaneFlags.TurnLeft : CarLaneFlags.TurnRight;
            status = "";

            if (m_ConnectionLaneQuery == default ||
                m_ConnectionLaneQuery.IsEmptyIgnoreFilter ||
                currentLane.m_Lane == Entity.Null ||
                !EntityManager.TryGetComponent(currentLane.m_Lane, out Game.Net.Curve currentCurve) ||
                currentCurve.m_Length < 1f)
            {
                return false;
            }

            float curveT = Mathf.Clamp01(currentLane.m_CurvePosition.x);
            Vector3 laneForward = GetCurrentLaneForward(currentLane, currentCurve, curveT, forward, out float curveSign);
            Vector3 laneRight = GetRightFromForward(laneForward, Vector3.Cross(Vector3.up, forward));
            bool invertTurnFlags = curveSign < 0f;
            turnFlag = side < 0
                ? (invertTurnFlags ? CarLaneFlags.TurnRight : CarLaneFlags.TurnLeft)
                : (invertTurnFlags ? CarLaneFlags.TurnLeft : CarLaneFlags.TurnRight);

            if (TryApplyCachedTurnIntent(car, currentLane.m_Lane, side, out turnFlag, out status))
                return true;

            Entity bestConnection = Entity.Null;
            Entity bestExit = Entity.Null;
            float2 bestConnectionCurvePosition = default;
            float2 bestExitCurvePosition = default;
            CarLaneFlags bestConnectionFlags = default;
            CarLaneFlags bestExitFlags = default;
            float bestScore = float.MaxValue;
            int scanned = 0;
            int road = 0;
            int sideMatches = 0;

            NativeArray<Entity> entities = m_ConnectionLaneQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity candidate = entities[i];
                    scanned++;
                    if (!EntityManager.TryGetComponent(candidate, out Game.Net.ConnectionLane connectionLane) ||
                        !EntityManager.TryGetComponent(candidate, out Game.Net.LaneConnection laneConnection) ||
                        !EntityManager.TryGetComponent(candidate, out Game.Net.Curve candidateCurve) ||
                        candidateCurve.m_Length < 2f ||
                        (connectionLane.m_Flags & Game.Net.ConnectionLaneFlags.Disabled) != (Game.Net.ConnectionLaneFlags)0 ||
                        (connectionLane.m_Flags & Game.Net.ConnectionLaneFlags.Road) == (Game.Net.ConnectionLaneFlags)0 ||
                        (connectionLane.m_RoadTypes != Game.Net.RoadTypes.None && (connectionLane.m_RoadTypes & Game.Net.RoadTypes.Car) == Game.Net.RoadTypes.None))
                    {
                        continue;
                    }

                    bool startsFromCurrent = laneConnection.m_StartLane == currentLane.m_Lane;
                    bool endsAtCurrent = laneConnection.m_EndLane == currentLane.m_Lane;
                    if (!startsFromCurrent && !endsAtCurrent)
                        continue;

                    road++;
                    float sign = startsFromCurrent ? 1f : -1f;
                    float entryT = startsFromCurrent ? 0f : 1f;
                    Vector3 entry = ToUnityVector(MathUtils.Position(candidateCurve.m_Bezier, entryT));
                    Vector3 entryDelta = entry - position;
                    float forwardDistance = Vector3.Dot(entryDelta, laneForward);
                    float entryDistance = entryDelta.magnitude;
                    if (forwardDistance < -8f || forwardDistance > 72f || entryDistance > 86f)
                        continue;

                    float targetT = Mathf.Clamp01(entryT + sign * Mathf.Min(0.9f, 22f / Mathf.Max(2f, candidateCurve.m_Length)));
                    Vector3 target = ToUnityVector(MathUtils.Position(candidateCurve.m_Bezier, targetT));
                    Vector3 targetForward = GetLaneForwardWithSign(candidateCurve, targetT, sign, laneForward);
                    Vector3 targetDelta = target - position;
                    float sideDistance = Vector3.Dot(targetDelta, laneRight) * side;
                    float turnDot = Vector3.Dot(targetForward, laneRight) * side;
                    float forwardDot = Vector3.Dot(targetForward, laneForward);

                    if (sideDistance < 0.4f || turnDot < 0.1f || forwardDot < -0.55f)
                        continue;

                    sideMatches++;
                    float score = entryDistance + Mathf.Abs(forwardDistance) * 0.25f - turnDot * 34f - Mathf.Max(0f, sideDistance) * 0.35f;
                    if (EntityManager.TryGetComponent(candidate, out Game.Net.CarLane candidateCarLane) &&
                        HasMatchingTurnLaneFlag(candidateCarLane, side, sign))
                    {
                        score -= 14f;
                    }

                    if (score >= bestScore)
                        continue;

                    Entity exitLane = sign > 0f ? laneConnection.m_EndLane : laneConnection.m_StartLane;
                    bool hasExit = TryBuildExitNavigationLane(exitLane, targetForward, side, out float2 exitCurvePosition, out CarLaneFlags exitFlags);
                    bestScore = score;
                    bestConnection = candidate;
                    bestExit = hasExit ? exitLane : Entity.Null;
                    bestConnectionCurvePosition = sign > 0f ? new float2(entryT, 1f) : new float2(entryT, 0f);
                    bestConnectionFlags = BuildNavigationLaneFlags(true, side, sign);
                    bestExitCurvePosition = exitCurvePosition;
                    bestExitFlags = exitFlags;
                }
            }
            finally
            {
                entities.Dispose();
            }

            if (bestConnection == Entity.Null)
            {
                if (--m_TurnLogCooldown <= 0)
                {
                    Mod.log.Info($"Direct Drive AI turn intent missed side={side} scanned={scanned} road={road} side={sideMatches}");
                    m_TurnLogCooldown = 90;
                }
                ClearNavigationBuffer(car);
                return false;
            }

            status = side < 0
                ? $"AI path intent: left queued connection={bestConnection}"
                : $"AI path intent: right queued connection={bestConnection}";
            CacheTurnIntent(
                currentLane.m_Lane,
                side,
                turnFlag,
                bestConnection,
                bestConnectionCurvePosition,
                bestConnectionFlags,
                bestExit,
                bestExitCurvePosition,
                bestExitFlags,
                status);
            WriteTurnIntentNavigation(car, bestConnection, bestConnectionCurvePosition, bestConnectionFlags, bestExit, bestExitCurvePosition, bestExitFlags);
            return true;
        }

        private bool TryApplyCachedTurnIntent(Entity car, Entity lane, int side, out CarLaneFlags turnFlag, out string status)
        {
            turnFlag = default;
            status = "";
            uint frame = m_SimulationSystem != null ? m_SimulationSystem.frameIndex : 0U;
            if (m_TurnIntentCacheLane != lane ||
                m_TurnIntentCacheSide != side ||
                m_TurnIntentCacheConnection == Entity.Null ||
                (frame != 0U && m_TurnIntentCacheFrame != 0U && frame - m_TurnIntentCacheFrame > kTurnIntentCacheFrames) ||
                !EntityManager.Exists(m_TurnIntentCacheConnection) ||
                (m_TurnIntentCacheExit != Entity.Null && !EntityManager.Exists(m_TurnIntentCacheExit)))
            {
                return false;
            }

            turnFlag = m_TurnIntentCacheTurnFlag;
            status = m_TurnIntentCacheStatus;
            WriteTurnIntentNavigation(
                car,
                m_TurnIntentCacheConnection,
                m_TurnIntentCacheConnectionCurvePosition,
                m_TurnIntentCacheConnectionFlags,
                m_TurnIntentCacheExit,
                m_TurnIntentCacheExitCurvePosition,
                m_TurnIntentCacheExitFlags);
            return true;
        }

        private void CacheTurnIntent(Entity lane, int side, CarLaneFlags turnFlag, Entity connection, float2 connectionCurvePosition, CarLaneFlags connectionFlags, Entity exit, float2 exitCurvePosition, CarLaneFlags exitFlags, string status)
        {
            m_TurnIntentCacheLane = lane;
            m_TurnIntentCacheSide = side;
            m_TurnIntentCacheTurnFlag = turnFlag;
            m_TurnIntentCacheConnection = connection;
            m_TurnIntentCacheConnectionCurvePosition = connectionCurvePosition;
            m_TurnIntentCacheConnectionFlags = connectionFlags;
            m_TurnIntentCacheExit = exit;
            m_TurnIntentCacheExitCurvePosition = exitCurvePosition;
            m_TurnIntentCacheExitFlags = exitFlags;
            m_TurnIntentCacheStatus = status;
            m_TurnIntentCacheFrame = m_SimulationSystem != null ? m_SimulationSystem.frameIndex : 0U;
        }

        private void ClearTurnIntentCache()
        {
            m_TurnIntentCacheLane = Entity.Null;
            m_TurnIntentCacheConnection = Entity.Null;
            m_TurnIntentCacheExit = Entity.Null;
            m_TurnIntentCacheSide = 0;
            m_TurnIntentCacheFrame = 0U;
            m_TurnIntentCacheTurnFlag = default;
            m_TurnIntentCacheConnectionFlags = default;
            m_TurnIntentCacheExitFlags = default;
            m_TurnIntentCacheConnectionCurvePosition = default;
            m_TurnIntentCacheExitCurvePosition = default;
            m_TurnIntentCacheStatus = "";
        }

        private void WriteTurnIntentNavigation(Entity car, Entity connection, float2 connectionCurvePosition, CarLaneFlags connectionFlags, Entity exit, float2 exitCurvePosition, CarLaneFlags exitFlags)
        {
            if (!EntityManager.HasBuffer<CarNavigationLane>(car))
                return;

            DynamicBuffer<CarNavigationLane> navigationLanes = EntityManager.GetBuffer<CarNavigationLane>(car);
            navigationLanes.Clear();
            navigationLanes.Add(new CarNavigationLane
            {
                m_Lane = connection,
                m_CurvePosition = connectionCurvePosition,
                m_Flags = connectionFlags
            });

            if (exit != Entity.Null)
            {
                navigationLanes.Add(new CarNavigationLane
                {
                    m_Lane = exit,
                    m_CurvePosition = exitCurvePosition,
                    m_Flags = exitFlags
                });
            }
        }

        private bool TryBuildExitNavigationLane(Entity lane, Vector3 preferredForward, int side, out float2 curvePosition, out CarLaneFlags flags)
        {
            curvePosition = default;
            flags = default;
            if (lane == Entity.Null ||
                !EntityManager.TryGetComponent(lane, out Game.Net.Curve curve) ||
                !EntityManager.TryGetComponent(lane, out Game.Net.CarLane carLane) ||
                curve.m_Length < 1f ||
                (carLane.m_Flags & Game.Net.CarLaneFlags.Forbidden) != (Game.Net.CarLaneFlags)0U)
            {
                return false;
            }

            Vector3 startTangent = ToUnityVector(MathUtils.Tangent(curve.m_Bezier, 0f));
            if (startTangent.sqrMagnitude < 0.001f)
                return false;

            startTangent.Normalize();
            float sign = Vector3.Dot(startTangent, preferredForward) >= 0f ? 1f : -1f;
            curvePosition = sign > 0f ? new float2(0f, 1f) : new float2(1f, 0f);
            flags = BuildNavigationLaneFlags(false, side, sign);
            return true;
        }

        private static CarLaneFlags BuildNavigationLaneFlags(bool connection, int side, float curveSign)
        {
            CarLaneFlags flags = CarLaneFlags.UpdateOptimalLane;
            if (connection)
                flags |= CarLaneFlags.Connection | CarLaneFlags.ResetSpeed | CarLaneFlags.RequestSpace;

            bool invert = curveSign < 0f;
            if (side < 0)
                flags |= invert ? CarLaneFlags.TurnRight : CarLaneFlags.TurnLeft;
            else
                flags |= invert ? CarLaneFlags.TurnLeft : CarLaneFlags.TurnRight;

            return flags;
        }

        private static bool HasMatchingTurnLaneFlag(Game.Net.CarLane carLane, int side, float curveSign)
        {
            bool reversedTravel = curveSign < 0f;
            Game.Net.CarLaneFlags leftFlags = reversedTravel
                ? Game.Net.CarLaneFlags.TurnRight | Game.Net.CarLaneFlags.GentleTurnRight
                : Game.Net.CarLaneFlags.TurnLeft | Game.Net.CarLaneFlags.GentleTurnLeft;
            Game.Net.CarLaneFlags rightFlags = reversedTravel
                ? Game.Net.CarLaneFlags.TurnLeft | Game.Net.CarLaneFlags.GentleTurnLeft
                : Game.Net.CarLaneFlags.TurnRight | Game.Net.CarLaneFlags.GentleTurnRight;
            return (carLane.m_Flags & (side < 0 ? leftFlags : rightFlags)) != (Game.Net.CarLaneFlags)0U;
        }

        private void UpdateLatestTransformFrame(Entity car, ObjectTransform transform, Moving moving, DirectDriveInputFrame input, bool braking, bool reversing)
        {
            if (!EntityManager.HasBuffer<TransformFrame>(car))
                return;

            TransformFlags flags = TransformFlags.RearLights;
            if (braking)
                flags |= TransformFlags.Braking;
            if (reversing)
                flags |= TransformFlags.Reversing;
            if (input.LeftHeld)
                flags |= TransformFlags.TurningLeft;
            if (input.RightHeld)
                flags |= TransformFlags.TurningRight;

            DynamicBuffer<TransformFrame> frames = EntityManager.GetBuffer<TransformFrame>(car);
            if (frames.Length == 0)
                return;

            if (ShouldResyncTransformFrames(frames, transform))
            {
                WriteAllTransformFrames(frames, transform, moving, flags);
                return;
            }

            int frameA = 0;
            int frameB = 0;
            try
            {
                UpdateFrame updateFrame = EntityManager.GetSharedComponentManaged<UpdateFrame>(car);
                ObjectInterpolateSystem.CalculateUpdateFrames(
                    m_SimulationSystem.frameIndex,
                    0f,
                    updateFrame.m_Index,
                    out uint updateFrameA,
                    out uint updateFrameB,
                    out float framePosition);

                frameA = Mathf.Clamp((int)updateFrameA, 0, frames.Length - 1);
                frameB = Mathf.Clamp((int)updateFrameB, 0, frames.Length - 1);
                WritePredictedTransformFrame(frames, frameA, transform, moving, flags, -framePosition);
                WritePredictedTransformFrame(frames, frameB, transform, moving, flags, 1f - framePosition);
                return;
            }
            catch
            {
            }

            WritePredictedTransformFrame(frames, frameA, transform, moving, flags, 0f);
        }

        private void PrimeTransformFrames(Entity car, ObjectTransform transform, Moving moving)
        {
            if (!EntityManager.HasBuffer<TransformFrame>(car))
                return;

            DynamicBuffer<TransformFrame> frames = EntityManager.GetBuffer<TransformFrame>(car);
            if (frames.Length == 0)
                return;

            WriteAllTransformFrames(frames, transform, moving, TransformFlags.RearLights);
        }

        private static bool ShouldResyncTransformFrames(DynamicBuffer<TransformFrame> frames, ObjectTransform transform)
        {
            bool laggedFrame = UnityEngine.Time.unscaledDeltaTime >= kTransformFrameLagResyncSeconds;
            float3 livePosition = transform.m_Position;
            for (int i = 0; i < frames.Length; i++)
            {
                float3 framePosition = frames[i].m_Position;
                if (!math.all(math.isfinite(framePosition)))
                {
                    return true;
                }

                float driftSq = math.lengthsq(framePosition - livePosition);
                if (driftSq > kTransformFrameDriftResyncDistanceSq ||
                    (laggedFrame && driftSq > kTransformFrameLagDriftResyncDistanceSq))
                {
                    return true;
                }
            }

            return false;
        }

        private static void WriteAllTransformFrames(DynamicBuffer<TransformFrame> frames, ObjectTransform transform, Moving moving, TransformFlags flags)
        {
            for (int i = 0; i < frames.Length; i++)
                WritePredictedTransformFrame(frames, i, transform, moving, flags, 0f);
        }

        private static void WritePredictedTransformFrame(DynamicBuffer<TransformFrame> frames, int index, ObjectTransform transform, Moving moving, TransformFlags flags, float frameOffset)
        {
            TransformFrame frame = frames[index];
            float seconds = frameOffset * kInterpolationFrameSeconds;
            frame.m_Position = transform.m_Position + moving.m_Velocity * seconds;
            frame.m_Rotation = OffsetRotation(transform.m_Rotation, moving.m_AngularVelocity.y * seconds);
            frame.m_Velocity = moving.m_Velocity;
            frame.m_Flags = flags;
            frames[index] = frame;
        }

        private static quaternion OffsetRotation(quaternion rotation, float yawRadians)
        {
            if (Mathf.Abs(yawRadians) < 0.0001f)
                return rotation;

            Quaternion current = ToUnityQuaternion(rotation);
            Quaternion offset = Quaternion.AngleAxis(yawRadians * Mathf.Rad2Deg, Vector3.up);
            return ToMathQuaternion(offset * current);
        }

        private static Quaternion AlignRotationToRoadGrade(Quaternion rotation, Vector3 laneForward)
        {
            if (laneForward.sqrMagnitude < 0.001f)
                return rotation;

            Vector3 flatForward = FlattenForward(rotation * Vector3.forward);
            Vector3 roadForward = laneForward.normalized;
            float slopeY = Mathf.Clamp(roadForward.y, -kRoadGradeMaxSlopeY, kRoadGradeMaxSlopeY);
            float horizontalScale = Mathf.Sqrt(Mathf.Max(0.001f, 1f - slopeY * slopeY));
            Vector3 gradedForward = flatForward * horizontalScale + Vector3.up * slopeY;
            if (gradedForward.sqrMagnitude < 0.001f)
                return rotation;

            Quaternion target = Quaternion.LookRotation(gradedForward.normalized, Vector3.up);
            return Quaternion.Slerp(rotation, target, kRoadGradePitchBlend);
        }

        private float ResolveGroundOffset(Entity car, Vector3 position, Vector3 forward, Vector3 right)
        {
            if (EntityManager.TryGetComponent(car, out CarCurrentLane currentLane) &&
                TryGetRoadPose(currentLane, position, forward, right, out _, out Vector3 roadPosition, out _, out _, out _, out _, out _))
            {
                return Mathf.Clamp(position.y - roadPosition.y, -0.25f, 1.25f);
            }

            return 0f;
        }

        private bool TryGetRoadPose(CarCurrentLane currentLane, Vector3 position, Vector3 fallbackForward, Vector3 fallbackRight, out Entity roadLane, out Vector3 lanePosition, out Vector3 laneForward, out Vector3 laneRight, out float curveT, out float curveSign, out float xzDistanceSq)
        {
            roadLane = Entity.Null;
            lanePosition = default;
            laneForward = fallbackForward;
            laneRight = fallbackRight;
            curveT = 0f;
            curveSign = 1f;
            xzDistanceSq = float.MaxValue;

            bool found = false;
            float bestScore = float.MaxValue;
            float bestXzDistanceSq = float.MaxValue;

            if (TryGetLanePose(currentLane, position, fallbackForward, fallbackRight, out Vector3 currentPosition, out Vector3 currentForward, out Vector3 currentRight, out float currentT, out float currentSign, out float currentXzDistanceSq))
            {
                float currentHeightDelta = Mathf.Abs(currentPosition.y - position.y);
                if (currentHeightDelta <= kRoadPoseMaxHeightDelta)
                {
                    found = true;
                    bestXzDistanceSq = currentXzDistanceSq;
                    bestScore = BuildRoadPoseScore(position, currentPosition, currentXzDistanceSq);
                    lanePosition = currentPosition;
                    laneForward = currentForward;
                    laneRight = currentRight;
                    curveT = currentT;
                    curveSign = currentSign;
                    roadLane = currentLane.m_Lane;

                    if (bestXzDistanceSq <= kCurrentRoadPoseAcceptDistanceSq && roadLane != Entity.Null)
                    {
                        xzDistanceSq = bestXzDistanceSq;
                        return true;
                    }
                }
            }

            if (TryFindNearbyRoadPose(position, fallbackForward, fallbackRight, out Entity nearbyLane, out Vector3 nearbyPosition, out Vector3 nearbyForward, out Vector3 nearbyRight, out float nearbyT, out float nearbySign, out float nearbyXzDistanceSq))
            {
                float nearbyScore = BuildRoadPoseScore(position, nearbyPosition, nearbyXzDistanceSq);
                if (!found || nearbyScore < bestScore || bestXzDistanceSq > 16f)
                {
                    found = true;
                    bestXzDistanceSq = nearbyXzDistanceSq;
                    lanePosition = nearbyPosition;
                    laneForward = nearbyForward;
                    laneRight = nearbyRight;
                    curveT = nearbyT;
                    curveSign = nearbySign;
                    roadLane = nearbyLane;
                }
            }

            xzDistanceSq = bestXzDistanceSq;
            return found && bestXzDistanceSq <= kMaxRoadAttachDistanceSq && roadLane != Entity.Null;
        }

        private bool TryFindNearbyRoadPose(Vector3 position, Vector3 fallbackForward, Vector3 fallbackRight, out Entity roadLane, out Vector3 lanePosition, out Vector3 laneForward, out Vector3 laneRight, out float curveT, out float curveSign, out float xzDistanceSq)
        {
            roadLane = Entity.Null;
            lanePosition = default;
            laneForward = fallbackForward;
            laneRight = fallbackRight;
            curveT = 0f;
            curveSign = 1f;
            xzDistanceSq = float.MaxValue;

            if (m_NetSearchSystem == null)
                return false;

            try
            {
                uint frame = GetSimulationFrame();
                if (TryUseCachedNearbyRoadPose(frame, position, fallbackForward, fallbackRight, out roadLane, out lanePosition, out laneForward, out laneRight, out curveT, out curveSign, out xzDistanceSq))
                    return true;

                JobHandle dependencies;
                NativeQuadTree<Entity, QuadTreeBoundsXZ> laneSearchTree = m_NetSearchSystem.GetLaneSearchTree(true, out dependencies);
                dependencies.Complete();

                float3 pos = ToMathVector(position);
                float radius = kNearbyLaneSearchRadius;
                NearbyRoadLaneIterator iterator = new NearbyRoadLaneIterator
                {
                    Bounds = new Bounds3(pos - new float3(radius, radius, radius), pos + new float3(radius, radius, radius)),
                    Position = pos,
                    MaxXzDistanceSq = radius * radius,
                    BestScore = float.MaxValue,
                    CurveData = GetComponentLookup<Game.Net.Curve>(true),
                    CarLaneData = GetComponentLookup<Game.Net.CarLane>(true),
                    ConnectionLaneData = GetComponentLookup<Game.Net.ConnectionLane>(true)
                };

                laneSearchTree.Iterate<NearbyRoadLaneIterator>(ref iterator, 0);
                if (iterator.ResultLane == Entity.Null)
                    return false;

                roadLane = iterator.ResultLane;
                lanePosition = ToUnityVector(iterator.ResultPosition);
                curveT = Mathf.Clamp01(iterator.ResultCurveT);
                xzDistanceSq = iterator.ResultXzDistanceSq;
                Game.Net.Curve curve = iterator.CurveData[roadLane];
                laneForward = GetLaneForwardClosestTo(curve, curveT, fallbackForward, out curveSign);
                laneRight = GetRightFromForward(laneForward, fallbackRight);
                CacheNearbyRoadPose(frame, roadLane, position);
                return true;
            }
            catch (Exception ex)
            {
                if (m_LogCooldown-- <= 0)
                {
                    Mod.log.Warn($"Direct Drive nearby road lane search failed: {ex.GetType().Name}: {ex.Message}");
                    m_LogCooldown = 240;
                }
                return false;
            }
        }

        private bool TryUseCachedNearbyRoadPose(uint frame, Vector3 position, Vector3 fallbackForward, Vector3 fallbackRight, out Entity roadLane, out Vector3 lanePosition, out Vector3 laneForward, out Vector3 laneRight, out float curveT, out float curveSign, out float xzDistanceSq)
        {
            roadLane = Entity.Null;
            lanePosition = default;
            laneForward = fallbackForward;
            laneRight = fallbackRight;
            curveT = 0f;
            curveSign = 1f;
            xzDistanceSq = float.MaxValue;

            if (m_NearbyRoadPoseCacheLane == Entity.Null ||
                !EntityManager.Exists(m_NearbyRoadPoseCacheLane) ||
                (frame != 0U && m_NearbyRoadPoseCacheFrame != 0U && frame - m_NearbyRoadPoseCacheFrame > kNearbyRoadPoseCacheFrames))
            {
                return false;
            }

            Vector3 delta = position - m_NearbyRoadPoseCacheCenter;
            delta.y = 0f;
            if (delta.sqrMagnitude > kNearbyRoadPoseCacheReuseDistanceSq)
                return false;

            if (!TryGetCurvePose(m_NearbyRoadPoseCacheLane, default, position, fallbackForward, fallbackRight, out lanePosition, out laneForward, out laneRight, out curveT, out curveSign, out xzDistanceSq))
            {
                ClearNearbyRoadPoseCache();
                return false;
            }

            roadLane = m_NearbyRoadPoseCacheLane;
            return true;
        }

        private void CacheNearbyRoadPose(uint frame, Entity lane, Vector3 position)
        {
            m_NearbyRoadPoseCacheLane = lane;
            m_NearbyRoadPoseCacheCenter = position;
            m_NearbyRoadPoseCacheFrame = frame;
        }

        private void ClearNearbyRoadPoseCache()
        {
            m_NearbyRoadPoseCacheLane = Entity.Null;
            m_NearbyRoadPoseCacheCenter = Vector3.zero;
            m_NearbyRoadPoseCacheFrame = 0U;
        }

        private uint GetSimulationFrame()
        {
            return m_SimulationSystem != null ? m_SimulationSystem.frameIndex : (uint)Mathf.Max(0, UnityEngine.Time.frameCount);
        }

        private static float BuildRoadPoseScore(Vector3 position, Vector3 roadPosition, float xzDistanceSq)
        {
            float heightDelta = roadPosition.y - position.y;
            return xzDistanceSq + heightDelta * heightDelta * 2f;
        }

        private bool TryGetLanePose(CarCurrentLane currentLane, Vector3 position, Vector3 fallbackForward, Vector3 fallbackRight, out Vector3 lanePosition, out Vector3 laneForward, out Vector3 laneRight, out float curveT, out float curveSign, out float xzDistanceSq)
        {
            lanePosition = default;
            laneForward = fallbackForward;
            laneRight = fallbackRight;
            curveT = 0f;
            curveSign = 1f;
            xzDistanceSq = float.MaxValue;

            if (currentLane.m_Lane == Entity.Null ||
                !TryGetCurvePose(currentLane.m_Lane, currentLane, position, fallbackForward, fallbackRight, out lanePosition, out laneForward, out laneRight, out curveT, out curveSign, out xzDistanceSq))
            {
                return false;
            }

            return true;
        }

        private bool TryGetCurvePose(Entity lane, CarCurrentLane currentLane, Vector3 position, Vector3 fallbackForward, Vector3 fallbackRight, out Vector3 lanePosition, out Vector3 laneForward, out Vector3 laneRight, out float curveT, out float curveSign, out float xzDistanceSq)
        {
            lanePosition = default;
            laneForward = fallbackForward;
            laneRight = fallbackRight;
            curveT = 0f;
            curveSign = 1f;
            xzDistanceSq = float.MaxValue;

            if (lane == Entity.Null ||
                !EntityManager.TryGetComponent(lane, out Game.Net.Curve curve) ||
                curve.m_Length < 1f)
            {
                return false;
            }

            MathUtils.Distance(curve.m_Bezier, ToMathVector(position), out float closestT);
            curveT = Mathf.Clamp01(closestT);
            lanePosition = ToUnityVector(MathUtils.Position(curve.m_Bezier, curveT));
            float dx = lanePosition.x - position.x;
            float dz = lanePosition.z - position.z;
            xzDistanceSq = dx * dx + dz * dz;
            if (lane == currentLane.m_Lane)
                laneForward = GetCurrentLaneForward(currentLane, curve, curveT, fallbackForward, out curveSign);
            else
                laneForward = GetLaneForwardClosestTo(curve, curveT, fallbackForward, out curveSign);
            laneRight = GetRightFromForward(laneForward, fallbackRight);
            return true;
        }

        private static void ApplyRoadPoseToCurrentLane(ref CarCurrentLane currentLane, Entity roadLane, float curveT, float curveSign)
        {
            if (roadLane == Entity.Null)
                return;

            curveT = Mathf.Clamp01(curveT);
            currentLane.m_Lane = roadLane;
            currentLane.m_ChangeLane = Entity.Null;
            currentLane.m_ChangeProgress = 0f;
            currentLane.m_CurvePosition.x = curveT;
            currentLane.m_CurvePosition.y = curveT;
            currentLane.m_CurvePosition.z = curveSign < 0f ? 0f : 1f;
            currentLane.m_LanePosition = 0f;
        }

        private bool CanSyncTrafficPresence(Vector3 vehicleForward, Vector3 laneForward, float xzDistanceSq)
        {
            if (xzDistanceSq > kTrafficPresenceMaxRoadAttachDistanceSq)
                return false;

            Vector3 flatVehicleForward = FlattenForward(vehicleForward);
            Vector3 flatLaneForward = FlattenForward(laneForward);
            return Vector3.Dot(flatVehicleForward, flatLaneForward) >= kTrafficPresenceMinForwardDot;
        }

        private Entity ResolveTrafficPresenceHaloLane(CarCurrentLane currentLane, Entity primaryLane, Vector3 position, Vector3 forward, Vector3 fallbackRight)
        {
            if (!DirectDriveRuntime.TrafficPresenceHaloEnabled)
                return Entity.Null;

            Entity bestLane = Entity.Null;
            float bestScore = float.MaxValue;
            TryScoreTrafficPresenceHaloCandidate(currentLane.m_Lane, primaryLane, position, forward, fallbackRight, ref bestLane, ref bestScore);
            TryScoreTrafficPresenceHaloCandidate(m_LastTrafficLane, primaryLane, position, forward, fallbackRight, ref bestLane, ref bestScore);
            TryFindSameRoadTrafficPresenceHalo(primaryLane, position, forward, fallbackRight, ref bestLane, ref bestScore);

            return bestLane;
        }

        private void TryFindSameRoadTrafficPresenceHalo(Entity primaryLane, Vector3 position, Vector3 forward, Vector3 fallbackRight, ref Entity bestLane, ref float bestScore)
        {
            if (primaryLane == Entity.Null ||
                !EntityManager.TryGetComponent(primaryLane, out Owner owner) ||
                owner.m_Owner == Entity.Null ||
                !EntityManager.Exists(owner.m_Owner) ||
                !EntityManager.HasBuffer<Game.Net.SubLane>(owner.m_Owner) ||
                !EntityManager.TryGetComponent(primaryLane, out Game.Net.CarLane primaryCarLane))
            {
                return;
            }

            DynamicBuffer<Game.Net.SubLane> subLanes = EntityManager.GetBuffer<Game.Net.SubLane>(owner.m_Owner, true);
            for (int i = 0; i < subLanes.Length; i++)
            {
                Entity candidate = subLanes[i].m_SubLane;
                if (candidate == primaryLane ||
                    !EntityManager.TryGetComponent(candidate, out Game.Net.CarLane candidateCarLane) ||
                    candidateCarLane.m_CarriagewayGroup != primaryCarLane.m_CarriagewayGroup)
                {
                    continue;
                }

                TryScoreTrafficPresenceHaloCandidate(candidate, primaryLane, position, forward, fallbackRight, ref bestLane, ref bestScore);
            }
        }

        private bool TryScoreTrafficPresenceHaloCandidate(Entity candidateLane, Entity primaryLane, Vector3 position, Vector3 forward, Vector3 fallbackRight, ref Entity bestLane, ref float bestScore)
        {
            if (candidateLane == Entity.Null ||
                candidateLane == primaryLane ||
                !EntityManager.Exists(candidateLane) ||
                !IsCompatibleTrafficHaloLane(primaryLane, candidateLane) ||
                !EntityManager.TryGetComponent(candidateLane, out Game.Net.CarLane candidateCarLane) ||
                (candidateCarLane.m_Flags & Game.Net.CarLaneFlags.Forbidden) != (Game.Net.CarLaneFlags)0U)
            {
                return false;
            }

            if (!TryGetCurvePose(candidateLane, default, position, forward, fallbackRight, out Vector3 lanePosition, out Vector3 laneForward, out Vector3 laneRight, out _, out _, out float xzDistanceSq))
                return false;

            if (xzDistanceSq > kTrafficPresenceHaloMaxRoadAttachDistanceSq)
                return false;

            float heightDelta = Mathf.Abs(lanePosition.y - position.y);
            if (heightDelta > kTrafficPresenceHaloMaxHeightDelta)
                return false;

            Vector3 laneDelta = position - lanePosition;
            laneDelta.y = 0f;
            Vector3 right = laneRight.sqrMagnitude > 0.001f ? laneRight.normalized : GetRightFromForward(laneForward, fallbackRight);
            float lateralMeters = Mathf.Abs(Vector3.Dot(laneDelta, right));
            if (lateralMeters > kTrafficPresenceHaloMaxLateralMeters)
                return false;

            Vector3 flatVehicleForward = FlattenForward(forward);
            Vector3 flatLaneForward = FlattenForward(laneForward);
            float forwardDot = Vector3.Dot(flatVehicleForward, flatLaneForward);
            if (forwardDot < kTrafficPresenceHaloMinForwardDot)
                return false;

            float score = lateralMeters * 2.5f + xzDistanceSq * 0.08f + heightDelta * 4f - forwardDot;
            if (score >= bestScore)
                return false;

            bestScore = score;
            bestLane = candidateLane;
            return true;
        }

        private bool IsCompatibleTrafficHaloLane(Entity primaryLane, Entity candidateLane)
        {
            if (primaryLane == Entity.Null || candidateLane == Entity.Null)
                return false;

            if (EntityManager.TryGetComponent(primaryLane, out Game.Net.CarLane primaryCarLane) &&
                EntityManager.TryGetComponent(candidateLane, out Game.Net.CarLane candidateCarLane) &&
                candidateCarLane.m_CarriagewayGroup != primaryCarLane.m_CarriagewayGroup)
            {
                return false;
            }

            bool primaryHasOwner = EntityManager.TryGetComponent(primaryLane, out Owner primaryOwner);
            bool candidateHasOwner = EntityManager.TryGetComponent(candidateLane, out Owner candidateOwner);
            if (primaryHasOwner && candidateHasOwner)
                return primaryOwner.m_Owner == candidateOwner.m_Owner;

            return !primaryHasOwner && !candidateHasOwner;
        }

        private void ApplyTrafficPresenceLookAhead(ref CarCurrentLane currentLane, Entity roadLane, float curveT, float curveSign, float speedMps, out float rearSpan, out float forwardSpan)
        {
            forwardSpan = kTrafficPresenceMinCurveSpan;
            rearSpan = kTrafficPresenceMinCurveSpan * 0.5f;
            if (roadLane != Entity.Null &&
                EntityManager.TryGetComponent(roadLane, out Game.Net.Curve curve) &&
                curve.m_Length > 1f)
            {
                float speedLead = Mathf.Clamp(speedMps * kTrafficPresenceSpeedLeadSeconds, 0f, kTrafficPresenceMaxForwardMeters - kTrafficPresenceForwardMeters);
                float forwardMeters = Mathf.Min(kTrafficPresenceMaxForwardMeters, kTrafficPresenceForwardMeters + speedLead);
                float rearMeters = Mathf.Min(kTrafficPresenceMaxRearMeters, kTrafficPresenceRearMeters + speedLead * 0.35f);
                forwardSpan = Mathf.Clamp(forwardMeters / curve.m_Length, kTrafficPresenceMinCurveSpan, kTrafficPresenceMaxCurveSpan);
                rearSpan = Mathf.Clamp(rearMeters / curve.m_Length, kTrafficPresenceMinCurveSpan * 0.5f, kTrafficPresenceMaxCurveSpan * 0.8f);
            }

            currentLane.m_CurvePosition.x = Mathf.Clamp01(curveT + (curveSign < 0f ? rearSpan : -rearSpan));
            currentLane.m_CurvePosition.y = Mathf.Clamp01(curveT + (curveSign < 0f ? -forwardSpan : forwardSpan));
            currentLane.m_CurvePosition.z = curveSign < 0f ? 0f : 1f;
        }

        private void SyncTrafficPresence(Entity car, CarCurrentLane currentLane, float curveT, float curveSign, float rearSpan, float forwardSpan, float speedMps, float steeringAbs)
        {
            Entity lane = currentLane.m_Lane;
            if (lane == Entity.Null)
            {
                ClearTrafficPresence();
                return;
            }

            Entity haloLane = DirectDriveRuntime.TrafficPresenceHaloEnabled && currentLane.m_ChangeLane != lane
                ? currentLane.m_ChangeLane
                : Entity.Null;
            currentLane.m_ChangeLane = haloLane;
            if (lane != m_TrafficPresenceCandidateLane)
            {
                if (m_LastTrafficChangeLane != Entity.Null && m_LastTrafficChangeLane != haloLane)
                    RemoveLaneObject(m_LastTrafficChangeLane, car);

                m_TrafficPresenceBridgeLane = Entity.Null;
                m_TrafficPresenceBridgeFramesRemaining = 0;
                m_TrafficPresenceCandidateLane = lane;
                m_TrafficPresenceCandidateFrames = 1;
            }

            if (m_TrafficPresenceCandidateFrames < kTrafficPresenceStableFrames)
            {
                m_TrafficPresenceCandidateFrames++;
                return;
            }

            uint frame = m_SimulationSystem != null ? m_SimulationSystem.frameIndex : 0U;
            if (frame != 0U &&
                m_LastTrafficPresenceSyncFrame != 0U &&
                frame - m_LastTrafficPresenceSyncFrame < kTrafficPresenceMinSyncFrames &&
                lane == m_LastTrafficLane)
            {
                return;
            }

            try
            {
                m_TrafficPresenceBridgeLane = Entity.Null;
                m_TrafficPresenceBridgeFramesRemaining = 0;

                SyncTrafficPresenceSafe(car, currentLane, curveT, curveSign, rearSpan, forwardSpan);
                m_LastTrafficPresenceSyncFrame = frame;

                if (!m_TrafficPresenceRestoredLogged)
                {
                    Mod.log.Info("Direct Drive stable traffic presence restored without writing CarCurrentLane back to the possessed vehicle.");
                    m_TrafficPresenceRestoredLogged = true;
                }
            }
            catch (Exception ex)
            {
                if (m_LogCooldown-- <= 0)
                {
                    Mod.log.Warn($"Direct Drive stable lane presence sync skipped after {ex.GetType().Name}: {ex.Message}");
                    m_LogCooldown = 180;
                }

                ClearTrafficPresence();
            }
        }

        private void SyncTrafficPresenceSafe(Entity car, CarCurrentLane currentLane, float curveT, float curveSign, float rearSpan, float forwardSpan)
        {
            Entity lane = currentLane.m_Lane;
            float2 curvePosition = currentLane.m_CurvePosition.xy;
            if (!math.all(math.isfinite(curvePosition)))
                return;

            Entity changeLane = DirectDriveRuntime.TrafficPresenceHaloEnabled && currentLane.m_ChangeLane != lane
                ? currentLane.m_ChangeLane
                : Entity.Null;
            float2 changeCurvePosition = changeLane != Entity.Null
                ? BuildTrafficPresenceCurvePosition(car, changeLane, curveT, curveSign, rearSpan, forwardSpan)
                : new float2(-1f, -1f);
            DirectDriveRuntime.SetTrafficPresenceTarget(lane, changeLane, curvePosition.x, curvePosition.y, curveT, curveSign, rearSpan, forwardSpan);

            RemoveTouchedTrafficPresenceLanes(car, lane, changeLane);

            bool laneChanged = lane != m_LastTrafficLane || changeLane != m_LastTrafficChangeLane;
            bool curveMoved = math.lengthsq(curvePosition - m_LastTrafficCurvePosition) > kTrafficPresenceCurveUpdateThresholdSq;
            bool changeCurveMoved = changeLane != Entity.Null &&
                math.lengthsq(changeCurvePosition - m_LastTrafficChangeCurvePosition) > kTrafficPresenceCurveUpdateThresholdSq;
            uint cleanupFrame = GetSimulationFrame();
            bool cleanupNearby = laneChanged ||
                cleanupFrame == 0U ||
                m_LastTrafficPresenceNearbyCleanupFrame == 0U ||
                cleanupFrame - m_LastTrafficPresenceNearbyCleanupFrame >= kTrafficPresenceNearbyCleanupFrames;
            if (cleanupNearby)
            {
                RemoveNearbyTrafficPresenceLanes(car, lane, changeLane);
                m_LastTrafficPresenceNearbyCleanupFrame = cleanupFrame;
            }

            if (!laneChanged && !curveMoved && !changeCurveMoved)
            {
                RecordTrafficPresenceDebug(lane, curvePosition, changeLane, changeCurvePosition, "active");
                return;
            }

            if (m_LastTrafficLane != Entity.Null && m_LastTrafficLane != lane)
                RemoveLaneObject(m_LastTrafficLane, car);

            if (m_LastTrafficChangeLane != Entity.Null && m_LastTrafficChangeLane != changeLane)
                RemoveLaneObject(m_LastTrafficChangeLane, car);

            if (lane != Entity.Null && UpsertLaneObject(lane, car, curvePosition))
                TrackTrafficPresenceLane(lane);

            if (changeLane != Entity.Null && UpsertLaneObject(changeLane, car, changeCurvePosition))
                TrackTrafficPresenceLane(changeLane);

            m_LastTrafficLane = lane;
            m_LastTrafficChangeLane = changeLane;
            m_LastTrafficCurvePosition = curvePosition;
            m_LastTrafficChangeCurvePosition = changeCurvePosition;
            RecordTrafficPresenceDebug(lane, curvePosition, changeLane, changeCurvePosition, changeLane != Entity.Null ? $"halo active {changeLane.Index}" : "halo same-lane");
        }

        private void TrackTrafficPresenceLane(Entity lane)
        {
            if (lane == Entity.Null)
                return;

            for (int i = 0; i < m_TouchedTrafficPresenceLanes.Count; i++)
            {
                if (m_TouchedTrafficPresenceLanes[i] == lane)
                    return;
            }

            m_TouchedTrafficPresenceLanes.Add(lane);
        }

        private void RemoveTouchedTrafficPresenceLanes(Entity car, Entity keepLane, Entity keepChangeLane)
        {
            for (int i = m_TouchedTrafficPresenceLanes.Count - 1; i >= 0; i--)
            {
                Entity lane = m_TouchedTrafficPresenceLanes[i];
                if (lane == Entity.Null ||
                    lane == keepLane ||
                    lane == keepChangeLane)
                {
                    continue;
                }

                RemoveLaneObject(lane, car);
                m_TouchedTrafficPresenceLanes.RemoveAt(i);
            }
        }

        private void RemoveNearbyTrafficPresenceLanes(Entity car, Entity keepLane, Entity keepChangeLane)
        {
            if (car == Entity.Null ||
                m_NetSearchSystem == null ||
                !EntityManager.TryGetComponent(car, out ObjectTransform transform))
            {
                return;
            }

            NativeList<Entity> nearbyLanes = new NativeList<Entity>(Allocator.Temp);
            try
            {
                JobHandle dependencies;
                NativeQuadTree<Entity, QuadTreeBoundsXZ> laneSearchTree = m_NetSearchSystem.GetLaneSearchTree(true, out dependencies);
                dependencies.Complete();

                float radius = kTrafficPresenceStaleCleanupRadius;
                float3 position = transform.m_Position;
                NearbyTrafficPresenceCleanupIterator iterator = new NearbyTrafficPresenceCleanupIterator
                {
                    Bounds = new Bounds3(position - new float3(radius, radius, radius), position + new float3(radius, radius, radius)),
                    Position = position,
                    MaxXzDistanceSq = radius * radius,
                    CurveData = GetComponentLookup<Game.Net.Curve>(true),
                    CarLaneData = GetComponentLookup<Game.Net.CarLane>(true),
                    ConnectionLaneData = GetComponentLookup<Game.Net.ConnectionLane>(true),
                    ResultLanes = nearbyLanes
                };

                laneSearchTree.Iterate<NearbyTrafficPresenceCleanupIterator>(ref iterator, 0);
                for (int i = 0; i < nearbyLanes.Length; i++)
                {
                    Entity lane = nearbyLanes[i];
                    if (lane != keepLane && lane != keepChangeLane)
                        RemoveLaneObject(lane, car);
                }
            }
            catch (Exception ex)
            {
                if (m_LogCooldown-- <= 0)
                {
                    Mod.log.Warn($"Direct Drive nearby stale traffic presence cleanup skipped after {ex.GetType().Name}: {ex.Message}");
                    m_LogCooldown = 240;
                }
            }
            finally
            {
                if (nearbyLanes.IsCreated)
                    nearbyLanes.Dispose();
            }
        }

        private float2 BuildTrafficPresenceCurvePosition(Entity car, Entity lane, float fallbackCurveT, float fallbackCurveSign, float rearSpan, float forwardSpan)
        {
            float curveT = Mathf.Clamp01(fallbackCurveT);
            float curveSign = fallbackCurveSign < 0f ? -1f : 1f;
            if (EntityManager.TryGetComponent(car, out ObjectTransform transform) &&
                EntityManager.TryGetComponent(lane, out Game.Net.Curve curve) &&
                curve.m_Length > 1f)
            {
                MathUtils.Distance(curve.m_Bezier, transform.m_Position, out curveT);
                curveT = Mathf.Clamp01(curveT);

                Vector3 fallbackForward = FlattenForward(ToUnityQuaternion(transform.m_Rotation) * Vector3.forward);
                GetLaneForwardClosestTo(curve, curveT, fallbackForward, out curveSign);
            }

            rearSpan = Mathf.Clamp(rearSpan, 0.0001f, 0.25f);
            forwardSpan = Mathf.Clamp(forwardSpan, 0.0001f, 0.25f);
            float start = Mathf.Clamp01(curveT + (curveSign < 0f ? rearSpan : -rearSpan));
            float end = Mathf.Clamp01(curveT + (curveSign < 0f ? -forwardSpan : forwardSpan));
            return new float2(start, end);
        }

        private void RecordTrafficPresenceDebug(Entity primaryLane, float2 primaryCurvePosition, Entity haloLane, float2 haloCurvePosition, string status)
        {
            if (!DirectDriveRuntime.TrafficPresenceDebugEnabled)
            {
                DirectDriveRuntime.ClearTrafficPresenceDebug();
                return;
            }

            string haloText = haloLane != Entity.Null ? haloLane.Index.ToString() : "none";
            DirectDriveRuntime.BeginTrafficPresenceDebug($"primary {primaryLane.Index}, halo {haloText}, {status}");
            AddTrafficPresenceDebugSegment(primaryLane, primaryCurvePosition, $"PRIMARY {primaryLane.Index}", DirectDriveRuntime.kTrafficPresenceDebugPrimary);

            if (haloLane != Entity.Null && haloLane != primaryLane)
                AddTrafficPresenceDebugSegment(haloLane, haloCurvePosition, $"HALO {haloLane.Index}", DirectDriveRuntime.kTrafficPresenceDebugHalo);
        }

        private void AddTrafficPresenceDebugSegment(Entity lane, float2 curvePosition, string label, int kind)
        {
            if (lane == Entity.Null ||
                !EntityManager.TryGetComponent(lane, out Game.Net.Curve curve) ||
                curve.m_Length < 1f ||
                !math.all(math.isfinite(curvePosition)))
            {
                return;
            }

            float startT = math.saturate(curvePosition.x);
            float endT = math.saturate(curvePosition.y);
            float labelT = math.saturate((startT + endT) * 0.5f);
            Vector3 lift = Vector3.up * 0.32f;
            Vector3 start = ToUnityVector(MathUtils.Position(curve.m_Bezier, startT)) + lift;
            Vector3 end = ToUnityVector(MathUtils.Position(curve.m_Bezier, endT)) + lift;
            Vector3 labelPosition = ToUnityVector(MathUtils.Position(curve.m_Bezier, labelT)) + Vector3.up * 1.15f;
            DirectDriveRuntime.AddTrafficPresenceDebugSegment(start, end, labelPosition, label, kind);
        }

        private bool UpsertLaneObject(Entity lane, Entity laneObject, float2 curvePosition)
        {
            if (lane == Entity.Null ||
                laneObject == Entity.Null ||
                !EntityManager.Exists(lane) ||
                !EntityManager.HasBuffer<Game.Net.LaneObject>(lane))
            {
                return false;
            }

            DynamicBuffer<Game.Net.LaneObject> laneObjects = EntityManager.GetBuffer<Game.Net.LaneObject>(lane);
            Game.Net.NetUtils.UpdateLaneObject(laneObjects, laneObject, curvePosition);
            return true;
        }

        private bool RemoveLaneObject(Entity lane, Entity laneObject)
        {
            if (lane == Entity.Null ||
                laneObject == Entity.Null ||
                !EntityManager.Exists(lane) ||
                !EntityManager.HasBuffer<Game.Net.LaneObject>(lane))
            {
                return false;
            }

            DynamicBuffer<Game.Net.LaneObject> laneObjects = EntityManager.GetBuffer<Game.Net.LaneObject>(lane);
            int before = laneObjects.Length;
            for (int i = laneObjects.Length - 1; i >= 0; i--)
            {
                if (laneObjects[i].m_LaneObject == laneObject)
                    laneObjects.RemoveAt(i);
            }

            return laneObjects.Length != before;
        }

        private struct NearbyTrafficPresenceCleanupIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>, IUnsafeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds3 Bounds;
            public float3 Position;
            public float MaxXzDistanceSq;
            public ComponentLookup<Game.Net.Curve> CurveData;
            public ComponentLookup<Game.Net.CarLane> CarLaneData;
            public ComponentLookup<Game.Net.ConnectionLane> ConnectionLaneData;
            public NativeList<Entity> ResultLanes;

            public bool Intersect(QuadTreeBoundsXZ bounds)
            {
                return MathUtils.Intersect(bounds.m_Bounds, Bounds);
            }

            public void Iterate(QuadTreeBoundsXZ bounds, Entity lane)
            {
                if (!MathUtils.Intersect(bounds.m_Bounds, Bounds) ||
                    !CurveData.HasComponent(lane) ||
                    !IsDriveableRoadLane(lane))
                {
                    return;
                }

                Game.Net.Curve curve = CurveData[lane];
                if (curve.m_Length < 1f)
                    return;

                MathUtils.Distance(curve.m_Bezier, Position, out float t);
                t = math.saturate(t);
                float3 lanePosition = MathUtils.Position(curve.m_Bezier, t);
                float2 delta = lanePosition.xz - Position.xz;
                if (math.lengthsq(delta) > MaxXzDistanceSq ||
                    math.abs(lanePosition.y - Position.y) > kRoadPoseMaxHeightDelta)
                {
                    return;
                }

                ResultLanes.Add(lane);
            }

            private bool IsDriveableRoadLane(Entity lane)
            {
                if (CarLaneData.HasComponent(lane))
                {
                    Game.Net.CarLane carLane = CarLaneData[lane];
                    return (carLane.m_Flags & Game.Net.CarLaneFlags.Forbidden) == (Game.Net.CarLaneFlags)0U;
                }

                if (ConnectionLaneData.HasComponent(lane))
                {
                    Game.Net.ConnectionLane connectionLane = ConnectionLaneData[lane];
                    return (connectionLane.m_Flags & Game.Net.ConnectionLaneFlags.Road) != (Game.Net.ConnectionLaneFlags)0 &&
                           (connectionLane.m_Flags & Game.Net.ConnectionLaneFlags.Disabled) == (Game.Net.ConnectionLaneFlags)0 &&
                           (connectionLane.m_RoadTypes == Game.Net.RoadTypes.None || (connectionLane.m_RoadTypes & Game.Net.RoadTypes.Car) != Game.Net.RoadTypes.None);
                }

                return false;
            }
        }

        private struct NearbyRoadLaneIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>, IUnsafeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds3 Bounds;
            public float3 Position;
            public float MaxXzDistanceSq;
            public float BestScore;
            public Entity ResultLane;
            public float ResultCurveT;
            public float ResultXzDistanceSq;
            public float3 ResultPosition;
            public ComponentLookup<Game.Net.Curve> CurveData;
            public ComponentLookup<Game.Net.CarLane> CarLaneData;
            public ComponentLookup<Game.Net.ConnectionLane> ConnectionLaneData;

            public bool Intersect(QuadTreeBoundsXZ bounds)
            {
                return MathUtils.Intersect(bounds.m_Bounds, Bounds);
            }

            public void Iterate(QuadTreeBoundsXZ bounds, Entity lane)
            {
                if (!MathUtils.Intersect(bounds.m_Bounds, Bounds) ||
                    !CurveData.HasComponent(lane) ||
                    !IsDriveableRoadLane(lane))
                {
                    return;
                }

                Game.Net.Curve curve = CurveData[lane];
                if (curve.m_Length < 1f)
                    return;

                MathUtils.Distance(curve.m_Bezier, Position, out float t);
                t = math.saturate(t);
                float3 lanePosition = MathUtils.Position(curve.m_Bezier, t);
                float2 delta = lanePosition.xz - Position.xz;
                float xzDistanceSq = math.lengthsq(delta);
                if (xzDistanceSq > MaxXzDistanceSq)
                    return;

                float heightDelta = lanePosition.y - Position.y;
                if (math.abs(heightDelta) > kRoadPoseMaxHeightDelta)
                    return;

                float score = xzDistanceSq + heightDelta * heightDelta * 2f;
                if (score >= BestScore)
                    return;

                BestScore = score;
                ResultLane = lane;
                ResultCurveT = t;
                ResultXzDistanceSq = xzDistanceSq;
                ResultPosition = lanePosition;
            }

            private bool IsDriveableRoadLane(Entity lane)
            {
                if (CarLaneData.HasComponent(lane))
                {
                    Game.Net.CarLane carLane = CarLaneData[lane];
                    return (carLane.m_Flags & Game.Net.CarLaneFlags.Forbidden) == (Game.Net.CarLaneFlags)0U;
                }

                if (ConnectionLaneData.HasComponent(lane))
                {
                    Game.Net.ConnectionLane connectionLane = ConnectionLaneData[lane];
                    return (connectionLane.m_Flags & Game.Net.ConnectionLaneFlags.Road) != (Game.Net.ConnectionLaneFlags)0 &&
                           (connectionLane.m_Flags & Game.Net.ConnectionLaneFlags.Disabled) == (Game.Net.ConnectionLaneFlags)0 &&
                           (connectionLane.m_RoadTypes == Game.Net.RoadTypes.None || (connectionLane.m_RoadTypes & Game.Net.RoadTypes.Car) != Game.Net.RoadTypes.None);
                }

                return false;
            }
        }

        private void ClearNavigationBuffer(Entity car)
        {
            ClearTurnIntentCache();
            ClearPathElementBuffer(car);
            if (!EntityManager.HasBuffer<CarNavigationLane>(car))
                return;

            DynamicBuffer<CarNavigationLane> navigationLanes = EntityManager.GetBuffer<CarNavigationLane>(car);
            if (navigationLanes.Length > 0)
                navigationLanes.Clear();
        }

        private void ParkLivePathfinding(Entity car)
        {
            if (car == Entity.Null || !EntityManager.Exists(car))
                return;

            ClearPathElementBuffer(car);
            if (!EntityManager.TryGetComponent(car, out PathOwner pathOwner))
                return;

            pathOwner.m_ElementIndex = 0;
            pathOwner.m_State &= ~(PathFlags.Failed | PathFlags.Stuck | PathFlags.Scheduled | PathFlags.Append | PathFlags.Updated | PathFlags.Obsolete | PathFlags.Divert | PathFlags.DivertObsolete | PathFlags.CachedObsolete);
            pathOwner.m_State |= PathFlags.Pending;
            EntityManager.SetComponentData(car, pathOwner);
        }

        private void ClearPathElementBuffer(Entity car)
        {
            if (!EntityManager.HasBuffer<PathElement>(car))
                return;

            DynamicBuffer<PathElement> pathElements = EntityManager.GetBuffer<PathElement>(car);
            if (pathElements.Length > 0)
                pathElements.Clear();
        }

        private void ClearPossessionFocus(Entity car, bool restoreGameplayController)
        {
            try
            {
                if (car != Entity.Null &&
                    m_SelectedInfo != null &&
                    m_SelectedInfo.selectedEntity == car)
                {
                    m_SelectedInfo.SetSelection(Entity.Null);
                }

                if (car != Entity.Null &&
                    m_ToolSystem != null &&
                    m_ToolSystem.selected == car)
                {
                    m_ToolSystem.selected = Entity.Null;
                    m_ToolSystem.selectedIndex = -1;
                }

                if (m_CameraUpdateSystem != null &&
                    m_CameraUpdateSystem.orbitCameraController != null &&
                    (car == Entity.Null || m_CameraUpdateSystem.orbitCameraController.followedEntity == car))
                {
                    m_CameraUpdateSystem.orbitCameraController.followedEntity = Entity.Null;
                    if (restoreGameplayController &&
                        ReferenceEquals(m_CameraUpdateSystem.activeCameraController, m_CameraUpdateSystem.orbitCameraController) &&
                        m_CameraUpdateSystem.gamePlayController != null)
                    {
                        m_CameraUpdateSystem.gamePlayController.TryMatchPosition(m_CameraUpdateSystem.orbitCameraController);
                        m_CameraUpdateSystem.activeCameraController = m_CameraUpdateSystem.gamePlayController;
                    }
                }
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"Direct Drive focus marker cleanup failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private bool TryFindNearestLiveCar(Vector3 position, out Entity result)
        {
            result = Entity.Null;
            if (m_LiveCarQuery == default || m_LiveCarQuery.IsEmptyIgnoreFilter)
                return false;

            float bestScore = float.MaxValue;
            NativeArray<Entity> entities = m_LiveCarQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity candidate = entities[i];
                    if (!IsDriveableCar(candidate) || !EntityManager.TryGetComponent(candidate, out ObjectTransform transform))
                        continue;

                    Vector3 delta = ToUnityVector(transform.m_Position) - position;
                    float distanceSq = delta.sqrMagnitude;
                    if (distanceSq > 180f * 180f)
                        continue;

                    float score = Mathf.Sqrt(distanceSq) + GetVehicleSelectionPenalty(candidate);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        result = candidate;
                    }
                }
            }
            finally
            {
                entities.Dispose();
            }

            return result != Entity.Null;
        }

        private bool IsDriveableCar(Entity entity)
        {
            return entity != Entity.Null &&
                   EntityManager.Exists(entity) &&
                   EntityManager.HasComponent<Car>(entity) &&
                   EntityManager.HasComponent<CarNavigation>(entity) &&
                   EntityManager.HasComponent<CarCurrentLane>(entity) &&
                   EntityManager.HasComponent<ObjectTransform>(entity) &&
                   EntityManager.HasComponent<Moving>(entity) &&
                   EntityManager.HasComponent<PrefabRef>(entity) &&
                   EntityManager.HasBuffer<TransformFrame>(entity) &&
                   !EntityManager.HasComponent<Deleted>(entity) &&
                   !EntityManager.HasComponent<Temp>(entity) &&
                   !EntityManager.HasComponent<TripSource>(entity) &&
                   !EntityManager.HasComponent<ParkedCar>(entity) &&
                   !EntityManager.HasComponent<Unspawned>(entity) &&
                   !EntityManager.HasComponent<Bicycle>(entity);
        }

        private Entity ResolveTransformEntity(Entity selected)
        {
            if (selected == Entity.Null || !EntityManager.Exists(selected))
                return Entity.Null;

            if (EntityManager.HasComponent<ObjectTransform>(selected) && !EntityManager.HasComponent<Deleted>(selected) && !EntityManager.HasComponent<Temp>(selected))
                return selected;

            if (EntityManager.TryGetComponent(selected, out Controller controller) &&
                controller.m_Controller != Entity.Null &&
                EntityManager.Exists(controller.m_Controller) &&
                EntityManager.HasComponent<ObjectTransform>(controller.m_Controller))
            {
                return controller.m_Controller;
            }

            if (EntityManager.TryGetComponent(selected, out Owner owner) &&
                owner.m_Owner != Entity.Null &&
                EntityManager.Exists(owner.m_Owner) &&
                EntityManager.HasComponent<ObjectTransform>(owner.m_Owner))
            {
                return owner.m_Owner;
            }

            return Entity.Null;
        }

        private Vector3 ResolveCameraSearchPosition()
        {
            Camera camera = Camera.main;
            if (camera != null)
                return camera.transform.position + camera.transform.forward * 35f;

            return DirectDriveRuntime.PosePosition;
        }

        private string GetVehicleName(Entity vehicle)
        {
            try
            {
                if (m_PrefabSystem != null &&
                    EntityManager.TryGetComponent(vehicle, out PrefabRef prefabRef) &&
                    prefabRef.m_Prefab != Entity.Null)
                {
                    return m_PrefabSystem.GetPrefabName(prefabRef.m_Prefab);
                }
            }
            catch
            {
            }

            return vehicle.ToString();
        }

        private float GetVehicleSelectionPenalty(Entity vehicle)
        {
            string name = GetVehicleName(vehicle).ToLowerInvariant();
            if (name.StartsWith("taxi") || name.StartsWith("car"))
                return -25f;
            if (name.Contains("bus") || name.Contains("truck") || name.Contains("tractor") || name.Contains("trailer") || name.Contains("ambulance") || name.Contains("fire") || name.Contains("police") || name.Contains("garbage") || name.Contains("maintenance"))
                return 60f;
            return 0f;
        }

        private static Vector3 GetCurrentLaneForward(CarCurrentLane currentLane, Game.Net.Curve curve, float curveT, Vector3 fallbackForward, out float curveSign)
        {
            curveSign = currentLane.m_CurvePosition.z < currentLane.m_CurvePosition.x ? -1f : 1f;
            return GetLaneForwardWithSign(curve, curveT, curveSign, fallbackForward);
        }

        private static Vector3 GetLaneForwardWithSign(Game.Net.Curve curve, float curveT, float curveSign, Vector3 fallbackForward)
        {
            Vector3 tangent = ToUnityVector(MathUtils.Tangent(curve.m_Bezier, Mathf.Clamp01(curveT)));
            if (tangent.sqrMagnitude < 0.001f)
                return fallbackForward.sqrMagnitude > 0.001f ? fallbackForward.normalized : Vector3.forward;

            tangent.Normalize();
            return tangent * curveSign;
        }

        private static Vector3 GetLaneForwardClosestTo(Game.Net.Curve curve, float curveT, Vector3 fallbackForward, out float curveSign)
        {
            Vector3 tangent = ToUnityVector(MathUtils.Tangent(curve.m_Bezier, Mathf.Clamp01(curveT)));
            if (tangent.sqrMagnitude < 0.001f)
            {
                curveSign = 1f;
                return fallbackForward.sqrMagnitude > 0.001f ? fallbackForward.normalized : Vector3.forward;
            }

            tangent.Normalize();
            Vector3 reference = FlattenForward(fallbackForward);
            curveSign = Vector3.Dot(tangent, reference) < 0f ? -1f : 1f;
            return tangent * curveSign;
        }

        private static Vector3 GetRightFromForward(Vector3 forward, Vector3 fallbackRight)
        {
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.001f)
                return fallbackRight.sqrMagnitude > 0.001f ? fallbackRight.normalized : Vector3.right;

            return right.normalized;
        }

        private static Quaternion LevelRotation(Quaternion rotation)
        {
            Vector3 forward = FlattenForward(rotation * Vector3.forward);
            return Quaternion.LookRotation(forward, Vector3.up);
        }

        private static Vector3 FlattenForward(Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
                return Vector3.forward;

            return forward.normalized;
        }

        private static Vector3 ToUnityVector(float3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        private static float3 ToMathVector(Vector3 value)
        {
            return new float3(value.x, value.y, value.z);
        }

        private static Quaternion ToUnityQuaternion(quaternion value)
        {
            return new Quaternion(value.value.x, value.value.y, value.value.z, value.value.w);
        }

        private static quaternion ToMathQuaternion(Quaternion value)
        {
            return new quaternion(value.x, value.y, value.z, value.w);
        }
    }
}
