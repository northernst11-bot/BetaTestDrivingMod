using Colossal.Logging;
using Game;
using Game.Modding;
using Game.Rendering;
using Game.SceneFlow;
using Game.Simulation;

namespace BetaTestDrivingMod
{
    public sealed class Mod : IMod
    {
        public const string kModName = "BetaTestDrivingMod";
        public const string kVersion = "0.3.14-source-link-public";

        public static readonly ILog log = LogManager.GetLogger($"{kModName}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info($"{kModName} {kVersion} loading");

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            DirectDriveRuntime.EnsureHud();
            updateSystem.UpdateAt<DirectDriveUiSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<DirectDriveInputSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAfter<DirectDriveFreezeSystem, CarNavigationSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<DirectDriveFreezeSystem, CarMoveSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<DirectDriveControlSystem, CarMoveSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<DirectDrivePoliceChaseSystem, DirectDriveControlSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<DirectDrivePoliceChaseSystem, PoliceCarAISystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<DirectDriveCameraSystem, CameraUpdateSystem>(SystemUpdatePhase.PreCulling);
            log.Info("Beta Test Driving Mod systems registered: direct physical vehicle control, frame-buffered input, road-height attach, AI road-turn intent, police chase retargeting, PreCulling chase camera, and COUI driving panel.");
        }

        public void OnDispose()
        {
            log.Info($"{kModName} disposing");
            DirectDriveRuntime.Reset();
        }
    }
}
