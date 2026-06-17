using UnityEngine;

namespace BetaTestDrivingMod
{
    internal sealed class DirectDriveHudBehaviour : MonoBehaviour
    {
        private Rect m_Window = new Rect(24f, 84f, 390f, 475f);
        private Vector2 m_Scroll;
        private GUIStyle m_Label;
        private GUIStyle m_OverlayLabel;
        private Texture2D m_LineTexture;

        private void Update()
        {
            DirectDriveRuntime.SampleUnityInput();
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawCollisionDebugOverlay();
            DrawTrafficPresenceDebugOverlay();

            if (!DirectDriveRuntime.HudVisible)
                return;

            m_Window.width = Mathf.Clamp(390f, 340f, Mathf.Max(340f, Screen.width - 32f));
            m_Window.height = Mathf.Clamp(Screen.height - 165f, 320f, 520f);
            m_Window = GUI.Window(7114021, m_Window, DrawWindow, "Beta Test Driving Mod (Stable)");
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
                wordWrap = false,
                fontSize = 13
            };
            m_OverlayLabel.normal.textColor = Color.white;
            m_LineTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            m_LineTexture.SetPixel(0, 0, Color.white);
            m_LineTexture.Apply();
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"v{Mod.kVersion}", m_Label);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("x", GUILayout.Width(28f), GUILayout.Height(22f)))
                DirectDriveRuntime.ToggleHud();
            GUILayout.EndHorizontal();

            m_Scroll = GUILayout.BeginScrollView(m_Scroll, false, true);
            GUILayout.Label(DirectDriveRuntime.StatusText, m_Label);
            if (DirectDriveRuntime.IsDriving)
                GUILayout.Label(DirectDriveRuntime.PossessedName, m_Label);
            GUILayout.Label(DirectDriveRuntime.ControlStatus, m_Label);

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (!DirectDriveRuntime.IsDriving)
            {
                if (GUILayout.Button("Possess Car", GUILayout.Height(32f)))
                    DirectDriveRuntime.RequestToggle();
            }
            else if (GUILayout.Button("Release", GUILayout.Height(32f)))
            {
                DirectDriveRuntime.RequestRelease();
            }
            GUILayout.EndHorizontal();

            DirectDriveRuntime.ApplyPublicSafeDefaults();

            GUILayout.Space(8f);
            DirectDriveRuntime.TargetSpeedMph = Slider("Forward speed mph", DirectDriveRuntime.TargetSpeedMph, 8f, 95f, "0");
            DirectDriveRuntime.ReverseSpeedMph = Slider("Reverse speed mph", DirectDriveRuntime.ReverseSpeedMph, 2f, 24f, "0");
            DirectDriveRuntime.AccelerationMps2 = Slider("Launch response", DirectDriveRuntime.AccelerationMps2, 4f, 40f, "0.0");
            DirectDriveRuntime.BrakeMps2 = Slider("Brake response", DirectDriveRuntime.BrakeMps2, 10f, 80f, "0.0");
            DirectDriveRuntime.CoastMps2 = Slider("Coast slowdown", DirectDriveRuntime.CoastMps2, 1f, 30f, "0.0");
            DirectDriveRuntime.ReverseAccelerationMps2 = Slider("Reverse response", DirectDriveRuntime.ReverseAccelerationMps2, 3f, 30f, "0.0");
            DirectDriveRuntime.MaxTurnDegPerSecond = Slider("Steering response", DirectDriveRuntime.MaxTurnDegPerSecond, 45f, 260f, "0");
            DirectDriveRuntime.LowSpeedTurnBoost = Slider("Low speed turn boost", DirectDriveRuntime.LowSpeedTurnBoost, 0.1f, 1f, "0.00");
            DirectDriveRuntime.RoadHeightStickiness = Slider("Road height stickiness", DirectDriveRuntime.RoadHeightStickiness, 0f, 1f, "0.00");
            DirectDriveRuntime.VehicleCollisionEnabled = GUILayout.Toggle(DirectDriveRuntime.VehicleCollisionEnabled, "Vehicle collision");
            bool hitboxOverlay = GUILayout.Toggle(DirectDriveRuntime.VisualCollisionDebugEnabled, "Show collision hitboxes");
            if (hitboxOverlay != DirectDriveRuntime.VisualCollisionDebugEnabled)
                DirectDriveRuntime.SetVisualCollisionDebugEnabled(hitboxOverlay);
            bool trafficPresenceOverlay = GUILayout.Toggle(DirectDriveRuntime.TrafficPresenceDebugEnabled, "Show AI traffic presence");
            if (trafficPresenceOverlay != DirectDriveRuntime.TrafficPresenceDebugEnabled)
                DirectDriveRuntime.SetTrafficPresenceDebugEnabled(trafficPresenceOverlay);
            DirectDriveRuntime.CollisionRetainedSpeed = Slider("Retained speed", DirectDriveRuntime.CollisionRetainedSpeed, 0f, 1f, "0.00");

            GUILayout.Space(8f);
            if (GUILayout.Button("Reset Settings", GUILayout.Height(28f)))
                DirectDriveRuntime.ResetSettings();

            GUILayout.Space(8f);
            GUILayout.Label("V toggles possession. F8 hides this panel. Arrow keys or WASD drive. Road intent assist chooses the next AI road turn; it does not steer the physical body.", m_Label);
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, m_Window.width, 24f));
        }

        private float Slider(string label, float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value.ToString(format)}", m_Label, GUILayout.Width(170f));
            value = GUILayout.HorizontalSlider(value, min, max, GUILayout.MinWidth(130f));
            GUILayout.EndHorizontal();
            return Mathf.Clamp(value, min, max);
        }

        private void DrawCollisionDebugOverlay()
        {
            if (!DirectDriveRuntime.VisualCollisionDebugEnabled ||
                !DirectDriveRuntime.IsDriving ||
                !DirectDriveRuntime.HasCollisionDebug)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
                return;

            Color selfColor = new Color(0.1f, 0.95f, 1f, 0.95f);
            Color targetColor = DirectDriveRuntime.CollisionDebugHit
                ? new Color(1f, 0.22f, 0.12f, 1f)
                : new Color(1f, 0.9f, 0.12f, 0.9f);
            Color sweepColor = DirectDriveRuntime.CollisionDebugHit
                ? new Color(1f, 0.35f, 0.12f, 1f)
                : new Color(0.2f, 1f, 0.45f, 0.8f);

            DrawCollisionBox(
                camera,
                DirectDriveRuntime.CollisionDebugSelfCenter,
                DirectDriveRuntime.CollisionDebugSelfRight,
                DirectDriveRuntime.CollisionDebugSelfForward,
                DirectDriveRuntime.CollisionDebugSelfHalfWidth,
                DirectDriveRuntime.CollisionDebugSelfHalfLength,
                DirectDriveRuntime.CollisionDebugSelfMinHeight,
                DirectDriveRuntime.CollisionDebugSelfMaxHeight,
                selfColor);

            if (DirectDriveRuntime.CollisionDebugHasTarget)
            {
                DrawCollisionBox(
                    camera,
                    DirectDriveRuntime.CollisionDebugTargetCenter,
                    DirectDriveRuntime.CollisionDebugTargetRight,
                    DirectDriveRuntime.CollisionDebugTargetForward,
                    DirectDriveRuntime.CollisionDebugTargetHalfWidth,
                    DirectDriveRuntime.CollisionDebugTargetHalfLength,
                    DirectDriveRuntime.CollisionDebugTargetMinHeight,
                    DirectDriveRuntime.CollisionDebugTargetMaxHeight,
                    targetColor);
            }

            DrawWorldLine(camera, DirectDriveRuntime.CollisionDebugSweepStart + Vector3.up * 0.25f, DirectDriveRuntime.CollisionDebugSweepEnd + Vector3.up * 0.25f, sweepColor, 3f);
            DrawWorldLabel(camera, DirectDriveRuntime.CollisionDebugSelfCenter + Vector3.up * 3.3f, $"SELF {DirectDriveRuntime.CollisionDebugSelfHalfWidth * 2f:0.0}x{DirectDriveRuntime.CollisionDebugSelfHalfLength * 2f:0.0}", selfColor);

            if (DirectDriveRuntime.CollisionDebugHasTarget)
                DrawWorldLabel(camera, DirectDriveRuntime.CollisionDebugTargetCenter + Vector3.up * 3.3f, DirectDriveRuntime.CollisionDebugHit ? "TARGET HIT" : "TARGET CHECK", targetColor);

            DrawWorldLabel(camera, DirectDriveRuntime.CollisionDebugSelfCenter + Vector3.up * 4.1f, DirectDriveRuntime.CollisionDebugStatus, sweepColor);
        }

        private void DrawTrafficPresenceDebugOverlay()
        {
            if (!DirectDriveRuntime.TrafficPresenceDebugEnabled ||
                !DirectDriveRuntime.IsDriving ||
                !DirectDriveRuntime.HasTrafficPresenceDebug)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
                return;

            int count = Mathf.Clamp(DirectDriveRuntime.TrafficPresenceDebugSegmentCount, 0, DirectDriveRuntime.kMaxTrafficPresenceDebugSegments);
            for (int i = 0; i < count; i++)
            {
                int kind = DirectDriveRuntime.TrafficPresenceDebugKinds[i];
                Color color = TrafficPresenceDebugColor(kind);
                Vector3 start = DirectDriveRuntime.TrafficPresenceDebugStarts[i];
                Vector3 end = DirectDriveRuntime.TrafficPresenceDebugEnds[i];
                Vector3 label = DirectDriveRuntime.TrafficPresenceDebugLabels[i];

                DrawWorldLine(camera, start, end, color, kind == DirectDriveRuntime.kTrafficPresenceDebugPrimary ? 4f : 3f);
                DrawWorldLine(camera, start, start + Vector3.up * 1.15f, color, 2f);
                DrawWorldLine(camera, end, end + Vector3.up * 1.15f, color, 2f);
                DrawWorldLabel(camera, label, DirectDriveRuntime.TrafficPresenceDebugTexts[i], color);
            }

            string status = DirectDriveRuntime.TrafficPresenceDebugStatus;
            if (!string.IsNullOrEmpty(DirectDriveRuntime.TrafficGuardDebugStatus))
                status = string.IsNullOrEmpty(status) ? DirectDriveRuntime.TrafficGuardDebugStatus : $"{status} | {DirectDriveRuntime.TrafficGuardDebugStatus}";

            DrawWorldLabel(camera, DirectDriveRuntime.PosePosition + Vector3.up * 5.1f, $"AI presence: {status}", new Color(0.45f, 1f, 0.68f, 0.98f));
        }

        private Color TrafficPresenceDebugColor(int kind)
        {
            switch (kind)
            {
                case DirectDriveRuntime.kTrafficPresenceDebugChangeLane:
                    return new Color(1f, 0.78f, 0.18f, 0.95f);
                case DirectDriveRuntime.kTrafficPresenceDebugHalo:
                    return new Color(0.45f, 0.65f, 1f, 0.92f);
                default:
                    return new Color(0.2f, 1f, 0.42f, 1f);
            }
        }

        private void DrawCollisionBox(Camera camera, Vector3 center, Vector3 right, Vector3 forward, float halfWidth, float halfLength, float minHeight, float maxHeight, Color color)
        {
            right = right.sqrMagnitude > 0.001f ? right.normalized : Vector3.right;
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
            halfWidth = Mathf.Max(0.05f, halfWidth);
            halfLength = Mathf.Max(0.05f, halfLength);

            float bottomY = Mathf.Min(minHeight, maxHeight);
            float topY = Mathf.Max(maxHeight, bottomY + 0.1f);
            Vector3 bottom = center + Vector3.up * bottomY;
            Vector3 up = Vector3.up * (topY - bottomY);

            Vector3 nearLeft = bottom - forward * halfLength - right * halfWidth;
            Vector3 nearRight = bottom - forward * halfLength + right * halfWidth;
            Vector3 farLeft = bottom + forward * halfLength - right * halfWidth;
            Vector3 farRight = bottom + forward * halfLength + right * halfWidth;

            DrawWorldLine(camera, nearLeft, nearRight, color, 2f);
            DrawWorldLine(camera, nearRight, farRight, color, 2f);
            DrawWorldLine(camera, farRight, farLeft, color, 2f);
            DrawWorldLine(camera, farLeft, nearLeft, color, 2f);

            DrawWorldLine(camera, nearLeft + up, nearRight + up, color, 1.5f);
            DrawWorldLine(camera, nearRight + up, farRight + up, color, 1.5f);
            DrawWorldLine(camera, farRight + up, farLeft + up, color, 1.5f);
            DrawWorldLine(camera, farLeft + up, nearLeft + up, color, 1.5f);

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
            if (string.IsNullOrEmpty(text) ||
                !TryProject(camera, world, out Vector2 position))
            {
                return;
            }

            Color previous = GUI.color;
            GUI.color = Color.black;
            GUI.Label(new Rect(position.x + 2f, position.y + 2f, 360f, 24f), text, m_OverlayLabel);
            GUI.color = color;
            GUI.Label(new Rect(position.x, position.y, 360f, 24f), text, m_OverlayLabel);
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
            return screen.x > -400f && screen.x < Screen.width + 400f && screen.y > -400f && screen.y < Screen.height + 400f;
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
    }
}
