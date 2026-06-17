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
        public const string kVersion = "0.3.53-stable";

        public static readonly ILog log = LogManager.GetLogger($"{kModName}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info($"{kModName} {kVersion} loading");

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            DirectDriveRuntime.EnsureHud();
            DirectDriveRuntime.ApplyPublicSafeDefaults();
            updateSystem.UpdateAt<DirectDriveUiSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<DirectDriveInputSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAfter<DirectDriveFreezeSystem, CarNavigationSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<DirectDriveFreezeSystem, CarMoveSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<DirectDriveControlSystem, CarMoveSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<DirectDriveCameraSystem, CameraUpdateSystem>(SystemUpdatePhase.PreCulling);
            log.Info("Beta Test Driving Mod systems registered: direct physical vehicle control, single-writer traffic presence, frame-buffered input, safe road attach defaults, PreCulling chase camera, and simplified public COUI driving panel. Experimental police chase and direct traffic guard systems are not scheduled in the public crashguard build.");
        }

        public void OnDispose()
        {
            log.Info($"{kModName} disposing");
            DirectDriveRuntime.Reset();
        }
    }
}
