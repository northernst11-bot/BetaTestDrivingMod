# Beta Test Driving Mod (Stable)

`0.3.27-stable`

Source / MIT license: https://github.com/northernst11-bot/BetaTestDrivingMod

## Open Source License

This project is open source under the MIT License.

Everyone may use, copy, modify, merge, publish, distribute, sublicense, sell, fork, and build on this mod's source code and original project assets. That includes making variants, replacement versions, and better versions of the mod. See [LICENSE](LICENSE).

Please keep the MIT copyright and license notice with substantial copies. The MIT license does not grant rights to Cities: Skylines II, Paradox or Colossal Order trademarks, game assets, or third-party content not owned by this project.

This stable public build keeps the tested Direct Drive control system, keeps crashguard safety around the riskiest control/camera paths, keeps the tested GTA-style attached chase camera, removes the hard tuning value limits from the panel and C# setting setters, and hides advanced assist/chase controls from the public UI.

Police chase note: the experimental chase controls are hidden and disabled in this public build while they keep being tested locally.

It does not spawn a fake player car. It takes over a real live vehicle already created by Cities: Skylines II, freezes the possessed car's vanilla physical path movement, then applies direct player movement after the normal car move step. Road intent assist now queues AI left/right turn intent and path connections, but it does not steer the physical body by itself.

## Controls

- `V`: possess or release a live vehicle
- Arrow keys: recommended driving controls
- `Up` / `W`: accelerate
- `Down` / `S`: brake first, reverse only after a second press at stop
- `Left` / `A`: steer left and request AI left-turn intent when road assist is enabled
- `Right` / `D`: steer right and request AI right-turn intent when road assist is enabled
- `F8`: hide or show the tuning panel
- `F7`: toggle the attached chase camera

Use the arrow keys if WASD moves your game camera or screen.

## Current Build

- Keeps vehicle collision enabled in the stable build.
- Restores the `0.3.24-traffic-presence-crashguard-local` transform-frame animation path for the stable publish.
- Tightens lane-object traffic presence to close, direction-matching road lanes so traffic does not react to a stale far-away marker.
- Uses cached vehicle collision checks, but no longer pushes or rewrites other live traffic vehicles on impact.
- Uses current vehicle transforms for collision checks instead of reading interpolation frame buffers from other traffic.
- Reuses recent road-turn intent resolution while the same steering input is held, reducing repeated lane-connection scans at intersections.
- Keeps fast sustained-contact vehicle collision for the possessed car without rewriting hit cars.
- Keeps collision tuning for collision on/off and retained speed after impact. Impact-push tuning is hidden in this stability build.
- Building collision and crash fire effects are not added in this build.
- Uses the responsive frame-buffered input path from the tested Direct Drive build.
- Adds a top-left game UI panel for drive status, possession, and tuning sliders.
- Applies typed tuning values only after Enter or leaving the number field, so multi-digit values can be typed normally.
- Allows manual tuning values outside the old speed, steering, response, road attach, and camera ranges.
- Removes unsupported panel layout styles from the UI so the menu keeps the same look without spamming invalid display warnings.
- Hides advanced road intent, road attach, AI body path, chase camera, and police chase toggles from the public panel.
- Forces hidden advanced controls back to safe defaults so old changed values do not keep running in the background.
- Adds a toggleable behind-car chase camera that takes over after the game's normal focus handoff and camera update.
- Adds camera distance, height, and look-ahead tuning sliders.
- Adds safety guards around direct control, path-freeze, and chase camera restore.
- Cleans the possessed car out of the last synced lane-object buffers when releasing so traffic is less likely to remember an old player-car location.
- Applies direct physical control after `CarMoveSystem` so keyboard movement is not fighting the vanilla path driver.
- Freezes vanilla physical path driving for the possessed car before `CarMoveSystem`.
- Keeps the car attached to nearby road lanes with road-height assist and the tested no-hover offset behavior.
- Syncs lane object presence so nearby traffic sees the possessed car at its current lane position.
- Uses nearby road-lane search instead of an expensive whole-city scan.
- Queues AI turn intent from steering input without forcing the physical car body to turn left or right through pathfinding.
- Keeps a compact F8 tuning panel for speed, launch, brake, reverse, steering, road-height stickiness, and camera feel.

This is still a beta, but this public update is based on the version that fixed the input delay and road attachment problems during testing.
