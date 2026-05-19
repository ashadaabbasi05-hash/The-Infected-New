# GAME MEI JO HOGYA

This document inventories game functionalities and project files found in the workspace and classifies their implementation status.

Methodology:
- Scanned `Assets/` for scripts, assets, and packages.
- Opened core gameplay scripts to confirm implementation (no TODO or NotImplemented markers found).
- Classification is based on file presence and quick code inspection. Recommend manual playtest for runtime completeness.

**Summary of Key Functionalities & Files**

- Game phases & flow:
  - `GameManager.cs` — Controls game phases (Exploration, GasWave, Meeting, Voting), wave counting and timers. (Implemented)

- HUD / UI:
  - `GameHUDController.cs` — Updates phase, wave and timer UI. (Implemented)
  - `GameHUDCanvasSetup.cs` — Canvas setup helper for HUD. (Implemented)
  - `ObjectiveHUDController.cs` — Controls objective HUD elements. (Implemented)
  - `AgentTracePanel.cs` — UI panel for tracing agents. (Implemented)
  - `DemoHelpPanel.cs` — Demo help UI panel. (Implemented)
  - `FinalHuntManager.cs` — Manages final hunt demo and related UI. (Implemented)
  - `GameEndManager.cs` — Handles game end logic and UI. (Implemented)

- Player & NPC behavior:
  - `PlayerMovement.cs` — Player movement handling. (Implemented)
  - `PlayerIdentity.cs` — Player identity data. (Implemented)
  - `BotMovement.cs` — Bot/AI movement logic. (Implemented)

- Interaction & tasks:
  - `TaskManager.cs` — Manages tasks/objectives. (Implemented)
  - `TaskInteractable.cs` — Interactable tasks with TryInteract methods. (Implemented)
  - `EscapeDoor.cs` — Door interactions and TryInteractCurrentDoor helper. (Implemented)

- Infection mechanics:
  - `InfectionSystem.cs` — Global infection logic. (Implemented)
  - `PhysicalInfectionZone.cs` — Scene zones that apply infection. (Implemented)

- Input & Mobile controls:
  - `MobileActionButtonsController.cs` — Mobile buttons (Interact / Trace / Help / Final Hunt) and auto-reference finding. (Implemented)
  - `InputSystem_Actions.inputactions` — Unity Input System asset. (Present)
  - `Joystick Pack/*` — Third-party joystick package scripts and examples (Present)

- Other gameplay controllers / utilities:
  - `FinalHuntManager.cs` — (see HUD list) (Implemented)
  - `MeetingController.cs` — Handles meeting phase logic. (Implemented)
  - `AgentTracePanel.cs` — Agent tracing UI and logging. (Implemented)
  - `CameraFollow.cs` — Camera follow behaviour. (Implemented)
  - `GameHUDCanvasSetup.cs` — Canvas setup (see HUD list) (Implemented)

- Audio & resources:
  - `ambient loop.mp3` — Ambient background music. (Present)
  - `freesound_community-gas-loop-2-73991.mp3` — Gas wave SFX. (Present)
  - `warning alaram.mp3` — Warning alarm SFX. (Present)

- TextMesh Pro resources:
  - `TextMesh Pro/*` — Fonts, shaders and resources (Present)

- Settings & templates:
  - `Settings/Lit2DSceneTemplate.scenetemplate` — Scene template. (Present)

**Complete file listing (Assets/)**

- AgentTracePanel.cs
- ambient loop.mp3
- BotMovement.cs
- freesound_community-gas-loop-2-73991.mp3
- FinalHuntManager.cs
- EscapeDoor.cs
- DemoHelpPanel.cs
- CameraFollow.cs
- GameHUDCanvasSetup.cs
- GameEndManager.cs
- GameHUDController.cs
- GameManager.cs
- PhysicalInfectionZone.cs
- ObjectiveHUDController.cs
- MobileActionButtonsController.cs
- MeetingController.cs
- warning alaram.mp3
- InfectionSystem.cs
- GasWaveEffectsController.cs
- InputSystem_Actions.inputactions
- TaskManager.cs
- PlayerMovement.cs
- PlayerIdentity.cs
- TaskInteractable.cs
- Joystick Pack/Examples/JoystickSetterExample.cs
- Joystick Pack/Examples/JoystickPlayerExample.cs
- TextMesh Pro/Shaders/SDFFunctions.hlsl
- TextMesh Pro/Fonts/LiberationSans.ttf
- TextMesh Pro/Fonts/LiberationSans - OFL.txt
- TextMesh Pro/Shaders/TMPro_Properties.cginc
- TextMesh Pro/Shaders/TMPro_Mobile.cginc
- TextMesh Pro/Shaders/TMPro.cginc
- TextMesh Pro/Shaders/TMPro_Surface.cginc
- TextMesh Pro/Shaders/TMP_Bitmap-Custom-Atlas.shader
- TextMesh Pro/Shaders/TMP_Bitmap.shader
- TextMesh Pro/Shaders/TMP_Bitmap-Mobile.shader
- TextMesh Pro/Shaders/TMP_SDF Overlay.shader
- TextMesh Pro/Shaders/TMP_SDF SSD.shader
- TextMesh Pro/Resources/LineBreaking Following Characters.txt
- TextMesh Pro/Shaders/TMP_Sprite.shader
- TextMesh Pro/Shaders/TMP_SDF.shader
- TextMesh Pro/Shaders/TMP_SDF-URP Unlit.shadergraph
- TextMesh Pro/Shaders/TMP_SDF-URP Lit.shadergraph
- TextMesh Pro/Shaders/TMP_SDF-Surface.shader
- TextMesh Pro/Shaders/TMP_SDF-Surface-Mobile.shader
- TextMesh Pro/Shaders/TMP_SDF-Mobile.shader
- TextMesh Pro/Shaders/TMP_SDF-Mobile-2-Pass.shader
- TextMesh Pro/Shaders/TMP_SDF-Mobile SSD.shader
- TextMesh Pro/Shaders/TMP_SDF-Mobile Overlay.shader
- TextMesh Pro/Shaders/TMP_SDF-Mobile Masking.shader
- TextMesh Pro/Shaders/TMP_SDF-HDRP UNLIT.shadergraph
- TextMesh Pro/Shaders/TMP_SDF-HDRP LIT.shadergraph
- TextMesh Pro/Resources/LineBreaking Leading Characters.txt
- TextMesh Pro/Shaders/TMP_SDF-Surface.shader
- Joystick Pack/Scripts/Base/Joystick.cs
- Settings/Lit2DSceneTemplate.scenetemplate
- Joystick Pack/Scripts/Joysticks/DynamicJoystick.cs
- Joystick Pack/Scripts/Joysticks/FixedJoystick.cs
- Joystick Pack/Scripts/Joysticks/FloatingJoystick.cs
- Joystick Pack/Scripts/Joysticks/VariableJoystick.cs
- Joystick Pack/Scripts/Editor/VariableJoystickEditor.cs
- Joystick Pack/Scripts/Editor/JoystickEditor.cs
- Joystick Pack/Scripts/Editor/FloatingJoystickEditor.cs
- Joystick Pack/Scripts/Editor/DynamicJoystickEditor.cs


**Classification**

- Fully implemented / Created:
  - All listed `.cs` scripts and assets in `Assets/` are present and contain code or data. Quick inspection of core scripts (`GameManager.cs`, `MobileActionButtonsController.cs`, `GameHUDController.cs`) shows full implementations.

- Partially implemented:
  - None detected by scanning files for common TODO or NotImplemented markers.

- Not started / Missing:
  - None detected in `Assets/` based on file presence.


**Notes & Next steps**
- Classification is conservative and based on static inspection only. Runtime behavior, missing inspector references, or unassigned prefabs may still cause features to be incomplete — playtesting in Unity Editor is recommended.
- If you want, I can:
  - Run a deeper scan to open each `.cs` and extract a one-line summary per file.
  - Generate a CSV or a more detailed Markdown table with per-file summaries.


Generated by automation on request.
