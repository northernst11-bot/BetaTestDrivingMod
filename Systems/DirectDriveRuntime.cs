using System;
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
        private const float kRenderPredictionMaxSeconds = 0.18f;
        private const float kRenderPredictionMaxDistanceSq = 12f * 12f;
        private const float kRenderPoseSharpness = 34f;
        private const string kKeyBindingPrefsPrefix = "BetaTestDrivingMod.DirectDrive.Key.";
        internal const int kMaxTrafficPresenceDebugSegments = 6;
        internal const int kTrafficPresenceDebugPrimary = 0;
        internal const int kTrafficPresenceDebugChangeLane = 1;
        internal const int kTrafficPresenceDebugHalo = 2;

        private static DirectDriveHudBehaviour s_Hud;
        private static bool s_ToggleRequested;
        private static bool s_ReleaseRequested;
        private static int s_LastSampleFrame = -1;
        private static float s_LastSampleTime = -999f;
        private static uint s_InputSequence;
        private static bool s_BrakePressedLatched;
        private static bool s_PoliceChaseTestRequested;
        private static bool s_KeyBindingsLoaded;
        private static int s_BlockHotkeysUntilFrame = -1;
        private static int s_RenderPoseFrame = -1;
        private static Vector3 s_RenderPosePosition;
        private static Quaternion s_RenderPoseRotation = Quaternion.identity;
        private static KeyCode s_ToggleDrivingKeyCode = KeyCode.V;
        private static KeyCode s_ThrottleKeyCode = KeyCode.W;
        private static KeyCode s_BrakeKeyCode = KeyCode.S;
        private static KeyCode s_SteerLeftKeyCode = KeyCode.A;
        private static KeyCode s_SteerRightKeyCode = KeyCode.D;
        private static KeyCode s_PanelKeyCode = KeyCode.F8;
        private static KeyCode s_ChaseCameraKeyCode = KeyCode.F7;
        private static KeyCode s_CollisionDebugKeyCode = KeyCode.Keypad0;
        private static KeyCode s_TrafficPresenceDebugKeyCode = KeyCode.Keypad9;

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
        internal static bool ReverseActive { get; private set; }
        internal static bool ReverseReady { get; private set; }
        internal static Vector3 PosePosition { get; private set; }
        internal static Quaternion PoseRotation { get; private set; } = Quaternion.identity;
        internal static Vector3 PoseVelocity { get; private set; }
        internal static float PoseAngularVelocityYaw { get; private set; }
        internal static float PoseSampleTime { get; private set; } = -999f;

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
        internal static bool RoadOnlyTrafficPresence { get; set; } = true;
        internal static bool TrafficPresenceHaloEnabled { get; set; }
        internal static bool VisualCollisionDebugEnabled { get; set; }
        internal static bool TrafficPresenceDebugEnabled { get; set; }
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
        internal static bool HasCollisionDebug { get; private set; }
        internal static bool CollisionDebugHasTarget { get; private set; }
        internal static bool CollisionDebugHit { get; private set; }
        internal static Vector3 CollisionDebugSelfCenter { get; private set; }
        internal static Vector3 CollisionDebugSelfRight { get; private set; } = Vector3.right;
        internal static Vector3 CollisionDebugSelfForward { get; private set; } = Vector3.forward;
        internal static float CollisionDebugSelfHalfWidth { get; private set; }
        internal static float CollisionDebugSelfHalfLength { get; private set; }
        internal static float CollisionDebugSelfMinHeight { get; private set; }
        internal static float CollisionDebugSelfMaxHeight { get; private set; } = 1.5f;
        internal static Vector3 CollisionDebugTargetCenter { get; private set; }
        internal static Vector3 CollisionDebugTargetRight { get; private set; } = Vector3.right;
        internal static Vector3 CollisionDebugTargetForward { get; private set; } = Vector3.forward;
        internal static float CollisionDebugTargetHalfWidth { get; private set; }
        internal static float CollisionDebugTargetHalfLength { get; private set; }
        internal static float CollisionDebugTargetMinHeight { get; private set; }
        internal static float CollisionDebugTargetMaxHeight { get; private set; } = 1.5f;
        internal static Vector3 CollisionDebugSweepStart { get; private set; }
        internal static Vector3 CollisionDebugSweepEnd { get; private set; }
        internal static string CollisionDebugStatus { get; private set; } = "";
        internal static bool HasTrafficPresenceTarget { get; private set; }
        internal static Entity TrafficPresenceLane { get; private set; } = Entity.Null;
        internal static Entity TrafficPresenceChangeLane { get; private set; } = Entity.Null;
        internal static float TrafficPresenceCurveStart { get; private set; }
        internal static float TrafficPresenceCurveEnd { get; private set; }
        internal static float TrafficPresenceCurveT { get; private set; }
        internal static float TrafficPresenceCurveSign { get; private set; } = 1f;
        internal static float TrafficPresenceRearSpan { get; private set; }
        internal static float TrafficPresenceForwardSpan { get; private set; }
        internal static bool HasTrafficPresenceDebug { get; private set; }
        internal static int TrafficPresenceDebugSegmentCount { get; private set; }
        internal static Vector3[] TrafficPresenceDebugStarts { get; } = new Vector3[kMaxTrafficPresenceDebugSegments];
        internal static Vector3[] TrafficPresenceDebugEnds { get; } = new Vector3[kMaxTrafficPresenceDebugSegments];
        internal static Vector3[] TrafficPresenceDebugLabels { get; } = new Vector3[kMaxTrafficPresenceDebugSegments];
        internal static string[] TrafficPresenceDebugTexts { get; } = new string[kMaxTrafficPresenceDebugSegments];
        internal static int[] TrafficPresenceDebugKinds { get; } = new int[kMaxTrafficPresenceDebugSegments];
        internal static string TrafficPresenceDebugStatus { get; private set; } = "";
        internal static string TrafficGuardDebugStatus { get; private set; } = "";
        internal static string ToggleDrivingKey { get; private set; } = "V";
        internal static string ThrottleKey { get; private set; } = "W";
        internal static string BrakeKey { get; private set; } = "S";
        internal static string SteerLeftKey { get; private set; } = "A";
        internal static string SteerRightKey { get; private set; } = "D";
        internal static string PanelKey { get; private set; } = "F8";
        internal static string ChaseCameraKey { get; private set; } = "F7";
        internal static string CollisionDebugKey { get; private set; } = "Keypad0";
        internal static string TrafficPresenceDebugKey { get; private set; } = "Keypad9";
        internal static string ReadyStatusText => $"Select or look near a car, then press {ToggleDrivingKey}.";

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
            UnityEngine.Object.DontDestroyOnLoad(hud);
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

        internal static void ToggleVisualCollisionDebug()
        {
            SetVisualCollisionDebugEnabled(!VisualCollisionDebugEnabled);
        }

        internal static void SetVisualCollisionDebugEnabled(bool enabled)
        {
            VisualCollisionDebugEnabled = enabled;
            if (!enabled)
                ClearCollisionDebug();
        }

        internal static void ToggleTrafficPresenceDebug()
        {
            SetTrafficPresenceDebugEnabled(!TrafficPresenceDebugEnabled);
        }

        internal static void SetTrafficPresenceDebugEnabled(bool enabled)
        {
            TrafficPresenceDebugEnabled = enabled;
            if (!enabled)
                ClearTrafficPresenceDebug();
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
            EnsureKeyBindingsLoaded();

            int frame = Time.frameCount;
            if (s_LastSampleFrame == frame)
                return;

            s_LastSampleFrame = frame;

            bool throttleHeld = IsKeyHeld(s_ThrottleKeyCode, KeyCode.UpArrow);
            bool brakeHeld = IsKeyHeld(s_BrakeKeyCode, KeyCode.DownArrow);
            bool leftHeld = IsKeyHeld(s_SteerLeftKeyCode, KeyCode.LeftArrow);
            bool rightHeld = IsKeyHeld(s_SteerRightKeyCode, KeyCode.RightArrow);
            bool brakePressed = IsKeyPressed(s_BrakeKeyCode, KeyCode.DownArrow);

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

            bool hotkeysAllowed = frame > s_BlockHotkeysUntilFrame;
            if (hotkeysAllowed && IsKeyPressed(s_ToggleDrivingKeyCode))
                RequestToggle();

            if (hotkeysAllowed && IsKeyPressed(s_PanelKeyCode))
                ToggleHud();

            if (hotkeysAllowed && IsKeyPressed(s_CollisionDebugKeyCode))
                ToggleVisualCollisionDebug();

            if (hotkeysAllowed && IsKeyPressed(s_TrafficPresenceDebugKeyCode))
                ToggleTrafficPresenceDebug();

            if (hotkeysAllowed && IsKeyPressed(s_ChaseCameraKeyCode))
                ChaseCameraEnabled = !ChaseCameraEnabled;

            if (Input.GetKeyDown(KeyCode.F9))
                RequestPoliceChaseTest();
        }

        private static bool IsKeyHeld(KeyCode primary, KeyCode fallback = KeyCode.None)
        {
            return (primary != KeyCode.None && Input.GetKey(primary)) ||
                   (fallback != KeyCode.None && fallback != primary && Input.GetKey(fallback));
        }

        private static bool IsKeyPressed(KeyCode primary, KeyCode fallback = KeyCode.None)
        {
            return (primary != KeyCode.None && Input.GetKeyDown(primary)) ||
                   (fallback != KeyCode.None && fallback != primary && Input.GetKeyDown(fallback));
        }

        internal static bool SetToggleDrivingKey(string value) => SetKeyBinding("ToggleDriving", value, ref s_ToggleDrivingKeyCode, key => ToggleDrivingKey = key);
        internal static bool SetThrottleKey(string value) => SetKeyBinding("Throttle", value, ref s_ThrottleKeyCode, key => ThrottleKey = key);
        internal static bool SetBrakeKey(string value) => SetKeyBinding("Brake", value, ref s_BrakeKeyCode, key => BrakeKey = key);
        internal static bool SetSteerLeftKey(string value) => SetKeyBinding("SteerLeft", value, ref s_SteerLeftKeyCode, key => SteerLeftKey = key);
        internal static bool SetSteerRightKey(string value) => SetKeyBinding("SteerRight", value, ref s_SteerRightKeyCode, key => SteerRightKey = key);
        internal static bool SetPanelKey(string value) => SetKeyBinding("Panel", value, ref s_PanelKeyCode, key => PanelKey = key);
        internal static bool SetChaseCameraKey(string value) => SetKeyBinding("ChaseCamera", value, ref s_ChaseCameraKeyCode, key => ChaseCameraKey = key);
        internal static bool SetCollisionDebugKey(string value) => SetKeyBinding("CollisionDebug", value, ref s_CollisionDebugKeyCode, key => CollisionDebugKey = key);
        internal static bool SetTrafficPresenceDebugKey(string value) => SetKeyBinding("TrafficPresenceDebug", value, ref s_TrafficPresenceDebugKeyCode, key => TrafficPresenceDebugKey = key);

        internal static void EnsureKeyBindingsLoaded()
        {
            if (s_KeyBindingsLoaded)
                return;

            s_KeyBindingsLoaded = true;
            LoadKeyBinding("ToggleDriving", ref s_ToggleDrivingKeyCode, key => ToggleDrivingKey = key);
            LoadKeyBinding("Throttle", ref s_ThrottleKeyCode, key => ThrottleKey = key);
            LoadKeyBinding("Brake", ref s_BrakeKeyCode, key => BrakeKey = key);
            LoadKeyBinding("SteerLeft", ref s_SteerLeftKeyCode, key => SteerLeftKey = key);
            LoadKeyBinding("SteerRight", ref s_SteerRightKeyCode, key => SteerRightKey = key);
            LoadKeyBinding("Panel", ref s_PanelKeyCode, key => PanelKey = key);
            LoadKeyBinding("ChaseCamera", ref s_ChaseCameraKeyCode, key => ChaseCameraKey = key);
            LoadKeyBinding("CollisionDebug", ref s_CollisionDebugKeyCode, key => CollisionDebugKey = key);
            LoadKeyBinding("TrafficPresenceDebug", ref s_TrafficPresenceDebugKeyCode, key => TrafficPresenceDebugKey = key);
            RefreshReadyStatus();
        }

        private static void LoadKeyBinding(string id, ref KeyCode target, Action<string> setDisplay)
        {
            string saved = PlayerPrefs.GetString(kKeyBindingPrefsPrefix + id, "");
            if (!TryParseKeyCode(saved, out KeyCode keyCode))
                return;

            target = keyCode;
            setDisplay(keyCode.ToString());
        }

        private static bool SetKeyBinding(string id, string value, ref KeyCode target, Action<string> setDisplay)
        {
            if (!TryParseKeyCode(value, out KeyCode keyCode))
                return false;

            target = keyCode;
            setDisplay(keyCode.ToString());
            PlayerPrefs.SetString(kKeyBindingPrefsPrefix + id, keyCode.ToString());
            PlayerPrefs.Save();
            s_BlockHotkeysUntilFrame = Mathf.Max(s_BlockHotkeysUntilFrame, Time.frameCount + 2);
            RefreshReadyStatus();
            return true;
        }

        private static bool TryParseKeyCode(string value, out KeyCode keyCode)
        {
            keyCode = KeyCode.None;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string text = value.Trim();
            if (text.Length == 1 && char.IsDigit(text[0]))
                text = "Alpha" + text;
            else if (string.Equals(text, "Ctrl", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(text, "Control", StringComparison.OrdinalIgnoreCase))
                text = "LeftControl";
            else if (string.Equals(text, "Alt", StringComparison.OrdinalIgnoreCase))
                text = "LeftAlt";
            else if (string.Equals(text, "Shift", StringComparison.OrdinalIgnoreCase))
                text = "LeftShift";
            else if (string.Equals(text, "Enter", StringComparison.OrdinalIgnoreCase))
                text = "Return";
            else if (string.Equals(text, "Esc", StringComparison.OrdinalIgnoreCase))
                text = "Escape";

            return Enum.TryParse(text, true, out keyCode) && keyCode != KeyCode.None;
        }

        private static void ResetKeyBindings()
        {
            s_ToggleDrivingKeyCode = KeyCode.V;
            s_ThrottleKeyCode = KeyCode.W;
            s_BrakeKeyCode = KeyCode.S;
            s_SteerLeftKeyCode = KeyCode.A;
            s_SteerRightKeyCode = KeyCode.D;
            s_PanelKeyCode = KeyCode.F8;
            s_ChaseCameraKeyCode = KeyCode.F7;
            s_CollisionDebugKeyCode = KeyCode.Keypad0;
            s_TrafficPresenceDebugKeyCode = KeyCode.Keypad9;
            ToggleDrivingKey = s_ToggleDrivingKeyCode.ToString();
            ThrottleKey = s_ThrottleKeyCode.ToString();
            BrakeKey = s_BrakeKeyCode.ToString();
            SteerLeftKey = s_SteerLeftKeyCode.ToString();
            SteerRightKey = s_SteerRightKeyCode.ToString();
            PanelKey = s_PanelKeyCode.ToString();
            ChaseCameraKey = s_ChaseCameraKeyCode.ToString();
            CollisionDebugKey = s_CollisionDebugKeyCode.ToString();
            TrafficPresenceDebugKey = s_TrafficPresenceDebugKeyCode.ToString();
            SaveDefaultKeyBindings();
            RefreshReadyStatus();
        }

        private static void SaveDefaultKeyBindings()
        {
            PlayerPrefs.SetString(kKeyBindingPrefsPrefix + "ToggleDriving", ToggleDrivingKey);
            PlayerPrefs.SetString(kKeyBindingPrefsPrefix + "Throttle", ThrottleKey);
            PlayerPrefs.SetString(kKeyBindingPrefsPrefix + "Brake", BrakeKey);
            PlayerPrefs.SetString(kKeyBindingPrefsPrefix + "SteerLeft", SteerLeftKey);
            PlayerPrefs.SetString(kKeyBindingPrefsPrefix + "SteerRight", SteerRightKey);
            PlayerPrefs.SetString(kKeyBindingPrefsPrefix + "Panel", PanelKey);
            PlayerPrefs.SetString(kKeyBindingPrefsPrefix + "ChaseCamera", ChaseCameraKey);
            PlayerPrefs.SetString(kKeyBindingPrefsPrefix + "CollisionDebug", CollisionDebugKey);
            PlayerPrefs.SetString(kKeyBindingPrefsPrefix + "TrafficPresenceDebug", TrafficPresenceDebugKey);
            PlayerPrefs.Save();
        }

        private static void RefreshReadyStatus()
        {
            if (!IsDriving &&
                (string.IsNullOrWhiteSpace(StatusText) ||
                 StatusText.StartsWith("Select or look near a car", StringComparison.OrdinalIgnoreCase)))
            {
                StatusText = ReadyStatusText;
            }
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

        internal static bool TryGetRenderPose(out Vector3 position, out Quaternion rotation, out Vector3 velocity, out float angularVelocityYaw)
        {
            position = PosePosition;
            rotation = PoseRotation;
            velocity = PoseVelocity;
            angularVelocityYaw = PoseAngularVelocityYaw;
            if (!IsDriving)
                return false;

            int frame = Time.frameCount;
            if (s_RenderPoseFrame != frame)
            {
                float elapsed = PoseSampleTime < -100f ? 0f : Mathf.Clamp(Time.unscaledTime - PoseSampleTime, 0f, kRenderPredictionMaxSeconds);
                Vector3 predictedPosition = PosePosition + PoseVelocity * elapsed;
                Quaternion predictedRotation = PoseRotation;
                if (Mathf.Abs(PoseAngularVelocityYaw) > 0.0001f)
                    predictedRotation = Quaternion.AngleAxis(PoseAngularVelocityYaw * elapsed * Mathf.Rad2Deg, Vector3.up) * PoseRotation;

                if (!IsFinite(predictedPosition) || !IsFinite(predictedRotation) ||
                    (predictedPosition - PosePosition).sqrMagnitude > kRenderPredictionMaxDistanceSq)
                {
                    predictedPosition = PosePosition;
                    predictedRotation = PoseRotation;
                }

                float frameDelta = Time.unscaledDeltaTime > 0f ? Time.unscaledDeltaTime : Time.deltaTime;
                float dt = Mathf.Clamp(frameDelta, 0f, 0.05f);
                float blend = 1f - Mathf.Exp(-kRenderPoseSharpness * dt);
                if (!IsFinite(s_RenderPosePosition) || !IsFinite(s_RenderPoseRotation) ||
                    (s_RenderPosePosition - PosePosition).sqrMagnitude > kRenderPredictionMaxDistanceSq)
                {
                    s_RenderPosePosition = predictedPosition;
                    s_RenderPoseRotation = predictedRotation;
                }
                else
                {
                    s_RenderPosePosition = Vector3.Lerp(s_RenderPosePosition, predictedPosition, blend);
                    s_RenderPoseRotation = Quaternion.Slerp(s_RenderPoseRotation, predictedRotation, blend);
                }

                s_RenderPoseFrame = frame;
            }

            position = s_RenderPosePosition;
            rotation = s_RenderPoseRotation;
            return true;
        }

        internal static void SetDriving(Entity entity, string name, Vector3 position, Quaternion rotation, Vector3 velocity, float angularVelocityYaw, float speedMph, bool braking, bool reversing, bool reverseReady, string controlStatus)
        {
            bool resetRenderPose = !IsDriving ||
                PossessedEntity != entity ||
                !IsFinite(s_RenderPosePosition) ||
                !IsFinite(s_RenderPoseRotation) ||
                (s_RenderPosePosition - position).sqrMagnitude > kRenderPredictionMaxDistanceSq;

            IsDriving = true;
            PossessedEntity = entity;
            PossessedName = name ?? "";
            PosePosition = position;
            PoseRotation = rotation;
            PoseVelocity = IsFinite(velocity) ? velocity : Vector3.zero;
            PoseAngularVelocityYaw = float.IsNaN(angularVelocityYaw) || float.IsInfinity(angularVelocityYaw) ? 0f : angularVelocityYaw;
            PoseSampleTime = Time.unscaledTime;
            SpeedMph = speedMph;
            Braking = braking;
            ReverseActive = reversing;
            ReverseReady = reverseReady;
            ControlStatus = controlStatus ?? "";
            if (resetRenderPose)
            {
                s_RenderPosePosition = position;
                s_RenderPoseRotation = rotation;
                s_RenderPoseFrame = -1;
            }

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
            ReverseActive = false;
            ReverseReady = false;
            PoseVelocity = Vector3.zero;
            PoseAngularVelocityYaw = 0f;
            PoseSampleTime = -999f;
            s_RenderPosePosition = Vector3.zero;
            s_RenderPoseRotation = Quaternion.identity;
            s_RenderPoseFrame = -1;
            StatusText = status ?? ReadyStatusText;
            ControlStatus = "Direct control ready";
            ClearCollisionDebug();
            ClearTrafficPresenceTarget();
            ClearTrafficPresenceDebug();
            ClearTrafficGuardDebug();
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
            RoadOnlyTrafficPresence = true;
            TrafficPresenceHaloEnabled = false;
            VisualCollisionDebugEnabled = false;
            TrafficPresenceDebugEnabled = false;
            CollisionRetainedSpeed = 0.35f;
            ChaseCameraEnabled = true;
            ChaseCameraDistance = 10.5f;
            ChaseCameraHeight = 3.25f;
            ChaseCameraLookAhead = 12f;
            ChaseCameraStatus = "Chase camera ready";
            PoliceChaseEnabled = false;
            s_PoliceChaseTestRequested = false;
            SetPoliceChase(false, "Police chase off", 0);
            ResetKeyBindings();
            ClearCollisionDebug();
            ClearTrafficPresenceDebug();
            ClearTrafficGuardDebug();
        }

        internal static void ApplyPublicSafeDefaults()
        {
            EnsureKeyBindingsLoaded();
            RoadIntentAssist = true;
            RoadHeightAssist = true;
            FreezeVanillaNavigation = true;
            VehicleCollisionEnabled = true;
            RoadOnlyTrafficPresence = true;
            TrafficPresenceHaloEnabled = false;
            ChaseCameraEnabled = true;
            PoliceChaseEnabled = false;
            s_PoliceChaseTestRequested = false;
            SetPoliceChase(false, "Police chase off", 0);
        }

        internal static void SetCollisionDebug(
            Vector3 selfCenter,
            Vector3 selfRight,
            Vector3 selfForward,
            float selfHalfWidth,
            float selfHalfLength,
            float selfMinHeight,
            float selfMaxHeight,
            Vector3 sweepStart,
            Vector3 sweepEnd,
            bool hasTarget,
            Vector3 targetCenter,
            Vector3 targetRight,
            Vector3 targetForward,
            float targetHalfWidth,
            float targetHalfLength,
            float targetMinHeight,
            float targetMaxHeight,
            bool hit,
            string status)
        {
            HasCollisionDebug = true;
            CollisionDebugSelfCenter = selfCenter;
            CollisionDebugSelfRight = selfRight.sqrMagnitude > 0.001f ? selfRight.normalized : Vector3.right;
            CollisionDebugSelfForward = selfForward.sqrMagnitude > 0.001f ? selfForward.normalized : Vector3.forward;
            CollisionDebugSelfHalfWidth = Mathf.Max(0.05f, selfHalfWidth);
            CollisionDebugSelfHalfLength = Mathf.Max(0.05f, selfHalfLength);
            CollisionDebugSelfMinHeight = selfMinHeight;
            CollisionDebugSelfMaxHeight = Mathf.Max(selfMaxHeight, selfMinHeight + 0.1f);
            CollisionDebugSweepStart = sweepStart;
            CollisionDebugSweepEnd = sweepEnd;
            CollisionDebugHasTarget = hasTarget;
            CollisionDebugTargetCenter = targetCenter;
            CollisionDebugTargetRight = targetRight.sqrMagnitude > 0.001f ? targetRight.normalized : Vector3.right;
            CollisionDebugTargetForward = targetForward.sqrMagnitude > 0.001f ? targetForward.normalized : Vector3.forward;
            CollisionDebugTargetHalfWidth = Mathf.Max(0.05f, targetHalfWidth);
            CollisionDebugTargetHalfLength = Mathf.Max(0.05f, targetHalfLength);
            CollisionDebugTargetMinHeight = targetMinHeight;
            CollisionDebugTargetMaxHeight = Mathf.Max(targetMaxHeight, targetMinHeight + 0.1f);
            CollisionDebugHit = hit;
            CollisionDebugStatus = status ?? "";
        }

        internal static void ClearCollisionDebug()
        {
            HasCollisionDebug = false;
            CollisionDebugHasTarget = false;
            CollisionDebugHit = false;
            CollisionDebugStatus = "";
        }

        internal static void BeginTrafficPresenceDebug(string status)
        {
            if (!TrafficPresenceDebugEnabled)
            {
                ClearTrafficPresenceDebug();
                return;
            }

            HasTrafficPresenceDebug = true;
            TrafficPresenceDebugSegmentCount = 0;
            TrafficPresenceDebugStatus = status ?? "";
        }

        internal static void AddTrafficPresenceDebugSegment(Vector3 start, Vector3 end, Vector3 label, string text, int kind)
        {
            if (!TrafficPresenceDebugEnabled ||
                TrafficPresenceDebugSegmentCount >= kMaxTrafficPresenceDebugSegments ||
                !IsFinite(start) ||
                !IsFinite(end) ||
                !IsFinite(label))
            {
                return;
            }

            int index = TrafficPresenceDebugSegmentCount++;
            TrafficPresenceDebugStarts[index] = start;
            TrafficPresenceDebugEnds[index] = end;
            TrafficPresenceDebugLabels[index] = label;
            TrafficPresenceDebugTexts[index] = text ?? "";
            TrafficPresenceDebugKinds[index] = Mathf.Clamp(kind, kTrafficPresenceDebugPrimary, kTrafficPresenceDebugHalo);
        }

        internal static void ClearTrafficPresenceDebug()
        {
            HasTrafficPresenceDebug = false;
            TrafficPresenceDebugSegmentCount = 0;
            TrafficPresenceDebugStatus = "";
        }

        internal static void SetTrafficGuardDebug(string status)
        {
            TrafficGuardDebugStatus = status ?? "";
        }

        internal static void ClearTrafficGuardDebug()
        {
            TrafficGuardDebugStatus = "";
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsNaN(value.z) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsInfinity(value.y) &&
                   !float.IsInfinity(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsNaN(value.z) &&
                   !float.IsNaN(value.w) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsInfinity(value.y) &&
                   !float.IsInfinity(value.z) &&
                   !float.IsInfinity(value.w);
        }

        internal static void SetTrafficPresenceTarget(
            Entity lane,
            Entity changeLane,
            float curveStart,
            float curveEnd,
            float curveT,
            float curveSign,
            float rearSpan,
            float forwardSpan)
        {
            if (lane == Entity.Null ||
                float.IsNaN(curveStart) ||
                float.IsNaN(curveEnd) ||
                float.IsInfinity(curveStart) ||
                float.IsInfinity(curveEnd))
            {
                ClearTrafficPresenceTarget();
                return;
            }

            HasTrafficPresenceTarget = true;
            TrafficPresenceLane = lane;
            TrafficPresenceChangeLane = changeLane;
            TrafficPresenceCurveStart = Mathf.Clamp01(curveStart);
            TrafficPresenceCurveEnd = Mathf.Clamp01(curveEnd);
            TrafficPresenceCurveT = Mathf.Clamp01(curveT);
            TrafficPresenceCurveSign = curveSign < 0f ? -1f : 1f;
            TrafficPresenceRearSpan = Mathf.Clamp(rearSpan, 0.0001f, 0.25f);
            TrafficPresenceForwardSpan = Mathf.Clamp(forwardSpan, 0.0001f, 0.25f);
        }

        internal static void ClearTrafficPresenceTarget()
        {
            HasTrafficPresenceTarget = false;
            TrafficPresenceLane = Entity.Null;
            TrafficPresenceChangeLane = Entity.Null;
            TrafficPresenceCurveStart = 0f;
            TrafficPresenceCurveEnd = 0f;
            TrafficPresenceCurveT = 0f;
            TrafficPresenceCurveSign = 1f;
            TrafficPresenceRearSpan = 0f;
            TrafficPresenceForwardSpan = 0f;
            ClearTrafficPresenceDebug();
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
            RoadOnlyTrafficPresence = true;
            TrafficPresenceHaloEnabled = false;
            VisualCollisionDebugEnabled = false;
            TrafficPresenceDebugEnabled = false;
            CollisionRetainedSpeed = 0.35f;
            ChaseCameraEnabled = true;
            ChaseCameraStatus = "Chase camera ready";
            HudVisible = false;
            PanelVisible = false;
            ClearCollisionDebug();
            ClearTrafficPresenceTarget();
            ClearTrafficPresenceDebug();
            ClearTrafficGuardDebug();

            if (s_Hud != null)
            {
                UnityEngine.Object.Destroy(s_Hud.gameObject);
                s_Hud = null;
            }
        }
    }
}
