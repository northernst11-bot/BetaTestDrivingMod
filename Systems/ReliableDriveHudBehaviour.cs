using UnityEngine;

namespace BetaTestDrivingMod
{
    internal sealed class ReliableDriveHudBehaviour : MonoBehaviour
    {
        private Rect m_Window = new Rect(0f, 72f, 460f, 560f);
        private bool m_MenuOpen;
        private GUIStyle m_Label;
        private GUIStyle m_OverlayLabel;
        private Texture2D m_LineTexture;
        private Vector2 m_Scroll;

        internal void ToggleMenu()
        {
            m_MenuOpen = !m_MenuOpen;
        }

        private void Update()
        {
            ReliableDriveRuntime.SampleUnityInput();
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawTurnReleaseOverlay();

            if (!m_MenuOpen)
                return;

            m_Window.width = Mathf.Clamp(460f, 360f, Mathf.Max(360f, Screen.width - 32f));
            m_Window.height = Mathf.Clamp(Screen.height - 150f, 360f, 650f);
            m_Window.x = Mathf.Clamp(336f, 12f, Mathf.Max(12f, Screen.width - m_Window.width - 18f));
            m_Window.y = Mathf.Clamp(84f, 48f, Mathf.Max(48f, Screen.height - m_Window.height - 18f));
            m_Window = GUI.Window(9425501, m_Window, DrawWindow, "Beta Test Driving Mod");
        }

        private void EnsureStyles()
        {
            if (m_Label != null)
                return;

            m_Label = new GUIStyle(GUI.skin.label)
            {
                wordWrap = true,
                fontSize = 12
            };
            m_Label.normal.textColor = Color.white;

            m_OverlayLabel = new GUIStyle(GUI.skin.label)
            {
                wordWrap = true,
                fontSize = 12
            };
            m_OverlayLabel.normal.textColor = Color.white;

            m_LineTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            m_LineTexture.SetPixel(0, 0, Color.white);
            m_LineTexture.Apply();
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(4f);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("x", GUILayout.Width(28f), GUILayout.Height(22f)))
                m_MenuOpen = false;
            GUILayout.EndHorizontal();

            m_Scroll = GUILayout.BeginScrollView(m_Scroll, false, true);
            GUILayout.Space(4f);
            GUILayout.Label($"v{Mod.kVersion}", m_Label);
            GUILayout.Label(ReliableDriveRuntime.StatusText, m_Label);
            if (ReliableDriveRuntime.IsDriving)
                GUILayout.Label(ReliableDriveRuntime.PossessedPrefab, m_Label);

            GUILayout.Space(6f);
            ReliableDriveRuntime.AllowServiceVehicles = GUILayout.Toggle(ReliableDriveRuntime.AllowServiceVehicles, "Allow bus, delivery, police, garbage, ambulance");
            ReliableDriveRuntime.AllowBikesAndMotorcycles = GUILayout.Toggle(ReliableDriveRuntime.AllowBikesAndMotorcycles, "Allow bicycles / motorcycles (experimental)");
            ReliableDriveRuntime.AllowWatercraft = GUILayout.Toggle(ReliableDriveRuntime.AllowWatercraft, "Allow boats / watercraft (experimental)");
            ReliableDriveRuntime.AllowRailVehicles = GUILayout.Toggle(ReliableDriveRuntime.AllowRailVehicles, "Allow trains / rail vehicles (experimental)");
            ReliableDriveRuntime.LaneAssistEnabled = GUILayout.Toggle(ReliableDriveRuntime.LaneAssistEnabled, "Road lane assist / merge aim");
            ReliableDriveRuntime.StrongJunctionOverrideEnabled = GUILayout.Toggle(ReliableDriveRuntime.StrongJunctionOverrideEnabled, "Expensive junction path override");
            ReliableDriveRuntime.BusStopAssistEnabled = GUILayout.Toggle(ReliableDriveRuntime.BusStopAssistEnabled, "Bus stop assist");
            GUILayout.Label(ReliableDriveRuntime.FocusStatus, m_Label);
            GUILayout.Label(ReliableDriveRuntime.SpeedAssistStatus, m_Label);
            GUILayout.Label(ReliableDriveRuntime.LaneAssistStatus, m_Label);
            GUILayout.Label(ReliableDriveRuntime.TurnGateStatus, m_Label);
            GUILayout.Label(ReliableDriveRuntime.TurnZoneStatus, m_Label);
            GUILayout.Label(ReliableDriveRuntime.BusAssistStatus, m_Label);

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (!ReliableDriveRuntime.IsDriving)
            {
                if (GUILayout.Button("Possess Live Car", GUILayout.Height(34f)))
                    ReliableDriveRuntime.RequestToggle();
            }
            else if (GUILayout.Button("Release", GUILayout.Height(34f)))
            {
                ReliableDriveRuntime.RequestRelease();
            }

            GUILayout.Label("Camera: game focus follows possessed vehicle", m_Label);
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            DrawSection("Main Driving");
            ReliableDriveRuntime.TargetSpeedMph = DrawSlider("Target speed mph", ReliableDriveRuntime.TargetSpeedMph, 5f, 80f, "0");
            ReliableDriveRuntime.AutoRoadSpeedEnabled = GUILayout.Toggle(ReliableDriveRuntime.AutoRoadSpeedEnabled, "Auto road / turn speed");
            ReliableDriveRuntime.RoadSpeedMultiplier = DrawSlider("Road speed percent", ReliableDriveRuntime.RoadSpeedMultiplier, 0.35f, 1.25f, "0.00");
            ReliableDriveRuntime.JunctionTurnSpeedMph = DrawSlider("Junction turn speed mph", ReliableDriveRuntime.JunctionTurnSpeedMph, 6f, 45f, "0");
            ReliableDriveRuntime.SharpTurnSpeedMph = DrawSlider("Sharp turn speed mph", ReliableDriveRuntime.SharpTurnSpeedMph, 4f, 34f, "0");
            ReliableDriveRuntime.ReverseSpeedMph = DrawSlider("Reverse speed mph", ReliableDriveRuntime.ReverseSpeedMph, 1f, 24f, "0");
            ReliableDriveRuntime.AccelerationRate = DrawSlider("Acceleration response", ReliableDriveRuntime.AccelerationRate, 0.5f, 28f, "0.0");
            ReliableDriveRuntime.BrakeRate = DrawSlider("Brake response", ReliableDriveRuntime.BrakeRate, 2f, 48f, "0.0");
            ReliableDriveRuntime.ReverseAccelerationRate = DrawSlider("Reverse response", ReliableDriveRuntime.ReverseAccelerationRate, 0.5f, 24f, "0.0");
            ReliableDriveRuntime.DirectionChangeRate = DrawSlider("Direction change response", ReliableDriveRuntime.DirectionChangeRate, 2f, 56f, "0.0");
            ReliableDriveRuntime.CoastingRate = DrawSlider("Coast slowdown response", ReliableDriveRuntime.CoastingRate, 0.5f, 28f, "0.0");
            ReliableDriveRuntime.SteeringStrength = DrawSlider("Steering strength", ReliableDriveRuntime.SteeringStrength, 0.8f, 14f, "0.0");
            ReliableDriveRuntime.LookAheadMeters = DrawSlider("Lane look ahead", ReliableDriveRuntime.LookAheadMeters, 4f, 32f, "0.0");

            DrawSection("Free Steering");
            ReliableDriveRuntime.FreeTurnLookAheadMin = DrawSlider("Turn lookahead slow", ReliableDriveRuntime.FreeTurnLookAheadMin, 1f, 18f, "0.0");
            ReliableDriveRuntime.FreeTurnLookAheadMax = DrawSlider("Turn lookahead fast", ReliableDriveRuntime.FreeTurnLookAheadMax, 2f, 32f, "0.0");
            ReliableDriveRuntime.FreeSteerOffsetSlow = DrawSlider("Steer offset slow", ReliableDriveRuntime.FreeSteerOffsetSlow, 0.2f, 8f, "0.00");
            ReliableDriveRuntime.FreeSteerOffsetFast = DrawSlider("Steer offset fast", ReliableDriveRuntime.FreeSteerOffsetFast, 0.2f, 10f, "0.00");
            ReliableDriveRuntime.FreeSteerDirectionSlow = DrawSlider("Direction pull slow", ReliableDriveRuntime.FreeSteerDirectionSlow, 0.02f, 2.5f, "0.00");
            ReliableDriveRuntime.FreeSteerDirectionFast = DrawSlider("Direction pull fast", ReliableDriveRuntime.FreeSteerDirectionFast, 0.02f, 3f, "0.00");
            ReliableDriveRuntime.BlockedUturnSteeringScale = DrawSlider("Blocked U-turn steer scale", ReliableDriveRuntime.BlockedUturnSteeringScale, 0f, 1f, "0.00");

            DrawSection("Lane Assist");
            ReliableDriveRuntime.LaneLookAheadMin = DrawSlider("Lane target slow", ReliableDriveRuntime.LaneLookAheadMin, 2f, 24f, "0.0");
            ReliableDriveRuntime.LaneLookAheadMax = DrawSlider("Lane target fast", ReliableDriveRuntime.LaneLookAheadMax, 8f, 48f, "0.0");
            ReliableDriveRuntime.MergeHoldRise = DrawSlider("Merge assist ramp up", ReliableDriveRuntime.MergeHoldRise, 0.02f, 0.5f, "0.00");
            ReliableDriveRuntime.MergeHoldFall = DrawSlider("Merge assist ramp down", ReliableDriveRuntime.MergeHoldFall, 0.02f, 0.6f, "0.00");

            DrawSection("Junction Turn Gate");
            ReliableDriveRuntime.JunctionGateSlow = DrawSlider("Turn scan gate slow", ReliableDriveRuntime.JunctionGateSlow, 4f, 120f, "0");
            ReliableDriveRuntime.JunctionGateFast = DrawSlider("Turn scan gate fast", ReliableDriveRuntime.JunctionGateFast, 4f, 160f, "0");
            ReliableDriveRuntime.ShowTurnReleaseZones = GUILayout.Toggle(ReliableDriveRuntime.ShowTurnReleaseZones, "Show anchored turn boxes");
            ReliableDriveRuntime.TurnLaneReleaseSlow = DrawSlider("Turn-lane Y slow", ReliableDriveRuntime.TurnLaneReleaseSlow, 4f, 80f, "0");
            ReliableDriveRuntime.TurnLaneReleaseFast = DrawSlider("Turn-lane Y fast", ReliableDriveRuntime.TurnLaneReleaseFast, 4f, 100f, "0");
            ReliableDriveRuntime.JunctionReleaseSlow = DrawSlider("Junction Y slow", ReliableDriveRuntime.JunctionReleaseSlow, 2f, 45f, "0");
            ReliableDriveRuntime.JunctionReleaseFast = DrawSlider("Junction Y fast", ReliableDriveRuntime.JunctionReleaseFast, 2f, 65f, "0");
            ReliableDriveRuntime.TurnZoneHalfWidth = DrawSlider("Turn zone X half-width", ReliableDriveRuntime.TurnZoneHalfWidth, 2f, 16f, "0.0");
            ReliableDriveRuntime.JunctionTurnBlendMin = DrawSlider("Turn blend min", ReliableDriveRuntime.JunctionTurnBlendMin, 0f, 1f, "0.00");
            ReliableDriveRuntime.JunctionTurnBlendMax = DrawSlider("Turn blend max", ReliableDriveRuntime.JunctionTurnBlendMax, 0f, 1f, "0.00");

            DrawSection("Linked Connector Search");
            ReliableDriveRuntime.LinkedForwardSlow = DrawSlider("Linked forward slow", ReliableDriveRuntime.LinkedForwardSlow, 0f, 40f, "0.0");
            ReliableDriveRuntime.LinkedForwardFast = DrawSlider("Linked forward fast", ReliableDriveRuntime.LinkedForwardFast, 0f, 60f, "0.0");
            ReliableDriveRuntime.LinkedBehind = DrawSlider("Linked behind", ReliableDriveRuntime.LinkedBehind, 0f, 32f, "0.0");
            ReliableDriveRuntime.LinkedRadiusSlow = DrawSlider("Linked radius slow", ReliableDriveRuntime.LinkedRadiusSlow, 2f, 40f, "0.0");
            ReliableDriveRuntime.LinkedRadiusFast = DrawSlider("Linked radius fast", ReliableDriveRuntime.LinkedRadiusFast, 2f, 70f, "0.0");

            DrawSection("Inside Junction Search");
            ReliableDriveRuntime.InsideForward = DrawSlider("Inside forward", ReliableDriveRuntime.InsideForward, 0f, 40f, "0.0");
            ReliableDriveRuntime.InsideBehind = DrawSlider("Inside behind", ReliableDriveRuntime.InsideBehind, 0f, 40f, "0.0");
            ReliableDriveRuntime.InsideRadiusSlow = DrawSlider("Inside radius slow", ReliableDriveRuntime.InsideRadiusSlow, 2f, 45f, "0.0");
            ReliableDriveRuntime.InsideRadiusFast = DrawSlider("Inside radius fast", ReliableDriveRuntime.InsideRadiusFast, 2f, 70f, "0.0");
            ReliableDriveRuntime.JunctionTargetAheadSlow = DrawSlider("Junction target ahead slow", ReliableDriveRuntime.JunctionTargetAheadSlow, 2f, 40f, "0.0");
            ReliableDriveRuntime.JunctionTargetAheadFast = DrawSlider("Junction target ahead fast", ReliableDriveRuntime.JunctionTargetAheadFast, 2f, 60f, "0.0");

            DrawSection("Junction Filters");
            ReliableDriveRuntime.JunctionSideMin = DrawSlider("Minimum side distance", ReliableDriveRuntime.JunctionSideMin, -2f, 5f, "0.00");
            ReliableDriveRuntime.JunctionTurnDotMin = DrawSlider("Minimum turn angle", ReliableDriveRuntime.JunctionTurnDotMin, -0.4f, 0.8f, "0.00");
            ReliableDriveRuntime.JunctionBackDotMin = DrawSlider("Back/U-turn cutoff", ReliableDriveRuntime.JunctionBackDotMin, -1f, 0.2f, "0.00");

            DrawSection("Road Keeper Turn Fallback");
            ReliableDriveRuntime.JunctionFallbackForwardSlow = DrawSlider("Keeper forward slow", ReliableDriveRuntime.JunctionFallbackForwardSlow, 0f, 28f, "0.0");
            ReliableDriveRuntime.JunctionFallbackForwardFast = DrawSlider("Keeper forward fast", ReliableDriveRuntime.JunctionFallbackForwardFast, 0f, 40f, "0.0");
            ReliableDriveRuntime.JunctionFallbackSideMin = DrawSlider("Keeper side min", ReliableDriveRuntime.JunctionFallbackSideMin, 0f, 18f, "0.0");
            ReliableDriveRuntime.JunctionFallbackSideMax = DrawSlider("Keeper side max", ReliableDriveRuntime.JunctionFallbackSideMax, 0f, 24f, "0.0");

            GUILayout.Space(8f);
            if (GUILayout.Button("Reset Driving Settings", GUILayout.Height(30f)))
                ReliableDriveRuntime.ResetDrivingSettings();

            GUILayout.Space(8f);
            GUILayout.Label("V toggles. W/up accelerates, S/down brakes first and reverses only after a second press at stop. A/D or left/right steer; triple-tap a turn key to allow a U-turn. F8 hides or opens this menu.", m_Label);
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, m_Window.width, 24f));
        }

        private void DrawTurnReleaseOverlay()
        {
            if (!ReliableDriveRuntime.ShowTurnReleaseZones ||
                !ReliableDriveRuntime.IsDriving ||
                !ReliableDriveRuntime.HasTurnZoneDebug)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
                return;

            Vector3 zoneEnd = ReliableDriveRuntime.TurnZonePosition + Vector3.up * 0.35f;
            Vector3 forward = ReliableDriveRuntime.TurnZoneForward;
            Vector3 right = ReliableDriveRuntime.TurnZoneRight;
            float halfWidth = ReliableDriveRuntime.TurnZoneHalfWidth;
            float laneY = ReliableDriveRuntime.TurnZoneLaneReleaseMeters;
            float junctionY = ReliableDriveRuntime.TurnZoneJunctionReleaseMeters;
            float scanY = ReliableDriveRuntime.TurnZoneScanGateMeters;
            bool hasRealJunction = ReliableDriveRuntime.TurnZoneHasRealJunction;
            Color laneColor = ReliableDriveRuntime.TurnZoneMatchingTurnLane
                ? new Color(0.1f, 1f, 0.35f, 0.9f)
                : new Color(0.1f, 0.8f, 1f, 0.55f);
            Color junctionColor = ReliableDriveRuntime.TurnZoneJunctionGate
                ? new Color(1f, 0.82f, 0.15f, 0.9f)
                : new Color(1f, 0.55f, 0.12f, 0.55f);
            Color activeColor = ReliableDriveRuntime.TurnZoneManualReleaseOpen
                ? new Color(0.25f, 1f, 1f, 1f)
                : hasRealJunction ? new Color(1f, 0.15f, 0.15f, 0.95f) : new Color(1f, 1f, 1f, 0.95f);

            DrawWorldZoneBox(camera, zoneEnd - forward * scanY, forward, right, scanY, halfWidth * 1.55f, 1.4f, new Color(0.75f, 0.55f, 1f, 0.45f));
            if (hasRealJunction)
            {
                DrawWorldZoneBox(camera, zoneEnd - forward * laneY, forward, right, laneY, halfWidth, 2.2f, laneColor);
                DrawWorldZoneBox(camera, zoneEnd - forward * junctionY, forward, right, junctionY, halfWidth * 1.25f, 2.8f, junctionColor);
                DrawWorldZoneBox(camera, zoneEnd - forward * 0.4f, forward, right, 0.8f, halfWidth * 1.45f, 3.4f, activeColor);
            }

            DrawWorldLabel(camera, zoneEnd - forward * scanY + Vector3.up * 2.2f, $"Scan gate Y {scanY:0}m", new Color(0.75f, 0.55f, 1f, 1f));
            if (hasRealJunction)
            {
                DrawWorldLabel(camera, zoneEnd - forward * laneY + right * halfWidth + Vector3.up * 2.6f, $"Turn-lane Y {laneY:0}m", laneColor);
                DrawWorldLabel(camera, zoneEnd - forward * junctionY - right * (halfWidth * 1.25f) + Vector3.up * 3.1f, $"Junction Y {junctionY:0}m", junctionColor);
                DrawWorldLabel(camera, zoneEnd + right * halfWidth + Vector3.up * 3.7f, $"X half-width {halfWidth:0.0}m", activeColor);
                DrawWorldLabel(camera, zoneEnd + Vector3.up * 4.2f, "Turn zone end", activeColor);
            }
            else
            {
                DrawWorldLabel(camera, zoneEnd + Vector3.up * 3.2f, "Scanning: no real junction found", activeColor);
            }

            if (TryProject(camera, zoneEnd + Vector3.up * 5.1f, out Vector2 labelPos))
            {
                Color previous = GUI.color;
                GUI.color = Color.black;
                GUI.Label(new Rect(labelPos.x + 2f, labelPos.y + 2f, 330f, 42f), ReliableDriveRuntime.TurnZoneStatus, m_OverlayLabel);
                GUI.color = Color.white;
                GUI.Label(new Rect(labelPos.x, labelPos.y, 330f, 42f), ReliableDriveRuntime.TurnZoneStatus, m_OverlayLabel);
                GUI.color = previous;
            }
        }

        private void DrawWorldZoneBox(Camera camera, Vector3 origin, Vector3 forward, Vector3 right, float length, float halfWidth, float height, Color color)
        {
            length = Mathf.Max(0.5f, length);
            halfWidth = Mathf.Max(0.5f, halfWidth);

            Vector3 nearLeft = origin - right * halfWidth;
            Vector3 nearRight = origin + right * halfWidth;
            Vector3 farLeft = origin + forward * length - right * halfWidth;
            Vector3 farRight = origin + forward * length + right * halfWidth;
            Vector3 up = Vector3.up * Mathf.Max(0.5f, height);

            DrawWorldLine(camera, nearLeft, nearRight, color, 2f);
            DrawWorldLine(camera, farLeft, farRight, color, 2f);
            DrawWorldLine(camera, nearLeft, farLeft, color, 2f);
            DrawWorldLine(camera, nearRight, farRight, color, 2f);

            DrawWorldLine(camera, nearLeft + up, nearRight + up, color, 1.5f);
            DrawWorldLine(camera, farLeft + up, farRight + up, color, 1.5f);
            DrawWorldLine(camera, nearLeft + up, farLeft + up, color, 1.5f);
            DrawWorldLine(camera, nearRight + up, farRight + up, color, 1.5f);

            DrawWorldLine(camera, nearLeft, nearLeft + up, color, 1.5f);
            DrawWorldLine(camera, nearRight, nearRight + up, color, 1.5f);
            DrawWorldLine(camera, farLeft, farLeft + up, color, 1.5f);
            DrawWorldLine(camera, farRight, farRight + up, color, 1.5f);
        }

        private void DrawWorldLine(Camera camera, Vector3 from, Vector3 to, Color color, float width)
        {
            if (!TryProject(camera, from, out Vector2 a) || !TryProject(camera, to, out Vector2 b))
                return;

            DrawScreenLine(a, b, color, width);
        }

        private void DrawWorldLabel(Camera camera, Vector3 world, string text, Color color)
        {
            if (!TryProject(camera, world, out Vector2 position))
                return;

            Color previous = GUI.color;
            GUI.color = Color.black;
            GUI.Label(new Rect(position.x + 2f, position.y + 2f, 220f, 22f), text, m_OverlayLabel);
            GUI.color = color;
            GUI.Label(new Rect(position.x, position.y, 220f, 22f), text, m_OverlayLabel);
            GUI.color = previous;
        }

        private bool TryProject(Camera camera, Vector3 world, out Vector2 screen)
        {
            Vector3 projected = camera.WorldToScreenPoint(world);
            if (projected.z <= 0.05f)
            {
                screen = default;
                return false;
            }

            screen = new Vector2(projected.x, Screen.height - projected.y);
            return screen.x > -300f && screen.x < Screen.width + 300f && screen.y > -300f && screen.y < Screen.height + 300f;
        }

        private void DrawScreenLine(Vector2 from, Vector2 to, Color color, float width)
        {
            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.5f)
                return;

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
            GUI.DrawTexture(new Rect(from.x, from.y - width * 0.5f, length, width), m_LineTexture);
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        private void DrawSection(string title)
        {
            GUILayout.Space(10f);
            GUILayout.Label(title, m_Label);
        }

        private float DrawSlider(string label, float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value.ToString(format)}", m_Label, GUILayout.Width(190f));
            value = GUILayout.HorizontalSlider(value, min, max, GUILayout.MinWidth(160f));
            GUILayout.EndHorizontal();
            return Mathf.Clamp(value, min, max);
        }
    }
}
