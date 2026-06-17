# Changelog

## Unreleased

- Parks the possessed car's vanilla route buffer while driving so the old blue route line cannot stretch far down the road.
- Keeps the live vanilla navigation target just ahead of the controlled car instead of letting it chase the old path.
- Clears the parked path state on release so vanilla traffic repaths from the car's current release lane and position.
- Adds a Road-only AI presence setting that clears the AI blocker and halo when the controlled car is off the road.
- Keeps the green primary AI presence active on the closest valid lane so traffic sees the controlled car.
- Tracks every lane-buffer marker touched during a drive and purges stale green presence from lanes the controlled car left.
- Periodically scans nearby same-height road lanes and clears older stale green markers for the controlled car.
- Removes all duplicate lane-object entries for the possessed car during cleanup, preventing old lanes from keeping an invisible queue blocker.
- Leaves the blue extra lane halo off by default; it remains an optional debug/tuning switch.
- Restricts blue halo lanes to the same road owner, same carriageway group, and same road height if enabled manually.
- Removes the direct traffic-guard braking system from the public update order; random nearby cars should no longer be stopped by mod-forced velocity edits.
- Moves AI traffic presence back to a single lane-buffer writer to reduce crash risk and stale lane reservations.
- Refreshes the under-car road/lane lookup more often so the marker tracks the moving controlled car more closely.
- Rebuilds the vanilla path handoff from the car's current release position so released vehicles do not try to return to their old route anchor.
- Adds configurable keybinds in the panel for possess/release, driving, panel, chase camera, collision debug, and AI traffic-presence debug controls.
- Keeps the green primary-lane marker active around the controlled car and only draws the blue halo if enabled manually.
- Tightens AI lane presence to a shorter near-car span with no speed-based lead, reducing far-ahead traffic slowdowns.
- Shrinks the visible AI lane-presence marker closer to vehicle size instead of drawing a long lane reservation.
- Adds a clickable AI traffic-presence debug overlay with Numpad 9 quick toggle, drawing the live primary lane and physical-overlap halo markers traffic should react to.
- Aligns the driven car's pitch to the nearby road grade on elevated up/down roads.
- Stores the actual per-tick movement velocity after road-height correction so transform frames follow slope movement instead of predicting flat.
- Clears old extra-lane reservations so adjacent lanes are not blocked by stale blue halo markers.
- Extends same-lane traffic presence forward/rearward so moving AI has a clearer blocker on the driven car's actual lane.
- Makes low-FPS transform-frame resync require real frame drift, reducing occasional snap-back during performance drops.
- Backs off the too-aggressive `0.3.36` transform-frame resync thresholds so low-FPS correction does not fight the smoother direct-control path.
- Loosens traffic presence from strict lane-center attachment so visibility does not disappear when the controlled car is angled across lanes.
- Removes the extra traffic-presence stabilization delay so newly detected under-car lanes can be marked immediately.
- Keeps the previous lane marked briefly during movement so AI traffic does not lose the controlled car during lane transitions.
- Removes the duplicate pre-move traffic-presence refresher so traffic presence is written only from the direct-control path.
- Resyncs controlled-car transform frames during FPS drops or large render-frame drift to reduce low-performance visual glitching.
- Skips collision candidates on different road heights so overpass/underpass vehicles cannot create invisible barriers.
- Replaces the panel checkbox rendering with clearer clickable ON/OFF pill toggles.
- Syncs the traffic-presence lane marker around the controlled car more often so AI traffic is less likely to pass through it.
- Adds a Numpad 0 collision-hitbox debug overlay for the driven car and nearest target vehicle.
- Adds a clickable panel setting for showing or hiding the collision hitbox overlay.
- Restores the last stable possessed-car transform-frame path after the render-state setup caused vertical bouncing.
- Stops forcing renderer update components and `TransformFrame` state/activity during direct driving so vehicle lights can follow the normal flags again.
- Restores normal CS2 transform-frame prediction timing after the capped prediction test still shook for local driving.
- Restores the smooth two-frame possessed-car `TransformFrame` update path after the anchored-frame test caused visible shaking.
- Hides the vanilla info-panel selection marker continuously while direct control is active.
- Uses valid moving-traffic interpolation frames for vehicle collision checks so hitboxes track the visible cars instead of leading them.
- Skips repeated traffic-presence cleanup when there is no synced lane marker to clear.
- Skips nearby road-lane tree searches when the current lane pose is already close and valid.
- Fixes the vanilla focus/selection marker sticking above a vehicle after taking control.
- Clears the possessed vehicle from game selection and camera follow state on release.
- Caches nearby road-lane pose lookup for a few simulation frames and skips road pose work when road assists are disabled.
- Re-licenses the repository under the MIT License so everyone can fork, modify, build, publish, and make improved variants of the mod.
- Updates the GitHub README, notice, local mod metadata, and Paradox publish description to remove the old inspection-only restriction.
- Renames the public listing and in-game labels to `Beta Test Driving Mod (Stable)`.
- Stops scheduling the disabled experimental police chase system in the public build.
- Restores the published possessed-car `TransformFrame` update path so camera and driving animation feel match the public build.
- Adds broad finite/range sanity guards for non-collision driving and chase-camera tuning values.
- Disables live lane-object buffer syncing in the local crashguard build while keeping dedicated vehicle collision enabled.
- Keeps a live game camera controller active while the custom chase camera applies its pose.
- Stops writing live road lane state when the possessed vehicle enters a road; road lookup is used only for height assist.
- Uses fixed crashguard hitboxes for vehicle collision during live driving instead of reading prefab mesh/geometry bounds in the collision loop.
- Restores stable lane-object traffic presence so other cars can see the possessed vehicle again without re-enabling the crashy `CarCurrentLane` write.

## 0.3.27-stable

- Publishes the `0.3.24-traffic-presence-crashguard-local` transform-frame animation behavior after local anchored/capped animation tests caused visible jitter.
- Keeps traffic presence limited to close, direction-matching road lanes so AI cars do not react to stale far-away lane markers.
- Keeps road-entry `CarCurrentLane` writes disabled for the crash fix.

## 0.3.27-0324-base-traffic-tightening-local

- Restores the `0.3.24-traffic-presence-crashguard-local` transform-frame animation behavior after the anchored/capped animation tests caused visible back-and-forth jitter.
- Keeps the traffic-presence lane gate tight so AI cars do not react to a stale far-away lane marker.
- Keeps road-entry `CarCurrentLane` writes disabled.

## 0.3.26-smooth-animation-local

- Restores CS2's expected two-frame possessed-car interpolation path so the car does not jitter back and forth.
- Caps render-frame prediction to a short two-tick window so the visible car stays close to the live transform and collision point.
- Keeps the tightened traffic-presence lane gate from `0.3.25`.

## 0.3.25-anchored-collision-local

- Anchors every possessed-car `TransformFrame` entry to the current live transform so the rendered car and collision hitbox do not drift apart while moving fast.
- Clears traffic lane-object presence whenever the nearest road lane is too far away or points away from the driven car.
- Uses a short forward traffic-presence lookahead instead of a same-point lane marker, so AI traffic reacts closer to the actual car.

## 0.3.24-traffic-presence-crashguard-local

- Restores traffic visibility by adding the possessed car back to road lane-object presence after the nearest road lane is stable for several frames.
- Keeps the road-entry crash fix by not writing `CarCurrentLane` road state back onto the possessed vehicle.
- Throttles lane-object presence updates and cleans the lane marker on release.

## 0.3.24-stable

- Publishes the crash-fixed traffic-presence build as `Beta Test Driving Mod (Stable)`.
- Keeps vehicle collision enabled with fixed crashguard hitboxes.
- Keeps road-entry lane-state writes disabled while restoring stable lane-object presence so traffic can see the driven car.

## 0.3.23-road-entry-crashguard-local

- Keeps vehicle collision enabled.
- Stops writing `CarCurrentLane` road state when the driven vehicle gets onto a road, matching the latest crash repro.
- Keeps road height assist by reading the nearest road pose without attaching the vehicle to that lane.
- Switches collision to fixed crashguard hitboxes so the live collision loop no longer reads prefab mesh/geometry bounds.

## 0.3.22-camera-controller-crashguard-local

- Keeps vehicle collision enabled.
- Avoids setting `CameraUpdateSystem.activeCameraController` to null during chase camera control.
- Adds one-time camera breadcrumbs before and after the first custom camera pose so native camera crashes can be isolated.
- Keeps shared lane-object buffer writes disabled from `0.3.21`.

## 0.3.21-lane-buffer-crashguard-local

- Keeps vehicle collision enabled instead of filtering out heavy vehicles.
- Stops writing shared road lane-object buffers during possession to avoid native ECS crashes after taking over traffic vehicles.
- Keeps the published possessed-car transform-frame update path, chase camera behavior, and cached collision candidate scans.

## 0.3.18-crashfix-local

- Restores the local test cache from the published `0.3.17-collision-tuning-public` package before applying local crashfix changes.
- Caches nearby vehicle collision candidates so dense traffic is not scanned and allocated every driving tick.
- Reuses recent road-turn intent resolution while the same steering input is held, reducing repeated lane-connection scans.
- Keeps the published collision tuning behavior intact while reducing per-frame allocation pressure and traffic-query work.

## 0.3.19-stability-local

- Keeps vehicle collision enabled by default while removing the riskiest collision-side writes.
- Stops pushing other live traffic vehicles or rewriting their transform frames after impacts.
- Uses current traffic transforms for collision checks instead of interpolation-frame reads.
- Clears queued impact state whenever collision is disabled.

## 0.3.20-collision-crashguard-local

- Keeps vehicle collision enabled instead of filtering out heavy vehicles.
- Restores the published possessed-car transform-frame update path for camera and driving feel.
- Keeps cached collision candidate scans to reduce repeated per-frame traffic allocations.
- Keeps collision checks from reading other traffic interpolation buffers or writing push transforms into hit vehicles.

## 0.3.17-collision-tuning-public

- Ports the CS2 GTA Edition vehicle collision layer into Beta Test Driving Mod.
- Adds moving-target hitbox alignment, fast sustained-contact shape collision, and smooth speed-based impact push for hit cars.
- Adds tuning panel controls for collision on/off, impact push, push duration, damping, and retained speed after impact.
- Keeps hitbox size fixed at the tested default and does not expose it as a tuning control.
- Leaves existing Beta Test Driving Mod speed, steering, road assist, camera, tuning, and possession mechanics unchanged.
- Does not add building collision or crash fire effects.

## 0.3.16-public-ui-safe

- Hides advanced road intent, road attach, AI body path, chase camera, and police chase toggles from the public driving panel.
- Forces hidden advanced controls back to safe defaults so old changed values do not keep running in the background.
- Keeps the top driving controls and the tuning tab available.

## 0.3.15-cache-refresh-public

- Reuploads the same source-link build as a fresh Paradox package after a local download checksum failure was seen for the previous backend version.
- Keeps the source transparency GitHub link and source notice used by that release in the mod description.
- Keeps the same tested crashguard build from `0.3.13-crashguard-public`.

## 0.3.14-source-link-public

- Publishes the source transparency link in the Paradox mod description.
- Adds the source availability note used by that release.
- Keeps the same tested crashguard build from `0.3.13-crashguard-public`.

## 0.3.13-crashguard-public

- Publishes the tested crashguard build.
- Adds top-level safety guards around possession/control, path-freeze, chase camera restore, and the experimental police chase system.
- Cleans the possessed car out of the last synced lane-object buffers when releasing so traffic is less likely to remember an old player-car location.
- Disables only the police chase feature if its experimental patrol/dispatch path throws, keeping normal driving, road attach, tuning, and camera behavior intact.

## 0.3.11-tuning-unlocked-public

- Publishes the tested tuning unlock update.
- Removes COUI tuning field min/max clamps so speed, steering, response, road attach, and camera values can be typed outside the old ranges.
- Removes C# setter clamps for tuning values while still rejecting `NaN` and infinity.
- Removes the old runtime caps on low-speed turn boost, road attach strength, and chase camera distance/height/look-ahead.

## 0.3.10-tuning-input-local

- Local test only; not published.
- Changes tuning number fields so typed values are kept locally while editing and only apply on Enter or when the field loses focus.
- Prevents invalid pasted values from turning runtime tuning settings into `NaN` or infinity.

## 0.3.9-chase-camera-public

- Publishes the tested attached chase camera update.
- Moves the chase camera system to run after the game's `CameraUpdateSystem` in `PreCulling`, so the game camera does not overwrite the attached view before the frame renders.
- Adds camera activation logging and a clearer panel status when the chase camera takes over.
- Adds F7 plus panel controls for chase camera enable, distance, height, and look-ahead tuning.

## 0.3.8-chase-camera-local

- Local test only; not published.
- Adds a toggleable GTA-style attached chase camera behind the possessed car.
- Uses the game's orbit/focus handoff as the camera activation sample, then applies a Direct Drive camera position after the normal camera update.
- Adds F7 plus panel controls for chase camera enable, distance, height, and look-ahead tuning.

## 0.3.7-chase-retarget-public

- Publishes the police chase retarget test after local validation.
- Reasserts chase target/path refresh on already-assigned police units so they do not stop at the old player location.
- Forces assigned chase units into emergency/warning light effects while the chase is active.
- Runs the chase refresh after vanilla police AI so the mod can override vanilla patrol cleanup during a pursuit.

## 0.3.6-chase-test-public

- Publishes the crashguard UI build that removes risky COUI display styles from the top-left driving panel.
- Adds an optional manual police chase start path from the driving panel/F9 so dispatch can be tested without depending on traffic-light detection.
- Improves red-light watch diagnostics and also checks the vehicle change lane for signal data.
- Police chase is still in testing and may not always dispatch or pursue correctly.

## 0.3.5-crashguard-local

- Removes remaining inline COUI `display` styles from the top-left panel after crash logs showed repeated invalid display warnings.
- Keeps the red-light police chase prototype local only, but leaves it disabled by default while crash testing.
- This build is for local testing and is not published.

## 0.3.4-police-chase-local

- Adds a local-only red-light police chase prototype.
- Arms the chase when the possessed car approaches a red lane signal and starts it only if the car crosses without stopping.
- Creates a temporary high-priority police patrol target on the possessed car, refreshes path target movement, and nudges nearby police cars into the pursuit.
- Adds police chase status, unit count, red-run count, and a chase toggle to the top-left panel.
- This build is for local testing and is not published.

## 0.3.3-ui-safe-public

- Removes unsupported COUI panel layout styles that could spam invalid display warnings in the Cities: Skylines II UI log.
- Keeps the same top-left menu design, controls, speed readout, and Direct Drive vehicle behavior from `0.3.2-ui-panel-public`.

## 0.3.2-ui-panel-public

- Fixes the top-left driving button double-toggle so clicking it opens/closes the panel reliably.
- Switches the panel speed readout to match the game's selected vehicle speed badge.
- Tightens the assist toggle row layout so labels and hints do not crowd together in-game.

## 0.3.1-ui-panel-local

- Adds a new top-left COUI/React driving panel for Direct Drive status, possession controls, assist toggles, and tuning sliders.
- Keeps the old top-left mod button but makes it open the new panel instead of the rough Unity IMGUI window.
- Binds the panel directly to the Direct Drive runtime so speed, input freshness, throttle/brake/steer input, road intent, road attach, and path-freeze state update live.
- Leaves the direct-drive vehicle control behavior from `0.3.0-direct-drive-public` unchanged.

## 0.3.0-direct-drive-public

- Replaces the active ReliableDrive control stack with the tested Direct Drive runtime while keeping the published `BetaTestDrivingMod` identity.
- Keeps the frame-buffered keyboard input that felt responsive in testing.
- Drives the possessed live car directly after the game's car movement step instead of asking vanilla physical path driving to move the body.
- Freezes vanilla physical path movement for the possessed car before `CarMoveSystem`, preventing the old AI path from fighting player control.
- Keeps the car attached to nearby road lanes with road-height assist and the tested no-hover offset behavior.
- Reworks Road intent assist so A/D or arrow steering queues AI left/right road-turn intent and path connections without physically steering the body off the road.
- Syncs the possessed car back into lane object traffic presence so other vehicles react to its current lane position instead of the original takeover spot.
- Removes the expensive whole-city road scan from the active driving path.

## 0.2.0-responsive-drive-local

- Reworks driving input around one per-frame sampler with latched key-down events for throttle, brake, steering, possess, and HUD toggle.
- Makes the simulation consume the latest buffered input frame instead of polling keyboard state independently.
- Adds stale-input protection so old commands are ignored if input sampling stops.
- Replaces direct steering with a faster front-axle-style target that yaws the desired path into A/D or arrow-key turns.
- Reworks brake-first reverse around latched brake presses, making reverse activation less dependent on simulation tick timing.
- Raises default acceleration, brake, reverse, direction-change, and coast response values for a more immediate feel.
- Adds a small launch bite to the speed ramp so throttle input starts moving the possessed vehicle sooner.

## 0.1.19-lane-keeper-speed-local

- Replaces the sideways manual junction fallback with a road-kept turn fallback so Road lane assist stops throwing the vehicle off the pavement when no connection lane is locked.
- Keeps normal lane switching available, but uses much smaller fallback side offsets near junctions.
- Adds Auto road / turn speed mode using the current `CarLane` speed limit plus configurable junction/sharp-turn caps.
- Adds F8 controls for road speed percent, junction turn speed, sharp turn speed, and the road-keeper fallback offsets.

## 0.1.19-road-node-junction-local

- Adds a cheap connected-road-node detector for T/four-way junctions on the current road.
- Keeps the expensive all-car-lane fallback disabled.
- Uses connection-lane detection only as a fallback after road-node detection.

## 0.1.19-strict-junction-scan-local

- Disables the experimental car-lane-wide junction fallback because it caused lag and false junction boxes.
- Keeps the visible scan overlay and strict connection-lane junction detection.
- Avoids junction debug scanning when both the overlay and Road lane assist are off.

## 0.1.19-lane-geometry-junction-local

- Adds a car-lane geometry fallback that detects crossing/side lanes inside the scan box when `ConnectionLane` lookup misses.
- Projects fallback junction targets back onto the current road lane so the turn box sits at the road entry point.
- Keeps the visible scan overlay and restored steering values.

## 0.1.19-visible-junction-scan-local

- Keeps the real-junction filter but shows a visible scan overlay when no junction target is found.
- Samples actual connection-lane curves at multiple points instead of only checking their endpoints.
- Labels the overlay as scanning when a real junction target has not been detected yet.

## 0.1.19-real-junction-zones-local

- Anchors turn-gate detection to actual CS2 road `ConnectionLane` targets instead of ordinary road lane segment ends.
- Hides the turn-zone overlay with a clear status when no real junction target is ahead.
- Uses the same real-junction anchor for lane-assist turn release, reducing false junction behavior on normal road chunks.

## 0.1.19-anchored-zones-local

- Anchors turn release boxes to the lane/junction end instead of drawing them from the moving car position.
- Labels visible turn-zone overlay controls: scan gate Y, turn-lane Y, junction Y, X half-width, and turn zone end.
- Renames the F8 turn-zone controls to clearer X/Y language.
- Captures keyboard driving input once per frame in the HUD object as well as in simulation, without changing steering force values.

## 0.1.19-turn-zones-local

- Restores the pre-agent steering strength, direction pull, and direct-steer look-ahead values after the over-tuned steering test.
- Adds F8 menu sliders for turn-lane release distance, junction release distance, and turn-box width.
- Adds an in-game Show turn release boxes overlay for seeing when Road lane assist will let manual steering take over.
- Keeps the road-height anchoring for free/direct steering targets.

## 0.1.19-junction-response-local

- Keeps the pre-agent source base and the direct A/D steering fix.
- Lets Road lane assist keep straight-road lane-change stability but release manual steering earlier in marked turn lanes and junction areas.
- Makes free/direct steering targets follow the current road lane height to reduce above-road and under-road aiming.
- Raises the default acceleration, direction-change, steering, and free-steer response values for quicker keyboard input.

## 0.1.19-direction-response-local

- Reverts the source base to the pre-agent `0.1.19-expanded-vehicles-local` build.
- Removes the later lane-step/free-drive/stale-traffic swarm changes from the active source.
- Makes default A/D steering use direct vehicle-facing targets instead of road-lane curve targets when lane assist is off.
- Raises default steering direction pull and acceleration response to reduce direction/input lag.
- Leaves Road lane assist and Expensive junction override available but opt-in.

## 0.1.18

- Repackages the tested `0.1.36-publish-defaults` live-vehicle control build under the public Beta Test Driving Mod item.
- Keeps Road lane assist / merge aim and Expensive junction path override available but unchecked by default.
- Adds the scrollable live tuning menu with public-safe defaults.
- Keeps arrow keys as the recommended controls when WASD moves the camera.
- Keeps the fake spawned-car, fake-light, and traffic-light stop experiments out of the public update.

## 0.1.16

- Replaces the old separate spawned-car sandbox with the ReliableDriveControl live-vehicle possession path.
- Possesses real live CS2 vehicles instead of spawning fake driver cars.
- Adds safer lane merge assist and triple-tap U-turn gating.
- Adds public wording that arrow keys are recommended when WASD moves the camera or screen.
- Keeps the broken traffic-light stop cap, junction-turn override, fake lights, and custom spawned-car experiments out of the public build.

## 0.1.15

- Public safe update copied from the better local DriveModeAI test behavior.
- Removes the extra vehicle/collision update-order hooks so the system runs in the safer simulation slot.
- Adds instant-brake `S` behavior as a setting and HUD toggle.
- Chooses a random currently loaded full-detail vehicle visual when possible, avoiding likely LOD meshes.
- Respawns the player vehicle visual if the game despawns it instead of leaving an invisible car.
- Marks the spawned player visual as hidden dummy traffic and guards vanilla renderer/light buffers instead of force-adding them.

## 0.1.14-test

- Local test only; not published.
- Feeds vanilla vehicle `TransformFrame` data again so CS2 can animate wheel/steering bones from motion.
- Adds steering-based turn-signal flags even at low speed.
- Adds brake, reverse, rear-light, and night headlight/interior-light flags through the normal renderer path.
- Marks vehicle effects updated when headlight state changes.

## 0.1.13

- Changes `S` into a brake-first control: the first press brakes to a stop and holds zero speed.
- Allows reverse only after releasing and pressing `S` again while stopped, preventing unwanted backward recoil.

## 0.1.12

- Raises steering strength to a real `0.4`-`4.0` range and removes the hidden low steering cap.
- Makes steering input respond faster and gives low-speed turns much more authority.
- Raises default road grip so stronger steering does not feel as slidey.
- Moves the chase camera farther behind the car and aims it closer to the vehicle so more of the car stays visible.
- Tags spawned Beta Test Driving vehicle visuals and cleans them when a save loads so old player cars do not stick around.
- Adds a narrow legacy cleanup for older untagged Beta Test Driving cars already saved as stopped moving car visuals.

## 0.1.11

- Removes the unsafe direct `TransformFrame` buffer writes from 0.1.10 after they caused a native crash during live testing.
- Removes the extra BetaTestDrivingMod update-order hook so the driving system only runs once in the simulation phase.
- Keeps the safer real ECS vehicle visual, nearest-lane traffic presence, and road-height following for ramps.

## 0.1.10

- Writes player-car transform frames directly to reduce visual popping/teleporting from the ECS renderer.
- Feeds nearest road-lane height back into the driver body so ramps and elevated roads can carry the car upward instead of letting it phase through.
- Sets vehicle transform light flags for headlights, rear lights, brake lights, reverse lights, and steering-based blinkers.
- Runs after the game's update-group pass so Beta Test Driving Mod's visual frames and light flags are less likely to be overwritten.

## 0.1.9

- Removes the unsafe manual moving-search-tree insert that could duplicate the player car and crash Burst with `NativeQuadTree.Add`.
- Keeps the safer nearest-lane object registration so same-lane traffic can still see the player car as an occupied lane object.

## 0.1.8

- Strengthens braking and jerk response so pressing brake stops the player car sooner.
- Pulls the chase camera farther back and higher so more of the car stays visible.
- Registers the player car as a moving object and nearest-lane object so traffic has a real blocker to react to.

## 0.1.7

- Replaces the unsafe CS2 debug RenderPrefabRenderer path with a real stopped vehicle ECS visual entity.
- Uses the normal game renderer for the player car visual so pedestrians should no longer get deformed.
- Falls back to the safe built-in player car shell only if the real vehicle entity cannot be created.
- Keeps the HUD version label so it is obvious when this safer build is loaded.

## 0.1.6

- Uses the real CS2 vehicle mesh local position and rotation instead of assuming the prefab is centered at zero.
- Stops non-vehicle selections like trees or buildings from deciding the spawned car's facing direction.
- Raises default road grip and uses it in movement so the car follows its nose more tightly instead of sliding.
- Softens default acceleration while keeping braking responsive for a more vanilla-feeling launch and stop.

## 0.1.5

- Tightened chase camera defaults and snapping so camera stays close behind the car.
- Reworked driving toward a slower vanilla-style road feel.
- Clamped old saved high-speed settings to a 45 mph playable cap.
- Reduced steering authority and turn rate to stop the sideways/drifty feel.
- Added a trigger-sized blocker shell as the first step toward player-car presence without reintroducing sticky physics.

## 0.1.4

- Added direct Unity keyboard fallback for WASD/arrow keys so driving no longer depends only on CS2 input bindings.
- Changed the player car to manual kinematic driver movement so road colliders cannot wedge it in place.
- Added live input readout to the HUD.
- Added a late camera follow update to keep the camera near the player car.

## 0.1.3

- Spawn now falls back to the camera pivot when CS2 reports no selected entity.
- Spawned driver car now tries to use a loaded in-game CS2 car mesh instead of the primitive visual shell.
- Primitive visual shell remains as a fallback only if no game car mesh is available.

## 0.1.1

- Added a dedicated local BetaTestDrivingMod simulation/training pass.
- Baked the trained driving profile into default speed, acceleration, braking, jerk, grip, steering, and vision settings.
- Matched runtime throttle and steering smoothing to the trained profile.

## 0.1.0

- Added a clean Beta Test Driving Mod mod shell.
- Added WASD player driving with `V` toggle.
- Added a top-right in-game control panel.
- Added trained-smoothing vehicle dynamics and simple forward obstacle assist.
- Added follow camera controls.
