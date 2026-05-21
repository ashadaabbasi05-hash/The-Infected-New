# Team A Packages

## Package: TeamA_MainMenu_V5

Version: V5

Status: Fixed and visible in Demo_MainMenu.

What it contains:
- Mobile-friendly sci-fi main menu UI package for THE INFECTED.
- Demo scene for validating the menu layout and button clicks.

Main prefabs:
- Assets/_TeamA_Packages/MainMenuPackage/Prefabs/MainMenuCanvas.prefab
- Assets/_TeamA_Packages/MainMenuPackage/Prefabs/MainMenuBackground.prefab

Demo scene:
- Assets/_TeamA_Packages/DemoScenes/Demo_MainMenu.unity

Scripts included:
- Assets/_TeamA_Packages/MainMenuPackage/Scripts/MainMenuUI.cs

Public methods/events:
- MainMenuUI.OnStartClicked
- MainMenuUI.OnSettingsClicked
- MainMenuUI.OnQuitClicked
- MainMenuUI.ClickStart()
- MainMenuUI.ClickSettings()
- MainMenuUI.ClickQuit()

What Team B must connect:
- MainMenuUI.OnStartClicked -> GameFlowManager.ShowCreateJoinRoom()
- MainMenuUI.OnSettingsClicked -> GameFlowManager.ShowSettings()
- MainMenuUI.OnQuitClicked -> Application.Quit() or hide Quit on mobile

What this package does NOT do:
- No gameplay logic.
- No GameManager integration.
- No Firebase integration.
- No backend or API calls.
- No direct infection, voting, meeting, or player movement logic.

Known issues:
- Quit behavior may be platform dependent on Android builds.
- Placeholder visuals are intentionally lightweight and can be replaced by production art later.
- The demo scene is intended as a lightweight Team A validation scene, not final production flow.

Import instructions:
1. Import or open the Unity project.
2. Copy or keep the Assets/_TeamA_Packages folder in the project.
3. Open Assets/_TeamA_Packages/DemoScenes/Demo_MainMenu.unity.
4. Enter Play Mode and test the buttons.

Team B integration notes:
- Connect MainMenuUI.OnStartClicked -> GameFlowManager.ShowCreateJoinRoom().
- Connect MainMenuUI.OnSettingsClicked -> GameFlowManager.ShowSettings().
- Connect MainMenuUI.OnQuitClicked -> Application.Quit() or hide Quit on mobile.