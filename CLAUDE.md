# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

"Delicate Balance": a Unity 6 (6000.5.8f1) first-person game using URP and the **new Input System** (`Keyboard.current` / `Mouse.current`; do not use the legacy `Input` class). Everything currently lives in one scene (subject to change), `Assets/Scenes/SampleScene.unity`. There is no build script, linter or test suite in the repo (the test-framework package is installed but there are no tests). Work happens in the Unity Editor; the Unity MCP tools (`mcp__unity-mcp__*`) can run editor code and read the console.

## Architecture

**Player** (`Assets/Prefabs/Player.prefab`): `PlayerMovement` (CharacterController, WASD + jump) on the root, with the Main Camera as a child carrying `FPSCameraLook`. Horizontal mouse rotates the player body, vertical rotates only the camera, and the cursor is locked. Note `Assets/Scripts/PlayerCamera.cs` contains the class `FPSCameraLook`.

**Minigames live in the main scene (the main scene will be shifted to another scene that has MainFloor/main as the main building. This is precisely why minigames are packaged as prefabs, in order to use them in both samplescene (for testing) and the final unmade scene), not separate scenes.** `Assets/Scripts/Minigames/` has one self-contained `MonoBehaviour` per minigame, each packaged as a prefab in `Assets/Prefabs/Interactibles/` (`DartBoard.prefab`, `MazeBoard.prefab`). The shared pattern:
- The player looks at the object (raycast from the centre of the camera viewport, only the prefab's own colliders count) and presses **E**.
- The script saves the camera pose, disables `PlayerMovement` and `FPSCameraLook` (remembering their previous enabled state), moves the camera to a fixed anchor, and drives its own input and UI.
- **Esc** restores the camera and the previous enabled states. The player GameObject itself is never disabled.
- Scene-specific references (Main Camera, `PlayerMovement`, `FPSCameraLook`, and for darts the UI text objects) are assigned per instance in the scene, not on the prefab asset. `MazeMinigame` auto-finds the two player scripts if its array is left empty and has its own Canvas inside the prefab. `DartboardMinigame` shares UI objects assigned in the scene.
- `FPSCameraLook` unlocks the cursor on Esc, so `MazeMinigame` re-enables the player scripts one frame after exit to avoid the same Esc press being seen twice. Keep this in mind if adding another minigame.

**MazeMinigame specifics** (prefab layout: `MazeBoard` > `Tilt` [kinematic Rigidbody] > `Board` [scale 0.2: model, box colliders, `StartPoint`, `GoalPoint`]; `Ball` is a sibling of `Tilt`, not a child, so the kinematic parent never drags it). WASD tilts the board about the board centre; the ball is real PhysX. Gotchas found while tuning: tilt axis is `Cross(up, direction)` (the ball rolls toward the board normal's lean), tilt rate must stay capped or the ball is launched over walls, `Rigidbody.maxAngularVelocity` must be raised for small balls, and colliders must not poke into the tilting board (the pedestal collider once did). Wall colliders were generated from the mesh's wall-top rectangles, not a `MeshCollider`.

**Darts specifics**: hold LMB for a sinusoidal horizontal camera sway, release to raycast against the board collider; a hidden fixed offset is added to the impact point and scoring is computed geometrically from the `BoardFace` transform.

## Assets and the Blender pipeline

3D assets are `.blend` files under `Assets/Blends/<name>/`, imported by Unity through Blender (Blender 5.2 is installed at `C:\Program Files\Blender Foundation\Blender 5.2\`; it can be run headless with `blender.exe -b file.blend --python script.py`).
- **A `.blend` import also brings in its Blender lights and cameras.** `maze.blend` contained a 1000-intensity point light that blew out the scene; its importer now has `importLights`/`importCameras` off. Check this for any new `.blend`.
- Unity's imported frame can differ from Blender's (the maze came in rotated 180° about Y relative to Blender top view). Verify orientation against a render before placing start/goal points.
- `maze.blend` now holds **two mazes side by side** (classic at the origin, the hard one ~31 units to +x in Blender coordinates, plus a stray `Plane.001` ~700 units away). `MazeMinigame` has a `MazeChoice` dropdown + `MazeVariant[]`: the shared model stays under `Board/MazeModel`, and each variant lists which model children belong to it, a `modelOffset` that centres it on the board, its own collider/start/goal root (`Variant_Classic`, `Variant_Hard`) and a per-maze ball radius (hard maze doors are only ~0.5 wide, so its ball is smaller). `ApplySelection()` shows the chosen maze and hides everything else, in the editor (via `OnValidate`) and at runtime. To add a maze, add the model parts, a variant root with colliders/points, an enum entry and a variant entry.
- `Assets/Blends/maze/maze_old.blend` / `Untitled.blend` are copies of the original single maze.
- Wall colliders for a maze are generated from the mesh's wall-top rectangles; check reachability with a grid search and a `Physics.Simulate` run before trusting a new layout. Unity can be driven headless for this (`Unity.exe -batchmode -projectPath ... -executeMethod Class.Method -logFile ... -quit`, wrapped in `Start-Process -Wait`), but only while the editor is closed on the project.

### MainFloor: gameplay vs environment split

Gameplay and environment art are developed in parallel, so they are kept in separate layers:
- `Assets/Blends/MainFloor/Main.blend` is the building. **Geometry is locked**: do not change scale, origin, orientation, object names/hierarchy, or merge/split meshes (colliders and prefab overrides match by name/position). Importer has `importLights`/`importCameras` off.
- `Assets/Prefabs/Environment/MainFloor_Gameplay.prefab` is a **Prefab Variant of `Main.blend`**. Gameplay additions (colliders, triggers, NavMesh, spawn points) go on the variant, never on the raw model, so re-imports of the blend don't lose them. `Floor.unity` contains an instance named `MainFloor`.
- Materials were extracted from the blend to `Assets/Materials/MainFloor/` (558 `.mat`). Retexturing happens on those URP materials in Unity; Blender shader/material changes no longer flow through for these. New materials added in Blender need extracting too (importer > Materials > Extract). On Windows two Blender materials differing only by case collide as files (`black metal` is saved as `black metal_lowercase.mat`).
- `Assets/Scenes/Floor.unity` = gameplay scene. `Assets/Scenes/Floor_Environment.unity` = lighting and decor (holds the Directional Light and an `Environment` root). `AdditiveSceneLoader` (on `EnvironmentSceneLoader` in Floor) loads the environment scene additively at runtime if it isn't already open; open both in the editor to work on them. Only one person should edit a given `.unity`/`.prefab`/`.blend` at a time (binary-ish, no hand merges).
- 25 meshes (`Board`, `DoorFrame*`) have no material assigned in Blender and use URP default `Lit`.

## Unity MCP / editor-scripting notes

- `Unity_RunCommand` needs `internal class CommandScript : IRunCommand` with `Execute(ExecutionResult result)`; top-level statements fail, and the parameter names are `Code`/`Title`.
- `AssetDatabase.DeleteAsset` is rejected there ("user interactions not supported"); overwrite assets instead (`PrefabUtility.SaveAsPrefabAsset`, reload existing materials).
- This Unity version marks `FindFirstObjectByType` and `GetInstanceID()` obsolete (the latter is a compile error); use `FindAnyObjectByType` / `GetEntityId()`.
- Physics can be exercised headless in edit mode: set `Physics.simulationMode = SimulationMode.Script`, call `Physics.Simulate(dt)`, and restore the mode in a `finally`.
- Rendering a preview from a temporary camera needs `UniversalAdditionalCameraData` copied from the Main Camera to look like the Game view.

## Version control

Both git (GitHub: `oneshot-death/Delicate-Balance-The-Game`, PRs into `main`) and Plastic SCM are used; `.plastic/*`, `Assembly-CSharp*.csproj` and Unity re-serialisation churn in `ProjectSettings/` regularly show up as local modifications and are normally not committed. `Library/`, `Temp/` and `Logs/` are generated.
