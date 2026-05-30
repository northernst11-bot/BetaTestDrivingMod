using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Game.Vehicles;

namespace BetaTestDrivingMod
{
    internal struct ReliableDriveInputFrame
    {
        internal float Throttle;
        internal float Brake;
        internal float Steering;
        internal bool LeftHeld;
        internal bool RightHeld;
        internal bool ThrottlePressed;
        internal bool BrakePressed;
        internal bool LeftPressed;
        internal bool RightPressed;
        internal bool Fresh;
        internal float AgeSeconds;
        internal int Frame;
        internal uint Sequence;
    }

    internal static class ReliableDriveRuntime
    {
        private const float kInputStaleSeconds = 0.35f;

        private static ReliableDriveHudBehaviour s_Hud;
        private static bool s_ToggleRequested;
        private static bool s_ReleaseRequested;
        private static int s_LastInputFrame = -1;
        private static float s_LastInputSampleTime = -999f;
        private static uint s_InputSequence;
        private static bool s_ThrottlePressedLatched;
        private static bool s_BrakePressedLatched;
        private static bool s_LeftPressedLatched;
        private static bool s_RightPressedLatched;
        internal static float InputThrottle { get; private set; }
        internal static float InputBrake { get; private set; }
        internal static float InputSteering { get; private set; }
        internal static bool InputLeftHeld { get; private set; }
        internal static bool InputRightHeld { get; private set; }
        internal static bool InputFresh { get; private set; } = true;
        internal static float InputAgeSeconds => s_LastInputSampleTime < -100f ? 999f : Mathf.Max(0f, Time.unscaledTime - s_LastInputSampleTime);
        internal static bool IsDriving { get; private set; }
        internal static Entity PossessedEntity { get; private set; } = Entity.Null;
        internal static bool DriverCamera { get; set; } = false;
        internal static bool AllowServiceVehicles { get; set; } = true;
        internal static bool AllowBikesAndMotorcycles { get; set; } = false;
        internal static bool AllowWatercraft { get; set; } = false;
        internal static bool AllowRailVehicles { get; set; } = false;
        internal static bool LaneAssistEnabled { get; set; } = false;
        internal static bool StrongJunctionOverrideEnabled { get; set; } = false;
        internal static bool BusStopAssistEnabled { get; set; } = true;
        internal static float TargetSpeedMph { get; set; } = 38f;
        internal static bool AutoRoadSpeedEnabled { get; set; } = true;
        internal static float RoadSpeedMultiplier { get; set; } = 1f;
        internal static float JunctionTurnSpeedMph { get; set; } = 22f;
        internal static float SharpTurnSpeedMph { get; set; } = 16f;
        internal static float ReverseSpeedMph { get; set; } = 8f;
        internal static float AccelerationRate { get; set; } = 14f;
        internal static float BrakeRate { get; set; } = 34f;
        internal static float ReverseAccelerationRate { get; set; } = 9f;
        internal static float DirectionChangeRate { get; set; } = 44f;
        internal static float CoastingRate { get; set; } = 10f;
        internal static float SteeringStrength { get; set; } = 7.4f;
        internal static float LookAheadMeters { get; set; } = 13f;
        internal static float CameraDistance { get; set; } = 10.5f;
        internal static float CameraHeight { get; set; } = 3.25f;
        internal static float FreeTurnLookAheadMin { get; set; } = 2f;
        internal static float FreeTurnLookAheadMax { get; set; } = 4.5f;
        internal static float FreeSteerOffsetSlow { get; set; } = 3.5f;
        internal static float FreeSteerOffsetFast { get; set; } = 5.5f;
        internal static float FreeSteerDirectionSlow { get; set; } = 1.75f;
        internal static float FreeSteerDirectionFast { get; set; } = 2.35f;
        internal static float BlockedUturnSteeringScale { get; set; } = 0.45f;
        internal static float LaneLookAheadMin { get; set; } = 8f;
        internal static float LaneLookAheadMax { get; set; } = 24f;
        internal static float JunctionGateSlow { get; set; } = 58f;
        internal static float JunctionGateFast { get; set; } = 76f;
        internal static bool ShowTurnReleaseZones { get; set; } = false;
        internal static float TurnLaneReleaseSlow { get; set; } = 24f;
        internal static float TurnLaneReleaseFast { get; set; } = 42f;
        internal static float JunctionReleaseSlow { get; set; } = 8f;
        internal static float JunctionReleaseFast { get; set; } = 18f;
        internal static float TurnZoneHalfWidth { get; set; } = 5f;
        internal static float JunctionTurnBlendMin { get; set; } = 0.98f;
        internal static float JunctionTurnBlendMax { get; set; } = 1f;
        internal static float LinkedForwardSlow { get; set; } = 10.5f;
        internal static float LinkedForwardFast { get; set; } = 10.9f;
        internal static float LinkedBehind { get; set; } = 9.7f;
        internal static float LinkedRadiusSlow { get; set; } = 14.4f;
        internal static float LinkedRadiusFast { get; set; } = 26.4f;
        internal static float InsideForward { get; set; } = 8.5f;
        internal static float InsideBehind { get; set; } = 15f;
        internal static float InsideRadiusSlow { get; set; } = 13f;
        internal static float InsideRadiusFast { get; set; } = 18f;
        internal static float JunctionTargetAheadSlow { get; set; } = 11.7f;
        internal static float JunctionTargetAheadFast { get; set; } = 20.7f;
        internal static float JunctionSideMin { get; set; } = 0.28f;
        internal static float JunctionTurnDotMin { get; set; } = 0.088f;
        internal static float JunctionBackDotMin { get; set; } = -0.17f;
        internal static float JunctionFallbackForwardSlow { get; set; } = 7.5f;
        internal static float JunctionFallbackForwardFast { get; set; } = 14f;
        internal static float JunctionFallbackSideMin { get; set; } = 2.5f;
        internal static float JunctionFallbackSideMax { get; set; } = 6.5f;
        internal static float MergeHoldRise { get; set; } = 0.12f;
        internal static float MergeHoldFall { get; set; } = 0.2f;

        internal static string StatusText { get; private set; } = "Waiting for live car";
        internal static string FocusStatus { get; private set; } = "Game focus ready";
        internal static string LaneAssistStatus { get; private set; } = "Lane assist ready";
        internal static string TurnGateStatus { get; private set; } = "U-turn gate ready";
        internal static string TurnZoneStatus { get; private set; } = "Turn zones hidden";
        internal static string BusAssistStatus { get; private set; } = "Bus assist ready";
        internal static string SpeedAssistStatus { get; private set; } = "Speed ramp ready";
        internal static string PossessedPrefab { get; private set; } = "";
        internal static string PossessedKind { get; private set; } = "car";
        internal static float SpeedMph { get; private set; }
        internal static float ThrottleInput { get; private set; }
        internal static float SteeringInput { get; private set; }
        internal static bool Braking { get; private set; }
        internal static bool ReverseReady { get; private set; }
        internal static Vector3 PosePosition { get; private set; }
        internal static Quaternion PoseRotation { get; private set; } = Quaternion.identity;
        internal static Entity TurnQueueEntity { get; private set; } = Entity.Null;
        internal static Entity TurnConnectionLane { get; private set; } = Entity.Null;
        internal static Entity TurnExitLane { get; private set; } = Entity.Null;
        internal static float2 TurnConnectionCurvePosition { get; private set; }
        internal static float2 TurnExitCurvePosition { get; private set; }
        internal static CarLaneFlags TurnConnectionFlags { get; private set; }
        internal static CarLaneFlags TurnExitFlags { get; private set; }
        internal static bool HasTurnZoneDebug { get; private set; }
        internal static Vector3 TurnZonePosition { get; private set; }
        internal static Vector3 TurnZoneForward { get; private set; } = Vector3.forward;
        internal static Vector3 TurnZoneRight { get; private set; } = Vector3.right;
        internal static float TurnZoneLaneReleaseMeters { get; private set; }
        internal static float TurnZoneJunctionReleaseMeters { get; private set; }
        internal static float TurnZoneScanGateMeters { get; private set; }
        internal static float TurnZoneRemainingMeters { get; private set; }
        internal static bool TurnZoneManualReleaseOpen { get; private set; }
        internal static bool TurnZoneMatchingTurnLane { get; private set; }
        internal static bool TurnZoneJunctionGate { get; private set; }
        internal static bool TurnZoneInsideConnection { get; private set; }
        internal static bool TurnZoneHasRealJunction { get; private set; }

        internal static void EnsureHud()
        {
            if (s_Hud != null)
                return;

            GameObject hud = new GameObject("BetaTestDrivingMod HUD");
            Object.DontDestroyOnLoad(hud);
            s_Hud = hud.AddComponent<ReliableDriveHudBehaviour>();
        }

        internal static void ToggleHud()
        {
            EnsureHud();
            s_Hud?.ToggleMenu();
        }

        internal static void RequestToggle()
        {
            s_ToggleRequested = true;
        }

        internal static void RequestRelease()
        {
            s_ReleaseRequested = true;
        }

        internal static void SetInputSnapshot(float throttle, float brake, bool leftHeld, bool rightHeld)
        {
            SetInputSnapshot(throttle, brake, leftHeld, rightHeld, false, false, false, false);
        }

        private static void SetInputSnapshot(float throttle, float brake, bool leftHeld, bool rightHeld, bool throttlePressed, bool brakePressed, bool leftPressed, bool rightPressed)
        {
            bool wasThrottleHeld = InputThrottle > 0.1f;
            bool wasBrakeHeld = InputBrake > 0.1f;
            bool wasLeftHeld = InputLeftHeld;
            bool wasRightHeld = InputRightHeld;

            InputThrottle = Mathf.Clamp01(throttle);
            InputBrake = Mathf.Clamp01(brake);
            InputLeftHeld = leftHeld;
            InputRightHeld = rightHeld;
            InputSteering = (rightHeld ? 1f : 0f) - (leftHeld ? 1f : 0f);
            s_LastInputSampleTime = Time.unscaledTime;
            s_InputSequence++;
            InputFresh = true;

            if (throttlePressed || (InputThrottle > 0.1f && !wasThrottleHeld))
                s_ThrottlePressedLatched = true;
            if (brakePressed || (InputBrake > 0.1f && !wasBrakeHeld))
                s_BrakePressedLatched = true;
            if (leftPressed || (leftHeld && !wasLeftHeld))
                s_LeftPressedLatched = true;
            if (rightPressed || (rightHeld && !wasRightHeld))
                s_RightPressedLatched = true;
        }

        internal static void SampleUnityInput()
        {
            int frame = Time.frameCount;
            if (s_LastInputFrame == frame)
                return;

            s_LastInputFrame = frame;
            bool leftHeld = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
            bool rightHeld = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
            bool throttleHeld = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
            bool brakeHeld = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
            bool throttlePressed = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow);
            bool brakePressed = Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow);
            bool leftPressed = Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow);
            bool rightPressed = Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow);

            SetInputSnapshot(
                throttleHeld ? 1f : 0f,
                brakeHeld ? 1f : 0f,
                leftHeld,
                rightHeld,
                throttlePressed,
                brakePressed,
                leftPressed,
                rightPressed);

            if (Input.GetKeyDown(KeyCode.V))
                RequestToggle();

            if (Input.GetKeyDown(KeyCode.F8))
                ToggleHud();
        }

        internal static void CaptureKeyboardDrivingKeys()
        {
            SampleUnityInput();
        }

        internal static ReliableDriveInputFrame ConsumeDriveInput()
        {
            SampleUnityInput();

            float age = InputAgeSeconds;
            bool fresh = age <= kInputStaleSeconds;
            InputFresh = fresh;

            ReliableDriveInputFrame frame = new ReliableDriveInputFrame
            {
                Throttle = fresh ? InputThrottle : 0f,
                Brake = fresh ? InputBrake : 0f,
                Steering = fresh ? InputSteering : 0f,
                LeftHeld = fresh && InputLeftHeld,
                RightHeld = fresh && InputRightHeld,
                ThrottlePressed = fresh && s_ThrottlePressedLatched,
                BrakePressed = fresh && s_BrakePressedLatched,
                LeftPressed = fresh && s_LeftPressedLatched,
                RightPressed = fresh && s_RightPressedLatched,
                Fresh = fresh,
                AgeSeconds = age,
                Frame = s_LastInputFrame,
                Sequence = s_InputSequence
            };

            s_ThrottlePressedLatched = false;
            s_BrakePressedLatched = false;
            s_LeftPressedLatched = false;
            s_RightPressedLatched = false;
            return frame;
        }

        internal static bool ConsumeToggleRequest()
        {
            bool requested = s_ToggleRequested;
            s_ToggleRequested = false;
            return requested;
        }

        internal static bool ConsumeReleaseRequest()
        {
            bool requested = s_ReleaseRequested;
            s_ReleaseRequested = false;
            return requested;
        }

        internal static void SetIdle(string status)
        {
            IsDriving = false;
            PossessedEntity = Entity.Null;
            StatusText = status ?? "Waiting for live car";
            PossessedPrefab = "";
            PossessedKind = "car";
            SpeedMph = 0f;
            ThrottleInput = 0f;
            SteeringInput = 0f;
            Braking = false;
            ReverseReady = false;
            LaneAssistStatus = "Lane assist ready";
            TurnGateStatus = "U-turn gate ready";
            TurnZoneStatus = "Turn zones hidden";
            BusAssistStatus = "Bus assist ready";
            SpeedAssistStatus = "Speed ramp ready";
            ClearTurnZoneDebug();
            ClearTurnNavigationQueue();
        }

        internal static void SetFocusStatus(string status)
        {
            FocusStatus = status ?? "";
        }

        internal static void SetLaneAssistStatus(string status)
        {
            LaneAssistStatus = status ?? "";
        }

        internal static void SetTurnGateStatus(string status)
        {
            TurnGateStatus = status ?? "";
        }

        internal static void SetBusAssistStatus(string status)
        {
            BusAssistStatus = status ?? "";
        }

        internal static void SetSpeedAssistStatus(string status)
        {
            SpeedAssistStatus = status ?? "";
        }

        internal static void SetTurnZoneDebug(Vector3 position, Vector3 forward, Vector3 right, float laneReleaseMeters, float junctionReleaseMeters, float scanGateMeters, float remainingMeters, bool currentIsConnectionLane, bool matchingTurnLane, bool junctionTurnGate, bool manualReleaseOpen, bool hasRealJunction = true)
        {
            HasTurnZoneDebug = true;
            TurnZonePosition = position;
            TurnZoneForward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
            TurnZoneRight = right.sqrMagnitude > 0.001f ? right.normalized : Vector3.right;
            TurnZoneLaneReleaseMeters = Mathf.Max(0f, laneReleaseMeters);
            TurnZoneJunctionReleaseMeters = Mathf.Max(0f, junctionReleaseMeters);
            TurnZoneScanGateMeters = Mathf.Max(0f, scanGateMeters);
            TurnZoneRemainingMeters = Mathf.Max(0f, remainingMeters);
            TurnZoneInsideConnection = currentIsConnectionLane;
            TurnZoneMatchingTurnLane = matchingTurnLane;
            TurnZoneJunctionGate = junctionTurnGate;
            TurnZoneManualReleaseOpen = manualReleaseOpen;
            TurnZoneHasRealJunction = hasRealJunction;

            if (!ShowTurnReleaseZones)
            {
                TurnZoneStatus = "Turn zones hidden";
                return;
            }

            if (!hasRealJunction)
            {
                TurnZoneStatus = $"Turn zone: scanning, no actual junction found  scan Y {TurnZoneScanGateMeters:0}m  X {TurnZoneHalfWidth:0.0}m";
                return;
            }

            string state = manualReleaseOpen ? "open" : "locked";
            string source = currentIsConnectionLane ? "inside junction" : matchingTurnLane ? "turn lane" : junctionTurnGate ? "junction gate" : "approach";
            TurnZoneStatus = $"Turn zone: {state} ({source})  distance {TurnZoneRemainingMeters:0}m  scan Y {TurnZoneScanGateMeters:0}m  turn-lane Y {TurnZoneLaneReleaseMeters:0}m  junction Y {TurnZoneJunctionReleaseMeters:0}m";
        }

        internal static void ClearTurnZoneDebug()
        {
            HasTurnZoneDebug = false;
            TurnZoneManualReleaseOpen = false;
            TurnZoneMatchingTurnLane = false;
            TurnZoneJunctionGate = false;
            TurnZoneInsideConnection = false;
            TurnZoneHasRealJunction = false;
            TurnZoneScanGateMeters = 0f;
            TurnZoneStatus = ShowTurnReleaseZones ? "Turn zone: no actual junction ahead" : "Turn zones hidden";
        }

        internal static void SetDriving(Entity entity, string prefab, Vector3 position, Quaternion rotation, float speedMph, float throttle, float steering, bool braking, bool reverseReady, string vehicleKind = "car")
        {
            IsDriving = true;
            PossessedEntity = entity;
            PossessedPrefab = prefab ?? "";
            PossessedKind = string.IsNullOrEmpty(vehicleKind) ? "car" : vehicleKind;
            PosePosition = position;
            PoseRotation = rotation;
            SpeedMph = speedMph;
            ThrottleInput = throttle;
            SteeringInput = steering;
            Braking = braking;
            ReverseReady = reverseReady;
            string inputState = InputFresh ? "" : $"  input stale {InputAgeSeconds:0.00}s";
            StatusText = $"Driving real {PossessedKind}  {SpeedMph:0} mph  input {ThrottleInput:0.0}/{SteeringInput:0.0}{inputState}";
        }

        internal static void SetTurnNavigationQueue(Entity entity, Entity connectionLane, float2 connectionCurvePosition, CarLaneFlags connectionFlags, Entity exitLane, float2 exitCurvePosition, CarLaneFlags exitFlags)
        {
            TurnQueueEntity = entity;
            TurnConnectionLane = connectionLane;
            TurnConnectionCurvePosition = connectionCurvePosition;
            TurnConnectionFlags = connectionFlags;
            TurnExitLane = exitLane;
            TurnExitCurvePosition = exitCurvePosition;
            TurnExitFlags = exitFlags;
        }

        internal static void ClearTurnNavigationQueue()
        {
            TurnQueueEntity = Entity.Null;
            TurnConnectionLane = Entity.Null;
            TurnExitLane = Entity.Null;
            TurnConnectionCurvePosition = default;
            TurnExitCurvePosition = default;
            TurnConnectionFlags = default;
            TurnExitFlags = default;
        }

        internal static void SyncPose(Vector3 position, Quaternion rotation, float speedMph)
        {
            if (!IsDriving)
                return;

            PosePosition = position;
            PoseRotation = rotation;
            SpeedMph = speedMph;
            string inputState = InputFresh ? "" : $"  input stale {InputAgeSeconds:0.00}s";
            StatusText = $"Driving real {PossessedKind}  {SpeedMph:0} mph  input {ThrottleInput:0.0}/{SteeringInput:0.0}{inputState}";
        }

        internal static void AdjustTargetSpeed(float deltaMph)
        {
            TargetSpeedMph = Mathf.Clamp(TargetSpeedMph + deltaMph, 5f, 80f);
        }

        internal static void AdjustSteering(float delta)
        {
            SteeringStrength = Mathf.Clamp(SteeringStrength + delta, 0.8f, 12f);
        }

        internal static void ResetDrivingSettings()
        {
            TargetSpeedMph = 38f;
            AutoRoadSpeedEnabled = true;
            RoadSpeedMultiplier = 1f;
            JunctionTurnSpeedMph = 22f;
            SharpTurnSpeedMph = 16f;
            AllowBikesAndMotorcycles = false;
            AllowWatercraft = false;
            AllowRailVehicles = false;
            LaneAssistEnabled = false;
            StrongJunctionOverrideEnabled = false;
            ShowTurnReleaseZones = false;
            ReverseSpeedMph = 8f;
            AccelerationRate = 14f;
            BrakeRate = 34f;
            ReverseAccelerationRate = 9f;
            DirectionChangeRate = 44f;
            CoastingRate = 10f;
            SteeringStrength = 7.4f;
            LookAheadMeters = 13f;
            FreeTurnLookAheadMin = 2f;
            FreeTurnLookAheadMax = 4.5f;
            FreeSteerOffsetSlow = 3.5f;
            FreeSteerOffsetFast = 5.5f;
            FreeSteerDirectionSlow = 1.75f;
            FreeSteerDirectionFast = 2.35f;
            BlockedUturnSteeringScale = 0.45f;
            LaneLookAheadMin = 8f;
            LaneLookAheadMax = 24f;
            JunctionGateSlow = 58f;
            JunctionGateFast = 76f;
            TurnLaneReleaseSlow = 24f;
            TurnLaneReleaseFast = 42f;
            JunctionReleaseSlow = 8f;
            JunctionReleaseFast = 18f;
            TurnZoneHalfWidth = 5f;
            JunctionTurnBlendMin = 0.98f;
            JunctionTurnBlendMax = 1f;
            LinkedForwardSlow = 10.5f;
            LinkedForwardFast = 10.9f;
            LinkedBehind = 9.7f;
            LinkedRadiusSlow = 14.4f;
            LinkedRadiusFast = 26.4f;
            InsideForward = 8.5f;
            InsideBehind = 15f;
            InsideRadiusSlow = 13f;
            InsideRadiusFast = 18f;
            JunctionTargetAheadSlow = 11.7f;
            JunctionTargetAheadFast = 20.7f;
            JunctionSideMin = 0.28f;
            JunctionTurnDotMin = 0.088f;
            JunctionBackDotMin = -0.17f;
            JunctionFallbackForwardSlow = 7.5f;
            JunctionFallbackForwardFast = 14f;
            JunctionFallbackSideMin = 2.5f;
            JunctionFallbackSideMax = 6.5f;
            MergeHoldRise = 0.12f;
            MergeHoldFall = 0.2f;
        }

        internal static void UpdateCamera(Camera camera)
        {
            // Custom camera control is intentionally disabled; use the game's built-in lock-on camera.
        }

        internal static void ResetCamera()
        {
        }

        internal static void Reset()
        {
            SetIdle("Mod disposed");
            s_ToggleRequested = false;
            s_ReleaseRequested = false;
            s_LastInputFrame = -1;
            s_LastInputSampleTime = -999f;
            s_InputSequence = 0;
            s_ThrottlePressedLatched = false;
            s_BrakePressedLatched = false;
            s_LeftPressedLatched = false;
            s_RightPressedLatched = false;
            InputFresh = true;
            SetInputSnapshot(0f, 0f, false, false);

            if (s_Hud != null)
            {
                Object.Destroy(s_Hud.gameObject);
                s_Hud = null;
            }
        }
    }
}
