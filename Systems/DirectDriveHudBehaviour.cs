using UnityEngine;

namespace BetaTestDrivingMod
{
    internal sealed class DirectDriveHudBehaviour : MonoBehaviour
    {
        private Rect m_Window = new Rect(24f, 84f, 390f, 475f);
        private Vector2 m_Scroll;
        private GUIStyle m_Label;

        private void Update()
        {
            DirectDriveRuntime.SampleUnityInput();
        }

        private void OnGUI()
        {
            if (!DirectDriveRuntime.HudVisible)
                return;

            EnsureStyles();
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
    }
}
