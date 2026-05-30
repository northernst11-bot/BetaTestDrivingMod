# Beta Test Driving Mod

`0.3.14-source-link-public`

Source transparency: https://github.com/northernst11-bot/BetaTestDrivingMod

## Source License

This repository is source-available for transparency only. It is not open source.

You may view and inspect the code, but you may not clone, download, copy, reuse, modify, compile, redistribute, reupload, sell, fork, or publish this code or a derivative mod without written permission. See [LICENSE](LICENSE).

This source release intentionally does not include compiled binaries, build output, local cache files, publish packages, or support for republishing. It is provided so players can inspect what the mod does, not so the mod can be copied or rebuilt.

This public build keeps the tested Direct Drive control system, adds crashguard safety around the riskiest control/chase/camera paths, improves police chase retargeting for assigned units, keeps the tested GTA-style attached chase camera, and removes the hard tuning value limits from the panel and C# setting setters.

Police chase disclaimer: the chase feature is still in testing. It uses Cities: Skylines II police patrol/pathfinding systems, but dispatch may fail, arrive late, or behave weirdly depending on police coverage, nearby units, traffic, and the game's own patrol AI.

It does not spawn a fake player car. It takes over a real live vehicle already created by Cities: Skylines II, freezes the possessed car's vanilla physical path movement, then applies direct player movement after the normal car move step. Road intent assist now queues AI left/right turn intent and path connections, but it does not steer the physical body by itself.

## Controls

- `V`: possess or release a live vehicle
- Arrow keys: recommended driving controls
- `Up` / `W`: accelerate
- `Down` / `S`: brake first, reverse only after a second press at stop
- `Left` / `A`: steer left and request AI left-turn intent when road assist is enabled
- `Right` / `D`: steer right and request AI right-turn intent when road assist is enabled
- `F8`: hide or show the tuning panel
- `F9`: start a manual police chase test while possessing a vehicle
- `F7`: toggle the attached chase camera

Use the arrow keys if WASD moves your game camera or screen.

## Current Build

- Uses the responsive frame-buffered input path from the tested Direct Drive build.
- Adds a top-left game UI panel for drive status, possession, assist toggles, and tuning sliders.
- Applies typed tuning values only after Enter or leaving the number field, so multi-digit values can be typed normally.
- Allows manual tuning values outside the old speed, steering, response, road attach, and camera ranges.
- Removes unsupported panel layout styles from the UI so the menu keeps the same look without spamming invalid display warnings.
- Adds an optional police chase testing feature that can start after the possessed car runs a red light without stopping.
- Leaves the police chase toggle off by default because the chase behavior is still experimental.
- Uses the game's police patrol request/pathfinding flow and nudges nearby police cars toward the possessed vehicle.
- Refreshes already-assigned police units so they keep targeting the moving possessed car instead of only the original chase location.
- Forces assigned police units into emergency/warning light effects during the active chase.
- Adds a toggleable behind-car chase camera that takes over after the game's normal focus handoff and camera update.
- Adds camera distance, height, and look-ahead tuning sliders.
- Adds safety guards around direct control, path-freeze, chase camera restore, and experimental police chase dispatch.
- Cleans the possessed car out of the last synced lane-object buffers when releasing so traffic is less likely to remember an old player-car location.
- Applies direct physical control after `CarMoveSystem` so keyboard movement is not fighting the vanilla path driver.
- Freezes vanilla physical path driving for the possessed car before `CarMoveSystem`.
- Keeps the car attached to nearby road lanes with road-height assist and the tested no-hover offset behavior.
- Syncs lane object presence so nearby traffic sees the possessed car at its current lane position.
- Uses nearby road-lane search instead of an expensive whole-city scan.
- Queues AI turn intent from steering input without forcing the physical car body to turn left or right through pathfinding.
- Keeps a compact F8 tuning panel for speed, launch, brake, reverse, steering, road-height stickiness, road intent assist, and the vanilla path freeze.

This is still a beta, but this public update is based on the version that fixed the input delay and road attachment problems during testing.
