using Unity.Entities;
using UnityEngine;

namespace BetaTestDrivingMod
{
    internal struct DirectDriveInputFrame
    {
        internal float Throttle;
        internal float Brake;
        internal float Steering;
        internal bool LeftHeld;
        internal bool RightHeld;
        internal bool BrakePressed;
        internal bool Fresh;
        internal float AgeSeconds;
        internal uint Sequence;
    }

    internal static class DirectDriveRuntime
    {
        private const float kInputStaleSeconds = 0.35f;

        private static DirectDriveHudBehaviour s_Hud;
        private static bool s_ToggleRequested;
        private static bool s_ReleaseRequested;
        private static int s_LastSampleFrame = -1;
        private static float s_LastSampleTime = -999f;
        private static uint s_InputSequence;
        private static bool s_BrakePressedLatched;
        private static bool s_PoliceChaseTestRequested;

        internal static float InputThrottle { get; private set; }
        internal static float InputBrake { get; private set; }
        internal static float InputSteering { get; private set; }
        internal static bool InputLeftHeld { get; private set; }
        internal static bool InputRightHeld { get; private set; }
        internal static bool InputFresh { get; private set; } = true;
        internal static float InputAgeSeconds => s_LastSampleTime < -100f ? 999f : Mathf.Max(0f, Time.unscaledTime - s_LastSampleTime);

        internal static bool IsDriving { get; private set; }
        internal static Entity PossessedEntity { get; private set; } = Entity.Null;
        internal static string StatusText { get; private set; } = "Select or look near a car, then press V.";
        internal static string PossessedName { get; private set; } = "";
        internal static string ControlStatus { get; private set; } = "Direct control ready";
        internal static float SpeedMph { get; private set; }
        internal static bool Braking { get; private set; }
        internal static bool ReverseReady { get; private set; }
        internal static Vector3 PosePosition { get; private set; }
        internal static Quaternion PoseRotation { get; private set; } = Quaternion.identity;

        internal static float TargetSpeedMph { get; set; } = 42f;
        internal static float ReverseSpeedMph { get; set; } = 9f;
        internal static float AccelerationMps2 { get; set; } = 19f;
        internal static float BrakeMps2 { get; set; } = 42f;
        internal static float CoastMps2 { get; set; } = 12f;
        internal static float ReverseAccelerationMps2 { get; set; } = 12f;
        internal static float MaxTurnDegPerSecond { get; set; } = 148f;
        internal static float LowSpeedTurnBoost { get; set; } = 0.58f;
        internal static float RoadHeightStickiness { get; set; } = 0.45f;
        internal static bool RoadIntentAssist { get; set; } = true;
        internal static bool RoadHeightAssist { get; set; } = true;
        internal static bool FreezeVanillaNavigation { get; set; } = true;
        internal static bool VehicleCollisionEnabled { get; set; } = true;
        internal static float CollisionRetainedSpeed { get; set; } = 0.35f;
        internal static bool ChaseCameraEnabled { get; set; } = true;
        internal static float ChaseCameraDistance { get; set; } = 10.5f;
        internal static float ChaseCameraHeight { get; set; } = 3.25f;
        internal static float ChaseCameraLookAhead { get; set; } = 12f;
        internal static string ChaseCameraStatus { get; private set; } = "Chase camera ready";
        internal static bool PoliceChaseEnabled { get; set; }
        internal static bool PoliceChaseActive { get; private set; }
        internal static string PoliceChaseStatus { get; private set; } = "Police chase off";
        internal static int PoliceChaseUnits { get; private set; }
        internal static int RedLightViolations { get; private set; }
        internal static bool HudVisible { get; private set; }
        internal static bool PanelVisible { get; private set; }

        internal static void SanitizeDrivingTuning()
        {
            TargetSpeedMph = ClampFinite(TargetSpeedMph, 42f, 0f, 240f);
            ReverseSpeedMph = ClampFinite(ReverseSpeedMph, 9f, 0f, 60f);
            AccelerationMps2 = ClampFinite(AccelerationMps2, 19f, 0f, 160f);
            BrakeMps2 = ClampFinite(BrakeMps2, 42f, 0f, 240f);
            CoastMps2 = ClampFinite(CoastMps2, 12f, 0f, 120f);
            ReverseAccelerationMps2 = ClampFinite(ReverseAccelerationMps2, 12f, 0f, 120f);
            MaxTurnDegPerSecond = ClampFinite(MaxTurnDegPerSecond, 148f, 0f, 720f);
            LowSpeedTurnBoost = ClampFinite(LowSpeedTurnBoost, 0.58f, 0f, 3f);
            RoadHeightStickiness = ClampFinite(RoadHeightStickiness, 0.45f, 0f, 2f);
            ChaseCameraDistance = ClampFinite(ChaseCameraDistance, 10.5f, 2f, 80f);
            ChaseCameraHeight = ClampFinite(ChaseCameraHeight, 3.25f, 0.5f, 40f);
            ChaseCameraLookAhead = ClampFinite(ChaseCameraLookAhead, 12f, 0f, 80f);
        }

        private static float ClampFinite(float value, float fallback, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return fallback;

            return Mathf.Clamp(value, min, max);
        }

        internal static void EnsureHud()
        {
            if (s_Hud != null)
                return;

            GameObject hud = new GameObject("Beta Test Driving Mod (Stable) HUD");
            Object.DontDestroyOnLoad(hud);
            s_Hud = hud.AddComponent<DirectDriveHudBehaviour>();
        }

        internal static void ToggleHud()
        {
            TogglePanel();
        }

        internal static void TogglePanel()
        {
            PanelVisible = !PanelVisible;
        }

        internal static void SetPanelVisible(bool visible)
        {
            PanelVisible = visible;
        }

        internal static void RequestToggle()
        {
            s_ToggleRequested = true;
        }

        internal static void RequestRelease()
        {
            s_ReleaseRequested = true;
        }

        internal static void RequestPoliceChaseTest()
        {
            ApplyPublicSafeDefaults();
            SetPoliceChase(false, "Police chase disabled in public build", 0);
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

        internal static bool ConsumePoliceChaseTestRequest()
        {
            bool requested = s_PoliceChaseTestRequested;
            s_PoliceChaseTestRequested = false;
            return requested;
        }

        internal static void SampleUnityInput()
        {
            int frame = Time.frameCount;
            if (s_LastSampleFrame == frame)
                return;

            s_LastSampleFrame = frame;

            bool throttleHeld = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
            bool brakeHeld = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
            bool leftHeld = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
            bool rightHeld = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
            bool brakePressed = Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow);

            bool wasBrakeHeld = InputBrake > 0.1f;
            InputThrottle = throttleHeld ? 1f : 0f;
            InputBrake = brakeHeld ? 1f : 0f;
            InputLeftHeld = leftHeld;
            InputRightHeld = rightHeld;
            InputSteering = (rightHeld ? 1f : 0f) - (leftHeld ? 1f : 0f);
            s_LastSampleTime = Time.unscaledTime;
            s_InputSequence++;
            InputFresh = true;

            if (brakePressed || (brakeHeld && !wasBrakeHeld))
                s_BrakePressedLatched = true;

            if (Input.GetKeyDown(KeyCode.V))
                RequestToggle();

            if (Input.GetKeyDown(KeyCode.F8))
                ToggleHud();

            if (Input.GetKeyDown(KeyCode.F7))
                ChaseCameraEnabled = !ChaseCameraEnabled;

            if (Input.GetKeyDown(KeyCode.F9))
                RequestPoliceChaseTest();
        }

        internal static DirectDriveInputFrame ConsumeDriveInput()
        {
            SampleUnityInput();

            float age = InputAgeSeconds;
            bool fresh = age <= kInputStaleSeconds;
            InputFresh = fresh;

            DirectDriveInputFrame frame = new DirectDriveInputFrame
            {
                Throttle = fresh ? InputThrottle : 0f,
                Brake = fresh ? InputBrake : 0f,
                Steering = fresh ? InputSteering : 0f,
                LeftHeld = fresh && InputLeftHeld,
                RightHeld = fresh && InputRightHeld,
                BrakePressed = fresh && s_BrakePressedLatched,
                Fresh = fresh,
                AgeSeconds = age,
                Sequence = s_InputSequence
            };

            s_BrakePressedLatched = false;
            return frame;
        }

        internal static void SetDriving(Entity entity, string name, Vector3 position, Quaternion rotation, float speedMph, bool braking, bool reverseReady, string controlStatus)
        {
            IsDriving = true;
            PossessedEntity = entity;
            PossessedName = name ?? "";
            PosePosition = position;
            PoseRotation = rotation;
            SpeedMph = speedMph;
            Braking = braking;
            ReverseReady = reverseReady;
            ControlStatus = controlStatus ?? "";

            string inputState = InputFresh ? "" : $" input stale {InputAgeSeconds:0.00}s";
            StatusText = $"Direct driving {SpeedMph:0} mph  input {InputThrottle:0.0}/{InputSteering:0.0}{inputState}";
        }

        internal static void SetIdle(string status)
        {
            IsDriving = false;
            PossessedEntity = Entity.Null;
            PossessedName = "";
            SpeedMph = 0f;
            Braking = false;
            ReverseReady = false;
            StatusText = status ?? "Select or look near a car, then press V.";
            ControlStatus = "Direct control ready";
        }

        internal static void RecordRedLightViolation()
        {
            RedLightViolations++;
        }

        internal static void SetPoliceChase(bool active, string status, int units)
        {
            PoliceChaseActive = active;
            PoliceChaseStatus = status ?? "";
            PoliceChaseUnits = Mathf.Max(0, units);
        }

        internal static void SetChaseCameraStatus(string status)
        {
            ChaseCameraStatus = status ?? "";
        }

        internal static void ResetSettings()
        {
            TargetSpeedMph = 42f;
            ReverseSpeedMph = 9f;
            AccelerationMps2 = 19f;
            BrakeMps2 = 42f;
            CoastMps2 = 12f;
            ReverseAccelerationMps2 = 12f;
            MaxTurnDegPerSecond = 148f;
            LowSpeedTurnBoost = 0.58f;
            RoadHeightStickiness = 0.45f;
            RoadIntentAssist = true;
            RoadHeightAssist = true;
            FreezeVanillaNavigation = true;
            VehicleCollisionEnabled = true;
            CollisionRetainedSpeed = 0.35f;
            ChaseCameraEnabled = true;
            ChaseCameraDistance = 10.5f;
            ChaseCameraHeight = 3.25f;
            ChaseCameraLookAhead = 12f;
            ChaseCameraStatus = "Chase camera ready";
            PoliceChaseEnabled = false;
            s_PoliceChaseTestRequested = false;
            SetPoliceChase(false, "Police chase off", 0);
        }

        internal static void ApplyPublicSafeDefaults()
        {
            RoadIntentAssist = true;
            RoadHeightAssist = true;
            FreezeVanillaNavigation = true;
            VehicleCollisionEnabled = true;
            ChaseCameraEnabled = true;
            PoliceChaseEnabled = false;
            s_PoliceChaseTestRequested = false;
            SetPoliceChase(false, "Police chase off", 0);
        }

        internal static void Reset()
        {
            SetIdle("Mod disposed");
            s_ToggleRequested = false;
            s_ReleaseRequested = false;
            s_LastSampleFrame = -1;
            s_LastSampleTime = -999f;
            s_InputSequence = 0;
            s_BrakePressedLatched = false;
            s_PoliceChaseTestRequested = false;
            InputThrottle = 0f;
            InputBrake = 0f;
            InputSteering = 0f;
            InputLeftHeld = false;
            InputRightHeld = false;
            InputFresh = true;
            PoliceChaseActive = false;
            PoliceChaseStatus = "Police chase off";
            PoliceChaseUnits = 0;
            RedLightViolations = 0;
            PoliceChaseEnabled = false;
            VehicleCollisionEnabled = true;
            CollisionRetainedSpeed = 0.35f;
            ChaseCameraEnabled = true;
            ChaseCameraStatus = "Chase camera ready";
            HudVisible = false;
            PanelVisible = false;

            if (s_Hud != null)
            {
                Object.Destroy(s_Hud.gameObject);
                s_Hud = null;
            }
        }
    }
}
