using System;
using Colossal.UI.Binding;
using Game;
using Game.UI;
using UnityEngine.Scripting;

namespace BetaTestDrivingMod
{
    internal sealed class ReliableDriveUiSystem : UISystemBase
    {
        private const string kGroup = "betatestdrivingmod";

        private ValueBinding<bool> m_PanelVisible;
        private ValueBinding<bool> m_IsDriving;
        private ValueBinding<bool> m_AllowServiceVehicles;
        private ValueBinding<bool> m_LaneAssistEnabled;
        private ValueBinding<bool> m_BusStopAssistEnabled;
        private ValueBinding<string> m_StatusText;
        private ValueBinding<string> m_PossessedPrefab;
        private ValueBinding<string> m_FocusStatus;
        private ValueBinding<string> m_SpeedAssistStatus;
        private ValueBinding<string> m_LaneAssistStatus;
        private ValueBinding<string> m_TurnGateStatus;
        private ValueBinding<string> m_BusAssistStatus;
        private ValueBinding<float> m_TargetSpeedMph;
        private ValueBinding<float> m_SteeringStrength;

        private bool m_PanelVisibleValue;
        private float m_NextStatusUpdate;

        public override GameMode gameMode => GameMode.Game;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();

            AddBinding(m_PanelVisible = new ValueBinding<bool>(kGroup, "PanelVisible", false, null, null));
            AddBinding(m_IsDriving = new ValueBinding<bool>(kGroup, "IsDriving", false, null, null));
            AddBinding(m_AllowServiceVehicles = new ValueBinding<bool>(kGroup, "AllowServiceVehicles", ReliableDriveRuntime.AllowServiceVehicles, null, null));
            AddBinding(m_LaneAssistEnabled = new ValueBinding<bool>(kGroup, "LaneAssistEnabled", ReliableDriveRuntime.LaneAssistEnabled, null, null));
            AddBinding(m_BusStopAssistEnabled = new ValueBinding<bool>(kGroup, "BusStopAssistEnabled", ReliableDriveRuntime.BusStopAssistEnabled, null, null));
            AddBinding(m_StatusText = new ValueBinding<string>(kGroup, "StatusText", ReliableDriveRuntime.StatusText, null, null));
            AddBinding(m_PossessedPrefab = new ValueBinding<string>(kGroup, "PossessedPrefab", ReliableDriveRuntime.PossessedPrefab, null, null));
            AddBinding(m_FocusStatus = new ValueBinding<string>(kGroup, "FocusStatus", ReliableDriveRuntime.FocusStatus, null, null));
            AddBinding(m_SpeedAssistStatus = new ValueBinding<string>(kGroup, "SpeedAssistStatus", ReliableDriveRuntime.SpeedAssistStatus, null, null));
            AddBinding(m_LaneAssistStatus = new ValueBinding<string>(kGroup, "LaneAssistStatus", ReliableDriveRuntime.LaneAssistStatus, null, null));
            AddBinding(m_TurnGateStatus = new ValueBinding<string>(kGroup, "TurnGateStatus", ReliableDriveRuntime.TurnGateStatus, null, null));
            AddBinding(m_BusAssistStatus = new ValueBinding<string>(kGroup, "BusAssistStatus", ReliableDriveRuntime.BusAssistStatus, null, null));
            AddBinding(m_TargetSpeedMph = new ValueBinding<float>(kGroup, "TargetSpeedMph", ReliableDriveRuntime.TargetSpeedMph, null, null));
            AddBinding(m_SteeringStrength = new ValueBinding<float>(kGroup, "SteeringStrength", ReliableDriveRuntime.SteeringStrength, null, null));

            AddBinding(new TriggerBinding(kGroup, "TogglePanel", new Action(TogglePanel)));
            AddBinding(new TriggerBinding(kGroup, "ToggleHud", new Action(ReliableDriveRuntime.ToggleHud)));
            AddBinding(new TriggerBinding<bool>(kGroup, "SetPanelVisible", new Action<bool>(SetPanelVisible), null));
            AddBinding(new TriggerBinding(kGroup, "ToggleDriving", new Action(ReliableDriveRuntime.RequestToggle)));
            AddBinding(new TriggerBinding(kGroup, "Release", new Action(ReliableDriveRuntime.RequestRelease)));
            AddBinding(new TriggerBinding<bool>(kGroup, "SetServiceVehicles", new Action<bool>(SetServiceVehicles), null));
            AddBinding(new TriggerBinding<bool>(kGroup, "SetLaneAssist", new Action<bool>(SetLaneAssist), null));
            AddBinding(new TriggerBinding<bool>(kGroup, "SetBusAssist", new Action<bool>(SetBusAssist), null));
            AddBinding(new TriggerBinding<float>(kGroup, "AdjustTargetSpeed", new Action<float>(ReliableDriveRuntime.AdjustTargetSpeed), null));
            AddBinding(new TriggerBinding<float>(kGroup, "AdjustSteering", new Action<float>(ReliableDriveRuntime.AdjustSteering), null));
        }

        [Preserve]
        protected override void OnUpdate()
        {
            base.OnUpdate();

            if (UnityEngine.Time.unscaledTime < m_NextStatusUpdate)
                return;

            m_NextStatusUpdate = UnityEngine.Time.unscaledTime + 0.2f;
            UpdateBindings();
        }

        private void TogglePanel()
        {
            SetPanelVisible(!m_PanelVisibleValue);
        }

        private void SetPanelVisible(bool visible)
        {
            m_PanelVisibleValue = visible;
            m_PanelVisible.Update(visible);
        }

        private void SetServiceVehicles(bool value)
        {
            ReliableDriveRuntime.AllowServiceVehicles = value;
            m_AllowServiceVehicles.Update(value);
        }

        private void SetLaneAssist(bool value)
        {
            ReliableDriveRuntime.LaneAssistEnabled = value;
            m_LaneAssistEnabled.Update(value);
        }

        private void SetBusAssist(bool value)
        {
            ReliableDriveRuntime.BusStopAssistEnabled = value;
            m_BusStopAssistEnabled.Update(value);
        }

        private void UpdateBindings()
        {
            m_IsDriving.Update(ReliableDriveRuntime.IsDriving);
            m_AllowServiceVehicles.Update(ReliableDriveRuntime.AllowServiceVehicles);
            m_LaneAssistEnabled.Update(ReliableDriveRuntime.LaneAssistEnabled);
            m_BusStopAssistEnabled.Update(ReliableDriveRuntime.BusStopAssistEnabled);
            m_StatusText.Update(ReliableDriveRuntime.StatusText ?? "");
            m_PossessedPrefab.Update(ReliableDriveRuntime.PossessedPrefab ?? "");
            m_FocusStatus.Update(ReliableDriveRuntime.FocusStatus ?? "");
            m_SpeedAssistStatus.Update(ReliableDriveRuntime.SpeedAssistStatus ?? "");
            m_LaneAssistStatus.Update(ReliableDriveRuntime.LaneAssistStatus ?? "");
            m_TurnGateStatus.Update(ReliableDriveRuntime.TurnGateStatus ?? "");
            m_BusAssistStatus.Update(ReliableDriveRuntime.BusAssistStatus ?? "");
            m_TargetSpeedMph.Update(ReliableDriveRuntime.TargetSpeedMph);
            m_SteeringStrength.Update(ReliableDriveRuntime.SteeringStrength);
        }
    }
}
