using System;
using Colossal.UI.Binding;
using Game;
using Game.UI;
using UnityEngine;
using UnityEngine.Scripting;

namespace BetaTestDrivingMod
{
    internal sealed class DirectDriveUiSystem : UISystemBase
    {
        private const string kGroup = "betatestdrivingmod";

        private ValueBinding<bool> m_PanelVisible;
        private ValueBinding<bool> m_IsDriving;
        private ValueBinding<string> m_StatusText;
        private ValueBinding<string> m_PossessedName;
        private ValueBinding<string> m_ControlStatus;
        private ValueBinding<float> m_SpeedMph;
        private ValueBinding<bool> m_Braking;
        private ValueBinding<bool> m_ReverseReady;
        private ValueBinding<float> m_InputThrottle;
        private ValueBinding<float> m_InputBrake;
        private ValueBinding<float> m_InputSteering;
        private ValueBinding<bool> m_InputFresh;
        private ValueBinding<float> m_InputAgeSeconds;
        private ValueBinding<bool> m_RoadIntentAssist;
        private ValueBinding<bool> m_RoadHeightAssist;
        private ValueBinding<bool> m_FreezeVanillaNavigation;
        private ValueBinding<bool> m_ChaseCameraEnabled;
        private ValueBinding<string> m_ChaseCameraStatus;
        private ValueBinding<bool> m_PoliceChaseEnabled;
        private ValueBinding<bool> m_PoliceChaseActive;
        private ValueBinding<string> m_PoliceChaseStatus;
        private ValueBinding<int> m_PoliceChaseUnits;
        private ValueBinding<int> m_RedLightViolations;
        private ValueBinding<float> m_TargetSpeedMph;
        private ValueBinding<float> m_ReverseSpeedMph;
        private ValueBinding<float> m_AccelerationMps2;
        private ValueBinding<float> m_BrakeMps2;
        private ValueBinding<float> m_CoastMps2;
        private ValueBinding<float> m_ReverseAccelerationMps2;
        private ValueBinding<float> m_MaxTurnDegPerSecond;
        private ValueBinding<float> m_LowSpeedTurnBoost;
        private ValueBinding<float> m_RoadHeightStickiness;
        private ValueBinding<float> m_ChaseCameraDistance;
        private ValueBinding<float> m_ChaseCameraHeight;
        private ValueBinding<float> m_ChaseCameraLookAhead;

        private float m_NextStatusUpdate;

        public override GameMode gameMode => GameMode.Game;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            DirectDriveRuntime.ApplyPublicSafeDefaults();

            AddBinding(m_PanelVisible = new ValueBinding<bool>(kGroup, "PanelVisible", DirectDriveRuntime.PanelVisible, null, null));
            AddBinding(m_IsDriving = new ValueBinding<bool>(kGroup, "IsDriving", DirectDriveRuntime.IsDriving, null, null));
            AddBinding(m_StatusText = new ValueBinding<string>(kGroup, "StatusText", DirectDriveRuntime.StatusText, null, null));
            AddBinding(m_PossessedName = new ValueBinding<string>(kGroup, "PossessedName", DirectDriveRuntime.PossessedName, null, null));
            AddBinding(m_ControlStatus = new ValueBinding<string>(kGroup, "ControlStatus", DirectDriveRuntime.ControlStatus, null, null));
            AddBinding(m_SpeedMph = new ValueBinding<float>(kGroup, "SpeedMph", DirectDriveRuntime.SpeedMph, null, null));
            AddBinding(m_Braking = new ValueBinding<bool>(kGroup, "Braking", DirectDriveRuntime.Braking, null, null));
            AddBinding(m_ReverseReady = new ValueBinding<bool>(kGroup, "ReverseReady", DirectDriveRuntime.ReverseReady, null, null));
            AddBinding(m_InputThrottle = new ValueBinding<float>(kGroup, "InputThrottle", DirectDriveRuntime.InputThrottle, null, null));
            AddBinding(m_InputBrake = new ValueBinding<float>(kGroup, "InputBrake", DirectDriveRuntime.InputBrake, null, null));
            AddBinding(m_InputSteering = new ValueBinding<float>(kGroup, "InputSteering", DirectDriveRuntime.InputSteering, null, null));
            AddBinding(m_InputFresh = new ValueBinding<bool>(kGroup, "InputFresh", DirectDriveRuntime.InputFresh, null, null));
            AddBinding(m_InputAgeSeconds = new ValueBinding<float>(kGroup, "InputAgeSeconds", DirectDriveRuntime.InputAgeSeconds, null, null));
            AddBinding(m_RoadIntentAssist = new ValueBinding<bool>(kGroup, "RoadIntentAssist", DirectDriveRuntime.RoadIntentAssist, null, null));
            AddBinding(m_RoadHeightAssist = new ValueBinding<bool>(kGroup, "RoadHeightAssist", DirectDriveRuntime.RoadHeightAssist, null, null));
            AddBinding(m_FreezeVanillaNavigation = new ValueBinding<bool>(kGroup, "FreezeVanillaNavigation", DirectDriveRuntime.FreezeVanillaNavigation, null, null));
            AddBinding(m_ChaseCameraEnabled = new ValueBinding<bool>(kGroup, "ChaseCameraEnabled", DirectDriveRuntime.ChaseCameraEnabled, null, null));
            AddBinding(m_ChaseCameraStatus = new ValueBinding<string>(kGroup, "ChaseCameraStatus", DirectDriveRuntime.ChaseCameraStatus, null, null));
            AddBinding(m_PoliceChaseEnabled = new ValueBinding<bool>(kGroup, "PoliceChaseEnabled", DirectDriveRuntime.PoliceChaseEnabled, null, null));
            AddBinding(m_PoliceChaseActive = new ValueBinding<bool>(kGroup, "PoliceChaseActive", DirectDriveRuntime.PoliceChaseActive, null, null));
            AddBinding(m_PoliceChaseStatus = new ValueBinding<string>(kGroup, "PoliceChaseStatus", DirectDriveRuntime.PoliceChaseStatus, null, null));
            AddBinding(m_PoliceChaseUnits = new ValueBinding<int>(kGroup, "PoliceChaseUnits", DirectDriveRuntime.PoliceChaseUnits, null, null));
            AddBinding(m_RedLightViolations = new ValueBinding<int>(kGroup, "RedLightViolations", DirectDriveRuntime.RedLightViolations, null, null));
            AddBinding(m_TargetSpeedMph = new ValueBinding<float>(kGroup, "TargetSpeedMph", DirectDriveRuntime.TargetSpeedMph, null, null));
            AddBinding(m_ReverseSpeedMph = new ValueBinding<float>(kGroup, "ReverseSpeedMph", DirectDriveRuntime.ReverseSpeedMph, null, null));
            AddBinding(m_AccelerationMps2 = new ValueBinding<float>(kGroup, "AccelerationMps2", DirectDriveRuntime.AccelerationMps2, null, null));
            AddBinding(m_BrakeMps2 = new ValueBinding<float>(kGroup, "BrakeMps2", DirectDriveRuntime.BrakeMps2, null, null));
            AddBinding(m_CoastMps2 = new ValueBinding<float>(kGroup, "CoastMps2", DirectDriveRuntime.CoastMps2, null, null));
            AddBinding(m_ReverseAccelerationMps2 = new ValueBinding<float>(kGroup, "ReverseAccelerationMps2", DirectDriveRuntime.ReverseAccelerationMps2, null, null));
            AddBinding(m_MaxTurnDegPerSecond = new ValueBinding<float>(kGroup, "MaxTurnDegPerSecond", DirectDriveRuntime.MaxTurnDegPerSecond, null, null));
            AddBinding(m_LowSpeedTurnBoost = new ValueBinding<float>(kGroup, "LowSpeedTurnBoost", DirectDriveRuntime.LowSpeedTurnBoost, null, null));
            AddBinding(m_RoadHeightStickiness = new ValueBinding<float>(kGroup, "RoadHeightStickiness", DirectDriveRuntime.RoadHeightStickiness, null, null));
            AddBinding(m_ChaseCameraDistance = new ValueBinding<float>(kGroup, "ChaseCameraDistance", DirectDriveRuntime.ChaseCameraDistance, null, null));
            AddBinding(m_ChaseCameraHeight = new ValueBinding<float>(kGroup, "ChaseCameraHeight", DirectDriveRuntime.ChaseCameraHeight, null, null));
            AddBinding(m_ChaseCameraLookAhead = new ValueBinding<float>(kGroup, "ChaseCameraLookAhead", DirectDriveRuntime.ChaseCameraLookAhead, null, null));

            AddBinding(new TriggerBinding(kGroup, "TogglePanel", new Action(TogglePanel)));
            AddBinding(new TriggerBinding(kGroup, "ToggleHud", new Action(TogglePanel)));
            AddBinding(new TriggerBinding<bool>(kGroup, "SetPanelVisible", new Action<bool>(SetPanelVisible), null));
            AddBinding(new TriggerBinding(kGroup, "ToggleDriving", new Action(DirectDriveRuntime.RequestToggle)));
            AddBinding(new TriggerBinding(kGroup, "Release", new Action(DirectDriveRuntime.RequestRelease)));
            AddBinding(new TriggerBinding(kGroup, "StartPoliceChaseTest", new Action(DirectDriveRuntime.RequestPoliceChaseTest)));
            AddBinding(new TriggerBinding(kGroup, "ResetSettings", new Action(ResetSettings)));
            AddBinding(new TriggerBinding<bool>(kGroup, "SetRoadIntentAssist", new Action<bool>(SetRoadIntentAssist), null));
            AddBinding(new TriggerBinding<bool>(kGroup, "SetRoadHeightAssist", new Action<bool>(SetRoadHeightAssist), null));
            AddBinding(new TriggerBinding<bool>(kGroup, "SetFreezeVanillaNavigation", new Action<bool>(SetFreezeVanillaNavigation), null));
            AddBinding(new TriggerBinding<bool>(kGroup, "SetChaseCameraEnabled", new Action<bool>(SetChaseCameraEnabled), null));
            AddBinding(new TriggerBinding<bool>(kGroup, "SetPoliceChaseEnabled", new Action<bool>(SetPoliceChaseEnabled), null));
            AddBinding(new TriggerBinding<float>(kGroup, "SetTargetSpeedMph", new Action<float>(SetTargetSpeedMph), null));
            AddBinding(new TriggerBinding<float>(kGroup, "SetReverseSpeedMph", new Action<float>(SetReverseSpeedMph), null));
            AddBinding(new TriggerBinding<float>(kGroup, "SetAccelerationMps2", new Action<float>(SetAccelerationMps2), null));
            AddBinding(new TriggerBinding<float>(kGroup, "SetBrakeMps2", new Action<float>(SetBrakeMps2), null));
            AddBinding(new TriggerBinding<float>(kGroup, "SetCoastMps2", new Action<float>(SetCoastMps2), null));
            AddBinding(new TriggerBinding<float>(kGroup, "SetReverseAccelerationMps2", new Action<float>(SetReverseAccelerationMps2), null));
            AddBinding(new TriggerBinding<float>(kGroup, "SetMaxTurnDegPerSecond", new Action<float>(SetMaxTurnDegPerSecond), null));
            AddBinding(new TriggerBinding<float>(kGroup, "SetLowSpeedTurnBoost", new Action<float>(SetLowSpeedTurnBoost), null));
            AddBinding(new TriggerBinding<float>(kGroup, "SetRoadHeightStickiness", new Action<float>(SetRoadHeightStickiness), null));
            AddBinding(new TriggerBinding<float>(kGroup, "SetChaseCameraDistance", new Action<float>(SetChaseCameraDistance), null));
            AddBinding(new TriggerBinding<float>(kGroup, "SetChaseCameraHeight", new Action<float>(SetChaseCameraHeight), null));
            AddBinding(new TriggerBinding<float>(kGroup, "SetChaseCameraLookAhead", new Action<float>(SetChaseCameraLookAhead), null));
        }

        [Preserve]
        protected override void OnUpdate()
        {
            base.OnUpdate();

            if (UnityEngine.Time.unscaledTime < m_NextStatusUpdate)
                return;

            m_NextStatusUpdate = UnityEngine.Time.unscaledTime + 0.08f;
            UpdateBindings();
        }

        private void TogglePanel()
        {
            DirectDriveRuntime.TogglePanel();
            UpdateBindings();
        }

        private void SetPanelVisible(bool visible)
        {
            DirectDriveRuntime.SetPanelVisible(visible);
            UpdateBindings();
        }

        private void ResetSettings()
        {
            DirectDriveRuntime.ResetSettings();
            UpdateBindings();
        }

        private void SetRoadIntentAssist(bool value)
        {
            DirectDriveRuntime.ApplyPublicSafeDefaults();
            UpdateBindings();
        }

        private void SetRoadHeightAssist(bool value)
        {
            DirectDriveRuntime.ApplyPublicSafeDefaults();
            UpdateBindings();
        }

        private void SetFreezeVanillaNavigation(bool value)
        {
            DirectDriveRuntime.ApplyPublicSafeDefaults();
            UpdateBindings();
        }

        private void SetChaseCameraEnabled(bool value)
        {
            DirectDriveRuntime.ApplyPublicSafeDefaults();
            UpdateBindings();
        }

        private void SetPoliceChaseEnabled(bool value)
        {
            DirectDriveRuntime.ApplyPublicSafeDefaults();
            UpdateBindings();
        }

        private void SetTargetSpeedMph(float value)
        {
            DirectDriveRuntime.TargetSpeedMph = AcceptFinite(value, DirectDriveRuntime.TargetSpeedMph);
            UpdateBindings();
        }

        private void SetReverseSpeedMph(float value)
        {
            DirectDriveRuntime.ReverseSpeedMph = AcceptFinite(value, DirectDriveRuntime.ReverseSpeedMph);
            UpdateBindings();
        }

        private void SetAccelerationMps2(float value)
        {
            DirectDriveRuntime.AccelerationMps2 = AcceptFinite(value, DirectDriveRuntime.AccelerationMps2);
            UpdateBindings();
        }

        private void SetBrakeMps2(float value)
        {
            DirectDriveRuntime.BrakeMps2 = AcceptFinite(value, DirectDriveRuntime.BrakeMps2);
            UpdateBindings();
        }

        private void SetCoastMps2(float value)
        {
            DirectDriveRuntime.CoastMps2 = AcceptFinite(value, DirectDriveRuntime.CoastMps2);
            UpdateBindings();
        }

        private void SetReverseAccelerationMps2(float value)
        {
            DirectDriveRuntime.ReverseAccelerationMps2 = AcceptFinite(value, DirectDriveRuntime.ReverseAccelerationMps2);
            UpdateBindings();
        }

        private void SetMaxTurnDegPerSecond(float value)
        {
            DirectDriveRuntime.MaxTurnDegPerSecond = AcceptFinite(value, DirectDriveRuntime.MaxTurnDegPerSecond);
            UpdateBindings();
        }

        private void SetLowSpeedTurnBoost(float value)
        {
            DirectDriveRuntime.LowSpeedTurnBoost = AcceptFinite(value, DirectDriveRuntime.LowSpeedTurnBoost);
            UpdateBindings();
        }

        private void SetRoadHeightStickiness(float value)
        {
            DirectDriveRuntime.RoadHeightStickiness = AcceptFinite(value, DirectDriveRuntime.RoadHeightStickiness);
            UpdateBindings();
        }

        private void SetChaseCameraDistance(float value)
        {
            DirectDriveRuntime.ChaseCameraDistance = AcceptFinite(value, DirectDriveRuntime.ChaseCameraDistance);
            UpdateBindings();
        }

        private void SetChaseCameraHeight(float value)
        {
            DirectDriveRuntime.ChaseCameraHeight = AcceptFinite(value, DirectDriveRuntime.ChaseCameraHeight);
            UpdateBindings();
        }

        private void SetChaseCameraLookAhead(float value)
        {
            DirectDriveRuntime.ChaseCameraLookAhead = AcceptFinite(value, DirectDriveRuntime.ChaseCameraLookAhead);
            UpdateBindings();
        }

        private static float AcceptFinite(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return fallback;

            return value;
        }

        private void UpdateBindings()
        {
            m_PanelVisible.Update(DirectDriveRuntime.PanelVisible);
            m_IsDriving.Update(DirectDriveRuntime.IsDriving);
            m_StatusText.Update(DirectDriveRuntime.StatusText ?? "");
            m_PossessedName.Update(DirectDriveRuntime.PossessedName ?? "");
            m_ControlStatus.Update(DirectDriveRuntime.ControlStatus ?? "");
            m_SpeedMph.Update(DirectDriveRuntime.SpeedMph);
            m_Braking.Update(DirectDriveRuntime.Braking);
            m_ReverseReady.Update(DirectDriveRuntime.ReverseReady);
            m_InputThrottle.Update(DirectDriveRuntime.InputThrottle);
            m_InputBrake.Update(DirectDriveRuntime.InputBrake);
            m_InputSteering.Update(DirectDriveRuntime.InputSteering);
            m_InputFresh.Update(DirectDriveRuntime.InputFresh);
            m_InputAgeSeconds.Update(DirectDriveRuntime.InputAgeSeconds);
            m_RoadIntentAssist.Update(DirectDriveRuntime.RoadIntentAssist);
            m_RoadHeightAssist.Update(DirectDriveRuntime.RoadHeightAssist);
            m_FreezeVanillaNavigation.Update(DirectDriveRuntime.FreezeVanillaNavigation);
            m_ChaseCameraEnabled.Update(DirectDriveRuntime.ChaseCameraEnabled);
            m_ChaseCameraStatus.Update(DirectDriveRuntime.ChaseCameraStatus ?? "");
            m_PoliceChaseEnabled.Update(DirectDriveRuntime.PoliceChaseEnabled);
            m_PoliceChaseActive.Update(DirectDriveRuntime.PoliceChaseActive);
            m_PoliceChaseStatus.Update(DirectDriveRuntime.PoliceChaseStatus ?? "");
            m_PoliceChaseUnits.Update(DirectDriveRuntime.PoliceChaseUnits);
            m_RedLightViolations.Update(DirectDriveRuntime.RedLightViolations);
            m_TargetSpeedMph.Update(DirectDriveRuntime.TargetSpeedMph);
            m_ReverseSpeedMph.Update(DirectDriveRuntime.ReverseSpeedMph);
            m_AccelerationMps2.Update(DirectDriveRuntime.AccelerationMps2);
            m_BrakeMps2.Update(DirectDriveRuntime.BrakeMps2);
            m_CoastMps2.Update(DirectDriveRuntime.CoastMps2);
            m_ReverseAccelerationMps2.Update(DirectDriveRuntime.ReverseAccelerationMps2);
            m_MaxTurnDegPerSecond.Update(DirectDriveRuntime.MaxTurnDegPerSecond);
            m_LowSpeedTurnBoost.Update(DirectDriveRuntime.LowSpeedTurnBoost);
            m_RoadHeightStickiness.Update(DirectDriveRuntime.RoadHeightStickiness);
            m_ChaseCameraDistance.Update(DirectDriveRuntime.ChaseCameraDistance);
            m_ChaseCameraHeight.Update(DirectDriveRuntime.ChaseCameraHeight);
            m_ChaseCameraLookAhead.Update(DirectDriveRuntime.ChaseCameraLookAhead);
        }
    }
}
