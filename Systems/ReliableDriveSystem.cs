using Colossal.Entities;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Game.UI.InGame;
using Game.Vehicles;
using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ObjectTransform = Game.Objects.Transform;

namespace BetaTestDrivingMod
{
    public sealed partial class ReliableDriveSystem : GameSystemBase
    {
        private const float kMpsToMph = 2.23693629f;
        private const float kMphToMps = 0.44704f;

        private SelectedInfoUISystem m_SelectedInfo;
        private ToolSystem m_ToolSystem;
        private CameraUpdateSystem m_CameraUpdateSystem;
        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_LiveCarQuery;
        private EntityQuery m_LiveWatercraftQuery;
        private EntityQuery m_LiveTrainQuery;
        private EntityQuery m_TransportStopQuery;
        private EntityQuery m_ConnectionLaneQuery;
        private Entity m_PossessedCar = Entity.Null;
        private Entity m_TurnTargetCacheLane = Entity.Null;
        private Vector3 m_TurnTargetCachePosition;
        private Vector3 m_TurnTargetCacheDirection;
        private float m_TurnTargetCacheUntil;
        private int m_TurnTargetCacheSide;
        private Entity m_TurnTargetCacheConnectionLane = Entity.Null;
        private Entity m_TurnTargetCacheExitLane = Entity.Null;
        private float2 m_TurnTargetCacheConnectionCurvePosition;
        private float2 m_TurnTargetCacheExitCurvePosition;
        private CarLaneFlags m_TurnTargetCacheConnectionFlags;
        private CarLaneFlags m_TurnTargetCacheExitFlags;
        private Entity m_PendingTurnConnectionLane = Entity.Null;
        private Entity m_PendingTurnExitLane = Entity.Null;
        private float2 m_PendingTurnConnectionCurvePosition;
        private float2 m_PendingTurnExitCurvePosition;
        private CarLaneFlags m_PendingTurnConnectionFlags;
        private CarLaneFlags m_PendingTurnExitFlags;
        private float m_MergeHold;
        private float m_WrongWayHold;
        private int m_LeftTapCount;
        private int m_RightTapCount;
        private float m_LeftTapUntil;
        private float m_RightTapUntil;
        private float m_UturnPermitUntil;
        private int m_UturnPermitDirection;
        private bool m_PreviousLeftHeld;
        private bool m_PreviousRightHeld;
        private bool m_ReverseArmed;
        private bool m_ReverseActive;
        private bool m_PreviousBrakeHeld;
        private float m_CommandedSpeedMps;
        private int m_LogCooldown;
        private int m_TurnLogCooldown;
        private string m_PossessedName = "";
        private bool m_PossessedWatercraft;
        private bool m_PossessedTrain;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SelectedInfo = World.GetOrCreateSystemManaged<SelectedInfoUISystem>();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_CameraUpdateSystem = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
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
                    ComponentType.ReadOnly<Unspawned>()
                }
            });
            m_LiveWatercraftQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Watercraft>(),
                    ComponentType.ReadWrite<WatercraftNavigation>(),
                    ComponentType.ReadWrite<WatercraftCurrentLane>(),
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
                    ComponentType.ReadOnly<Unspawned>()
                }
            });
            m_LiveTrainQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Train>(),
                    ComponentType.ReadWrite<TrainNavigation>(),
                    ComponentType.ReadWrite<TrainCurrentLane>(),
                    ComponentType.ReadOnly<ObjectTransform>(),
                    ComponentType.ReadOnly<Moving>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<TransformFrame>(),
                    ComponentType.ReadOnly<TrainBogieFrame>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<TripSource>(),
                    ComponentType.ReadOnly<ParkedTrain>(),
                    ComponentType.ReadOnly<Unspawned>()
                }
            });
            m_TransportStopQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Routes.BusStop>(),
                    ComponentType.ReadOnly<Game.Routes.TransportStop>(),
                    ComponentType.ReadOnly<Game.Routes.Position>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>()
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
            ReliableDriveRuntime.SetIdle("Select or look near a moving car, then press V.");
        }

        protected override void OnUpdate()
        {
            ReliableDriveRuntime.EnsureHud();

            if (ReliableDriveRuntime.ConsumeReleaseRequest())
                Release("Released by user");

            if (ReliableDriveRuntime.ConsumeToggleRequest())
            {
                if (m_PossessedCar != Entity.Null)
                    Release("Released by toggle");
                else
                    TryPossessBestCar();
            }

            if (m_PossessedCar == Entity.Null)
                return;

            if (m_PossessedWatercraft)
            {
                if (!IsDriveableLiveWatercraft(m_PossessedCar))
                {
                    Release("Possessed watercraft disappeared or stopped being driveable");
                    return;
                }

                try
                {
                    ApplyPlayerWatercraftNavigation(m_PossessedCar);
                }
                catch (Exception ex)
                {
                    Mod.log.Warn($"Reliable Drive released watercraft after control exception: {ex.GetType().Name}: {ex.Message}");
                    Release($"Released after {ex.GetType().Name}");
                }

                return;
            }

            if (m_PossessedTrain)
            {
                if (!IsDriveableLiveTrain(m_PossessedCar))
                {
                    Release("Possessed train disappeared or stopped being driveable");
                    return;
                }

                try
                {
                    ApplyPlayerTrainNavigation(m_PossessedCar);
                }
                catch (Exception ex)
                {
                    Mod.log.Warn($"Reliable Drive released train after control exception: {ex.GetType().Name}: {ex.Message}");
                    Release($"Released after {ex.GetType().Name}");
                }

                return;
            }

            if (!IsDriveableLiveCar(m_PossessedCar))
            {
                Release("Possessed car disappeared or stopped being driveable");
                return;
            }

            try
            {
                ApplyPlayerNavigation(m_PossessedCar);
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"Reliable Drive released car after control exception: {ex.GetType().Name}: {ex.Message}");
                Release($"Released after {ex.GetType().Name}");
            }
        }

        private void TryPossessBestCar()
        {
            Entity selected = ResolveTransformEntity(m_SelectedInfo != null ? m_SelectedInfo.selectedEntity : Entity.Null);
            if (IsDriveableLiveCar(selected) && IsRoadVehicleAllowed(selected, true))
            {
                Possess(selected, "selected live vehicle", false);
                return;
            }

            if (ReliableDriveRuntime.AllowWatercraft && IsDriveableLiveWatercraft(selected))
            {
                Possess(selected, "selected live watercraft", true);
                return;
            }

            if (ReliableDriveRuntime.AllowRailVehicles && IsDriveableLiveTrain(selected))
            {
                Possess(selected, "selected live train", false, true);
                return;
            }

            Vector3 searchPosition = ResolveCameraSearchPosition();
            if (TryFindNearestLiveCar(searchPosition, out Entity nearest))
            {
                Possess(nearest, "nearest live vehicle");
                return;
            }

            if (ReliableDriveRuntime.AllowWatercraft && TryFindNearestLiveWatercraft(searchPosition, out Entity nearestWatercraft))
            {
                Possess(nearestWatercraft, "nearest live watercraft", true);
                return;
            }

            if (ReliableDriveRuntime.AllowRailVehicles && TryFindNearestLiveTrain(searchPosition, out Entity nearestTrain))
            {
                Possess(nearestTrain, "nearest live train", false, true);
                return;
            }

            ReliableDriveRuntime.SetIdle("No live driveable vehicle found near camera. Let traffic spawn, enable extra vehicle types if needed, then press V.");
            if (m_LogCooldown-- <= 0)
            {
                Mod.log.Info("Reliable Drive possession rejected: no allowed live vehicle with navigation/current lane/TransformFrame was found near camera.");
                m_LogCooldown = 120;
            }
        }

        private void Possess(Entity car, string reason, bool watercraft = false, bool train = false)
        {
            m_PossessedCar = car;
            m_PossessedWatercraft = watercraft;
            m_PossessedTrain = train;
            m_MergeHold = 0f;
            m_WrongWayHold = 0f;
            ResetTurnTapGate();
            m_ReverseArmed = false;
            m_ReverseActive = false;
            m_PreviousBrakeHeld = false;
            ReliableDriveRuntime.ResetCamera();
            m_PossessedName = GetVehicleName(car);

            ObjectTransform transform = EntityManager.GetComponentData<ObjectTransform>(car);
            Moving moving = EntityManager.GetComponentData<Moving>(car);
            Vector3 forward = ToUnityQuaternion(transform.m_Rotation) * Vector3.forward;
            m_CommandedSpeedMps = Mathf.Clamp(Vector3.Dot(ToUnityVector(moving.m_Velocity), forward), -ReliableDriveRuntime.ReverseSpeedMph * kMphToMps, ReliableDriveRuntime.TargetSpeedMph * kMphToMps);
            ReliableDriveRuntime.SetDriving(car, m_PossessedName, ToUnityVector(transform.m_Position), ToUnityQuaternion(transform.m_Rotation), math.length(moving.m_Velocity) * kMpsToMph, 0f, 0f, false, false, train ? "train" : watercraft ? "watercraft" : GetRoadVehicleKind(car));
            FocusPossessedVehicle(car);
            Mod.log.Info($"Reliable Drive possessed {car} '{m_PossessedName}' by {reason}. watercraft={watercraft} train={train}. This build does not spawn or delete vehicles.");
        }

        private void Release(string reason)
        {
            if (m_PossessedCar != Entity.Null)
                Mod.log.Info($"Reliable Drive released {m_PossessedCar} '{m_PossessedName}': {reason}");

            m_PossessedCar = Entity.Null;
            m_PossessedWatercraft = false;
            m_PossessedTrain = false;
            m_PossessedName = "";
            m_MergeHold = 0f;
            m_WrongWayHold = 0f;
            ResetTurnTapGate();
            m_ReverseArmed = false;
            m_ReverseActive = false;
            m_PreviousBrakeHeld = false;
            m_CommandedSpeedMps = 0f;
            ReliableDriveRuntime.SetIdle(reason);
        }

        private void FocusPossessedVehicle(Entity car)
        {
            try
            {
                if (car == Entity.Null || !EntityManager.Exists(car))
                    return;

                if (m_SelectedInfo != null)
                    m_SelectedInfo.SetSelection(car);

                if (m_ToolSystem != null)
                {
                    m_ToolSystem.selected = car;
                    m_ToolSystem.selectedIndex = -1;
                }

                if (m_CameraUpdateSystem != null && m_CameraUpdateSystem.orbitCameraController != null)
                {
                    m_CameraUpdateSystem.orbitCameraController.followedEntity = car;
                    m_CameraUpdateSystem.orbitCameraController.TryMatchPosition(m_CameraUpdateSystem.activeCameraController);

                    if (EntityManager.TryGetComponent(car, out PrefabRef prefabRef) &&
                        prefabRef.m_Prefab != Entity.Null &&
                        EntityManager.TryGetComponent(prefabRef.m_Prefab, out ObjectGeometryData geometry))
                    {
                        float zoom = math.length(MathUtils.Extents(geometry.m_Bounds)) * 2.5f;
                        m_CameraUpdateSystem.orbitCameraController.zoom = Mathf.Clamp(zoom, 6f, 38f);
                    }

                    m_CameraUpdateSystem.activeCameraController = m_CameraUpdateSystem.orbitCameraController;
                    ReliableDriveRuntime.SetFocusStatus("Focused possessed vehicle with game lock-on");
                    return;
                }

                ReliableDriveRuntime.SetFocusStatus("Selected possessed vehicle; game camera focus unavailable");
            }
            catch (Exception ex)
            {
                ReliableDriveRuntime.SetFocusStatus($"Focus failed: {ex.GetType().Name}");
                Mod.log.Warn($"Reliable Drive focus failed for {car}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void ApplyPlayerNavigation(Entity car)
        {
            ObjectTransform transform = EntityManager.GetComponentData<ObjectTransform>(car);
            Moving moving = EntityManager.GetComponentData<Moving>(car);
            CarNavigation navigation = EntityManager.GetComponentData<CarNavigation>(car);
            CarCurrentLane currentLane = EntityManager.GetComponentData<CarCurrentLane>(car);

            Vector3 position = ToUnityVector(transform.m_Position);
            Quaternion rotation = ToUnityQuaternion(transform.m_Rotation);
            Vector3 forward = rotation * Vector3.forward;
            Vector3 right = rotation * Vector3.right;
            Vector3 velocity = ToUnityVector(moving.m_Velocity);
            float signedForwardSpeed = Vector3.Dot(velocity, forward);
            float speedMps = velocity.magnitude;

            ReliableDriveInputFrame input = ReliableDriveRuntime.ConsumeDriveInput();
            float throttle = input.Throttle;
            float brake = input.Brake;
            UpdateTurnTapGate(input.LeftHeld, input.RightHeld, input.LeftPressed, input.RightPressed);
            float steering = input.Steering;
            bool brakeHeld = brake > 0.1f;
            bool reverseCommand = UpdateReverseState(throttle, brakeHeld, input.BrakePressed, signedForwardSpeed);
            bool braking = false;

            float desiredSpeedMps;
            string speedAssistReason = null;
            if (throttle > 0.1f)
            {
                desiredSpeedMps = BuildForwardDesiredSpeedMps(currentLane, throttle, out speedAssistReason);
            }
            else if (brakeHeld)
            {
                if (reverseCommand)
                {
                    desiredSpeedMps = -ReliableDriveRuntime.ReverseSpeedMph * kMphToMps;
                }
                else
                {
                    desiredSpeedMps = 0f;
                    braking = signedForwardSpeed > 0.4f || speedMps > 1.2f;
                    if (math.abs(signedForwardSpeed) < 0.6f)
                        m_ReverseArmed = true;
                }
            }
            else
            {
                desiredSpeedMps = 0f;
                braking = speedMps > 1.6f;
            }

            float absSteering = Mathf.Abs(steering);
            float speedBlend = Mathf.InverseLerp(0f, 18f, Mathf.Abs(speedMps));
            Vector3 targetDirection = desiredSpeedMps < -0.1f ? -forward : forward;
            Vector3 steerDirection = targetDirection;
            Vector3 targetPosition;
            bool currentIsConnectionLane = currentLane.m_Lane != Entity.Null && EntityManager.HasComponent<Game.Net.ConnectionLane>(currentLane.m_Lane);
            ResetPendingTurnConnector();
            if (ReliableDriveRuntime.ShowTurnReleaseZones && !ReliableDriveRuntime.LaneAssistEnabled)
                UpdateTurnZoneDebug(currentLane, position, forward, right, steering, speedMps);

            bool wrongWayRecovery = false;
            if (!ReliableDriveRuntime.LaneAssistEnabled)
            {
                BuildDirectSteerTarget(position, forward, right, targetDirection, steering, speedMps, desiredSpeedMps < -0.1f, out targetPosition, out steerDirection);
                if (TryGetCurrentLaneSurfaceHeight(currentLane, speedMps, desiredSpeedMps < -0.1f, out float surfaceY))
                    targetPosition.y = Mathf.Lerp(targetPosition.y, surfaceY, 0.85f);

                ReliableDriveRuntime.SetLaneAssistStatus("Direct steer: lane pull off");
            }
            else if (!TryBuildLaneAssistTarget(currentLane, position, forward, right, steering, speedMps, out targetPosition, out steerDirection, out wrongWayRecovery))
            {
                if (TryBuildRoadSafeFreeTarget(currentLane, position, forward, right, targetDirection, steering, speedMps, desiredSpeedMps < -0.1f, out targetPosition, out steerDirection))
                {
                    ReliableDriveRuntime.SetLaneAssistStatus(ReliableDriveRuntime.LaneAssistEnabled ? "Lane assist: road-safe free steer" : "Lane assist off: road-safe free steer");
                }
                else
                {
                    float cruiseLookAhead = Mathf.Clamp(ReliableDriveRuntime.LookAheadMeters + Mathf.Abs(speedMps) * 0.25f, 5f, 16f);
                    float turnLookAhead = Mathf.Lerp(ReliableDriveRuntime.FreeTurnLookAheadMin, ReliableDriveRuntime.FreeTurnLookAheadMax, speedBlend);
                    float lookAhead = Mathf.Lerp(cruiseLookAhead, turnLookAhead, absSteering);
                    float steerOffset = steering * ReliableDriveRuntime.SteeringStrength * Mathf.Lerp(ReliableDriveRuntime.FreeSteerOffsetSlow, ReliableDriveRuntime.FreeSteerOffsetFast, speedBlend);
                    float gatedSteering = steering;
                    if (ReliableDriveRuntime.LaneAssistEnabled && absSteering >= 0.18f && !currentIsConnectionLane && !IsUturnAllowedForSteering(steering))
                    {
                        gatedSteering *= ReliableDriveRuntime.BlockedUturnSteeringScale;
                        steerOffset *= ReliableDriveRuntime.BlockedUturnSteeringScale;
                        ReliableDriveRuntime.SetTurnGateStatus("U-turn blocked: triple-tap A/D to allow");
                    }
                    else if (ReliableDriveRuntime.LaneAssistEnabled && absSteering >= 0.18f && currentIsConnectionLane)
                    {
                        ReliableDriveRuntime.SetTurnGateStatus("Turn gate: free steer inside junction");
                    }

                    steerDirection = (targetDirection + right * (gatedSteering * Mathf.Lerp(ReliableDriveRuntime.FreeSteerDirectionSlow, ReliableDriveRuntime.FreeSteerDirectionFast, speedBlend))).normalized;
                    targetPosition = position + targetDirection * lookAhead + right * steerOffset;
                    ReliableDriveRuntime.SetLaneAssistStatus(ReliableDriveRuntime.LaneAssistEnabled ? "Lane assist: free steer" : "Lane assist off: no road lane");
                }
            }

            if (wrongWayRecovery && desiredSpeedMps > 0f)
                desiredSpeedMps = Mathf.Min(desiredSpeedMps, 12f * kMphToMps);
            ApplyRoadTurnSpeedAssist(currentLane, forward, steerDirection, steering, speedMps, throttle, reverseCommand, currentIsConnectionLane, ref desiredSpeedMps, ref speedAssistReason);
            ApplyBusStopAssist(car, position, forward, right, speedMps, ref targetPosition, ref desiredSpeedMps);
            desiredSpeedMps = SmoothCommandedSpeed(desiredSpeedMps, signedForwardSpeed, throttle > 0.1f, brakeHeld, reverseCommand, speedAssistReason);

            navigation.m_TargetPosition = ToMathVector(targetPosition);
            navigation.m_TargetRotation = default;
            navigation.m_MaxSpeed = desiredSpeedMps;
            EntityManager.SetComponentData(car, navigation);

            const CarLaneFlags signalMask = CarLaneFlags.TurnLeft | CarLaneFlags.TurnRight;
            currentLane.m_LaneFlags &= ~signalMask;
            bool invertTurnFlags = currentLane.m_CurvePosition.z < currentLane.m_CurvePosition.x;
            if (steering < -0.18f)
                currentLane.m_LaneFlags |= invertTurnFlags ? CarLaneFlags.TurnRight : CarLaneFlags.TurnLeft;
            else if (steering > 0.18f)
                currentLane.m_LaneFlags |= invertTurnFlags ? CarLaneFlags.TurnLeft : CarLaneFlags.TurnRight;
            EntityManager.SetComponentData(car, currentLane);
            UpdatePossessedNavigationQueue(car);

            m_PreviousBrakeHeld = brakeHeld;
            ReliableDriveRuntime.SetDriving(car, m_PossessedName, position, rotation, speedMps * kMpsToMph, throttle, steering, braking, m_ReverseArmed || m_ReverseActive);
        }

        private void ApplyPlayerWatercraftNavigation(Entity watercraft)
        {
            ObjectTransform transform = EntityManager.GetComponentData<ObjectTransform>(watercraft);
            Moving moving = EntityManager.GetComponentData<Moving>(watercraft);
            WatercraftNavigation navigation = EntityManager.GetComponentData<WatercraftNavigation>(watercraft);
            WatercraftCurrentLane currentLane = EntityManager.GetComponentData<WatercraftCurrentLane>(watercraft);

            Vector3 position = ToUnityVector(transform.m_Position);
            Quaternion rotation = ToUnityQuaternion(transform.m_Rotation);
            Vector3 forward = rotation * Vector3.forward;
            Vector3 right = rotation * Vector3.right;
            Vector3 velocity = ToUnityVector(moving.m_Velocity);
            float signedForwardSpeed = Vector3.Dot(velocity, forward);
            float speedMps = velocity.magnitude;

            ReliableDriveInputFrame input = ReliableDriveRuntime.ConsumeDriveInput();
            float throttle = input.Throttle;
            float brake = input.Brake;
            float steering = input.Steering;
            bool brakeHeld = brake > 0.1f;
            bool reverseCommand = UpdateReverseState(throttle, brakeHeld, input.BrakePressed, signedForwardSpeed);
            bool braking = false;

            float desiredSpeedMps;
            if (throttle > 0.1f)
            {
                desiredSpeedMps = ReliableDriveRuntime.TargetSpeedMph * kMphToMps * throttle;
            }
            else if (brakeHeld)
            {
                if (reverseCommand)
                {
                    desiredSpeedMps = -ReliableDriveRuntime.ReverseSpeedMph * kMphToMps;
                }
                else
                {
                    desiredSpeedMps = 0f;
                    braking = signedForwardSpeed > 0.4f || speedMps > 1.2f;
                    if (math.abs(signedForwardSpeed) < 0.6f)
                        m_ReverseArmed = true;
                }
            }
            else
            {
                desiredSpeedMps = 0f;
                braking = speedMps > 1.6f;
            }

            Vector3 targetDirection = desiredSpeedMps < -0.1f ? -forward : forward;
            Vector3 steerDirection;
            Vector3 targetPosition;
            if (!TryBuildWatercraftTarget(currentLane, position, forward, right, targetDirection, steering, speedMps, desiredSpeedMps < -0.1f, out targetPosition, out steerDirection))
            {
                float speedBlend = Mathf.InverseLerp(0f, 18f, Mathf.Abs(speedMps));
                float lookAhead = Mathf.Lerp(12f, 34f, speedBlend);
                float turnDegrees = Mathf.Lerp(48f, 118f, Mathf.Max(Mathf.Abs(steering), speedBlend * 0.35f)) * steering;
                steerDirection = Quaternion.AngleAxis(turnDegrees, Vector3.up) * targetDirection;
                if (steerDirection.sqrMagnitude < 0.001f)
                    steerDirection = targetDirection;
                steerDirection.Normalize();
                targetPosition = position + steerDirection * lookAhead;
                ReliableDriveRuntime.SetLaneAssistStatus("Watercraft: open-water steering");
            }

            desiredSpeedMps = SmoothCommandedSpeed(desiredSpeedMps, signedForwardSpeed, throttle > 0.1f, brakeHeld, reverseCommand);
            navigation.m_TargetPosition = ToMathVector(targetPosition);
            navigation.m_TargetDirection = ToMathVector(steerDirection.sqrMagnitude > 0.001f ? steerDirection.normalized : targetDirection);
            navigation.m_MaxSpeed = desiredSpeedMps;
            EntityManager.SetComponentData(watercraft, navigation);
            UpdatePossessedWatercraftNavigationQueue(watercraft);

            m_PreviousBrakeHeld = brakeHeld;
            ReliableDriveRuntime.SetTurnGateStatus("Watercraft: road turn gates ignored");
            ReliableDriveRuntime.SetBusAssistStatus("Bus assist: watercraft ignored");
            ReliableDriveRuntime.SetDriving(watercraft, m_PossessedName, position, rotation, speedMps * kMpsToMph, throttle, steering, braking, m_ReverseArmed || m_ReverseActive, "watercraft");
        }

        private void ApplyPlayerTrainNavigation(Entity train)
        {
            ObjectTransform transform = EntityManager.GetComponentData<ObjectTransform>(train);
            Moving moving = EntityManager.GetComponentData<Moving>(train);
            TrainNavigation navigation = EntityManager.GetComponentData<TrainNavigation>(train);

            Vector3 position = ToUnityVector(transform.m_Position);
            Quaternion rotation = ToUnityQuaternion(transform.m_Rotation);
            Vector3 forward = rotation * Vector3.forward;
            Vector3 velocity = ToUnityVector(moving.m_Velocity);
            float signedForwardSpeed = Vector3.Dot(velocity, forward);
            float speedMps = velocity.magnitude;

            ReliableDriveInputFrame input = ReliableDriveRuntime.ConsumeDriveInput();
            float throttle = input.Throttle;
            float brake = input.Brake;
            float steering = input.Steering;
            bool brakeHeld = brake > 0.1f;
            bool reverseCommand = UpdateReverseState(throttle, brakeHeld, input.BrakePressed, signedForwardSpeed);
            bool braking = false;

            float desiredSpeedMps;
            if (throttle > 0.1f)
            {
                desiredSpeedMps = ReliableDriveRuntime.TargetSpeedMph * kMphToMps * throttle;
            }
            else if (brakeHeld)
            {
                if (reverseCommand)
                {
                    desiredSpeedMps = -ReliableDriveRuntime.ReverseSpeedMph * kMphToMps;
                }
                else
                {
                    desiredSpeedMps = 0f;
                    braking = signedForwardSpeed > 0.4f || speedMps > 1.2f;
                    if (math.abs(signedForwardSpeed) < 0.6f)
                        m_ReverseArmed = true;
                }
            }
            else
            {
                desiredSpeedMps = 0f;
                braking = speedMps > 1.6f;
            }

            navigation.m_Speed = SmoothCommandedSpeed(desiredSpeedMps, signedForwardSpeed, throttle > 0.1f, brakeHeld, reverseCommand);
            EntityManager.SetComponentData(train, navigation);

            m_PreviousBrakeHeld = brakeHeld;
            ReliableDriveRuntime.SetLaneAssistStatus("Train: rail path controls direction");
            ReliableDriveRuntime.SetBusAssistStatus("Bus assist: train ignored");
            ReliableDriveRuntime.SetDriving(train, m_PossessedName, position, rotation, speedMps * kMpsToMph, throttle, steering, braking, m_ReverseArmed || m_ReverseActive, "train");
        }

        private bool UpdateReverseState(float throttle, bool brakeHeld, bool brakePressed, float signedForwardSpeed)
        {
            bool stoppedForReverse = math.abs(signedForwardSpeed) < 0.8f;

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

        private float BuildForwardDesiredSpeedMps(CarCurrentLane currentLane, float throttle, out string speedAssistReason)
        {
            float manualSpeedMps = ReliableDriveRuntime.TargetSpeedMph * kMphToMps;
            if (!ReliableDriveRuntime.AutoRoadSpeedEnabled ||
                !TryGetCurrentRoadSpeedLimitMps(currentLane, out float roadSpeedMps))
            {
                speedAssistReason = "Manual speed";
                return manualSpeedMps * throttle;
            }

            float roadTargetMps = Mathf.Clamp(roadSpeedMps * ReliableDriveRuntime.RoadSpeedMultiplier, 5f * kMphToMps, 90f * kMphToMps);
            speedAssistReason = $"Road speed {roadTargetMps * kMpsToMph:0} mph";
            return roadTargetMps * throttle;
        }

        private void ApplyRoadTurnSpeedAssist(CarCurrentLane currentLane, Vector3 forward, Vector3 steerDirection, float steering, float speedMps, float throttle, bool reverseCommand, bool currentIsConnectionLane, ref float desiredSpeedMps, ref string speedAssistReason)
        {
            if (!ReliableDriveRuntime.AutoRoadSpeedEnabled ||
                throttle <= 0.1f ||
                reverseCommand ||
                desiredSpeedMps <= 0f)
            {
                return;
            }

            float absSteering = Mathf.Abs(steering);
            bool junctionGateOpen = currentIsConnectionLane || ReliableDriveRuntime.TurnZoneManualReleaseOpen;
            Vector3 flatForward = FlattenDirection(forward);
            Vector3 flatSteer = FlattenDirection(steerDirection);
            float directionAngle = Vector3.Angle(flatForward, flatSteer);
            float pathTurnBlend = Mathf.InverseLerp(10f, 70f, directionAngle);
            float manualTurnBlend = absSteering >= 0.18f
                ? Mathf.Clamp01((absSteering - 0.18f) / 0.82f)
                : 0f;

            float turnBlend = pathTurnBlend;
            if (junctionGateOpen)
                turnBlend = Mathf.Max(turnBlend, manualTurnBlend);
            else
                turnBlend = Mathf.Max(turnBlend, manualTurnBlend * 0.18f);

            if (TryGetCurrentLaneCurviness(currentLane, out float curviness))
                turnBlend = Mathf.Max(turnBlend, Mathf.InverseLerp(0.08f, 0.34f, curviness) * 0.45f);

            if (turnBlend <= 0.04f)
                return;

            float fastTurnMph = junctionGateOpen ? ReliableDriveRuntime.JunctionTurnSpeedMph : Mathf.Max(ReliableDriveRuntime.JunctionTurnSpeedMph, 42f);
            float slowTurnMph = junctionGateOpen ? ReliableDriveRuntime.SharpTurnSpeedMph : ReliableDriveRuntime.JunctionTurnSpeedMph;
            float turnCapMps = Mathf.Lerp(fastTurnMph, slowTurnMph, Mathf.Clamp01(turnBlend)) * kMphToMps;
            if (desiredSpeedMps > turnCapMps)
            {
                desiredSpeedMps = turnCapMps;
                speedAssistReason = junctionGateOpen
                    ? $"Road speed + junction cap {desiredSpeedMps * kMpsToMph:0} mph"
                    : $"Road speed + curve cap {desiredSpeedMps * kMpsToMph:0} mph";
            }
        }

        private bool TryGetCurrentRoadSpeedLimitMps(CarCurrentLane currentLane, out float speedLimitMps)
        {
            speedLimitMps = 0f;
            if (currentLane.m_Lane == Entity.Null ||
                !EntityManager.TryGetComponent(currentLane.m_Lane, out Game.Net.CarLane carLane))
            {
                return false;
            }

            speedLimitMps = carLane.m_SpeedLimit > 0.5f ? carLane.m_SpeedLimit : carLane.m_DefaultSpeedLimit;
            return speedLimitMps > 0.5f && speedLimitMps < 95f;
        }

        private bool TryGetCurrentLaneCurviness(CarCurrentLane currentLane, out float curviness)
        {
            curviness = 0f;
            if (currentLane.m_Lane == Entity.Null ||
                !EntityManager.TryGetComponent(currentLane.m_Lane, out Game.Net.CarLane carLane))
            {
                return false;
            }

            curviness = Mathf.Max(0f, carLane.m_Curviness);
            return curviness > 0f;
        }

        private static Vector3 FlattenDirection(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
                return Vector3.forward;

            return direction.normalized;
        }

        private float SmoothCommandedSpeed(float targetSpeedMps, float signedForwardSpeed, bool throttleHeld, bool brakeHeld, bool reverseCommand, string speedAssistReason = null)
        {
            float dt = Mathf.Clamp(UnityEngine.Time.deltaTime, 0.008f, 0.06f);

            if (math.abs(m_CommandedSpeedMps) < 0.05f && math.abs(signedForwardSpeed) > 0.5f)
                m_CommandedSpeedMps = signedForwardSpeed;

            bool changingDirection = math.sign(targetSpeedMps) != math.sign(m_CommandedSpeedMps) &&
                                     math.abs(targetSpeedMps) > 0.1f &&
                                     math.abs(m_CommandedSpeedMps) > 0.1f;

            if (throttleHeld &&
                targetSpeedMps > 1.2f &&
                signedForwardSpeed > -0.25f &&
                signedForwardSpeed < 0.8f &&
                m_CommandedSpeedMps >= 0f &&
                m_CommandedSpeedMps < 1.15f)
            {
                m_CommandedSpeedMps = Mathf.Min(targetSpeedMps, 1.15f);
            }

            float rate;
            if (brakeHeld && !reverseCommand)
            {
                rate = ReliableDriveRuntime.BrakeRate;
            }
            else if (changingDirection)
            {
                rate = ReliableDriveRuntime.DirectionChangeRate;
            }
            else if (reverseCommand)
            {
                rate = ReliableDriveRuntime.ReverseAccelerationRate;
            }
            else if (throttleHeld && math.abs(targetSpeedMps) > math.abs(m_CommandedSpeedMps))
            {
                rate = ReliableDriveRuntime.AccelerationRate;
            }
            else
            {
                rate = ReliableDriveRuntime.CoastingRate;
            }

            m_CommandedSpeedMps = Mathf.MoveTowards(m_CommandedSpeedMps, targetSpeedMps, rate * dt);
            if (math.abs(targetSpeedMps) < 0.1f && math.abs(m_CommandedSpeedMps) < 0.25f)
                m_CommandedSpeedMps = 0f;

            string reason = string.IsNullOrEmpty(speedAssistReason) ? "Speed ramp" : speedAssistReason;
            ReliableDriveRuntime.SetSpeedAssistStatus($"{reason}: command {m_CommandedSpeedMps * kMpsToMph:0} mph -> target {targetSpeedMps * kMpsToMph:0} mph");
            return m_CommandedSpeedMps;
        }

        private void UpdatePossessedNavigationQueue(Entity car)
        {
            if (!EntityManager.HasBuffer<CarNavigationLane>(car))
                return;

            DynamicBuffer<CarNavigationLane> navigationLanes = EntityManager.GetBuffer<CarNavigationLane>(car);
            if (navigationLanes.Length > 0)
                navigationLanes.Clear();
        }

        private void UpdatePossessedWatercraftNavigationQueue(Entity watercraft)
        {
            // Keep the water route buffer intact during this first local test. We override the per-frame
            // watercraft target after vanilla navigation, but preserving the route lowers despawn/reroute risk.
        }

        private bool ApplyTrainSwitchIntent(Entity train, ObjectTransform transform, float steering, float speedMps)
        {
            if (!EntityManager.HasBuffer<TrainNavigationLane>(train) ||
                !EntityManager.TryGetComponent(train, out TrainCurrentLane currentLane) ||
                currentLane.m_Front.m_Lane == Entity.Null ||
                !EntityManager.TryGetComponent(currentLane.m_Front.m_Lane, out Game.Net.Curve currentCurve) ||
                currentCurve.m_Length < 1f)
            {
                return false;
            }

            const TrainLaneFlags turnMask = TrainLaneFlags.TurnLeft | TrainLaneFlags.TurnRight;
            currentLane.m_Front.m_LaneFlags &= ~turnMask;
            currentLane.m_Rear.m_LaneFlags &= ~turnMask;

            if (Mathf.Abs(steering) < 0.24f)
            {
                EntityManager.SetComponentData(train, currentLane);
                return false;
            }

            int desiredSide = steering > 0f ? 1 : -1;
            TrainLaneFlags turnFlag = desiredSide > 0 ? TrainLaneFlags.TurnRight : TrainLaneFlags.TurnLeft;
            currentLane.m_Front.m_LaneFlags |= turnFlag;
            currentLane.m_Rear.m_LaneFlags |= turnFlag;

            if (m_ConnectionLaneQuery == default || m_ConnectionLaneQuery.IsEmptyIgnoreFilter)
            {
                EntityManager.SetComponentData(train, currentLane);
                return false;
            }

            Vector3 position = ToUnityVector(transform.m_Position);
            Quaternion rotation = ToUnityQuaternion(transform.m_Rotation);
            Vector3 forward = rotation * Vector3.forward;
            Vector3 fallbackRight = rotation * Vector3.right;
            float currentT = Mathf.Clamp01(currentLane.m_Front.m_CurvePosition.y);
            float curveSign = math.abs(currentLane.m_Front.m_CurvePosition.w - currentLane.m_Front.m_CurvePosition.y) > 0.001f
                ? math.sign(currentLane.m_Front.m_CurvePosition.w - currentLane.m_Front.m_CurvePosition.y)
                : math.sign(currentLane.m_Front.m_CurvePosition.w - currentLane.m_Front.m_CurvePosition.x);
            if (math.abs(curveSign) < 0.1f)
                curveSign = 1f;

            Vector3 railForward = GetLaneForwardWithSign(currentCurve, currentT, curveSign, forward);
            Vector3 railRight = GetRightFromForward(railForward, fallbackRight);
            float speedBlend = Mathf.InverseLerp(0f, 34f, Mathf.Abs(speedMps));
            float switchReach = Mathf.Lerp(36f, 95f, speedBlend);
            float targetAhead = Mathf.Lerp(18f, 54f, speedBlend);
            Entity bestConnection = Entity.Null;
            Entity bestExitLane = Entity.Null;
            Game.Net.Curve bestExitCurve = default;
            float2 bestConnectionCurvePosition = default;
            float2 bestExitCurvePosition = default;
            float bestExitSign = 1f;
            float bestExitEntryT = 0f;
            float bestScore = float.MaxValue;
            int scanned = 0;
            int trackConnections = 0;
            int sideMatches = 0;

            NativeArray<Entity> entities = m_ConnectionLaneQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity candidate = entities[i];
                    scanned++;
                    if (!EntityManager.TryGetComponent(candidate, out Game.Net.ConnectionLane connectionLane) ||
                        (connectionLane.m_Flags & Game.Net.ConnectionLaneFlags.Track) == (Game.Net.ConnectionLaneFlags)0 ||
                        (connectionLane.m_Flags & Game.Net.ConnectionLaneFlags.Disabled) != (Game.Net.ConnectionLaneFlags)0 ||
                        !EntityManager.TryGetComponent(candidate, out Game.Net.LaneConnection laneConnection) ||
                        !EntityManager.TryGetComponent(candidate, out Game.Net.Curve candidateCurve) ||
                        candidateCurve.m_Length < 1f)
                    {
                        continue;
                    }

                    bool startsFromCurrent = laneConnection.m_StartLane == currentLane.m_Front.m_Lane;
                    bool endsAtCurrent = laneConnection.m_EndLane == currentLane.m_Front.m_Lane;
                    if (!startsFromCurrent && !endsAtCurrent)
                        continue;

                    trackConnections++;
                    float connectionSign = startsFromCurrent ? 1f : -1f;
                    float entryT = startsFromCurrent ? 0f : 1f;
                    float exitTOnConnection = startsFromCurrent ? 1f : 0f;
                    Entity exitLane = startsFromCurrent ? laneConnection.m_EndLane : laneConnection.m_StartLane;
                    float exitEntryT = Mathf.Clamp01(startsFromCurrent ? laneConnection.m_EndPosition : laneConnection.m_StartPosition);
                    if (exitLane == Entity.Null ||
                        exitLane == currentLane.m_Front.m_Lane ||
                        !EntityManager.TryGetComponent(exitLane, out Game.Net.TrackLane exitTrackLane) ||
                        !EntityManager.TryGetComponent(exitLane, out Game.Net.Curve exitCurve) ||
                        exitCurve.m_Length < 1f)
                    {
                        continue;
                    }

                    Vector3 entryPoint = ToUnityVector(MathUtils.Position(candidateCurve.m_Bezier, entryT));
                    Vector3 toEntry = entryPoint - position;
                    float entryForward = Vector3.Dot(toEntry, railForward);
                    float entryDistance = toEntry.magnitude;
                    if (entryForward < -18f || entryDistance > switchReach)
                        continue;

                    Vector3 connectionEntryDirection = GetLaneForwardWithSign(candidateCurve, entryT, connectionSign, railForward);
                    Vector3 connectionExitDirection = GetLaneForwardWithSign(candidateCurve, exitTOnConnection, connectionSign, connectionEntryDirection);
                    float forwardDot = Vector3.Dot(connectionEntryDirection, railForward);
                    if (forwardDot < -0.25f)
                        continue;

                    Vector3 midPoint = ToUnityVector(MathUtils.Position(candidateCurve.m_Bezier, 0.5f));
                    Vector3 exitPoint = ToUnityVector(MathUtils.Position(candidateCurve.m_Bezier, exitTOnConnection));
                    float sideFromDirection = Vector3.Dot(connectionEntryDirection, railRight) * desiredSide;
                    float sideFromShape = Vector3.Dot((midPoint - entryPoint).normalized, railRight) * desiredSide;
                    float sideFromExit = Vector3.Dot((exitPoint - entryPoint).normalized, railRight) * desiredSide;
                    float sideScore = Mathf.Max(sideFromDirection, Mathf.Max(sideFromShape, sideFromExit));
                    if (sideScore < 0.08f)
                        continue;

                    Vector3 exitTangent = ToUnityVector(MathUtils.Tangent(exitCurve.m_Bezier, exitEntryT));
                    if (exitTangent.sqrMagnitude < 0.001f)
                        continue;

                    exitTangent.Normalize();
                    float exitSign = Vector3.Dot(exitTangent, connectionExitDirection) >= 0f ? 1f : -1f;
                    float exitEndT = exitSign > 0f ? 1f : 0f;
                    Vector3 exitDirection = exitTangent * exitSign;
                    if (Vector3.Dot(exitDirection, connectionExitDirection) < -0.2f)
                        continue;

                    float score = entryDistance - sideScore * 42f - Mathf.Max(0f, forwardDot) * 10f;
                    if ((exitTrackLane.m_Flags & (desiredSide > 0 ? Game.Net.TrackLaneFlags.TurnRight : Game.Net.TrackLaneFlags.TurnLeft)) != (Game.Net.TrackLaneFlags)0)
                        score -= 18f;
                    if ((exitTrackLane.m_Flags & Game.Net.TrackLaneFlags.Switch) != (Game.Net.TrackLaneFlags)0)
                        score -= 6f;

                    sideMatches++;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestConnection = candidate;
                        bestExitLane = exitLane;
                        bestExitCurve = exitCurve;
                        bestConnectionCurvePosition = startsFromCurrent ? new float2(0f, 1f) : new float2(1f, 0f);
                        bestExitCurvePosition = new float2(exitEntryT, exitEndT);
                        bestExitSign = exitSign;
                        bestExitEntryT = exitEntryT;
                    }
                }
            }
            finally
            {
                entities.Dispose();
            }

            EntityManager.SetComponentData(train, currentLane);

            if (bestConnection == Entity.Null || bestExitLane == Entity.Null)
            {
                if (--m_TurnLogCooldown <= 0)
                {
                    Mod.log.Info($"Reliable Drive train switch missed side={desiredSide} scanned={scanned} track={trackConnections} side={sideMatches}");
                    m_TurnLogCooldown = 120;
                }
                return false;
            }

            DynamicBuffer<TrainNavigationLane> navigationLanes = EntityManager.GetBuffer<TrainNavigationLane>(train);
            navigationLanes.Clear();
            navigationLanes.Add(new TrainNavigationLane
            {
                m_Lane = bestConnection,
                m_CurvePosition = bestConnectionCurvePosition,
                m_Flags = TrainLaneFlags.Connection | turnFlag
            });
            navigationLanes.Add(new TrainNavigationLane
            {
                m_Lane = bestExitLane,
                m_CurvePosition = bestExitCurvePosition,
                m_Flags = TrainLaneFlags.TryReserve | turnFlag
            });

            float exitTargetT = Mathf.Clamp01(bestExitEntryT + bestExitSign * targetAhead / Mathf.Max(1f, bestExitCurve.m_Length));
            Vector3 exitTarget = ToUnityVector(MathUtils.Position(bestExitCurve.m_Bezier, exitTargetT));
            if (--m_TurnLogCooldown <= 0)
            {
                Mod.log.Info($"Reliable Drive train switch queued side={desiredSide} connection={bestConnection} exit={bestExitLane} score={bestScore:0.0} target={exitTarget}");
                m_TurnLogCooldown = 120;
            }
            ReliableDriveRuntime.SetLaneAssistStatus(desiredSide > 0 ? "Train: switch right queued" : "Train: switch left queued");
            ReliableDriveRuntime.SetTurnGateStatus("Train switch: branch queued");
            return true;
        }

        private bool TryBuildWatercraftTarget(WatercraftCurrentLane currentLane, Vector3 position, Vector3 forward, Vector3 right, Vector3 targetDirection, float steering, float speedMps, bool reverse, out Vector3 targetPosition, out Vector3 steerDirection)
        {
            targetPosition = default;
            steerDirection = targetDirection;

            if (currentLane.m_Lane == Entity.Null ||
                !EntityManager.TryGetComponent(currentLane.m_Lane, out Game.Net.Curve curve) ||
                curve.m_Length < 1f)
            {
                return false;
            }

            float curveT = Mathf.Clamp01(currentLane.m_CurvePosition.x);
            float curveSign = currentLane.m_CurvePosition.z < currentLane.m_CurvePosition.x ? -1f : 1f;
            Vector3 laneForward = GetLaneForwardWithSign(curve, curveT, curveSign, forward);
            float speedBlend = Mathf.InverseLerp(0f, 24f, Mathf.Abs(speedMps));
            float absSteering = Mathf.Abs(steering);
            float lookAhead = Mathf.Lerp(14f, 42f, speedBlend);
            float travelSign = reverse ? -curveSign : curveSign;
            float targetT = Mathf.Clamp01(curveT + travelSign * lookAhead / Mathf.Max(1f, curve.m_Length));
            Vector3 laneTarget = ToUnityVector(MathUtils.Position(curve.m_Bezier, targetT));
            Vector3 travelDirection = reverse ? -laneForward : laneForward;
            float turnDegrees = Mathf.Lerp(28f, 62f, Mathf.Max(absSteering, speedBlend * 0.4f)) * steering;
            Vector3 freeDirection = Quaternion.AngleAxis(turnDegrees, Vector3.up) * targetDirection;
            if (freeDirection.sqrMagnitude < 0.001f)
                freeDirection = targetDirection;

            float assistWeight = Mathf.Lerp(0.34f, 0.08f, Mathf.Clamp01(absSteering / 0.8f));
            steerDirection = Vector3.Slerp(freeDirection.normalized, travelDirection.normalized, assistWeight).normalized;
            targetPosition = Vector3.Lerp(position + steerDirection * lookAhead, laneTarget, assistWeight * 0.32f);
            ReliableDriveRuntime.SetLaneAssistStatus(absSteering > 0.18f ? "Watercraft: free steering with light waterway help" : "Watercraft: light waterway hold");
            return true;
        }

        private bool TryBuildRoadSafeFreeTarget(CarCurrentLane currentLane, Vector3 position, Vector3 forward, Vector3 right, Vector3 targetDirection, float steering, float speedMps, bool reverse, out Vector3 targetPosition, out Vector3 steerDirection)
        {
            targetPosition = default;
            steerDirection = targetDirection;

            if (currentLane.m_Lane == Entity.Null ||
                (currentLane.m_LaneFlags & (CarLaneFlags.Area | CarLaneFlags.ParkingSpace | CarLaneFlags.TransformTarget)) != (CarLaneFlags)0U ||
                !EntityManager.TryGetComponent(currentLane.m_Lane, out Game.Net.Curve curve) ||
                curve.m_Length < 1f)
            {
                return false;
            }

            float curveT = Mathf.Clamp01(currentLane.m_CurvePosition.x);
            Vector3 laneForward = GetCurrentLaneForward(currentLane, curve, curveT, forward, out float curveSign);
            Vector3 laneRight = GetRightFromForward(laneForward, right);
            bool currentIsConnectionLane = EntityManager.HasComponent<Game.Net.ConnectionLane>(currentLane.m_Lane);
            float absSteering = Mathf.Abs(steering);
            float speedBlend = Mathf.InverseLerp(0f, 22f, Mathf.Abs(speedMps));
            float lookAhead = Mathf.Lerp(
                Mathf.Clamp(ReliableDriveRuntime.LookAheadMeters * 0.55f, 4f, 11f),
                Mathf.Lerp(ReliableDriveRuntime.FreeTurnLookAheadMin, ReliableDriveRuntime.FreeTurnLookAheadMax, speedBlend),
                absSteering);

            float travelSign = reverse ? -curveSign : curveSign;
            float targetT = Mathf.Clamp01(curveT + travelSign * lookAhead / Mathf.Max(1f, curve.m_Length));
            Vector3 laneTarget = ToUnityVector(MathUtils.Position(curve.m_Bezier, targetT));
            Vector3 travelDirection = reverse ? -laneForward : laneForward;
            float steerOffset = steering * ReliableDriveRuntime.SteeringStrength * Mathf.Lerp(ReliableDriveRuntime.FreeSteerOffsetSlow, ReliableDriveRuntime.FreeSteerOffsetFast, speedBlend);
            float gatedSteering = steering;

            if (ReliableDriveRuntime.LaneAssistEnabled && absSteering >= 0.18f && !currentIsConnectionLane && !IsUturnAllowedForSteering(steering))
            {
                gatedSteering *= ReliableDriveRuntime.BlockedUturnSteeringScale;
                steerOffset *= ReliableDriveRuntime.BlockedUturnSteeringScale;
                ReliableDriveRuntime.SetTurnGateStatus("U-turn blocked: triple-tap A/D to allow");
            }
            else if (ReliableDriveRuntime.LaneAssistEnabled && absSteering >= 0.18f && currentIsConnectionLane)
            {
                ReliableDriveRuntime.SetTurnGateStatus("Turn gate: road-safe free steer inside junction");
            }

            float maxOffset = Mathf.Lerp(3.2f, 5.8f, speedBlend);
            targetPosition = laneTarget + laneRight * Mathf.Clamp(steerOffset, -maxOffset, maxOffset);
            steerDirection = (travelDirection + laneRight * (gatedSteering * Mathf.Lerp(ReliableDriveRuntime.FreeSteerDirectionSlow, ReliableDriveRuntime.FreeSteerDirectionFast, speedBlend))).normalized;
            return true;
        }

        private static void BuildDirectSteerTarget(Vector3 position, Vector3 forward, Vector3 right, Vector3 targetDirection, float steering, float speedMps, bool reverse, out Vector3 targetPosition, out Vector3 steerDirection)
        {
            float absSteering = Mathf.Abs(steering);
            float speedBlend = Mathf.InverseLerp(0f, 22f, Mathf.Abs(speedMps));
            float shapedSteering = steering == 0f ? 0f : Mathf.Sign(steering) * Mathf.Lerp(absSteering, Mathf.Sqrt(absSteering), 0.62f);
            Vector3 travelDirection = reverse ? -forward : targetDirection;

            float maxYaw = Mathf.Lerp(76f, 38f, speedBlend);
            Vector3 yawDirection = Quaternion.AngleAxis(maxYaw * shapedSteering, Vector3.up) * travelDirection;
            float yawBlend = Mathf.Lerp(0.96f, 0.74f, speedBlend);
            steerDirection = Vector3.Slerp(travelDirection, yawDirection, yawBlend);
            if (steerDirection.sqrMagnitude < 0.001f)
                steerDirection = travelDirection;
            steerDirection.Normalize();

            float cruiseLookAhead = Mathf.Lerp(4.6f, 10.2f, speedBlend);
            float turnLookAhead = Mathf.Lerp(2.8f, 6.4f, speedBlend);
            float sideNudge = shapedSteering * Mathf.Lerp(1.4f, 2.9f, speedBlend);
            targetPosition = position + steerDirection * Mathf.Lerp(cruiseLookAhead, turnLookAhead, absSteering) + right * sideNudge;
        }

        private bool TryGetCurrentLaneSurfaceHeight(CarCurrentLane currentLane, float speedMps, bool reverse, out float surfaceY)
        {
            surfaceY = 0f;

            if (currentLane.m_Lane == Entity.Null ||
                !EntityManager.TryGetComponent(currentLane.m_Lane, out Game.Net.Curve curve) ||
                curve.m_Length < 1f)
            {
                return false;
            }

            float curveT = Mathf.Clamp01(currentLane.m_CurvePosition.x);
            float curveSign = currentLane.m_CurvePosition.z < currentLane.m_CurvePosition.x ? -1f : 1f;
            float travelSign = reverse ? -curveSign : curveSign;
            float speedBlend = Mathf.InverseLerp(0f, 24f, Mathf.Abs(speedMps));
            float heightAhead = Mathf.Lerp(3.5f, 10f, speedBlend);
            float targetT = Mathf.Clamp01(curveT + travelSign * heightAhead / Mathf.Max(1f, curve.m_Length));
            surfaceY = ToUnityVector(MathUtils.Position(curve.m_Bezier, targetT)).y;
            return true;
        }

        private void UpdateTurnZoneDebug(CarCurrentLane currentLane, Vector3 position, Vector3 forward, Vector3 right, float steering, float speedMps)
        {
            if (currentLane.m_Lane == Entity.Null ||
                (currentLane.m_LaneFlags & (CarLaneFlags.Area | CarLaneFlags.ParkingSpace | CarLaneFlags.TransformTarget)) != (CarLaneFlags)0U ||
                !EntityManager.TryGetComponent(currentLane.m_Lane, out Game.Net.Curve curve) ||
                curve.m_Length < 1f)
            {
                ReliableDriveRuntime.ClearTurnZoneDebug();
                return;
            }

            bool hasCarLane = EntityManager.TryGetComponent(currentLane.m_Lane, out Game.Net.CarLane carLane);
            float curveT = Mathf.Clamp01(currentLane.m_CurvePosition.x);
            Vector3 laneForward = GetCurrentLaneForward(currentLane, curve, curveT, forward, out float curveSign);
            Vector3 laneRight = GetRightFromForward(laneForward, right);
            bool currentIsConnectionLane = EntityManager.HasComponent<Game.Net.ConnectionLane>(currentLane.m_Lane);
            bool matchingTurnLane = hasCarLane && HasMatchingTurnLaneFlag(carLane, steering, curveSign);
            float speedBlend = Mathf.InverseLerp(0f, 24f, Mathf.Abs(speedMps));
            float scanGateMeters = Mathf.Lerp(ReliableDriveRuntime.JunctionGateSlow, ReliableDriveRuntime.JunctionGateFast, speedBlend);
            float turnLaneReleaseMeters = Mathf.Lerp(ReliableDriveRuntime.TurnLaneReleaseSlow, ReliableDriveRuntime.TurnLaneReleaseFast, speedBlend);
            float junctionReleaseMeters = Mathf.Lerp(ReliableDriveRuntime.JunctionReleaseSlow, ReliableDriveRuntime.JunctionReleaseFast, speedBlend);
            if (!TryFindTurnZoneAnchor(currentLane, curve, curveT, curveSign, position, laneForward, laneRight, scanGateMeters, out Vector3 turnZoneAnchor, out float turnZoneDistance, out _))
            {
                Vector3 scanEnd = position + laneForward * scanGateMeters;
                ReliableDriveRuntime.SetTurnZoneDebug(scanEnd, laneForward, laneRight, turnLaneReleaseMeters, junctionReleaseMeters, scanGateMeters, scanGateMeters, false, false, false, false, false);
                return;
            }

            bool junctionTurnGate = currentIsConnectionLane || turnZoneDistance <= scanGateMeters;
            bool manualReleaseOpen = currentIsConnectionLane ||
                                     (matchingTurnLane && turnZoneDistance <= turnLaneReleaseMeters) ||
                                     (junctionTurnGate && turnZoneDistance <= junctionReleaseMeters);

            ReliableDriveRuntime.SetTurnZoneDebug(turnZoneAnchor, laneForward, laneRight, turnLaneReleaseMeters, junctionReleaseMeters, scanGateMeters, turnZoneDistance, currentIsConnectionLane, matchingTurnLane, junctionTurnGate, manualReleaseOpen, true);
        }

        private bool TryFindTurnZoneAnchor(CarCurrentLane currentLane, Game.Net.Curve currentCurve, float curveT, float curveSign, Vector3 position, Vector3 laneForward, Vector3 laneRight, float scanGateMeters, out Vector3 anchorPosition, out float anchorDistance, out bool linkedToCurrent)
        {
            anchorPosition = default;
            anchorDistance = 0f;
            linkedToCurrent = false;

            if (currentLane.m_Lane == Entity.Null)
                return false;

            if (EntityManager.HasComponent<Game.Net.ConnectionLane>(currentLane.m_Lane))
            {
                float endT = curveSign > 0f ? 1f : 0f;
                anchorPosition = ToUnityVector(MathUtils.Position(currentCurve.m_Bezier, endT));
                anchorDistance = Mathf.Max(0f, Vector3.Dot(anchorPosition - position, laneForward));
                linkedToCurrent = true;
                return true;
            }

            float maxForward = Mathf.Max(scanGateMeters + 28f, 72f);
            float maxSide = Mathf.Max(18f, ReliableDriveRuntime.TurnZoneHalfWidth * 2.4f);
            float bestScore = float.MaxValue;
            if (TryFindRoadNodeTurnZoneAnchor(currentLane, currentCurve, curveT, curveSign, position, laneForward, maxForward, out anchorPosition, out anchorDistance))
            {
                linkedToCurrent = true;
                return true;
            }

            if (m_ConnectionLaneQuery != default && !m_ConnectionLaneQuery.IsEmptyIgnoreFilter)
            {
                NativeArray<Entity> entities = m_ConnectionLaneQuery.ToEntityArray(Allocator.Temp);
                try
                {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        Entity candidate = entities[i];
                        if (!EntityManager.TryGetComponent(candidate, out Game.Net.Curve candidateCurve) ||
                            !EntityManager.TryGetComponent(candidate, out Game.Net.ConnectionLane connectionLane) ||
                            !EntityManager.TryGetComponent(candidate, out Game.Net.LaneConnection laneConnection) ||
                            candidateCurve.m_Length < 3f ||
                            (connectionLane.m_Flags & Game.Net.ConnectionLaneFlags.Disabled) != (Game.Net.ConnectionLaneFlags)0 ||
                            (connectionLane.m_Flags & Game.Net.ConnectionLaneFlags.Road) == (Game.Net.ConnectionLaneFlags)0 ||
                            (connectionLane.m_RoadTypes != Game.Net.RoadTypes.None && (connectionLane.m_RoadTypes & Game.Net.RoadTypes.Car) == Game.Net.RoadTypes.None))
                        {
                            continue;
                        }

                        bool startsFromCurrent = laneConnection.m_StartLane == currentLane.m_Lane;
                        bool endsAtCurrent = laneConnection.m_EndLane == currentLane.m_Lane;

                        bool linkedCandidate = startsFromCurrent || endsAtCurrent;
                        TestTurnZoneAnchorCandidate(candidateCurve, 0f, linkedCandidate, position, laneForward, laneRight, maxForward, maxSide, ref bestScore, ref anchorPosition, ref anchorDistance, ref linkedToCurrent);
                        TestTurnZoneAnchorCandidate(candidateCurve, 0.25f, linkedCandidate, position, laneForward, laneRight, maxForward, maxSide, ref bestScore, ref anchorPosition, ref anchorDistance, ref linkedToCurrent);
                        TestTurnZoneAnchorCandidate(candidateCurve, 0.5f, linkedCandidate, position, laneForward, laneRight, maxForward, maxSide, ref bestScore, ref anchorPosition, ref anchorDistance, ref linkedToCurrent);
                        TestTurnZoneAnchorCandidate(candidateCurve, 0.75f, linkedCandidate, position, laneForward, laneRight, maxForward, maxSide, ref bestScore, ref anchorPosition, ref anchorDistance, ref linkedToCurrent);
                        TestTurnZoneAnchorCandidate(candidateCurve, 1f, linkedCandidate, position, laneForward, laneRight, maxForward, maxSide, ref bestScore, ref anchorPosition, ref anchorDistance, ref linkedToCurrent);
                    }
                }
                finally
                {
                    entities.Dispose();
                }
            }

            return bestScore < float.MaxValue * 0.5f;
        }

        private bool TryFindRoadNodeTurnZoneAnchor(CarCurrentLane currentLane, Game.Net.Curve currentCurve, float curveT, float curveSign, Vector3 position, Vector3 laneForward, float maxForward, out Vector3 anchorPosition, out float anchorDistance)
        {
            anchorPosition = default;
            anchorDistance = 0f;

            if (currentLane.m_Lane == Entity.Null ||
                !EntityManager.TryGetComponent(currentLane.m_Lane, out Owner owner) ||
                owner.m_Owner == Entity.Null ||
                !EntityManager.TryGetComponent(owner.m_Owner, out Game.Net.Edge edge))
            {
                return false;
            }

            float bestScore = float.MaxValue;
            TestRoadNodeTurnZoneCandidate(edge.m_Start, position, laneForward, maxForward, ref bestScore, ref anchorPosition, ref anchorDistance);
            TestRoadNodeTurnZoneCandidate(edge.m_End, position, laneForward, maxForward, ref bestScore, ref anchorPosition, ref anchorDistance);

            if (EntityManager.HasBuffer<Game.Net.ConnectedNode>(owner.m_Owner))
            {
                DynamicBuffer<Game.Net.ConnectedNode> nodes = EntityManager.GetBuffer<Game.Net.ConnectedNode>(owner.m_Owner, true);
                for (int i = 0; i < nodes.Length; i++)
                    TestRoadNodeTurnZoneCandidate(nodes[i].m_Node, position, laneForward, maxForward, ref bestScore, ref anchorPosition, ref anchorDistance);
            }

            return bestScore < float.MaxValue * 0.5f;
        }

        private void TestRoadNodeTurnZoneCandidate(Entity node, Vector3 position, Vector3 laneForward, float maxForward, ref float bestScore, ref Vector3 anchorPosition, ref float anchorDistance)
        {
            if (node == Entity.Null ||
                !EntityManager.TryGetComponent(node, out Game.Net.Node nodeData) ||
                CountConnectedRoadEdges(node) < 3)
            {
                return;
            }

            Vector3 nodePosition = ToUnityVector(nodeData.m_Position);
            Vector3 delta = nodePosition - position;
            float forwardDistance = Vector3.Dot(delta, laneForward);
            if (forwardDistance < -6f || forwardDistance > maxForward)
                return;

            float lateralDistance = Vector3.Cross(laneForward, delta).magnitude;
            if (lateralDistance > Mathf.Max(22f, ReliableDriveRuntime.TurnZoneHalfWidth * 2.6f))
                return;

            float score = forwardDistance + lateralDistance * 1.7f;
            if (score >= bestScore)
                return;

            bestScore = score;
            anchorPosition = nodePosition;
            anchorDistance = Mathf.Max(0f, forwardDistance);
        }

        private int CountConnectedRoadEdges(Entity node)
        {
            if (node == Entity.Null || !EntityManager.HasBuffer<Game.Net.ConnectedEdge>(node))
                return 0;

            int count = 0;
            DynamicBuffer<Game.Net.ConnectedEdge> edges = EntityManager.GetBuffer<Game.Net.ConnectedEdge>(node, true);
            for (int i = 0; i < edges.Length; i++)
            {
                Entity edge = edges[i].m_Edge;
                if (edge != Entity.Null &&
                    EntityManager.Exists(edge) &&
                    EntityManager.HasComponent<Game.Net.Road>(edge) &&
                    !EntityManager.HasComponent<Deleted>(edge) &&
                    !EntityManager.HasComponent<Temp>(edge))
                {
                    count++;
                }
            }

            return count;
        }

        private static void TestTurnZoneAnchorCandidate(Game.Net.Curve candidateCurve, float entryT, bool linkedCandidate, Vector3 position, Vector3 laneForward, Vector3 laneRight, float maxForward, float maxSide, ref float bestScore, ref Vector3 anchorPosition, ref float anchorDistance, ref bool linkedToCurrent)
        {
            Vector3 entryPoint = ToUnityVector(MathUtils.Position(candidateCurve.m_Bezier, entryT));
            Vector3 delta = entryPoint - position;
            float forwardDistance = Vector3.Dot(delta, laneForward);
            float sideDistance = Mathf.Abs(Vector3.Dot(delta, laneRight));
            if (forwardDistance < -8f || forwardDistance > maxForward || sideDistance > maxSide)
                return;

            Vector3 tangent = ToUnityVector(MathUtils.Tangent(candidateCurve.m_Bezier, entryT));
            if (tangent.sqrMagnitude > 0.001f)
            {
                tangent.Normalize();
                if (Mathf.Abs(Vector3.Dot(tangent, laneForward)) < 0.08f && sideDistance > maxSide * 0.65f)
                    return;
            }

            float score = forwardDistance + sideDistance * 1.8f + (linkedCandidate ? -35f : 18f);
            if (score >= bestScore)
                return;

            bestScore = score;
            anchorPosition = entryPoint;
            anchorDistance = Mathf.Max(0f, forwardDistance);
            linkedToCurrent = linkedCandidate;
        }

        private bool TryBuildLaneAssistTarget(CarCurrentLane currentLane, Vector3 position, Vector3 forward, Vector3 right, float steering, float speedMps, out Vector3 targetPosition, out Vector3 targetDirection, out bool wrongWayRecovery)
        {
            targetPosition = default;
            targetDirection = forward;
            wrongWayRecovery = false;

            if (currentLane.m_Lane == Entity.Null ||
                (currentLane.m_LaneFlags & (CarLaneFlags.Area | CarLaneFlags.ParkingSpace | CarLaneFlags.TransformTarget)) != (CarLaneFlags)0U ||
                !EntityManager.TryGetComponent(currentLane.m_Lane, out Game.Net.Curve curve) ||
                curve.m_Length < 1f)
            {
                m_MergeHold = 0f;
                ReliableDriveRuntime.ClearTurnZoneDebug();
                return false;
            }

            bool hasCarLane = EntityManager.TryGetComponent(currentLane.m_Lane, out Game.Net.CarLane carLane);
            float curveT = Mathf.Clamp01(currentLane.m_CurvePosition.x);
            Vector3 laneForward = GetCurrentLaneForward(currentLane, curve, curveT, forward, out float curveSign);
            Vector3 laneRight = GetRightFromForward(laneForward, right);
            bool matchingTurnLane = hasCarLane && HasMatchingTurnLaneFlag(carLane, steering, curveSign);
            float laneFacingDot = Vector3.Dot(forward, laneForward);
            if (laneFacingDot < -0.2f)
                m_WrongWayHold = Mathf.MoveTowards(m_WrongWayHold, 1f, 0.18f);
            else if (laneFacingDot > 0.35f)
                m_WrongWayHold = Mathf.MoveTowards(m_WrongWayHold, 0f, 0.12f);

            wrongWayRecovery = m_WrongWayHold > 0.25f && IsUturnAllowedForSteering(steering);
            float speedBlend = Mathf.InverseLerp(0f, 24f, Mathf.Abs(speedMps));
            float aheadMeters = wrongWayRecovery ? Mathf.Lerp(7f, 13f, speedBlend) : Mathf.Lerp(ReliableDriveRuntime.LaneLookAheadMin, ReliableDriveRuntime.LaneLookAheadMax, speedBlend);
            float targetT = Mathf.Clamp01(curveT + curveSign * aheadMeters / Mathf.Max(1f, curve.m_Length));
            Vector3 sameLaneTarget = ToUnityVector(MathUtils.Position(curve.m_Bezier, targetT));
            Vector3 sameLaneDirection = GetLaneForwardWithSign(curve, targetT, curveSign, laneForward);

            if (wrongWayRecovery)
            {
                targetPosition = sameLaneTarget;
                targetDirection = sameLaneDirection;
                m_MergeHold = 0f;
                ReliableDriveRuntime.SetLaneAssistStatus("Lane assist: wrong-way recovery");
                return true;
            }

            if (m_WrongWayHold > 0.25f && !IsUturnAllowedForSteering(steering))
            {
                float gatedLookAhead = Mathf.Lerp(7f, 14f, speedBlend);
                targetPosition = position + forward * gatedLookAhead + right * steering * ReliableDriveRuntime.SteeringStrength * 0.35f;
                targetDirection = forward;
                m_MergeHold = 0f;
                ReliableDriveRuntime.SetLaneAssistStatus("Lane assist: U-turn blocked");
                ReliableDriveRuntime.SetTurnGateStatus("U-turn blocked: triple-tap A/D to allow");
                return true;
            }

            float absSteering = Mathf.Abs(steering);
            if (absSteering < 0.18f)
                m_MergeHold = Mathf.MoveTowards(m_MergeHold, 0f, ReliableDriveRuntime.MergeHoldFall);
            else
                m_MergeHold = Mathf.MoveTowards(m_MergeHold, 1f, ReliableDriveRuntime.MergeHoldRise);

            bool currentIsConnectionLane = EntityManager.HasComponent<Game.Net.ConnectionLane>(currentLane.m_Lane);
            float remainingMeters = curveSign > 0f
                ? (1f - curveT) * Mathf.Max(1f, curve.m_Length)
                : curveT * Mathf.Max(1f, curve.m_Length);
            float scanGateMeters = Mathf.Lerp(ReliableDriveRuntime.JunctionGateSlow, ReliableDriveRuntime.JunctionGateFast, speedBlend);
            float turnLaneReleaseMeters = Mathf.Lerp(ReliableDriveRuntime.TurnLaneReleaseSlow, ReliableDriveRuntime.TurnLaneReleaseFast, speedBlend);
            float junctionReleaseMeters = Mathf.Lerp(ReliableDriveRuntime.JunctionReleaseSlow, ReliableDriveRuntime.JunctionReleaseFast, speedBlend);
            bool hasTurnZoneAnchor = TryFindTurnZoneAnchor(currentLane, curve, curveT, curveSign, position, laneForward, laneRight, scanGateMeters, out Vector3 turnZoneAnchor, out float turnZoneDistance, out _);
            bool junctionTurnGate = currentIsConnectionLane || (hasTurnZoneAnchor && turnZoneDistance <= scanGateMeters);
            bool manualReleaseOpen = currentIsConnectionLane ||
                                     (hasTurnZoneAnchor && matchingTurnLane && turnZoneDistance <= turnLaneReleaseMeters) ||
                                     (hasTurnZoneAnchor && junctionTurnGate && turnZoneDistance <= junctionReleaseMeters);
            if (hasTurnZoneAnchor)
                ReliableDriveRuntime.SetTurnZoneDebug(turnZoneAnchor, laneForward, laneRight, turnLaneReleaseMeters, junctionReleaseMeters, scanGateMeters, turnZoneDistance, currentIsConnectionLane, matchingTurnLane, junctionTurnGate, manualReleaseOpen, true);
            else
                ReliableDriveRuntime.SetTurnZoneDebug(position + laneForward * scanGateMeters, laneForward, laneRight, turnLaneReleaseMeters, junctionReleaseMeters, scanGateMeters, scanGateMeters, false, false, false, false, false);

            if (absSteering >= 0.18f &&
                TryFindJunctionBoxTurnTarget(currentLane, currentIsConnectionLane, junctionTurnGate, position, laneForward, laneRight, steering, speedMps, out Vector3 junctionTarget, out Vector3 junctionDirection))
            {
                float turnBlend = Mathf.Lerp(ReliableDriveRuntime.JunctionTurnBlendMin, ReliableDriveRuntime.JunctionTurnBlendMax, Mathf.Clamp01((absSteering - 0.18f) / 0.72f));
                targetPosition = Vector3.Lerp(sameLaneTarget, junctionTarget, turnBlend);
                targetDirection = Vector3.Slerp(sameLaneDirection, junctionDirection, Mathf.Clamp01(turnBlend + 0.05f)).normalized;
                m_MergeHold = 0f;
                ReliableDriveRuntime.SetLaneAssistStatus(steering > 0f ? "Lane assist: junction box right" : "Lane assist: junction box left");
                return true;
            }

            if (absSteering >= 0.18f &&
                TryFindSideStreetTurnTarget(currentLane, curve, curveT, curveSign, matchingTurnLane, position, laneForward, laneRight, steering, speedMps, out Vector3 sideStreetTarget, out Vector3 sideStreetDirection))
            {
                targetPosition = sideStreetTarget;
                targetDirection = sideStreetDirection;
                m_MergeHold = 0f;
                ReliableDriveRuntime.SetLaneAssistStatus(steering > 0f ? "Lane assist: strong side-street right" : "Lane assist: strong side-street left");
                return true;
            }

            if (absSteering >= 0.18f &&
                hasTurnZoneAnchor &&
                TryBuildManualJunctionTurnTarget(curve, curveT, curveSign, matchingTurnLane, currentIsConnectionLane, junctionTurnGate, turnZoneDistance, position, laneForward, laneRight, steering, speedMps, out Vector3 manualTurnTarget, out Vector3 manualTurnDirection))
            {
                targetPosition = manualTurnTarget;
                targetDirection = manualTurnDirection;
                m_MergeHold = 0f;
                ReliableDriveRuntime.SetLaneAssistStatus(steering > 0f ? "Lane assist: road-kept turn right" : "Lane assist: road-kept turn left");
                return true;
            }

            if (absSteering >= 0.18f && !junctionTurnGate)
                ReliableDriveRuntime.SetTurnGateStatus(hasTurnZoneAnchor ? $"Turn gate: approach locked {turnZoneDistance:0}m" : "Turn gate: no junction target");

            if (hasCarLane &&
                absSteering >= 0.18f &&
                TryFindMergeLaneTarget(currentLane, carLane, curveT, aheadMeters, laneForward, laneRight, steering, out Vector3 mergeTarget, out Vector3 mergeDirection))
            {
                float blend = Mathf.Clamp01((absSteering - 0.18f) / 0.82f) * Mathf.SmoothStep(0f, 1f, m_MergeHold);
                targetPosition = Vector3.Lerp(sameLaneTarget, mergeTarget, blend);
                targetDirection = Vector3.Slerp(sameLaneDirection, mergeDirection, blend).normalized;
                ReliableDriveRuntime.SetLaneAssistStatus(steering > 0f ? "Lane assist: aiming merge right" : "Lane assist: aiming merge left");
                return true;
            }

            targetPosition = sameLaneTarget;
            targetDirection = sameLaneDirection;
            ReliableDriveRuntime.SetLaneAssistStatus("Lane assist: holding lane center");
            return true;
        }

        private bool TryFindJunctionBoxTurnTarget(CarCurrentLane currentLane, bool currentIsConnectionLane, bool junctionTurnGate, Vector3 position, Vector3 laneForward, Vector3 laneRight, float steering, float speedMps, out Vector3 turnTarget, out Vector3 turnDirection)
        {
            turnTarget = default;
            turnDirection = laneForward;

            if (m_ConnectionLaneQuery == default || m_ConnectionLaneQuery.IsEmptyIgnoreFilter)
                return false;

            if (!junctionTurnGate)
                return false;

            int desiredSide = steering > 0f ? 1 : -1;
            float now = UnityEngine.Time.unscaledTime;
            if (m_TurnTargetCacheLane == currentLane.m_Lane &&
                m_TurnTargetCacheSide == desiredSide &&
                now < m_TurnTargetCacheUntil &&
                Vector3.Dot(m_TurnTargetCachePosition - position, laneForward) > -6f)
            {
                turnTarget = m_TurnTargetCachePosition;
                turnDirection = m_TurnTargetCacheDirection;
                SetPendingTurnConnector(
                    m_TurnTargetCacheConnectionLane,
                    m_TurnTargetCacheConnectionCurvePosition,
                    m_TurnTargetCacheConnectionFlags,
                    m_TurnTargetCacheExitLane,
                    m_TurnTargetCacheExitCurvePosition,
                    m_TurnTargetCacheExitFlags);
                ReliableDriveRuntime.SetTurnGateStatus(currentIsConnectionLane ? "Turn gate: inside junction" : "Turn gate: junction cached");
                return true;
            }

            float speedBlend = Mathf.InverseLerp(0f, 24f, speedMps);
            float linkedActivationForward = currentIsConnectionLane ? Mathf.Lerp(14f, 24f, speedBlend) : Mathf.Lerp(ReliableDriveRuntime.LinkedForwardSlow, ReliableDriveRuntime.LinkedForwardFast, speedBlend);
            float linkedActivationBehind = ReliableDriveRuntime.LinkedBehind;
            float linkedActivationRadius = currentIsConnectionLane ? Mathf.Lerp(18f, 26f, speedBlend) : Mathf.Lerp(ReliableDriveRuntime.LinkedRadiusSlow, ReliableDriveRuntime.LinkedRadiusFast, speedBlend);
            float insideActivationForward = ReliableDriveRuntime.InsideForward;
            float insideActivationBehind = ReliableDriveRuntime.InsideBehind;
            float insideActivationRadius = Mathf.Lerp(ReliableDriveRuntime.InsideRadiusSlow, ReliableDriveRuntime.InsideRadiusFast, speedBlend);
            float targetAhead = Mathf.Lerp(ReliableDriveRuntime.JunctionTargetAheadSlow, ReliableDriveRuntime.JunctionTargetAheadFast, speedBlend);
            Entity bestLane = Entity.Null;
            Game.Net.Curve bestCurve = default;
            float bestSign = 1f;
            float bestTargetT = 0f;
            Vector3 bestDirection = laneForward;
            float bestScore = float.MaxValue;
            int scanned = 0;
            int road = 0;
            int near = 0;
            int linkedNear = 0;
            int unlinkedNear = 0;
            int side = 0;
            int linkedSide = 0;
            int unlinkedSide = 0;
            bool bestLinkedToCurrent = false;
            Entity bestExitLane = Entity.Null;
            float2 bestConnectionCurvePosition = default;
            float2 bestExitCurvePosition = default;
            CarLaneFlags bestConnectionFlags = default;
            CarLaneFlags bestExitFlags = default;

            NativeArray<Entity> entities = m_ConnectionLaneQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity candidate = entities[i];
                    scanned++;
                    if (!EntityManager.TryGetComponent(candidate, out Game.Net.Curve candidateCurve) ||
                        !EntityManager.TryGetComponent(candidate, out Game.Net.ConnectionLane connectionLane) ||
                        !EntityManager.TryGetComponent(candidate, out Game.Net.LaneConnection laneConnection) ||
                        candidateCurve.m_Length < 3f ||
                        (connectionLane.m_Flags & Game.Net.ConnectionLaneFlags.Disabled) != (Game.Net.ConnectionLaneFlags)0 ||
                        (connectionLane.m_Flags & Game.Net.ConnectionLaneFlags.Road) == (Game.Net.ConnectionLaneFlags)0 ||
                        (connectionLane.m_RoadTypes != Game.Net.RoadTypes.None && (connectionLane.m_RoadTypes & Game.Net.RoadTypes.Car) == Game.Net.RoadTypes.None))
                    {
                        continue;
                    }

                    if (EntityManager.TryGetComponent(candidate, out Game.Net.CarLane candidateCarLane) &&
                        (candidateCarLane.m_Flags & Game.Net.CarLaneFlags.Forbidden) != (Game.Net.CarLaneFlags)0U)
                    {
                        continue;
                    }

                    road++;
                    bool startsFromCurrent = laneConnection.m_StartLane == currentLane.m_Lane;
                    bool endsAtCurrent = laneConnection.m_EndLane == currentLane.m_Lane;
                    bool isCurrentConnection = candidate == currentLane.m_Lane;
                    bool linkedToCurrent = startsFromCurrent || endsAtCurrent || isCurrentConnection;
                    Vector3 candidateStart = ToUnityVector(candidateCurve.m_Bezier.a);
                    Vector3 candidateEnd = ToUnityVector(candidateCurve.m_Bezier.d);

                    float entryT;
                    float candidateSign;
                    Vector3 entryPoint;
                    if (isCurrentConnection)
                    {
                        float t = Mathf.Clamp01(currentLane.m_CurvePosition.x);
                        Vector3 rawTangent = ToUnityVector(MathUtils.Tangent(candidateCurve.m_Bezier, t));
                        if (rawTangent.sqrMagnitude < 0.001f)
                            continue;

                        rawTangent.Normalize();
                        candidateSign = Vector3.Dot(rawTangent, laneForward) >= 0f ? 1f : -1f;
                        entryT = t;
                        entryPoint = ToUnityVector(MathUtils.Position(candidateCurve.m_Bezier, entryT));
                    }
                    else if (startsFromCurrent)
                    {
                        entryT = 0f;
                        candidateSign = 1f;
                        entryPoint = ToUnityVector(candidateCurve.m_Bezier.a);
                    }
                    else if (endsAtCurrent)
                    {
                        entryT = 1f;
                        candidateSign = -1f;
                        entryPoint = ToUnityVector(candidateCurve.m_Bezier.d);
                    }
                    else
                    {
                        float endpointDistance = Mathf.Min(Vector3.Distance(candidateStart, position), Vector3.Distance(candidateEnd, position));
                        if (endpointDistance > insideActivationRadius + Mathf.Min(24f, candidateCurve.m_Length))
                            continue;

                        if (!TryFindClosestCurveSample(candidateCurve, position, out float closestT, out Vector3 closestPoint, out float closestDistance) ||
                            closestDistance > insideActivationRadius)
                        {
                            continue;
                        }

                        Vector3 rawTangent = ToUnityVector(MathUtils.Tangent(candidateCurve.m_Bezier, closestT));
                        if (rawTangent.sqrMagnitude < 0.001f)
                            continue;

                        rawTangent.Normalize();
                        candidateSign = Vector3.Dot(rawTangent, laneForward) >= 0f ? 1f : -1f;
                        entryT = closestT;
                        entryPoint = closestPoint;
                    }

                    Vector3 entryDelta = entryPoint - position;
                    float entryDistance = entryDelta.magnitude;
                    float entryForwardDistance = Vector3.Dot(entryDelta, laneForward);
                    if (linkedToCurrent &&
                        !isCurrentConnection &&
                        (entryDistance > linkedActivationRadius ||
                         entryForwardDistance > linkedActivationForward ||
                         entryForwardDistance < -linkedActivationBehind))
                    {
                        continue;
                    }

                    if (!linkedToCurrent &&
                        (entryDistance > insideActivationRadius ||
                         entryForwardDistance > insideActivationForward ||
                         entryForwardDistance < -insideActivationBehind))
                    {
                        continue;
                    }

                    near++;
                    if (linkedToCurrent)
                        linkedNear++;
                    else
                        unlinkedNear++;

                    float targetT = Mathf.Clamp01(entryT + candidateSign * targetAhead / Mathf.Max(1f, candidateCurve.m_Length));
                    if (Mathf.Abs(targetT - entryT) < 0.035f)
                        continue;

                    Vector3 candidateTarget = ToUnityVector(MathUtils.Position(candidateCurve.m_Bezier, targetT));
                    Vector3 candidateForward = GetLaneForwardWithSign(candidateCurve, targetT, candidateSign, laneForward);
                    Vector3 entryForward = GetLaneForwardWithSign(candidateCurve, entryT, candidateSign, laneForward);
                    Vector3 delta = candidateTarget - position;
                    float forwardDistance = Vector3.Dot(delta, laneForward);
                    float sideDistance = Vector3.Dot(delta, laneRight) * desiredSide;
                    float turnDot = Vector3.Dot(candidateForward, laneRight) * desiredSide;
                    float finalForwardDot = Vector3.Dot(candidateForward, laneForward);

                    if (Vector3.Dot(entryForward, laneForward) < -0.58f ||
                        forwardDistance < -10f ||
                        sideDistance < ReliableDriveRuntime.JunctionSideMin ||
                        turnDot < ReliableDriveRuntime.JunctionTurnDotMin ||
                        (finalForwardDot < ReliableDriveRuntime.JunctionBackDotMin && !IsUturnAllowedForSteering(steering)))
                    {
                        continue;
                    }

                    side++;
                    if (linkedToCurrent)
                        linkedSide++;
                    else
                        unlinkedSide++;

                    float sourceBonus = linkedToCurrent ? -11.5f : 11.5f;
                    float score =
                        entryDistance * 1.06f +
                        Mathf.Abs(entryForwardDistance) * 0.64f +
                        Mathf.Abs(forwardDistance - Mathf.Lerp(10f, 22f, speedBlend)) * 0.16f +
                        Mathf.Abs(sideDistance - Mathf.Lerp(7f, 18f, speedBlend)) * 0.54f +
                        (1f - Mathf.Clamp01(turnDot)) * 16.8f +
                        Mathf.Max(0f, -finalForwardDot) * 18.9f +
                        sourceBonus;

                    if (score < bestScore)
                    {
                        Entity candidateExitLane = candidateSign > 0f ? laneConnection.m_EndLane : laneConnection.m_StartLane;
                        float2 candidateExitCurvePosition = default;
                        CarLaneFlags candidateExitFlags = default;
                        bool hasExitLane = TryBuildExitNavigationLane(candidateExitLane, candidateSign > 0f ? candidateEnd : candidateStart, candidateForward, steering, out candidateExitCurvePosition, out candidateExitFlags);

                        bestScore = score;
                        bestLane = candidate;
                        bestCurve = candidateCurve;
                        bestSign = candidateSign;
                        bestTargetT = targetT;
                        bestDirection = candidateForward;
                        bestLinkedToCurrent = linkedToCurrent;
                        bestExitLane = hasExitLane ? candidateExitLane : Entity.Null;
                        bestConnectionCurvePosition = candidateSign > 0f ? new float2(entryT, 1f) : new float2(entryT, 0f);
                        bestConnectionFlags = BuildNavigationLaneFlags(true, steering, candidateSign, true);
                        bestExitCurvePosition = candidateExitCurvePosition;
                        bestExitFlags = candidateExitFlags;
                    }
                }
            }
            finally
            {
                entities.Dispose();
            }

            if (bestLane == Entity.Null)
            {
                if (currentIsConnectionLane || linkedSide > 0 || unlinkedSide > 0)
                {
                    Vector3 sideDirection = laneRight * desiredSide;
                    float fallbackForward = Mathf.Lerp(ReliableDriveRuntime.JunctionFallbackForwardSlow, ReliableDriveRuntime.JunctionFallbackForwardFast, speedBlend);
                    float fallbackSide = Mathf.Lerp(ReliableDriveRuntime.JunctionFallbackSideMin, ReliableDriveRuntime.JunctionFallbackSideMax, Mathf.Clamp01(Mathf.Abs(steering)));
                    fallbackSide += Mathf.Clamp(speedMps * 0.04f, 0f, 1.4f);
                    turnTarget = position + laneForward * fallbackForward + sideDirection * fallbackSide;
                    Vector3 roadKeptDirection = (laneForward + sideDirection * Mathf.Lerp(0.22f, 0.55f, Mathf.Clamp01(Mathf.Abs(steering)))).normalized;
                    turnDirection = Vector3.Slerp(laneForward, roadKeptDirection, Mathf.Clamp01(0.35f + Mathf.Abs(steering) * 0.28f)).normalized;
                    m_TurnTargetCacheLane = currentLane.m_Lane;
                    m_TurnTargetCacheSide = desiredSide;
                    m_TurnTargetCachePosition = turnTarget;
                    m_TurnTargetCacheDirection = turnDirection;
                    m_TurnTargetCacheConnectionLane = Entity.Null;
                    m_TurnTargetCacheConnectionCurvePosition = default;
                    m_TurnTargetCacheConnectionFlags = default;
                    m_TurnTargetCacheExitLane = Entity.Null;
                    m_TurnTargetCacheExitCurvePosition = default;
                    m_TurnTargetCacheExitFlags = default;
                    m_TurnTargetCacheUntil = now + 0.22f;
                    ReliableDriveRuntime.SetTurnGateStatus("Turn gate: road-keeper fallback");
                    if (--m_TurnLogCooldown <= 0)
                    {
                        Mod.log.Info($"Reliable Drive junction-box manual fallback side={desiredSide} currentConnection={currentIsConnectionLane} gate={junctionTurnGate} scanned={scanned} road={road} near={near} linkedNear={linkedNear} unlinkedNear={unlinkedNear} side={side} linkedSide={linkedSide} unlinkedSide={unlinkedSide}");
                        m_TurnLogCooldown = 90;
                    }
                    return true;
                }

                if (--m_TurnLogCooldown <= 0)
                {
                    Mod.log.Info($"Reliable Drive junction-box turn missed side={desiredSide} currentConnection={currentIsConnectionLane} gate={junctionTurnGate} scanned={scanned} road={road} near={near} linkedNear={linkedNear} unlinkedNear={unlinkedNear} side={side} linkedSide={linkedSide} unlinkedSide={unlinkedSide}");
                    m_TurnLogCooldown = 90;
                }
                return false;
            }

            turnTarget = ToUnityVector(MathUtils.Position(bestCurve.m_Bezier, bestTargetT));
            turnDirection = bestDirection;
            m_TurnTargetCacheLane = currentLane.m_Lane;
            m_TurnTargetCacheSide = desiredSide;
            m_TurnTargetCachePosition = turnTarget;
            m_TurnTargetCacheDirection = turnDirection;
            m_TurnTargetCacheConnectionLane = bestLane;
            m_TurnTargetCacheConnectionCurvePosition = bestConnectionCurvePosition;
            m_TurnTargetCacheConnectionFlags = bestConnectionFlags;
            m_TurnTargetCacheExitLane = bestExitLane;
            m_TurnTargetCacheExitCurvePosition = bestExitCurvePosition;
            m_TurnTargetCacheExitFlags = bestExitFlags;
            m_TurnTargetCacheUntil = now + 0.35f;
            SetPendingTurnConnector(bestLane, bestConnectionCurvePosition, bestConnectionFlags, bestExitLane, bestExitCurvePosition, bestExitFlags);
            ReliableDriveRuntime.SetTurnGateStatus("Turn gate: junction path locked");
            if (--m_TurnLogCooldown <= 0)
            {
                Mod.log.Info($"Reliable Drive junction-box turn queued side={desiredSide} lane={bestLane} score={bestScore:0.0} currentConnection={currentIsConnectionLane} gate={junctionTurnGate} linked={bestLinkedToCurrent} t={bestTargetT:0.00} scanned={scanned} road={road} near={near} linkedNear={linkedNear} unlinkedNear={unlinkedNear} side={side} linkedSide={linkedSide} unlinkedSide={unlinkedSide}");
                m_TurnLogCooldown = 90;
            }
            return true;
        }

        private static bool TryFindClosestCurveSample(Game.Net.Curve curve, Vector3 position, out float bestT, out Vector3 bestPosition, out float bestDistance)
        {
            bestT = 0f;
            bestPosition = default;
            bestDistance = float.MaxValue;

            const int sampleCount = 9;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)(sampleCount - 1);
                Vector3 sample = ToUnityVector(MathUtils.Position(curve.m_Bezier, t));
                float distance = Vector3.Distance(position, sample);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestT = t;
                    bestPosition = sample;
                }
            }

            return bestDistance < float.MaxValue * 0.5f;
        }

        private bool TryBuildManualJunctionTurnTarget(Game.Net.Curve currentCurve, float curveT, float curveSign, bool matchingTurnLane, bool currentIsConnectionLane, bool junctionTurnGate, float distanceToTurnZone, Vector3 position, Vector3 laneForward, Vector3 laneRight, float steering, float speedMps, out Vector3 turnTarget, out Vector3 turnDirection)
        {
            turnTarget = default;
            turnDirection = laneForward;

            float remainingMeters = Mathf.Max(0f, distanceToTurnZone);

            float speedBlend = Mathf.InverseLerp(0f, 22f, speedMps);
            float turnLaneArmDistance = Mathf.Lerp(ReliableDriveRuntime.TurnLaneReleaseSlow, ReliableDriveRuntime.TurnLaneReleaseFast, speedBlend);
            float generalJunctionArmDistance = Mathf.Lerp(ReliableDriveRuntime.JunctionReleaseSlow, ReliableDriveRuntime.JunctionReleaseFast, speedBlend);
            bool canReleaseTurn = currentIsConnectionLane ||
                                  (matchingTurnLane && remainingMeters <= turnLaneArmDistance) ||
                                  (junctionTurnGate && remainingMeters <= generalJunctionArmDistance);
            if (!canReleaseTurn)
                return false;

            int desiredSide = steering > 0f ? 1 : -1;
            float absSteering = Mathf.Abs(steering);
            Vector3 sideDirection = laneRight * desiredSide;

            float keeperForward = Mathf.Lerp(ReliableDriveRuntime.JunctionFallbackForwardSlow, ReliableDriveRuntime.JunctionFallbackForwardFast, speedBlend);
            float forwardAhead = Mathf.Clamp(remainingMeters + keeperForward * 0.45f, 5f, 22f);
            float targetT = Mathf.Clamp01(curveT + curveSign * forwardAhead / Mathf.Max(1f, currentCurve.m_Length));
            Vector3 centerTarget = ToUnityVector(MathUtils.Position(currentCurve.m_Bezier, targetT));
            Vector3 centerDirection = GetLaneForwardWithSign(currentCurve, targetT, curveSign, laneForward);
            float sideAhead = Mathf.Lerp(ReliableDriveRuntime.JunctionFallbackSideMin, ReliableDriveRuntime.JunctionFallbackSideMax, Mathf.Clamp01(absSteering)) + Mathf.Clamp(speedMps * 0.04f, 0f, 1.4f);
            if (matchingTurnLane && !currentIsConnectionLane)
                sideAhead *= 0.72f;

            turnTarget = centerTarget + sideDirection * sideAhead;
            Vector3 roadKeptDirection = (centerDirection + sideDirection * Mathf.Lerp(0.2f, 0.5f, Mathf.Clamp01(absSteering))).normalized;
            turnDirection = Vector3.Slerp(centerDirection, roadKeptDirection, Mathf.Clamp01(0.32f + absSteering * 0.24f)).normalized;
            ReliableDriveRuntime.SetTurnGateStatus("Turn gate: road-keeper release");

            return true;
        }

        private void ResetTurnTapGate()
        {
            m_LeftTapCount = 0;
            m_RightTapCount = 0;
            m_LeftTapUntil = 0f;
            m_RightTapUntil = 0f;
            m_UturnPermitUntil = 0f;
            m_UturnPermitDirection = 0;
            m_PreviousLeftHeld = false;
            m_PreviousRightHeld = false;
            ReliableDriveRuntime.SetTurnGateStatus("U-turn gate: triple-tap A/D to allow");
            ReliableDriveRuntime.SetBusAssistStatus("Bus assist ready");
            ResetTurnTargetCache();
        }

        private void UpdateTurnTapGate(bool leftHeld, bool rightHeld, bool leftPressed, bool rightPressed)
        {
            float now = UnityEngine.Time.unscaledTime;
            if (!leftHeld && !rightHeld && now > m_UturnPermitUntil)
                m_UturnPermitDirection = 0;

            if (leftPressed || (leftHeld && !m_PreviousLeftHeld))
                RegisterTurnTap(-1, now);

            if (rightPressed || (rightHeld && !m_PreviousRightHeld))
                RegisterTurnTap(1, now);

            m_PreviousLeftHeld = leftHeld;
            m_PreviousRightHeld = rightHeld;

            if (m_UturnPermitDirection != 0 && now <= m_UturnPermitUntil)
            {
                ReliableDriveRuntime.SetTurnGateStatus(m_UturnPermitDirection < 0 ? "U-turn allowed left" : "U-turn allowed right");
            }
            else if (leftHeld || rightHeld)
            {
                int count = leftHeld ? m_LeftTapCount : m_RightTapCount;
                ReliableDriveRuntime.SetTurnGateStatus($"U-turn locked: tap {count}/3");
            }
            else
            {
                ReliableDriveRuntime.SetTurnGateStatus("U-turn locked: triple-tap A/D");
            }
        }

        private void RegisterTurnTap(int direction, float now)
        {
            const float tapWindowSeconds = 0.48f;
            const float permitSeconds = 2.35f;

            if (direction < 0)
            {
                m_LeftTapCount = now <= m_LeftTapUntil ? m_LeftTapCount + 1 : 1;
                m_RightTapCount = 0;
                m_LeftTapUntil = now + tapWindowSeconds;
                if (m_LeftTapCount >= 3)
                {
                    m_UturnPermitDirection = -1;
                    m_UturnPermitUntil = now + permitSeconds;
                    m_LeftTapCount = 0;
                    m_RightTapCount = 0;
                }
            }
            else
            {
                m_RightTapCount = now <= m_RightTapUntil ? m_RightTapCount + 1 : 1;
                m_LeftTapCount = 0;
                m_RightTapUntil = now + tapWindowSeconds;
                if (m_RightTapCount >= 3)
                {
                    m_UturnPermitDirection = 1;
                    m_UturnPermitUntil = now + permitSeconds;
                    m_LeftTapCount = 0;
                    m_RightTapCount = 0;
                }
            }
        }

        private bool IsUturnAllowedForSteering(float steering)
        {
            if (m_UturnPermitDirection == 0 || UnityEngine.Time.unscaledTime > m_UturnPermitUntil)
                return false;

            if (Mathf.Abs(steering) < 0.18f)
                return true;

            return math.sign(steering) == m_UturnPermitDirection;
        }

        private void ApplyBusStopAssist(Entity car, Vector3 position, Vector3 forward, Vector3 right, float speedMps, ref Vector3 targetPosition, ref float desiredSpeedMps)
        {
            if (!ReliableDriveRuntime.BusStopAssistEnabled)
            {
                ReliableDriveRuntime.SetBusAssistStatus("Bus assist off");
                return;
            }

            if (!IsBusVehicle(car))
            {
                ReliableDriveRuntime.SetBusAssistStatus("Bus assist: bus only");
                return;
            }

            if (!TryFindNearbyBusStop(position, forward, right, out Vector3 stopPosition, out int waitingPassengers, out float distance, out int scannedStops, out int candidateStops, out float nearestDistance))
            {
                if (scannedStops <= 0)
                    ReliableDriveRuntime.SetBusAssistStatus("Bus assist: no bus stops found");
                else if (nearestDistance < float.MaxValue * 0.5f)
                    ReliableDriveRuntime.SetBusAssistStatus($"Bus assist: {scannedStops} stops, nearest {nearestDistance:0}m, {candidateStops} usable");
                else
                    ReliableDriveRuntime.SetBusAssistStatus($"Bus assist: {scannedStops} stops, none near front");
                return;
            }

            float slowMph = distance < 9f ? 3f : distance < 18f ? 10f : 18f;
            desiredSpeedMps = Mathf.Min(desiredSpeedMps, slowMph * kMphToMps);
            float blend = Mathf.InverseLerp(48f, 8f, distance);
            Vector3 stopApproach = stopPosition - forward * Mathf.Clamp(distance * 0.18f, 2f, 8f);
            targetPosition = Vector3.Lerp(targetPosition, stopApproach, Mathf.Clamp01(blend) * 0.7f);
            string passengerText = waitingPassengers >= 0 ? $"{waitingPassengers} waiting" : "passengers unknown";
            ReliableDriveRuntime.SetBusAssistStatus($"Bus assist: stop {distance:0}m, {passengerText}, {candidateStops}/{scannedStops} usable");
        }

        private bool TryFindNearbyBusStop(Vector3 position, Vector3 forward, Vector3 right, out Vector3 stopPosition, out int waitingPassengers, out float distance, out int scannedStops, out int candidateStops, out float nearestDistance)
        {
            stopPosition = default;
            waitingPassengers = -1;
            distance = float.MaxValue;
            scannedStops = 0;
            candidateStops = 0;
            nearestDistance = float.MaxValue;

            if (m_TransportStopQuery == default || m_TransportStopQuery.IsEmptyIgnoreFilter)
                return false;

            Entity bestStop = Entity.Null;
            float bestScore = float.MaxValue;
            NativeArray<Entity> entities = m_TransportStopQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity stop = entities[i];
                    if (!EntityManager.TryGetComponent(stop, out Game.Routes.Position routePosition))
                        continue;

                    scannedStops++;
                    Vector3 candidate = ToUnityVector(routePosition.m_Position);
                    Vector3 delta = candidate - position;
                    float candidateDistance = delta.magnitude;
                    if (candidateDistance < nearestDistance)
                        nearestDistance = candidateDistance;

                    float forwardDistance = Vector3.Dot(delta, forward);
                    if (forwardDistance < -6f || forwardDistance > 68f)
                        continue;

                    float lateralDistance = Mathf.Abs(Vector3.Dot(delta, right));
                    if (lateralDistance > 32f)
                        continue;

                    candidateStops++;
                    float score = candidateDistance + lateralDistance * 0.9f + Mathf.Max(0f, -forwardDistance) * 5f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestStop = stop;
                        stopPosition = candidate;
                        distance = candidateDistance;
                    }
                }
            }
            finally
            {
                entities.Dispose();
            }

            if (bestStop == Entity.Null)
                return false;

            if (EntityManager.TryGetComponent(bestStop, out Game.Routes.WaitingPassengers waiting))
                waitingPassengers = waiting.m_Count;

            return true;
        }

        private bool TryFindSideStreetTurnTarget(CarCurrentLane currentLane, Game.Net.Curve currentCurve, float curveT, float curveSign, bool matchingTurnLane, Vector3 position, Vector3 laneForward, Vector3 laneRight, float steering, float speedMps, out Vector3 turnTarget, out Vector3 turnDirection)
        {
            turnTarget = default;
            turnDirection = laneForward;

            int desiredSide = steering > 0f ? 1 : -1;
            float now = UnityEngine.Time.unscaledTime;
            if (m_TurnTargetCacheLane == currentLane.m_Lane &&
                m_TurnTargetCacheSide == desiredSide &&
                now < m_TurnTargetCacheUntil)
            {
                turnTarget = m_TurnTargetCachePosition;
                turnDirection = m_TurnTargetCacheDirection;
                SetPendingTurnConnector(
                    m_TurnTargetCacheConnectionLane,
                    m_TurnTargetCacheConnectionCurvePosition,
                    m_TurnTargetCacheConnectionFlags,
                    m_TurnTargetCacheExitLane,
                    m_TurnTargetCacheExitCurvePosition,
                    m_TurnTargetCacheExitFlags);
                return true;
            }

            if (m_ConnectionLaneQuery == default || m_ConnectionLaneQuery.IsEmptyIgnoreFilter)
                return false;

            float remainingMeters = curveSign > 0f
                ? (1f - curveT) * Mathf.Max(1f, currentCurve.m_Length)
                : curveT * Mathf.Max(1f, currentCurve.m_Length);

            float connectorArmDistance = GetJunctionConnectorArmDistance(matchingTurnLane, speedMps);
            if (remainingMeters > connectorArmDistance)
                return false;

            Vector3 currentLaneEnd = ToUnityVector(MathUtils.Position(currentCurve.m_Bezier, curveSign > 0f ? 1f : 0f));
            Entity bestLane = Entity.Null;
            Entity bestExitLane = Entity.Null;
            Game.Net.Curve bestCurve = default;
            float bestSign = 1f;
            Vector3 bestDirection = laneForward;
            float2 bestConnectionCurvePosition = default;
            float2 bestExitCurvePosition = default;
            CarLaneFlags bestConnectionFlags = default;
            CarLaneFlags bestExitFlags = default;
            float bestScore = float.MaxValue;
            int scanned = 0;
            int roadCandidates = 0;
            int nearCandidates = 0;
            int sideCandidates = 0;

            NativeArray<Entity> entities = m_ConnectionLaneQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity candidate = entities[i];
                    scanned++;
                    if (candidate == currentLane.m_Lane ||
                        !EntityManager.TryGetComponent(candidate, out Game.Net.Curve candidateCurve) ||
                        !EntityManager.TryGetComponent(candidate, out Game.Net.ConnectionLane connectionLane) ||
                        !EntityManager.TryGetComponent(candidate, out Game.Net.LaneConnection laneConnection) ||
                        candidateCurve.m_Length < 3f ||
                        (connectionLane.m_Flags & Game.Net.ConnectionLaneFlags.Disabled) != (Game.Net.ConnectionLaneFlags)0 ||
                        (connectionLane.m_Flags & Game.Net.ConnectionLaneFlags.Road) == (Game.Net.ConnectionLaneFlags)0 ||
                        (connectionLane.m_RoadTypes != Game.Net.RoadTypes.None && (connectionLane.m_RoadTypes & Game.Net.RoadTypes.Car) == Game.Net.RoadTypes.None))
                    {
                        continue;
                    }

                    bool hasCandidateCarLane = EntityManager.TryGetComponent(candidate, out Game.Net.CarLane candidateCarLane);
                    if (hasCandidateCarLane &&
                        (candidateCarLane.m_Flags & Game.Net.CarLaneFlags.Forbidden) != (Game.Net.CarLaneFlags)0U)
                    {
                        continue;
                    }

                    roadCandidates++;
                    bool referencesCurrentLane = laneConnection.m_StartLane == currentLane.m_Lane ||
                                                 laneConnection.m_EndLane == currentLane.m_Lane;
                    Vector3 start = ToUnityVector(candidateCurve.m_Bezier.a);
                    Vector3 end = ToUnityVector(candidateCurve.m_Bezier.d);
                    float startDistance = Vector3.Distance(start, currentLaneEnd);
                    float endDistance = Vector3.Distance(end, currentLaneEnd);
                    float entryDistance = Mathf.Min(startDistance, endDistance);
                    if (!referencesCurrentLane && entryDistance > (matchingTurnLane ? 32f : 18f))
                        continue;

                    nearCandidates++;
                    float candidateSign = startDistance <= endDistance ? 1f : -1f;
                    float entryT = candidateSign > 0f ? 0f : 1f;
                    Entity exitLane = candidateSign > 0f ? laneConnection.m_EndLane : laneConnection.m_StartLane;
                    Vector3 entryDirection = GetLaneForwardWithSign(candidateCurve, entryT, candidateSign, laneForward);
                    if (Vector3.Dot(entryDirection, laneForward) < -0.45f)
                        continue;

                    float speedBlend = Mathf.InverseLerp(0f, 22f, speedMps);
                    float connectionTargetAhead = Mathf.Lerp(10f, 24f, speedBlend);
                    float connectionTargetT = Mathf.Clamp01(entryT + candidateSign * connectionTargetAhead / Mathf.Max(1f, candidateCurve.m_Length));
                    Vector3 candidatePosition = ToUnityVector(MathUtils.Position(candidateCurve.m_Bezier, connectionTargetT));
                    Vector3 candidateForward = GetLaneForwardWithSign(candidateCurve, connectionTargetT, candidateSign, entryDirection);
                    float forwardDot = Vector3.Dot(candidateForward, laneForward);
                    float turnDot = Vector3.Dot(candidateForward, laneRight) * desiredSide;
                    Vector3 delta = candidatePosition - position;
                    float forwardDistance = Vector3.Dot(delta, laneForward);
                    float sideDistance = Vector3.Dot(delta, laneRight) * desiredSide;

                    // This is for normal side turns only. U-turns stay behind the existing triple-tap recovery gate.
                    if (turnDot < 0.22f || forwardDot < -0.36f || sideDistance < 0.75f || forwardDistance < -7f)
                        continue;

                    sideCandidates++;
                    float connectionFlagBonus = hasCandidateCarLane && HasMatchingTurnLaneFlag(candidateCarLane, steering, candidateSign) ? -12f : 0f;
                    float sourceBonus = referencesCurrentLane ? -18f : 0f;
                    float score =
                        entryDistance * 0.9f +
                        Mathf.Abs(remainingMeters) * 0.08f +
                        Mathf.Abs(forwardDistance - Mathf.Lerp(14f, 28f, speedBlend)) * 0.25f +
                        Mathf.Abs(sideDistance - Mathf.Lerp(7f, 18f, speedBlend)) * 0.45f +
                        (1f - turnDot) * 18f +
                        sourceBonus +
                        connectionFlagBonus;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestLane = candidate;
                        bestExitLane = Entity.Null;
                        bestCurve = candidateCurve;
                        bestSign = candidateSign;
                        bestDirection = candidateForward;
                        bestConnectionCurvePosition = candidateSign > 0f ? new float2(0f, 1f) : new float2(1f, 0f);
                        bestConnectionFlags = BuildNavigationLaneFlags(true, steering, candidateSign, true);
                        bestExitCurvePosition = default;
                        bestExitFlags = default;
                        if (TryBuildExitNavigationLane(exitLane, candidateSign > 0f ? end : start, candidateForward, steering, out float2 exitCurvePosition, out CarLaneFlags exitFlags))
                        {
                            bestExitLane = exitLane;
                            bestExitCurvePosition = exitCurvePosition;
                            bestExitFlags = exitFlags;
                        }
                    }
                }
            }
            finally
            {
                entities.Dispose();
            }

            if (bestLane == Entity.Null)
            {
                if (--m_TurnLogCooldown <= 0)
                {
                    Mod.log.Info($"Reliable Drive turn scan missed side={desiredSide} remaining={remainingMeters:0.0} matchingLane={matchingTurnLane} scanned={scanned} road={roadCandidates} near={nearCandidates} side={sideCandidates}");
                    m_TurnLogCooldown = 90;
                }
                return false;
            }

            float targetAhead = Mathf.Lerp(11f, 26f, Mathf.InverseLerp(0f, 24f, speedMps));
            float bestEntryT = bestSign > 0f ? 0f : 1f;
            float targetT = Mathf.Clamp01(bestEntryT + bestSign * targetAhead / Mathf.Max(1f, bestCurve.m_Length));
            turnTarget = ToUnityVector(MathUtils.Position(bestCurve.m_Bezier, targetT));
            turnDirection = GetLaneForwardWithSign(bestCurve, targetT, bestSign, bestDirection);
            m_TurnTargetCacheLane = currentLane.m_Lane;
            m_TurnTargetCacheSide = desiredSide;
            m_TurnTargetCachePosition = turnTarget;
            m_TurnTargetCacheDirection = turnDirection;
            m_TurnTargetCacheConnectionLane = bestLane;
            m_TurnTargetCacheConnectionCurvePosition = bestConnectionCurvePosition;
            m_TurnTargetCacheConnectionFlags = bestConnectionFlags;
            m_TurnTargetCacheExitLane = bestExitLane;
            m_TurnTargetCacheExitCurvePosition = bestExitCurvePosition;
            m_TurnTargetCacheExitFlags = bestExitFlags;
            m_TurnTargetCacheUntil = now + 0.18f;
            SetPendingTurnConnector(bestLane, bestConnectionCurvePosition, bestConnectionFlags, bestExitLane, bestExitCurvePosition, bestExitFlags);
            if (--m_TurnLogCooldown <= 0)
            {
                Mod.log.Info($"Reliable Drive turn scan queued side={desiredSide} lane={bestLane} exit={bestExitLane} score={bestScore:0.0} remaining={remainingMeters:0.0} scanned={scanned} road={roadCandidates} near={nearCandidates} side={sideCandidates}");
                m_TurnLogCooldown = 90;
            }
            return true;
        }

        private static float GetJunctionConnectorArmDistance(bool matchingTurnLane, float speedMps)
        {
            float speedBlend = Mathf.InverseLerp(0f, 22f, speedMps);
            return matchingTurnLane
                ? Mathf.Lerp(1.35f, 3.75f, speedBlend)
                : Mathf.Lerp(0.75f, 2.25f, speedBlend);
        }

        private bool TryFindMergeLaneTarget(CarCurrentLane currentLane, Game.Net.CarLane currentCarLane, float curveT, float aheadMeters, Vector3 laneForward, Vector3 right, float steering, out Vector3 mergeTarget, out Vector3 mergeDirection)
        {
            mergeTarget = default;
            mergeDirection = laneForward;

            if (!EntityManager.TryGetComponent(currentLane.m_Lane, out Owner owner) ||
                !EntityManager.TryGetComponent(currentLane.m_Lane, out Game.Net.SlaveLane slaveLane) ||
                !EntityManager.HasBuffer<Game.Net.SubLane>(owner.m_Owner))
            {
                return false;
            }

            DynamicBuffer<Game.Net.SubLane> subLanes = EntityManager.GetBuffer<Game.Net.SubLane>(owner.m_Owner, true);
            int minIndex = math.clamp((int)slaveLane.m_MinIndex, 0, math.max(0, subLanes.Length - 1));
            int maxIndex = math.clamp((int)slaveLane.m_MaxIndex, 0, math.max(0, subLanes.Length - 1));
            if (maxIndex < minIndex)
                return false;

            float desiredSide = math.sign(steering);
            Vector3 currentPosition = ToUnityVector(MathUtils.Position(EntityManager.GetComponentData<Game.Net.Curve>(currentLane.m_Lane).m_Bezier, curveT));
            Entity bestLane = Entity.Null;
            Game.Net.Curve bestCurve = default;
            float bestScore = float.MaxValue;
            float bestSign = 1f;

            for (int i = minIndex; i <= maxIndex; i++)
            {
                Entity candidate = subLanes[i].m_SubLane;
                if (candidate == currentLane.m_Lane ||
                    !EntityManager.TryGetComponent(candidate, out Game.Net.Curve candidateCurve) ||
                    !EntityManager.TryGetComponent(candidate, out Game.Net.CarLane candidateCarLane) ||
                    candidateCurve.m_Length < 1f ||
                    candidateCarLane.m_CarriagewayGroup != currentCarLane.m_CarriagewayGroup ||
                    (candidateCarLane.m_Flags & Game.Net.CarLaneFlags.Forbidden) != (Game.Net.CarLaneFlags)0U)
                {
                    continue;
                }

                Vector3 candidatePosition = ToUnityVector(MathUtils.Position(candidateCurve.m_Bezier, curveT));
                float side = Vector3.Dot(candidatePosition - currentPosition, right);
                if (side * desiredSide <= 0.75f)
                    continue;

                Vector3 rawTangent = ToUnityVector(MathUtils.Tangent(candidateCurve.m_Bezier, curveT));
                if (rawTangent.sqrMagnitude < 0.001f)
                    continue;

                rawTangent.Normalize();
                float candidateSign = Vector3.Dot(rawTangent, laneForward) >= 0f ? 1f : -1f;
                Vector3 candidateForward = rawTangent * candidateSign;
                if (Vector3.Dot(candidateForward, laneForward) < 0.65f)
                    continue;

                float forwardError = Mathf.Abs(Vector3.Dot(candidatePosition - currentPosition, laneForward));
                float sideDistance = Mathf.Abs(side);
                float score = sideDistance + forwardError * 0.35f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestLane = candidate;
                    bestCurve = candidateCurve;
                    bestSign = candidateSign;
                }
            }

            if (bestLane == Entity.Null)
                return false;

            float targetT = Mathf.Clamp01(curveT + bestSign * aheadMeters / Mathf.Max(1f, bestCurve.m_Length));
            mergeTarget = ToUnityVector(MathUtils.Position(bestCurve.m_Bezier, targetT));
            mergeDirection = GetLaneForwardWithSign(bestCurve, targetT, bestSign, laneForward);
            return true;
        }

        private bool TryBuildExitNavigationLane(Entity lane, Vector3 entryPoint, Vector3 preferredForward, float steering, out float2 curvePosition, out CarLaneFlags flags)
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

            Vector3 start = ToUnityVector(curve.m_Bezier.a);
            Vector3 end = ToUnityVector(curve.m_Bezier.d);
            float sign = Vector3.Distance(start, entryPoint) <= Vector3.Distance(end, entryPoint) ? 1f : -1f;
            Vector3 laneForward = GetLaneForwardWithSign(curve, sign > 0f ? 0f : 1f, sign, preferredForward);
            if (Vector3.Dot(laneForward, preferredForward) < -0.35f)
                return false;

            curvePosition = sign > 0f ? new float2(0f, 1f) : new float2(1f, 0f);
            flags = BuildNavigationLaneFlags(false, steering, sign, (carLane.m_Flags & Game.Net.CarLaneFlags.Twoway) != (Game.Net.CarLaneFlags)0U);
            return true;
        }

        private static CarLaneFlags BuildNavigationLaneFlags(bool connection, float steering, float curveSign, bool requestSpace)
        {
            CarLaneFlags flags = CarLaneFlags.UpdateOptimalLane;
            if (connection)
                flags |= CarLaneFlags.Connection | CarLaneFlags.ResetSpeed;
            if (requestSpace)
                flags |= CarLaneFlags.RequestSpace;

            bool invertTurnFlags = curveSign < 0f;
            if (steering < -0.18f)
                flags |= invertTurnFlags ? CarLaneFlags.TurnRight : CarLaneFlags.TurnLeft;
            else if (steering > 0.18f)
                flags |= invertTurnFlags ? CarLaneFlags.TurnLeft : CarLaneFlags.TurnRight;

            return flags;
        }

        private void SetPendingTurnConnector(Entity connectionLane, float2 connectionCurvePosition, CarLaneFlags connectionFlags, Entity exitLane, float2 exitCurvePosition, CarLaneFlags exitFlags)
        {
            m_PendingTurnConnectionLane = connectionLane;
            m_PendingTurnConnectionCurvePosition = connectionCurvePosition;
            m_PendingTurnConnectionFlags = connectionFlags;
            m_PendingTurnExitLane = exitLane;
            m_PendingTurnExitCurvePosition = exitCurvePosition;
            m_PendingTurnExitFlags = exitFlags;

            if (ReliableDriveRuntime.StrongJunctionOverrideEnabled &&
                m_PossessedCar != Entity.Null &&
                connectionLane != Entity.Null &&
                exitLane != Entity.Null &&
                EntityManager.Exists(connectionLane) &&
                EntityManager.Exists(exitLane))
            {
                ReliableDriveRuntime.SetTurnNavigationQueue(m_PossessedCar, connectionLane, connectionCurvePosition, connectionFlags, exitLane, exitCurvePosition, exitFlags);
                ReliableDriveRuntime.SetTurnGateStatus("Turn gate: strong path override queued");
            }
        }

        private void ResetPendingTurnConnector()
        {
            m_PendingTurnConnectionLane = Entity.Null;
            m_PendingTurnExitLane = Entity.Null;
            m_PendingTurnConnectionCurvePosition = default;
            m_PendingTurnExitCurvePosition = default;
            m_PendingTurnConnectionFlags = default;
            m_PendingTurnExitFlags = default;
            ReliableDriveRuntime.ClearTurnNavigationQueue();
        }

        private static bool HasMatchingTurnLaneFlag(Game.Net.CarLane carLane, float steering, float curveSign)
        {
            if (Mathf.Abs(steering) < 0.18f)
                return false;

            bool reversedTravel = curveSign < 0f;
            Game.Net.CarLaneFlags leftFlags = reversedTravel
                ? Game.Net.CarLaneFlags.TurnRight | Game.Net.CarLaneFlags.GentleTurnRight
                : Game.Net.CarLaneFlags.TurnLeft | Game.Net.CarLaneFlags.GentleTurnLeft;
            Game.Net.CarLaneFlags rightFlags = reversedTravel
                ? Game.Net.CarLaneFlags.TurnLeft | Game.Net.CarLaneFlags.GentleTurnLeft
                : Game.Net.CarLaneFlags.TurnRight | Game.Net.CarLaneFlags.GentleTurnRight;

            Game.Net.CarLaneFlags desiredFlags = steering > 0f ? rightFlags : leftFlags;
            return (carLane.m_Flags & desiredFlags) != (Game.Net.CarLaneFlags)0U;
        }

        private void ResetTurnTargetCache()
        {
            m_TurnTargetCacheLane = Entity.Null;
            m_TurnTargetCachePosition = default;
            m_TurnTargetCacheDirection = default;
            m_TurnTargetCacheUntil = 0f;
            m_TurnTargetCacheSide = 0;
            m_TurnTargetCacheConnectionLane = Entity.Null;
            m_TurnTargetCacheExitLane = Entity.Null;
            m_TurnTargetCacheConnectionCurvePosition = default;
            m_TurnTargetCacheExitCurvePosition = default;
            m_TurnTargetCacheConnectionFlags = default;
            m_TurnTargetCacheExitFlags = default;
            ResetPendingTurnConnector();
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
            {
                return fallbackForward.sqrMagnitude > 0.001f ? fallbackForward.normalized : Vector3.forward;
            }

            tangent.Normalize();
            return tangent * curveSign;
        }

        private static Vector3 GetRightFromForward(Vector3 forward, Vector3 fallbackRight)
        {
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.001f)
                return fallbackRight.sqrMagnitude > 0.001f ? fallbackRight.normalized : Vector3.right;

            return right.normalized;
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
                    if (!IsDriveableLiveCar(candidate) || !IsRoadVehicleAllowed(candidate, false) || !EntityManager.TryGetComponent(candidate, out ObjectTransform transform))
                        continue;

                    Vector3 candidatePosition = ToUnityVector(transform.m_Position);
                    float distance = Vector3.Distance(position, candidatePosition);
                    if (distance > 180f)
                        continue;

                    float score = distance + GetVehicleSelectionPenalty(candidate);
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

        private bool TryFindNearestLiveWatercraft(Vector3 position, out Entity result)
        {
            result = Entity.Null;
            if (m_LiveWatercraftQuery == default || m_LiveWatercraftQuery.IsEmptyIgnoreFilter)
                return false;

            float bestScore = float.MaxValue;
            NativeArray<Entity> entities = m_LiveWatercraftQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity candidate = entities[i];
                    if (!IsDriveableLiveWatercraft(candidate) || !EntityManager.TryGetComponent(candidate, out ObjectTransform transform))
                        continue;

                    Vector3 candidatePosition = ToUnityVector(transform.m_Position);
                    float distance = Vector3.Distance(position, candidatePosition);
                    if (distance > 260f)
                        continue;

                    float score = distance + GetVehicleSelectionPenalty(candidate);
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

        private bool TryFindNearestLiveTrain(Vector3 position, out Entity result)
        {
            result = Entity.Null;
            if (m_LiveTrainQuery == default || m_LiveTrainQuery.IsEmptyIgnoreFilter)
                return false;

            float bestScore = float.MaxValue;
            NativeArray<Entity> entities = m_LiveTrainQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity candidate = entities[i];
                    if (!IsDriveableLiveTrain(candidate) || !EntityManager.TryGetComponent(candidate, out ObjectTransform transform))
                        continue;

                    Vector3 candidatePosition = ToUnityVector(transform.m_Position);
                    float distance = Vector3.Distance(position, candidatePosition);
                    if (distance > 320f)
                        continue;

                    float score = distance + GetVehicleSelectionPenalty(candidate);
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

        private bool IsDriveableLiveCar(Entity entity)
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
                   !EntityManager.HasComponent<Unspawned>(entity);
        }

        private bool IsDriveableLiveWatercraft(Entity entity)
        {
            return ReliableDriveRuntime.AllowWatercraft &&
                   entity != Entity.Null &&
                   EntityManager.Exists(entity) &&
                   EntityManager.HasComponent<Watercraft>(entity) &&
                   EntityManager.HasComponent<WatercraftNavigation>(entity) &&
                   EntityManager.HasComponent<WatercraftCurrentLane>(entity) &&
                   EntityManager.HasComponent<ObjectTransform>(entity) &&
                   EntityManager.HasComponent<Moving>(entity) &&
                   EntityManager.HasComponent<PrefabRef>(entity) &&
                   EntityManager.HasBuffer<TransformFrame>(entity) &&
                   !EntityManager.HasComponent<Deleted>(entity) &&
                   !EntityManager.HasComponent<Temp>(entity) &&
                   !EntityManager.HasComponent<TripSource>(entity) &&
                   !EntityManager.HasComponent<Unspawned>(entity);
        }

        private bool IsDriveableLiveTrain(Entity entity)
        {
            return ReliableDriveRuntime.AllowRailVehicles &&
                   entity != Entity.Null &&
                   EntityManager.Exists(entity) &&
                   EntityManager.HasComponent<Train>(entity) &&
                   EntityManager.HasComponent<TrainNavigation>(entity) &&
                   EntityManager.HasComponent<TrainCurrentLane>(entity) &&
                   EntityManager.HasComponent<ObjectTransform>(entity) &&
                   EntityManager.HasComponent<Moving>(entity) &&
                   EntityManager.HasComponent<PrefabRef>(entity) &&
                   EntityManager.HasBuffer<TransformFrame>(entity) &&
                   EntityManager.HasBuffer<TrainBogieFrame>(entity) &&
                   !EntityManager.HasComponent<Deleted>(entity) &&
                   !EntityManager.HasComponent<Temp>(entity) &&
                   !EntityManager.HasComponent<TripSource>(entity) &&
                   !EntityManager.HasComponent<ParkedTrain>(entity) &&
                   !EntityManager.HasComponent<Unspawned>(entity);
        }

        private Entity ResolveTransformEntity(Entity selected)
        {
            if (selected == Entity.Null || !EntityManager.Exists(selected))
                return Entity.Null;

            if (EntityManager.HasComponent<ObjectTransform>(selected) && !EntityManager.HasComponent<Deleted>(selected) && !EntityManager.HasComponent<Temp>(selected))
                return selected;

            if (EntityManager.TryGetComponent(selected, out Controller controller) && controller.m_Controller != Entity.Null && EntityManager.Exists(controller.m_Controller) && EntityManager.HasComponent<ObjectTransform>(controller.m_Controller))
                return controller.m_Controller;

            if (EntityManager.TryGetComponent(selected, out Owner owner) && owner.m_Owner != Entity.Null && EntityManager.Exists(owner.m_Owner) && EntityManager.HasComponent<ObjectTransform>(owner.m_Owner))
                return owner.m_Owner;

            return Entity.Null;
        }

        private Entity ResolveDriveableSelection(Entity selected)
        {
            if (selected == Entity.Null || !EntityManager.Exists(selected))
                return Entity.Null;

            if (IsAnyDriveableEntity(selected))
                return selected;

            if (EntityManager.TryGetComponent(selected, out Controller controller) &&
                controller.m_Controller != Entity.Null &&
                EntityManager.Exists(controller.m_Controller) &&
                IsAnyDriveableEntity(controller.m_Controller))
            {
                return controller.m_Controller;
            }

            if (EntityManager.TryGetComponent(selected, out Owner owner) &&
                owner.m_Owner != Entity.Null &&
                EntityManager.Exists(owner.m_Owner) &&
                IsAnyDriveableEntity(owner.m_Owner))
            {
                return owner.m_Owner;
            }

            return Entity.Null;
        }

        private bool IsAnyDriveableEntity(Entity entity)
        {
            return (IsDriveableLiveCar(entity) && IsRoadVehicleAllowed(entity, true)) ||
                   (ReliableDriveRuntime.AllowWatercraft && IsDriveableLiveWatercraft(entity)) ||
                   (ReliableDriveRuntime.AllowRailVehicles && IsDriveableLiveTrain(entity));
        }

        private Vector3 ResolveCameraSearchPosition()
        {
            Camera camera = Camera.main;
            if (camera != null)
                return camera.transform.position + camera.transform.forward * 35f;

            return ReliableDriveRuntime.PosePosition;
        }

        private string GetVehicleName(Entity vehicle)
        {
            try
            {
                if (EntityManager.TryGetComponent(vehicle, out PrefabRef prefabRef) && prefabRef.m_Prefab != Entity.Null)
                    return m_PrefabSystem.GetPrefabName(prefabRef.m_Prefab);
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
            if (name.Contains("dacia") || name.Contains("golf") || name.Contains("renault") || name.Contains("peugeot") || name.Contains("tesla") || name.Contains("mustang"))
                return -12f;
            if (IsRequestedServiceVehicleName(name) && ReliableDriveRuntime.AllowServiceVehicles)
                return -6f;
            if (ReliableDriveRuntime.AllowBikesAndMotorcycles && IsBikeOrMotorcycleName(name))
                return -4f;
            if (ReliableDriveRuntime.AllowWatercraft && IsBoatOrWatercraftName(name))
                return -3f;
            if (ReliableDriveRuntime.AllowRailVehicles && IsTrainOrRailName(name))
                return -3f;
            if (name.Contains("bus") || name.Contains("truck") || name.Contains("tractor") || name.Contains("ambulance") || name.Contains("fire") || name.Contains("police") || name.Contains("garbage") || name.Contains("maintenance"))
                return 35f;
            if (IsBikeOrMotorcycleName(name))
                return 45f;

            return 0f;
        }

        private bool IsBadAutomaticVehicle(Entity vehicle)
        {
            string name = GetVehicleName(vehicle).ToLowerInvariant();
            if (name.Contains("motorbike") || name.Contains("scooter") || name.Contains("bicycle") || name.Contains("tractor") || name.Contains("trailer") || name.Contains("train") || name.Contains("tram"))
                return true;

            if (ReliableDriveRuntime.AllowServiceVehicles && IsRequestedServiceVehicleName(name))
                return false;

            return name.Contains("bus") ||
                   name.Contains("truck") ||
                   name.Contains("ambulance") ||
                   name.Contains("fire") ||
                   name.Contains("police") ||
                   name.Contains("garbage") ||
                   name.Contains("maintenance") ||
                   name.Contains("snowplow") ||
                   name.Contains("delivery") ||
                   name.Contains("postvan") ||
                   name.Contains("hearse") ||
                   name.Contains("prison");
        }

        private bool IsRoadVehicleAllowed(Entity vehicle, bool selected)
        {
            string name = GetVehicleName(vehicle).ToLowerInvariant();

            if (EntityManager.HasComponent<Bicycle>(vehicle) || IsBikeOrMotorcycleName(name))
                return ReliableDriveRuntime.AllowBikesAndMotorcycles;

            if (ReliableDriveRuntime.AllowServiceVehicles && IsRequestedServiceVehicleName(name))
                return true;

            if (selected)
                return !IsBadAutomaticVehicle(vehicle);

            return !IsBadAutomaticVehicle(vehicle);
        }

        private string GetRoadVehicleKind(Entity vehicle)
        {
            string name = GetVehicleName(vehicle).ToLowerInvariant();
            if (EntityManager.HasComponent<Bicycle>(vehicle) || IsBikeOrMotorcycleName(name))
                return IsMotorcycleName(name) ? "motorcycle" : "bicycle";

            if (IsRequestedServiceVehicleName(name))
                return "service vehicle";

            return "car";
        }

        private static bool IsBikeOrMotorcycleName(string name)
        {
            return IsMotorcycleName(name) ||
                   name.Contains("bicycle") ||
                   name.Contains("bike") ||
                   name.Contains("cycle");
        }

        private static bool IsMotorcycleName(string name)
        {
            return name.Contains("motorbike") ||
                   name.Contains("motorcycle") ||
                   name.Contains("scooter") ||
                   name.Contains("moped");
        }

        private static bool IsBoatOrWatercraftName(string name)
        {
            return name.Contains("boat") ||
                   name.Contains("ship") ||
                   name.Contains("ferry") ||
                   name.Contains("watercraft");
        }

        private static bool IsTrainOrRailName(string name)
        {
            return name.Contains("train") ||
                   name.Contains("tram") ||
                   name.Contains("rail") ||
                   name.Contains("subway");
        }

        private static bool IsRequestedServiceVehicleName(string name)
        {
            return name.Contains("bus") ||
                   name.Contains("delivery") ||
                   name.Contains("postvan") ||
                   name.Contains("police") ||
                   name.Contains("garbage") ||
                   name.Contains("ambulance");
        }

        private bool IsBusVehicle(Entity vehicle)
        {
            return vehicle != Entity.Null &&
                   EntityManager.Exists(vehicle) &&
                   (EntityManager.HasComponent<Game.Vehicles.PublicTransport>(vehicle) || IsBusVehicleName(m_PossessedName));
        }

        private static bool IsBusVehicleName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            string lower = name.ToLowerInvariant();
            return lower.Contains("bus") || lower.Contains("coach");
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
    }
}
