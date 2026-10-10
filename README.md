# FANUC Robot Digital Twin

A Unity-based digital twin and simulation environment for a FANUC-inspired six-axis industrial robot. The project includes robot joint control, pneumatic actuators, suction end-effectors, conveyor targets, safety interlocks, and a runtime control panel for interactive testing.

> This is a simulation and visualization project. It is not a calibrated FANUC controller and must not be used to control physical equipment.

## Features

- Six-axis robot arm simulation with configurable local rotation axes and joint limits.
- Smooth Play Mode and Edit Mode preview for robot joint motion.
- Three pneumatic piston actuators with travel limits and progress readouts.
- End-effector piston and barrel travel controls.
- Suction-cup attachment and target-object interaction.
- Runtime UI Toolkit control panel with motion status and DCS-style safety interlock state.
- Motion recording and playback for repeatable demonstrations.
- Conveyor and target materials for a factory-cell style scene.

## Requirements

- Unity **2022.3.62f1**
- Windows, macOS, or Linux
- Input System package enabled

## Opening the project

1. Clone the repository:

   ```bash
   git clone https://github.com/asifbk/Fanuc-Robot-DT.git
   cd Fanuc-Robot-DT
   ```

2. Open the project with Unity Hub.
3. Select Unity `2022.3.62f1` when prompted.
4. Open `Assets/Scenes/SampleScene.unity`.
5. Press **Play** to start the simulation.

## Runtime controls

The control panel is created when the scene enters Play Mode. Use the panel sliders and buttons to control the robot and actuators.

- **Escape**: stop motion and hide the control panel.
- Use the panel to adjust six robot axes and piston travel.
- Use the suction control to attach or release a compatible target.
- Use recording and playback controls to capture and replay a motion sequence.

The exact available controls depend on the components configured in the scene.

## Project structure

| Path | Description |
| --- | --- |
| `Assets/Scenes` | Unity scenes, including the sample scene |
| `Assets/Scripts` | Robot, actuator, suction, UI, and safety-interlock scripts |
| `Assets/Models` | Imported robot and factory-cell models |
| `Assets/Materials` | Robot, conveyor, target, and environment materials |
| `Assets/Prefabs` | Reusable scene objects |
| `Packages/manifest.json` | Unity package dependencies |
| `ProjectSettings` | Unity project configuration |

## Main scripts

- `RobotSixAxisController.cs` — controls the six rotational robot joints.
- `PistonExtensionController.cs` — drives the primary pneumatic piston actuators.
- `ActuatorTravelSliderController.cs` — controls end-effector piston and barrel travel.
- `PistonControlPanel.cs` — builds and updates the runtime UI Toolkit panel.
- `RobotSuctionController.cs` — handles suction attachment and release.
- `DcsSafetyInterlock.cs` — blocks motion when the safety interlock is tripped.

## Notes

- Imported assets and generated Unity folders can make the repository large.
- Unity-generated folders such as `Library`, `Temp`, `Logs`, and `UserSettings` are excluded by `.gitignore`.
- One FBX asset is larger than GitHub's recommended 50 MB limit. Git LFS is recommended if the asset changes frequently or the repository grows.

## License

No license has been specified yet. Add a license before distributing or reusing this project publicly.
