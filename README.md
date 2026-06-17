# Beta Test Driving Mod (Stable)

`0.3.54-stable`

Source / MIT license: https://github.com/northernst11-bot/BetaTestDrivingMod

## Open Source License

This project is open source under the MIT License.

Everyone may use, copy, modify, merge, publish, distribute, sublicense, sell, fork, and build on this mod's source code and original project assets. That includes making variants, replacement versions, and better versions of the mod. See [LICENSE](LICENSE).

Please keep the MIT copyright and license notice with substantial copies. The MIT license does not grant rights to Cities: Skylines II, Paradox or Colossal Order trademarks, game assets, or third-party content not owned by this project.

This stable public build keeps the tested Direct Drive control system, keeps crashguard safety around the riskiest control/camera paths, keeps the tested GTA-style attached chase camera, removes the hard tuning value limits from the panel and C# setting setters, and hides advanced assist/chase controls from the public UI.

Police chase note: the experimental chase controls are hidden and disabled in this public build while they keep being tested locally.

It does not spawn a fake player car. It takes over a real live vehicle already created by Cities: Skylines II, freezes the possessed car's vanilla physical path movement, then applies direct player movement after the normal car move step. Road intent assist now queues AI left/right turn intent and path connections, but it does not steer the physical body by itself.

## Fixed in 0.3.54

- Smooths direct driving through short FPS drops by using capped real-time catch-up for the possessed car's control step.
- Keeps the attached chase camera following the smoothed driving pose so hitches feel less noticeable from behind the car.
- Preserves the stable vehicle animation path after testing a more aggressive render-frame smoother that could make vehicle animations repeat.
- Reduces repeated traffic-presence cleanup work while keeping the green AI presence and stale-lane cleanup behavior.
- Refreshes nearby vehicle collision candidates a little less aggressively to reduce per-frame query churn without disabling vehicle collision.
- Skips unnecessary info-panel selection cleanup work when there is no active vanilla selection marker.
- Adds new gallery screenshots showing the driving panel, chase camera, elevated roads, and keybind tuning view.

## Fixed in 0.3.53

- Parks the possessed car's old blue vanilla route while driving so it does not stretch down the road and leave traffic reacting to a stale path.
- Rebuilds vanilla pathfinding from the car's current lane and position when you release control.
- Keeps the green AI presence active on the controlled car while leaving the optional blue extra-lane halo off by default.
- Cleans stale lane markers and duplicate lane-object entries from lanes the controlled car already left.
- Shortens AI presence near the car and removes direct traffic-guard braking so cars should not stop too far away or on unrelated lanes.
- Adds configurable keybinds plus Numpad 0 collision and Numpad 9 AI-presence debug overlays.
- Keeps the recent elevated-road pitch, low-FPS sync, focus-icon cleanup, and hitbox alignment fixes.

## Controls

- `V`: possess or release a live vehicle
- Arrow keys: recommended driving controls
- `Up` / `W`: accelerate
- `Down` / `S`: brake first, reverse only after a second press at stop
- `Left` / `A`: steer left and request AI left-turn intent when road assist is enabled
- `Right` / `D`: steer right and request AI right-turn intent when road assist is enabled
- `F8`: hide or show the tuning panel
- `F7`: toggle the attached chase camera
- `Numpad 0`: show or hide the collision-hitbox debug overlay
- `Numpad 9`: show or hide the AI traffic-presence debug overlay

Use the arrow keys if WASD moves your game camera or screen.
Open the tuning panel's Keybinds section to change the primary keys. Arrow keys remain available as driving fallbacks.

## Current Build

- Keeps vehicle collision enabled in the stable build.
- Smooths low-FPS direct driving with capped real-time catch-up while keeping normal vehicle animation frames intact.
- Keeps the chase camera attached to the smoothed driving pose so short hitches are less obvious while driving.
- Reduces repeated traffic-presence and collision-candidate cleanup work without removing traffic presence or collision features.
- Parks the possessed car's vanilla route buffer while you are driving so the old blue route does not stretch far down the road.
- Keeps the live vanilla navigation target only just ahead of the controlled car instead of letting it chase the old path.
- Clears the parked route state on release so vanilla traffic rebuilds a fresh path from the exact place you let go.
- Adds a Road-only AI presence setting that clears the AI blocker and halo when the driven car is off the road.
- Keeps the green primary AI presence active on the closest valid lane so traffic sees the controlled car.
- Purges the possessed car from every lane-buffer marker touched during the drive so lanes you left should not keep an invisible queue blocker.
- Periodically scans nearby same-height road lanes to remove older stale green markers the touched-lane list did not create.
- Removes all duplicate green lane-object entries for the possessed car when cleaning a lane.
- Leaves the blue extra lane halo off by default; it remains an optional debug/tuning switch.
- If enabled manually, blue halo lanes are limited to the same road owner, same carriageway group, and same road height.
- Removes the direct traffic-guard braking system from the public update order; traffic should react through lane presence instead of the mod forcing nearby cars to stop.
- Moves AI traffic presence back to one writer so the mod does not update the same lane buffers from two systems.
- Refreshes the under-car road/lane lookup more often so pathfinding presence follows the controlled car more closely while moving.
- Rebuilds the vanilla path handoff from the car's current release position so a released vehicle does not try to return to its old route anchor.
- Adds a Keybinds section in the panel for possess/release, driving, panel, chase camera, collision debug, and AI-presence debug controls.
- Keeps the green primary marker on the closest valid lane and does not draw the blue halo unless that optional switch is enabled.
- Tightens AI lane presence to a shorter near-car span with no speed-based lead, so traffic should not slow down as far ahead.
- Shrinks the visible AI lane-presence marker closer to vehicle size instead of drawing a long lane reservation.
- Adds a clickable AI traffic-presence debug overlay that draws the live primary lane marker traffic should react to.
- Adds Numpad 9 as a quick toggle for the AI traffic-presence overlay.
- Aligns the driven car's pitch to the nearby road grade on elevated up/down roads so the body and frame velocity follow the slope.
- Stores the actual per-tick movement velocity after road-height correction, reducing visual frame mismatch while climbing or descending.
- Clears old extra-lane reservations so adjacent lanes are not blocked by stale blue halo markers.
- Extends same-lane front/rear presence so cars behind the driven car get a clearer blocker while it is moving.
- Makes low-FPS transform-frame resync require actual frame drift, reducing occasional visual snap-back during performance drops.
- Backs off the too-aggressive low-FPS transform-frame resync so the visible car is less likely to lag back and forth during performance drops.
- Loosens traffic visibility from strict road attachment so the car can be seen even when it is angled across lane centers.
- Removes the lane-presence stabilization delay so a new under-car lane can be marked immediately.
- Keeps the previous lane marked briefly during movement so traffic does not lose you while the controlled car crosses lane boundaries.
- Keeps low-FPS transform-frame resync, but with the smoother threshold from the previous better-feeling build.
- Removes the duplicate pre-move traffic-presence refresher so lane-object updates happen from the direct-control path only.
- Resyncs the controlled car's transform frames during FPS drops or large render-frame drift to reduce low-performance visual glitching.
- Adds a clickable collision-hitbox overlay setting, with Numpad 0 as the quick toggle.
- Skips vehicle collision targets on different road heights so cars below/above bridges do not create invisible barriers.
- Uses a clearer ON/OFF pill switch for clickable panel toggles.
- Keeps the traffic-presence lane marker straddling the controlled car and refreshes it more often so AI cars see your occupied spot sooner.
- Hides the vanilla focus/info-panel marker while a vehicle is directly controlled.
- Restores the last stable possessed-car animation path so controlled vehicles do not bounce vertically.
- Leaves `TransformFrame` state/activity alone during live driving so brake, reverse, and turn-light flags work normally again.
- Keeps moving-traffic collision hitboxes aligned with the visible vehicle pose.
- Caches nearby road-lane pose lookups and skips nearby searches when the current lane pose is already close and valid.
- Restores the `0.3.24-traffic-presence-crashguard-local` transform-frame animation path for the stable publish.
- Keeps lane-object traffic presence close to the driven car while allowing nearby same-height lanes to see it during swerves.
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
