using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>Independent Play Mode controls for the robot's three piston actuators.</summary>
[DisallowMultipleComponent]
public sealed class PistonControlPanel : MonoBehaviour
{
    private const int PistonCount = 3;
    private const int SuctionCupCount = 2;
    private const int RobotAxisCount = 6;
    private const int EndEffectorPistonCount = 2;
    private const int PanelSortingOrder = 50;
    private const float PanelWidth = 310f;
    private const float PanelPadding = 10f;
    private const float ScreenInset = 18f;
    private const float ControlGap = 7f;
    private const float RefreshInterval = 0.1f;
    private const float RecordingSampleInterval = 0.05f;
    private const float PlaybackStartAlignmentDuration = 1f;
    private const float ContactTolerance = 0.005f;
    private const float Zero = 0f;
    private const float ButtonHeight = 25f;
    private const float RowHeight = 56f;
    private const float ProgressHeight = 4f;
    private const string RuntimeChildName = "Runtime Piston Control Panel";
    private const string PanelSettingsResourcePath = "UI/RobotPistonPanelSettings";
    private const string EventSystemTypeName = "UnityEngine.EventSystems.EventSystem";
    private const string InputModuleTypeName = "UnityEngine.InputSystem.UI.InputSystemUIInputModule";
    private const string RecordingPreferencePrefix = "RTR.PistonControlPanel.TargetRecording.";

    [Serializable]
    private sealed class MotionSample
    {
        public float elapsedSeconds;
        public float[] jointAngles = new float[RobotAxisCount];
        public float[] pistonExtensions = new float[PistonCount];
        public bool hasEndEffectorPistonTravel;
        public float endEffectorPiston1TravelZ;
        public float endEffectorPiston2TravelZ;
        public bool suctionAttached;
    }

    [Serializable]
    private sealed class MotionRecording
    {
        public int targetSlot;
        public bool hasTargetStartPose;
        public Vector3 targetStartPosition;
        public Quaternion targetStartRotation;
        public MotionSample[] samples;
    }

    private static readonly Color Graphite = new Color32(24, 27, 30, 245);
    private static readonly Color GraphiteRaised = new Color32(37, 41, 45, 255);
    private static readonly Color GraphiteDeep = new Color32(15, 17, 19, 255);
    private static readonly Color Gold = new Color32(207, 165, 72, 255);
    private static readonly Color GoldDim = new Color32(139, 109, 43, 255);
    private static readonly Color TextPrimary = new Color32(235, 232, 222, 255);
    private static readonly Color TextMuted = new Color32(164, 164, 156, 255);
    private static readonly Color TrackColor = new Color32(62, 66, 68, 255);
    private static readonly Color StopColor = new Color32(151, 56, 48, 255);
    private static readonly Color DcsReadyColor = new Color32(38, 88, 70, 255);
    private static readonly Color DcsDisabledColor = new Color32(86, 69, 39, 255);
    private static readonly Color DcsTripColor = new Color32(151, 56, 48, 255);
    // Piston state is read and controlled through PistonExtensionController's public API.

    [SerializeField, Tooltip("Order: P1 Fixed cup, P2 Carriage drive, P3 Moving cup")]
    private PistonExtensionController[] pistonControllers = new PistonExtensionController[PistonCount];

    [SerializeField, Tooltip("Optional fixed-tool suction contact transforms, ordered to match the two cup colliders.")]
    private Transform[] fixedSuctionCupReferences = new Transform[SuctionCupCount];

    [SerializeField, Tooltip("End-effector component whose piston sliders are exposed in Play Mode.")]
    private ActuatorTravelSliderController endEffectorTravelController;

    [SerializeField]
    private bool visibleByDefault = true;

    private GameObject runtimeChild;
    private UIDocument uiDocument;
    private PanelSettings runtimePanelSettings;
    private VisualElement rootElement;
    private VisualElement panelElement;
    private Button visibilityButton;
    private Button dcsToggleButton;
    private Label dcsStatusLabel;
    private DcsSafetyInterlock dcsInterlock;
    private readonly Label[] percentageLabels = new Label[PistonCount];
    private readonly VisualElement[] progressFills = new VisualElement[PistonCount];
    private readonly Slider[] extensionSliders = new Slider[PistonCount];
    private readonly Slider[] endEffectorPistonSliders = new Slider[EndEffectorPistonCount];
    private readonly Label[] endEffectorPistonValueLabels = new Label[EndEffectorPistonCount];
    private readonly List<ActuatorTravelSliderController> linkedPiston1Controllers = new List<ActuatorTravelSliderController>();
    private readonly Transform[] suctionCups = new Transform[SuctionCupCount];
    private readonly Collider[] suctionCupColliders = new Collider[SuctionCupCount];
    private Button suctionButton;
    private Label suctionStatusLabel;
    private Label motionStatusLabel;
    private GameObject heldTarget;
    private Rigidbody heldTargetRigidbody;
    private bool heldTargetWasKinematic;
    private bool heldTargetUsedGravity;
    private Transform heldTargetOriginalParent;
    private RobotSuctionController robotSuctionController;

    private readonly List<MotionSample> recordingSamples = new List<MotionSample>();
    private RobotSixAxisController robotController;
    private int recordedTargetSlot;
    private Vector3 recordedTargetStartPosition;
    private Quaternion recordedTargetStartRotation = Quaternion.identity;
    private bool recordedTargetStartPoseValid;
    private float recordingElapsedSeconds;
    private float recordingSampleTimer;
    private MotionRecording playbackRecording;
    private readonly float[] playbackStartJointAngles = new float[RobotAxisCount];
    private readonly float[] playbackStartPistonExtensions = new float[PistonCount];
    private float playbackStartEndEffectorPiston1TravelZ;
    private float playbackStartEndEffectorPiston2TravelZ;
    private int playbackTargetSlot;
    private int playbackSampleIndex;
    private float playbackElapsedSeconds;
    private float playbackStartAlignmentElapsed;
    private bool playbackActive;
    private bool panelVisible;
    private float refreshTimer;

    private void OnEnable()
    {
        if (Application.isPlaying)
        {
            CreateRuntimePanel();
        }
    }

    private void Update()
    {
        if (!Application.isPlaying || runtimeChild == null)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                StopMotionPlayback();
                StopAllTargetsAtCurrent();
                if (panelVisible)
                {
                    SetPanelVisible(false);
                }
            }
            else
            {
                HandleRecordingAndPlaybackKeys(keyboard);
            }
        }

        if (playbackActive)
        {
            UpdateMotionPlayback(Time.unscaledDeltaTime);
        }
        else
        {
            CaptureMotion(Time.unscaledDeltaTime);
        }

        refreshTimer += Time.unscaledDeltaTime;
        if (refreshTimer >= RefreshInterval)
        {
            refreshTimer = 0f;
            RefreshReadouts();
            RefreshDcsControl();
            RefreshSuctionControl();
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (Application.isPlaying && !hasFocus)
        {
            StopMotionPlayback();
            StopAllTargetsAtCurrent();
        }
    }

    private void OnApplicationPause(bool isPaused)
    {
        if (Application.isPlaying && isPaused)
        {
            StopMotionPlayback();
            StopAllTargetsAtCurrent();
        }
    }

    private void OnDisable()
    {
        if (Application.isPlaying)
        {
            CleanupRuntimePanel();
        }
    }

    private void OnDestroy()
    {
        if (Application.isPlaying)
        {
            CleanupRuntimePanel();
        }
    }

    private void CreateRuntimePanel()
    {
        if (runtimeChild != null)
        {
            return;
        }

        robotController = GetComponent<RobotSixAxisController>();
        robotSuctionController = GetComponent<RobotSuctionController>();
        CacheLinkedPiston1Controllers();
        dcsInterlock = FindFirstObjectByType<DcsSafetyInterlock>();
        recordingSamples.Clear();
        recordingElapsedSeconds = 0f;
        recordingSampleTimer = 0f;
        recordedTargetSlot = 0;
        recordedTargetStartPoseValid = false;

        runtimeChild = new GameObject(RuntimeChildName);
        runtimeChild.transform.SetParent(transform, false);
        runtimeChild.SetActive(false);

        runtimePanelSettings = Resources.Load<PanelSettings>(PanelSettingsResourcePath);
        if (runtimePanelSettings == null)
        {
            Debug.LogError("Missing piston panel settings resource: " + PanelSettingsResourcePath, this);
            Destroy(runtimeChild);
            runtimeChild = null;
            return;
        }

        uiDocument = runtimeChild.AddComponent<UIDocument>();
        uiDocument.panelSettings = runtimePanelSettings;
        uiDocument.sortingOrder = PanelSortingOrder;
        EnsureEventSystem(runtimeChild);
        runtimeChild.SetActive(true);

        rootElement = uiDocument.rootVisualElement;
        BuildVisualTree();
        RefreshReadouts();
        CaptureMotionSample();
    }

    private void CacheLinkedPiston1Controllers()
    {
        linkedPiston1Controllers.Clear();
        if (endEffectorTravelController == null || endEffectorTravelController.Piston1Target == null)
        {
            return;
        }

        ActuatorTravelSliderController[] controllers = GetComponentsInChildren<ActuatorTravelSliderController>(true);
        for (int i = 0; i < controllers.Length; i++)
        {
            ActuatorTravelSliderController controller = controllers[i];
            if (controller != null && controller != endEffectorTravelController
                && controller.Piston1Target == endEffectorTravelController.Piston1Target)
            {
                linkedPiston1Controllers.Add(controller);
            }
        }
    }

    private void BuildVisualTree()
    {
        rootElement.Clear();
        rootElement.pickingMode = PickingMode.Ignore;
        rootElement.style.position = Position.Absolute;
        rootElement.style.left = 0f;
        rootElement.style.right = 0f;
        rootElement.style.top = 0f;
        rootElement.style.bottom = 0f;
        rootElement.style.flexDirection = FlexDirection.Column;
        rootElement.style.alignItems = Align.FlexStart;
        rootElement.style.unityFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        rootElement.style.fontSize = 11f;
        rootElement.style.color = TextPrimary;
        rootElement.style.paddingLeft = ScreenInset;
        rootElement.style.paddingTop = ScreenInset;
        rootElement.focusable = true;
        rootElement.RegisterCallback<KeyDownEvent>(OnRootKeyDown);

        dcsToggleButton = new Button(ToggleDcs) { name = "dcs-toggle-button" };
        StyleActionButton(dcsToggleButton, TextPrimary, DcsDisabledColor);
        dcsToggleButton.style.width = 126f;
        dcsToggleButton.style.minWidth = 126f;
        dcsToggleButton.style.flexShrink = 0f;
        dcsToggleButton.style.marginBottom = 2f;
        dcsToggleButton.tooltip = "Enable or disable DCS safety monitoring for the robot workcell.";
        rootElement.Add(dcsToggleButton);

        dcsStatusLabel = new Label("DCS DISABLED");
        dcsStatusLabel.style.color = TextMuted;
        dcsStatusLabel.style.fontSize = 10f;
        dcsStatusLabel.style.marginBottom = 5f;
        rootElement.Add(dcsStatusLabel);

        visibilityButton = new Button(() => SetPanelVisible(!panelVisible));
        visibilityButton.name = "piston-panel-visibility";
        visibilityButton.style.height = ButtonHeight;
        visibilityButton.style.width = 126f;
        visibilityButton.style.minWidth = 126f;
        visibilityButton.style.flexShrink = 0f;
        visibilityButton.style.marginBottom = ControlGap;
        visibilityButton.style.paddingLeft = 10f;
        visibilityButton.style.paddingRight = 10f;
        visibilityButton.style.backgroundColor = GraphiteDeep;
        visibilityButton.style.color = Gold;
        visibilityButton.style.borderTopColor = GoldDim;
        visibilityButton.style.borderBottomColor = GoldDim;
        visibilityButton.style.borderLeftColor = GoldDim;
        visibilityButton.style.borderRightColor = GoldDim;
        visibilityButton.style.borderTopWidth = 1f;
        visibilityButton.style.borderBottomWidth = 1f;
        visibilityButton.style.borderLeftWidth = 1f;
        visibilityButton.style.borderRightWidth = 1f;
        visibilityButton.style.fontSize = 11f;
        visibilityButton.style.unityFontStyleAndWeight = FontStyle.Bold;
        rootElement.Add(visibilityButton);

        panelElement = new VisualElement { name = "piston-control-box" };
        panelElement.pickingMode = PickingMode.Position;
        panelElement.style.width = PanelWidth;
        panelElement.style.maxWidth = Length.Percent(100f);
        panelElement.style.flexShrink = 0f;
        panelElement.style.paddingLeft = PanelPadding;
        panelElement.style.paddingRight = PanelPadding;
        panelElement.style.paddingTop = PanelPadding;
        panelElement.style.paddingBottom = PanelPadding;
        panelElement.style.backgroundColor = Graphite;
        panelElement.style.borderTopColor = GoldDim;
        panelElement.style.borderBottomColor = GoldDim;
        panelElement.style.borderLeftColor = GoldDim;
        panelElement.style.borderRightColor = GoldDim;
        panelElement.style.borderTopWidth = 1f;
        panelElement.style.borderBottomWidth = 1f;
        panelElement.style.borderLeftWidth = 1f;
        panelElement.style.borderRightWidth = 1f;
        panelElement.style.borderTopLeftRadius = 5f;
        panelElement.style.borderTopRightRadius = 5f;
        panelElement.style.borderBottomLeftRadius = 5f;
        panelElement.style.borderBottomRightRadius = 5f;
        panelElement.style.flexDirection = FlexDirection.Column;
        rootElement.Add(panelElement);

        bool hasPistonActuators = false;
        for (int i = 0; i < PistonCount; i++)
        {
            hasPistonActuators |= GetController(i) != null;
        }

        Label title = new Label(hasPistonActuators ? "HYDRAULIC CONTROL" : "FIXED END EFFECTOR");
        title.style.color = Gold;
        title.style.fontSize = 15f;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.marginBottom = 2f;
        panelElement.Add(title);

        string hintText = hasPistonActuators
            ? "Manual motion records while playing · press S to save · press 1 / 2 / 3 to replay a target"
            : "Use the robot joint controls to move the tool · align both contact pads with one target";
        Label hint = new Label(hintText);
        hint.style.color = TextMuted;
        hint.style.fontSize = 10f;
        hint.style.marginBottom = 5f;
        hint.style.whiteSpace = WhiteSpace.Normal;
        panelElement.Add(hint);

        if (hasPistonActuators)
        {
            string[] rowLabels = { "P1  Fixed Cup", "P2  Carriage Drive", "P3  Moving Cup" };
            for (int i = 0; i < PistonCount; i++)
            {
                panelElement.Add(BuildPistonRow(i, rowLabels[i]));
            }

            Button retractAllButton = new Button(RetractAllPistons) { text = "RETRACT ALL" };
            StyleActionButton(retractAllButton, Gold, GraphiteDeep);
            retractAllButton.style.marginTop = 6f;
            panelElement.Add(retractAllButton);
        }
        else
        {
            Label jointControlHint = new Label("Joint movement is available from the Robot Teach Pendant.");
            jointControlHint.style.color = TextMuted;
            jointControlHint.style.fontSize = 10f;
            jointControlHint.style.marginBottom = 5f;
            jointControlHint.style.whiteSpace = WhiteSpace.Normal;
            panelElement.Add(jointControlHint);
        }

        if (endEffectorTravelController != null)
        {
            Label endEffectorTitle = new Label("END EFFECTOR PISTONS");
            endEffectorTitle.style.color = Gold;
            endEffectorTitle.style.fontSize = 11f;
            endEffectorTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            endEffectorTitle.style.marginTop = 5f;
            endEffectorTitle.style.marginBottom = 2f;
            panelElement.Add(endEffectorTitle);
            panelElement.Add(BuildEndEffectorPistonRow(0, "Piston 1 · Local Z"));
            panelElement.Add(BuildEndEffectorPistonRow(1, "Piston 2 · Local Z"));
        }

        Button stopButton = new Button(StopAllTargetsAtCurrent) { text = "SAFETY STOP" };
        StyleActionButton(stopButton, TextPrimary, StopColor);
        stopButton.style.marginTop = 4f;
        panelElement.Add(stopButton);

        suctionButton = new Button(ToggleSuction) { text = "SUCTION GRAB" };
        StyleActionButton(suctionButton, TextPrimary, new Color32(35, 91, 77, 255));
        suctionButton.style.marginTop = 4f;
        suctionButton.tooltip = "Toggle suction; all eight cups must contact the same configured target to attach it.";
        panelElement.Add(suctionButton);

        suctionStatusLabel = new Label("Vacuum off · align both cups with one object");
        suctionStatusLabel.style.color = TextMuted;
        suctionStatusLabel.style.fontSize = 10f;
        suctionStatusLabel.style.marginTop = 3f;
        panelElement.Add(suctionStatusLabel);

        motionStatusLabel = new Label("REC · move the robot; grab a target before saving");
        motionStatusLabel.style.color = TextMuted;
        motionStatusLabel.style.fontSize = 10f;
        motionStatusLabel.style.marginTop = 3f;
        panelElement.Add(motionStatusLabel);

        panelVisible = visibleByDefault;
        panelElement.style.display = panelVisible ? DisplayStyle.Flex : DisplayStyle.None;
        visibilityButton.text = panelVisible ? "HIDE CONTROL" : "SHOW CONTROL";
        RefreshDcsControl();
    }

    private VisualElement BuildPistonRow(int pistonIndex, string rowLabel)
    {
        VisualElement row = new VisualElement { name = "piston-row-" + (pistonIndex + 1) };
        row.style.width = Length.Percent(100f);
        row.style.flexShrink = 0f;
        row.style.minHeight = RowHeight;
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.paddingTop = 3f;
        row.style.paddingBottom = 3f;
        row.style.borderBottomWidth = 1f;
        row.style.borderBottomColor = GraphiteRaised;

        VisualElement info = new VisualElement();
        info.style.flexGrow = 1f;
        info.style.flexShrink = 1f;
        info.style.minWidth = 0f;
        info.style.marginRight = 6f;
        info.style.flexDirection = FlexDirection.Column;

        VisualElement titleLine = new VisualElement();
        titleLine.style.flexDirection = FlexDirection.Row;
        titleLine.style.justifyContent = Justify.SpaceBetween;
        titleLine.style.alignItems = Align.Center;

        Label rowTitle = new Label(rowLabel);
        rowTitle.style.color = TextPrimary;
        rowTitle.style.fontSize = 11f;
        rowTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        titleLine.Add(rowTitle);

        Label percentage = new Label("--%");
        percentage.style.color = Gold;
        percentage.style.fontSize = 10f;
        percentageLabels[pistonIndex] = percentage;
        titleLine.Add(percentage);
        info.Add(titleLine);

        VisualElement progressTrack = new VisualElement();
        progressTrack.style.height = ProgressHeight;
        progressTrack.style.marginTop = 3f;
        progressTrack.style.backgroundColor = TrackColor;
        progressTrack.style.borderTopLeftRadius = 2f;
        progressTrack.style.borderTopRightRadius = 2f;
        progressTrack.style.borderBottomLeftRadius = 2f;
        progressTrack.style.borderBottomRightRadius = 2f;

        VisualElement progressFill = new VisualElement();
        progressFill.style.height = ProgressHeight;
        progressFill.style.width = Length.Percent(0f);
        progressFill.style.backgroundColor = Gold;
        progressTrack.Add(progressFill);
        progressFills[pistonIndex] = progressFill;
        info.Add(progressTrack);

        Slider extensionSlider = new Slider(0f, 1f);
        extensionSlider.name = "piston-extension-slider-" + (pistonIndex + 1);
        extensionSlider.tooltip = "Set the commanded piston extension from fully retracted to fully extended.";
        extensionSlider.style.flexGrow = 1f;
        extensionSlider.style.flexShrink = 1f;
        extensionSlider.style.minWidth = 0f;
        extensionSlider.style.height = 18f;
        extensionSlider.style.marginTop = 2f;
        extensionSlider.RegisterValueChangedCallback(evt =>
        {
            PistonExtensionController pistonController = GetController(pistonIndex);
            if (pistonController != null)
            {
                pistonController.SetExtension(evt.newValue);
            }
        });
        extensionSliders[pistonIndex] = extensionSlider;
        info.Add(extensionSlider);
        row.Add(info);

        Button retractButton = new Button { text = "− RET" };
        Button extendButton = new Button { text = "+ EXT" };
        StyleHoldButton(retractButton);
        StyleHoldButton(extendButton);
        retractButton.style.marginRight = 3f;
        retractButton.tooltip = "Set this piston target to fully retracted.";
        extendButton.tooltip = "Set this piston target to fully extended.";
        retractButton.clicked += () => SetControllerTarget(GetController(pistonIndex), 0f);
        extendButton.clicked += () => SetControllerTarget(GetController(pistonIndex), 1f);
        row.Add(retractButton);
        row.Add(extendButton);

        bool controllerAssigned = GetController(pistonIndex) != null;
        retractButton.SetEnabled(controllerAssigned);
        extendButton.SetEnabled(controllerAssigned);
        extensionSlider.SetEnabled(controllerAssigned);
        return row;
    }

    private void SetEndEffectorPiston1Travel(float travel)
    {
        if (endEffectorTravelController == null)
        {
            return;
        }

        endEffectorTravelController.SetPiston1TravelZ(travel);
        for (int i = 0; i < linkedPiston1Controllers.Count; i++)
        {
            ActuatorTravelSliderController linkedController = linkedPiston1Controllers[i];
            if (linkedController != null)
            {
                linkedController.SetPiston1TravelZ(travel);
            }
        }
    }

    private VisualElement BuildEndEffectorPistonRow(int pistonIndex, string rowLabel)
    {
        VisualElement row = new VisualElement { name = "end-effector-piston-row-" + (pistonIndex + 1) };
        row.style.width = Length.Percent(100f);
        row.style.flexShrink = 0f;
        row.style.minHeight = 40f;
        row.style.flexDirection = FlexDirection.Column;
        row.style.paddingTop = 3f;
        row.style.paddingBottom = 3f;
        row.style.borderBottomWidth = 1f;
        row.style.borderBottomColor = GraphiteRaised;

        VisualElement titleLine = new VisualElement();
        titleLine.style.flexDirection = FlexDirection.Row;
        titleLine.style.justifyContent = Justify.SpaceBetween;
        titleLine.style.alignItems = Align.Center;

        Label title = new Label(rowLabel);
        title.style.color = TextPrimary;
        title.style.fontSize = 11f;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        titleLine.Add(title);

        Label valueLabel = new Label("-- / --");
        valueLabel.style.color = Gold;
        valueLabel.style.fontSize = 10f;
        endEffectorPistonValueLabels[pistonIndex] = valueLabel;
        titleLine.Add(valueLabel);
        row.Add(titleLine);

        Slider travelSlider = new Slider(Zero, ActuatorTravelSliderController.MaximumPistonTravel);
        travelSlider.name = "end-effector-piston-slider-" + (pistonIndex + 1);
        travelSlider.tooltip = pistonIndex == 1
            ? "Move Piston 2 along its own local Z stroke axis from 0 to 0.6 units; travel is compensated for its 100x mesh scale."
            : "Move Piston 1 along local Z from 0 to 0.6 units.";
        travelSlider.style.flexGrow = 1f;
        travelSlider.style.flexShrink = 1f;
        travelSlider.style.minWidth = 0f;
        travelSlider.style.height = 18f;
        travelSlider.RegisterValueChangedCallback(evt =>
        {
            if (endEffectorTravelController == null)
            {
                return;
            }

            if (pistonIndex == 0)
            {
                SetEndEffectorPiston1Travel(evt.newValue);
            }
            else
            {
                endEffectorTravelController.SetPiston2TravelZ(evt.newValue);
            }
        });
        endEffectorPistonSliders[pistonIndex] = travelSlider;
        row.Add(travelSlider);
        travelSlider.SetEnabled(endEffectorTravelController != null && endEffectorTravelController.isActiveAndEnabled);
        return row;
    }

    private static void StyleHoldButton(Button button)
    {
        button.style.height = ButtonHeight;
        button.style.minWidth = 45f;
        button.style.paddingLeft = 5f;
        button.style.paddingRight = 5f;
        button.style.backgroundColor = GraphiteRaised;
        button.style.color = TextPrimary;
        button.style.borderTopColor = GoldDim;
        button.style.borderBottomColor = GoldDim;
        button.style.borderLeftColor = GoldDim;
        button.style.borderRightColor = GoldDim;
        button.style.borderTopWidth = 1f;
        button.style.borderBottomWidth = 1f;
        button.style.borderLeftWidth = 1f;
        button.style.borderRightWidth = 1f;
        button.style.fontSize = 9f;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
    }

    private static void StyleActionButton(Button button, Color textColor, Color backgroundColor)
    {
        button.style.height = ButtonHeight;
        button.style.width = Length.Percent(100f);
        button.style.backgroundColor = backgroundColor;
        button.style.color = textColor;
        button.style.borderTopColor = GoldDim;
        button.style.borderBottomColor = GoldDim;
        button.style.borderLeftColor = GoldDim;
        button.style.borderRightColor = GoldDim;
        button.style.borderTopWidth = 1f;
        button.style.borderBottomWidth = 1f;
        button.style.borderLeftWidth = 1f;
        button.style.borderRightWidth = 1f;
        button.style.fontSize = 10f;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
    }

    private void ToggleDcs()
    {
        if (dcsInterlock == null)
        {
            dcsInterlock = FindFirstObjectByType<DcsSafetyInterlock>();
        }

        if (dcsInterlock != null)
        {
            dcsInterlock.ToggleDcs();
        }

        RefreshDcsControl();
    }

    private void RefreshDcsControl()
    {
        if (dcsToggleButton == null || dcsStatusLabel == null)
        {
            return;
        }

        if (dcsInterlock == null)
        {
            dcsInterlock = FindFirstObjectByType<DcsSafetyInterlock>();
        }

        if (dcsInterlock == null || !dcsInterlock.HasController)
        {
            dcsToggleButton.text = "DCS UNAVAILABLE";
            dcsToggleButton.SetEnabled(false);
            dcsToggleButton.style.backgroundColor = DcsDisabledColor;
            dcsStatusLabel.text = "No robot safety controller found";
            dcsStatusLabel.style.color = StopColor;
            return;
        }

        dcsToggleButton.SetEnabled(true);
        if (!dcsInterlock.IsEnabled)
        {
            dcsToggleButton.text = "ENABLE DCS";
            dcsToggleButton.style.backgroundColor = DcsDisabledColor;
            dcsStatusLabel.text = "DCS DISABLED · Motion unrestricted";
            dcsStatusLabel.style.color = TextMuted;
        }
        else if (dcsInterlock.IsTripped)
        {
            dcsToggleButton.text = "DISABLE DCS";
            dcsToggleButton.style.backgroundColor = DcsTripColor;
            dcsStatusLabel.text = "SAFETY TRIPPED · Clear zone, then cycle DCS";
            dcsStatusLabel.style.color = DcsTripColor;
        }
        else
        {
            dcsToggleButton.text = "DISABLE DCS";
            dcsToggleButton.style.backgroundColor = DcsReadyColor;
            dcsStatusLabel.text = "DCS ARMED · Workcell monitored";
            dcsStatusLabel.style.color = DcsReadyColor;
        }
    }

    private void SetPanelVisible(bool isVisible)
    {
        panelVisible = isVisible;
        if (panelElement != null)
        {
            panelElement.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (visibilityButton != null)
        {
            visibilityButton.text = isVisible ? "HIDE CONTROL" : "SHOW CONTROL";
        }

        if (!isVisible)
        {
            StopMotionPlayback();
            StopAllTargetsAtCurrent();
        }
    }

    private void OnRootKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode == KeyCode.Escape)
        {
            StopMotionPlayback();
            StopAllTargetsAtCurrent();
            SetPanelVisible(false);
            evt.StopPropagation();
        }
    }

    private void StopAllTargetsAtCurrent()
    {
        if (robotController == null)
        {
            robotController = GetComponent<RobotSixAxisController>();
        }

        if (robotController != null)
        {
            robotController.StopMotion();
        }

        for (int i = 0; i < PistonCount; i++)
        {
            PistonExtensionController controller = GetController(i);
            if (controller != null)
            {
                controller.SetExtension(controller.CurrentExtension01);
            }
        }
    }

    private void RetractAllPistons()
    {
        StopAllTargetsAtCurrent();
        for (int i = 0; i < PistonCount; i++)
        {
            PistonExtensionController controller = GetController(i);
            if (controller != null)
            {
                controller.Retract();
            }
        }
    }

    private void HandleRecordingAndPlaybackKeys(Keyboard keyboard)
    {
        if (keyboard.sKey.wasPressedThisFrame)
        {
            SaveMotionRecording();
        }

        if (playbackActive)
        {
            return;
        }

        if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)
        {
            PlayMotionRecording(1);
        }
        else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame)
        {
            PlayMotionRecording(2);
        }
        else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame)
        {
            PlayMotionRecording(3);
        }
    }

    private void CaptureMotion(float deltaTime)
    {
        if (robotController == null)
        {
            robotController = GetComponent<RobotSixAxisController>();
        }

        if (robotController == null)
        {
            return;
        }

        recordingElapsedSeconds += deltaTime;
        recordingSampleTimer += deltaTime;
        if (recordingSampleTimer >= RecordingSampleInterval)
        {
            recordingSampleTimer %= RecordingSampleInterval;
            CaptureMotionSample();
        }
    }

    private void CaptureMotionSample()
    {
        if (robotController == null)
        {
            return;
        }

        MotionSample sample = new MotionSample { elapsedSeconds = recordingElapsedSeconds };
        for (int axisIndex = 0; axisIndex < RobotAxisCount; axisIndex++)
        {
            sample.jointAngles[axisIndex] = robotController.GetCurrentAngle(axisIndex);
        }

        for (int pistonIndex = 0; pistonIndex < PistonCount; pistonIndex++)
        {
            PistonExtensionController controller = GetController(pistonIndex);
            sample.pistonExtensions[pistonIndex] = controller != null ? controller.CurrentExtension01 : 0f;
        }

        if (endEffectorTravelController != null)
        {
            sample.hasEndEffectorPistonTravel = true;
            sample.endEffectorPiston1TravelZ = endEffectorTravelController.Piston1TravelZ;
            sample.endEffectorPiston2TravelZ = endEffectorTravelController.Piston2TravelZ;
        }

        GameObject attachedTarget = GetCurrentlyAttachedTarget();
        sample.suctionAttached = attachedTarget != null;
        if (attachedTarget != null && !playbackActive)
        {
            int attachedTargetSlot = ResolveTargetSlot(attachedTarget.transform);
            if (attachedTargetSlot > 0 && (!recordedTargetStartPoseValid || recordedTargetSlot != attachedTargetSlot))
            {
                recordedTargetSlot = attachedTargetSlot;
                bool hasRobotAttachmentPose = robotSuctionController != null
                    && robotSuctionController.AttachedTarget == attachedTarget.transform;
                recordedTargetStartPosition = hasRobotAttachmentPose
                    ? robotSuctionController.AttachedTargetStartPosition
                    : attachedTarget.transform.position;
                recordedTargetStartRotation = hasRobotAttachmentPose
                    ? robotSuctionController.AttachedTargetStartRotation
                    : attachedTarget.transform.rotation;
                recordedTargetStartPoseValid = true;
            }
        }

        recordingSamples.Add(sample);
    }

    private GameObject GetCurrentlyAttachedTarget()
    {
        if (robotSuctionController == null)
            robotSuctionController = GetComponent<RobotSuctionController>();

        Transform robotAttachedTarget = robotSuctionController != null ? robotSuctionController.AttachedTarget : null;
        return robotAttachedTarget != null ? robotAttachedTarget.gameObject : heldTarget;
    }

    private void SaveMotionRecording()
    {
        if (robotController == null)
        {
            robotController = GetComponent<RobotSixAxisController>();
        }

        if (robotController == null)
        {
            SetMotionStatus("Cannot save · robot-axis controller is missing");
            return;
        }

        GameObject attachedTarget = GetCurrentlyAttachedTarget();
        if (attachedTarget != null)
        {
            int attachedTargetSlot = ResolveTargetSlot(attachedTarget.transform);
            if (attachedTargetSlot > 0)
            {
                recordedTargetSlot = attachedTargetSlot;
            }
        }

        if (recordedTargetSlot < 1 || recordedTargetSlot > 3)
        {
            SetMotionStatus("Cannot assign recording · grab Target 1, 2, or 3 before saving with S");
            return;
        }

        CaptureMotionSample();
        if (recordingSamples.Count < 2)
        {
            SetMotionStatus("Recording is too short · move the robot before saving with S");
            return;
        }

        MotionRecording recording = new MotionRecording
        {
            targetSlot = recordedTargetSlot,
            hasTargetStartPose = recordedTargetStartPoseValid,
            targetStartPosition = recordedTargetStartPosition,
            targetStartRotation = recordedTargetStartRotation,
            samples = recordingSamples.ToArray()
        };
        string json = JsonUtility.ToJson(recording);
        PlayerPrefs.SetString(GetRecordingPreferenceKey(recordedTargetSlot), json);
        PlayerPrefs.Save();

        int savedSlot = recordedTargetSlot;
        int savedSampleCount = recordingSamples.Count;
        recordingSamples.Clear();
        recordingElapsedSeconds = 0f;
        recordingSampleTimer = 0f;
        recordedTargetSlot = 0;
        recordedTargetStartPoseValid = false;
        CaptureMotionSample();
        SetMotionStatus("Saved Target " + savedSlot + " · " + savedSampleCount + " samples · press " + savedSlot + " to replay");
    }

    private void ResetTargetForPlayback(MotionRecording recording)
    {
        if (!recording.hasTargetStartPose)
        {
            return;
        }

        GameObject targetObject = FindTargetObject(recording.targetSlot);
        if (targetObject == null)
        {
            SetMotionStatus("Target " + recording.targetSlot + " is missing from the scene");
            return;
        }

        GameObject conveyor = GameObject.Find("industrial_conveyor_belt");
        targetObject.transform.SetParent(conveyor != null ? conveyor.transform : null, true);
        targetObject.transform.SetPositionAndRotation(recording.targetStartPosition, recording.targetStartRotation);
        Rigidbody targetBody = targetObject.GetComponent<Rigidbody>();
        if (targetBody != null)
        {
            targetBody.position = recording.targetStartPosition;
            targetBody.rotation = recording.targetStartRotation;
        }
    }

    private static GameObject FindTargetObject(int targetSlot)
    {
        GameObject targetObject = GameObject.Find("Target " + targetSlot);
        if (targetObject != null)
        {
            return targetObject;
        }

        string legacyName = targetSlot switch
        {
            1 => "Target_Retracted_All_Cylinders",
            2 => "Target_Max_Extended_All_Cylinders",
            3 => "Target_Irregular_Manual_Adjustment",
            _ => string.Empty
        };
        return string.IsNullOrEmpty(legacyName) ? null : GameObject.Find(legacyName);
    }

    private void PlayMotionRecording(int targetSlot)
    {
        string json = PlayerPrefs.GetString(GetRecordingPreferenceKey(targetSlot), string.Empty);
        if (string.IsNullOrEmpty(json))
        {
            SetMotionStatus("No recording for Target " + targetSlot + " · grab it, move, then press S");
            return;
        }

        MotionRecording recording = JsonUtility.FromJson<MotionRecording>(json);
        if (recording == null || recording.targetSlot != targetSlot || recording.samples == null || recording.samples.Length < 2)
        {
            SetMotionStatus("Saved recording for Target " + targetSlot + " is invalid");
            return;
        }

        for (int sampleIndex = 0; sampleIndex < recording.samples.Length; sampleIndex++)
        {
            MotionSample sample = recording.samples[sampleIndex];
            if (sample == null || sample.jointAngles == null || sample.jointAngles.Length != RobotAxisCount
                || sample.pistonExtensions == null || sample.pistonExtensions.Length != PistonCount)
            {
                SetMotionStatus("Saved recording for Target " + targetSlot + " is incomplete");
                return;
            }
        }

        StopMotionPlayback();
        if (heldTarget != null)
        {
            ReleaseSuctionTarget();
        }

        if (robotSuctionController == null)
            robotSuctionController = GetComponent<RobotSuctionController>();
        if (robotSuctionController != null)
            robotSuctionController.SetSuctionEnabled(false);

        ResetTargetForPlayback(recording);
        if (robotController == null)
        {
            robotController = GetComponent<RobotSixAxisController>();
        }

        if (robotController != null)
        {
            for (int axisIndex = 0; axisIndex < RobotAxisCount; axisIndex++)
            {
                playbackStartJointAngles[axisIndex] = robotController.GetCurrentAngle(axisIndex);
            }
        }

        for (int pistonIndex = 0; pistonIndex < PistonCount; pistonIndex++)
        {
            PistonExtensionController controller = GetController(pistonIndex);
            playbackStartPistonExtensions[pistonIndex] = controller != null ? controller.CurrentExtension01 : 0f;
        }
        playbackStartEndEffectorPiston1TravelZ = endEffectorTravelController != null
            ? endEffectorTravelController.Piston1TravelZ
            : 0f;
        playbackStartEndEffectorPiston2TravelZ = endEffectorTravelController != null
            ? endEffectorTravelController.Piston2TravelZ
            : 0f;

        playbackRecording = recording;
        playbackTargetSlot = targetSlot;
        playbackSampleIndex = 0;
        playbackElapsedSeconds = 0f;
        playbackStartAlignmentElapsed = 0f;
        playbackActive = true;
        SetMotionStatus("Aligning saved start pose · playing Target " + targetSlot + " next");
    }

    private void UpdateMotionPlayback(float deltaTime)
    {
        if (playbackRecording == null || playbackRecording.samples == null || playbackRecording.samples.Length == 0)
        {
            StopMotionPlayback();
            return;
        }

        playbackStartAlignmentElapsed += deltaTime;
        if (playbackStartAlignmentElapsed < PlaybackStartAlignmentDuration)
        {
            float alignment = Mathf.Clamp01(playbackStartAlignmentElapsed / PlaybackStartAlignmentDuration);
            ApplyPlaybackStartAlignment(playbackRecording.samples[0], alignment);
            return;
        }

        playbackElapsedSeconds += deltaTime;
        MotionSample[] samples = playbackRecording.samples;
        while (playbackSampleIndex < samples.Length - 2
            && samples[playbackSampleIndex + 1].elapsedSeconds <= playbackElapsedSeconds)
        {
            playbackSampleIndex++;
        }

        MotionSample first = samples[playbackSampleIndex];
        MotionSample second = playbackSampleIndex < samples.Length - 1
            ? samples[playbackSampleIndex + 1]
            : first;
        float sampleDuration = second.elapsedSeconds - first.elapsedSeconds;
        float interpolation = sampleDuration > 0f
            ? Mathf.Clamp01((playbackElapsedSeconds - first.elapsedSeconds) / sampleDuration)
            : 0f;
        ApplyMotionSample(first, second, interpolation);

        if (playbackElapsedSeconds >= samples[samples.Length - 1].elapsedSeconds)
        {
            int completedTargetSlot = playbackTargetSlot;
            ApplyMotionSample(samples[samples.Length - 1], samples[samples.Length - 1], 0f);
            playbackActive = false;
            playbackRecording = null;
            playbackTargetSlot = 0;
            recordingSamples.Clear();
            recordingElapsedSeconds = 0f;
            recordingSampleTimer = 0f;
            recordedTargetSlot = 0;
            CaptureMotionSample();
            SetMotionStatus("Target " + completedTargetSlot + " replay complete");
        }
    }

    private void ApplyPlaybackStartAlignment(MotionSample startSample, float alignment)
    {
        if (robotController != null)
        {
            for (int axisIndex = 0; axisIndex < RobotAxisCount; axisIndex++)
            {
                float angle = Mathf.Lerp(playbackStartJointAngles[axisIndex], startSample.jointAngles[axisIndex], alignment);
                robotController.SetJointImmediate(axisIndex, angle);
            }
        }

        for (int pistonIndex = 0; pistonIndex < PistonCount; pistonIndex++)
        {
            PistonExtensionController controller = GetController(pistonIndex);
            if (controller != null)
            {
                float extension = Mathf.Lerp(playbackStartPistonExtensions[pistonIndex], startSample.pistonExtensions[pistonIndex], alignment);
                controller.SetExtensionImmediate(extension);
            }
        }

        if (endEffectorTravelController != null && startSample.hasEndEffectorPistonTravel)
        {
            float piston1Travel = Mathf.Lerp(playbackStartEndEffectorPiston1TravelZ, startSample.endEffectorPiston1TravelZ, alignment);
            float piston2Travel = Mathf.Lerp(playbackStartEndEffectorPiston2TravelZ, startSample.endEffectorPiston2TravelZ, alignment);
            endEffectorTravelController.SetPiston1TravelZImmediate(piston1Travel);
            endEffectorTravelController.SetPiston2TravelZImmediate(piston2Travel);
        }
    }

    private void ApplyMotionSample(MotionSample first, MotionSample second, float interpolation)
    {
        if (robotController == null)
        {
            robotController = GetComponent<RobotSixAxisController>();
        }

        if (robotController != null)
        {
            for (int axisIndex = 0; axisIndex < RobotAxisCount; axisIndex++)
            {
                float angle = Mathf.Lerp(first.jointAngles[axisIndex], second.jointAngles[axisIndex], interpolation);
                robotController.SetJointImmediate(axisIndex, angle);
            }
        }

        for (int pistonIndex = 0; pistonIndex < PistonCount; pistonIndex++)
        {
            PistonExtensionController controller = GetController(pistonIndex);
            if (controller != null)
            {
                float extension = Mathf.Lerp(first.pistonExtensions[pistonIndex], second.pistonExtensions[pistonIndex], interpolation);
                controller.SetExtensionImmediate(extension);
            }
        }

        if (endEffectorTravelController != null && first.hasEndEffectorPistonTravel && second.hasEndEffectorPistonTravel)
        {
            float piston1Travel = Mathf.Lerp(first.endEffectorPiston1TravelZ, second.endEffectorPiston1TravelZ, interpolation);
            float piston2Travel = Mathf.Lerp(first.endEffectorPiston2TravelZ, second.endEffectorPiston2TravelZ, interpolation);
            endEffectorTravelController.SetPiston1TravelZImmediate(piston1Travel);
            endEffectorTravelController.SetPiston2TravelZImmediate(piston2Travel);
        }

        SetPlaybackSuctionState(first.suctionAttached);
    }

    private void SetPlaybackSuctionState(bool shouldBeAttached)
    {
        if (robotSuctionController == null)
            robotSuctionController = GetComponent<RobotSuctionController>();

        if (robotSuctionController != null)
        {
            robotSuctionController.SetSuctionEnabled(shouldBeAttached);
            return;
        }

        if (shouldBeAttached)
        {
            if (heldTarget == null)
                TryGrabWithSuction(playbackTargetSlot);
        }
        else if (heldTarget != null)
        {
            ReleaseSuctionTarget();
        }
    }

    private void StopMotionPlayback()
    {
        playbackActive = false;
        playbackRecording = null;
        playbackTargetSlot = 0;
    }

    private static string GetRecordingPreferenceKey(int targetSlot)
    {
        return RecordingPreferencePrefix + targetSlot;
    }

    private void SetMotionStatus(string message)
    {
        if (motionStatusLabel != null)
        {
            motionStatusLabel.text = message;
        }
    }

    private void ToggleSuction()
    {
        StopMotionPlayback();
        if (robotSuctionController == null)
            robotSuctionController = GetComponent<RobotSuctionController>();

        if (robotSuctionController != null)
        {
            robotSuctionController.ToggleSuction();
            RefreshSuctionControl();
            return;
        }

        if (heldTarget != null)
        {
            ReleaseSuctionTarget();
            return;
        }

        TryGrabWithSuction();
    }

    private void RefreshSuctionControl()
    {
        if (suctionButton == null || suctionStatusLabel == null)
            return;

        if (robotSuctionController == null)
            robotSuctionController = GetComponent<RobotSuctionController>();
        if (robotSuctionController == null)
            return;

        suctionButton.text = robotSuctionController.AttachedTarget != null
            ? "RELEASE OBJECT"
            : robotSuctionController.IsSuctionEnabled ? "VACUUM ON · RELEASE" : "SUCTION GRAB";
        suctionStatusLabel.text = robotSuctionController.SuctionStatus;
        suctionStatusLabel.style.color = robotSuctionController.AttachedTarget != null ? Gold : TextMuted;
    }

    private void TryGrabWithSuction(int expectedTargetSlot = 0)
    {
        DiscoverSuctionCups();
        for (int cupIndex = 0; cupIndex < SuctionCupCount; cupIndex++)
        {
            GetSuctionCupCollider(cupIndex);
        }
        Physics.SyncTransforms();

        Transform sharedTarget = null;
        Transform firstContactCup = null;
        int contactingCupCount = 0;
        for (int cupIndex = 0; cupIndex < SuctionCupCount; cupIndex++)
        {
            Collider cupCollider = GetSuctionCupCollider(cupIndex);
            if (cupCollider == null || !cupCollider.enabled)
            {
                continue;
            }

            if (!TryFindContactTarget(cupCollider, expectedTargetSlot, out Transform contactTarget))
            {
                continue;
            }

            if (sharedTarget != null && sharedTarget != contactTarget)
            {
                SetSuctionStatus("Cups touch different objects · align both on one object");
                return;
            }

            sharedTarget = contactTarget;
            if (firstContactCup == null)
            {
                firstContactCup = suctionCups[cupIndex];
            }
            contactingCupCount++;
        }

        if (contactingCupCount < SuctionCupCount || sharedTarget == null)
        {
            string status = contactingCupCount == 0
                ? "No cup contact · position both cups on one object"
                : "Only one cup is in contact · both cups must touch";
            SetSuctionStatus(status);
            return;
        }

        Transform secondContactCup = null;
        for (int cupIndex = 0; cupIndex < SuctionCupCount; cupIndex++)
        {
            if (suctionCups[cupIndex] != null && suctionCups[cupIndex] != firstContactCup)
            {
                secondContactCup = suctionCups[cupIndex];
                break;
            }
        }

        Transform attachmentParent = FindCommonAncestor(firstContactCup, secondContactCup);
        if (attachmentParent == null)
        {
            SetSuctionStatus("Could not find a shared robot frame for both cups");
            return;
        }

        heldTarget = sharedTarget.gameObject;
        if (!playbackActive)
        {
            recordedTargetSlot = ResolveTargetSlot(sharedTarget);
            recordedTargetStartPosition = sharedTarget.position;
            recordedTargetStartRotation = sharedTarget.rotation;
            recordedTargetStartPoseValid = recordedTargetSlot > 0;
        }

        heldTargetOriginalParent = heldTarget.transform.parent;
        heldTargetRigidbody = heldTarget.GetComponent<Rigidbody>();
        if (heldTargetRigidbody != null)
        {
            heldTargetWasKinematic = heldTargetRigidbody.isKinematic;
            heldTargetUsedGravity = heldTargetRigidbody.useGravity;
            heldTargetRigidbody.isKinematic = true;
            heldTargetRigidbody.useGravity = false;
        }

        heldTarget.transform.SetParent(attachmentParent, true);
        suctionButton.text = "RELEASE OBJECT";
        SetSuctionStatus("Vacuum attached · held by both suction cups");
    }

    private void ReleaseSuctionTarget()
    {
        if (heldTarget != null)
        {
            heldTarget.transform.SetParent(heldTargetOriginalParent, true);
        }

        if (heldTargetRigidbody != null)
        {
            heldTargetRigidbody.useGravity = heldTargetUsedGravity;
            heldTargetRigidbody.isKinematic = heldTargetWasKinematic;
        }

        heldTarget = null;
        heldTargetRigidbody = null;
        heldTargetOriginalParent = null;
        if (suctionButton != null)
        {
            suctionButton.text = "SUCTION GRAB";
        }

        SetSuctionStatus("Vacuum off · align both cups with one object");
    }

    private void DiscoverSuctionCups()
    {
        for (int cupIndex = 0; cupIndex < SuctionCupCount; cupIndex++)
        {
            if (suctionCups[cupIndex] != null)
            {
                continue;
            }

            if (fixedSuctionCupReferences != null
                && cupIndex < fixedSuctionCupReferences.Length
                && fixedSuctionCupReferences[cupIndex] != null)
            {
                suctionCups[cupIndex] = fixedSuctionCupReferences[cupIndex];
                continue;
            }

            for (int pistonIndex = 0; pistonIndex < PistonCount; pistonIndex++)
            {
                PistonExtensionController controller = GetController(pistonIndex);
                if (controller == null)
                {
                    continue;
                }

                Transform[] descendants = controller.GetComponentsInChildren<Transform>(true);
                for (int childIndex = 0; childIndex < descendants.Length; childIndex++)
                {
                    Transform candidate = descendants[childIndex];
                    if (!candidate.name.StartsWith("Suction Cap", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    bool alreadyAdded = false;
                    for (int addedIndex = 0; addedIndex < cupIndex; addedIndex++)
                    {
                        alreadyAdded |= suctionCups[addedIndex] == candidate;
                    }

                    if (!alreadyAdded)
                    {
                        suctionCups[cupIndex] = candidate;
                        break;
                    }
                }

                if (suctionCups[cupIndex] != null)
                {
                    break;
                }
            }
        }
    }

    private Collider GetSuctionCupCollider(int cupIndex)
    {
        DiscoverSuctionCups();
        Transform cup = suctionCups[cupIndex];
        if (cup == null || !cup.gameObject.activeInHierarchy)
        {
            return null;
        }

        Collider cupCollider = suctionCupColliders[cupIndex];
        if (cupCollider != null)
        {
            return cupCollider;
        }

        cupCollider = cup.GetComponent<Collider>();
        if (cupCollider == null)
        {
            SphereCollider sphereCollider = cup.gameObject.AddComponent<SphereCollider>();
            Renderer cupRenderer = cup.GetComponent<Renderer>();
            if (cupRenderer != null)
            {
                Vector3 localCenter = cup.InverseTransformPoint(cupRenderer.bounds.center);
                Vector3 extents = cupRenderer.bounds.extents;
                float maximumWorldExtent = Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z));
                Vector3 scale = cup.lossyScale;
                float maximumScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                sphereCollider.center = localCenter;
                sphereCollider.radius = maximumScale > 0f ? maximumWorldExtent / maximumScale : 0.01f;
            }
            else
            {
                sphereCollider.radius = 0.01f;
            }

            sphereCollider.isTrigger = true;
            cupCollider = sphereCollider;
        }

        suctionCupColliders[cupIndex] = cupCollider;
        return cupCollider;
    }

    private bool TryFindContactTarget(Collider cupCollider, int expectedTargetSlot, out Transform targetRoot)
    {
        targetRoot = null;
        Bounds cupBounds = cupCollider.bounds;
        float cupRadius = Mathf.Max(cupBounds.extents.x, Mathf.Max(cupBounds.extents.y, cupBounds.extents.z));
        Collider[] candidates = Physics.OverlapSphere(cupBounds.center, cupRadius + ContactTolerance,
            ~0, QueryTriggerInteraction.Collide);
        float closestDistance = float.PositiveInfinity;

        for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
        {
            Collider candidate = candidates[candidateIndex];
            if (candidate == null || candidate == cupCollider || !candidate.enabled || IsRobotCollider(candidate.transform))
            {
                continue;
            }

            Transform candidateRoot = ResolveTargetRoot(candidate);
            if (candidateRoot == null || (expectedTargetSlot > 0 && ResolveTargetSlot(candidateRoot) != expectedTargetSlot))
            {
                continue;
            }

            Vector3 closestPoint = candidate.ClosestPoint(cupBounds.center);
            float distance = Vector3.Distance(cupBounds.center, closestPoint);
            if (distance > cupRadius + ContactTolerance || distance >= closestDistance)
            {
                continue;
            }

            closestDistance = distance;
            targetRoot = candidateRoot;
        }

        return targetRoot != null;
    }

    private bool IsRobotCollider(Transform candidate)
    {
        return candidate == transform || candidate.IsChildOf(transform);
    }

    private static Transform ResolveTargetRoot(Collider targetCollider)
    {
        if (targetCollider.attachedRigidbody != null)
        {
            return targetCollider.attachedRigidbody.transform;
        }

        for (Transform ancestor = targetCollider.transform; ancestor != null; ancestor = ancestor.parent)
        {
            if (ancestor.name.StartsWith("Target_", StringComparison.OrdinalIgnoreCase)
                || ancestor.name.StartsWith("Target ", StringComparison.OrdinalIgnoreCase)
                || ancestor.name.StartsWith("Grabbable", StringComparison.OrdinalIgnoreCase))
            {
                return ancestor;
            }
        }

        return targetCollider.transform;
    }

    private static int ResolveTargetSlot(Transform target)
    {
        for (Transform ancestor = target; ancestor != null; ancestor = ancestor.parent)
        {
            if (string.Equals(ancestor.name, "Target 1", StringComparison.OrdinalIgnoreCase)
                || ancestor.name.StartsWith("Target_Retracted", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (string.Equals(ancestor.name, "Target 2", StringComparison.OrdinalIgnoreCase)
                || ancestor.name.StartsWith("Target_Max_Extended", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            if (string.Equals(ancestor.name, "Target 3", StringComparison.OrdinalIgnoreCase)
                || ancestor.name.StartsWith("Target_Irregular", StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }
        }

        return 0;
    }

    private static Transform FindCommonAncestor(Transform first, Transform second)
    {
        if (first == null || second == null)
        {
            return null;
        }

        for (Transform ancestor = first.parent; ancestor != null; ancestor = ancestor.parent)
        {
            if (second == ancestor || second.IsChildOf(ancestor))
            {
                return ancestor;
            }
        }

        return null;
    }

    private void SetSuctionStatus(string message)
    {
        if (suctionStatusLabel != null)
        {
            suctionStatusLabel.text = message;
        }
    }

    private void RefreshReadouts()
    {
        for (int i = 0; i < PistonCount; i++)
        {
            PistonExtensionController controller = GetController(i);
            if (percentageLabels[i] == null || progressFills[i] == null || extensionSliders[i] == null)
            {
                continue;
            }

            if (controller == null)
            {
                percentageLabels[i].text = "--%";
                progressFills[i].style.width = Length.Percent(0f);
                extensionSliders[i].SetEnabled(false);
                extensionSliders[i].SetValueWithoutNotify(0f);
                continue;
            }

            float extension = controller.CurrentExtension01;

            if (float.IsNaN(extension) || float.IsInfinity(extension))
            {
                extension = 0f;
            }

            extension = Mathf.Clamp01(extension);
            percentageLabels[i].text = Mathf.RoundToInt(extension * 100f) + "%";
            progressFills[i].style.width = Length.Percent(extension * 100f);
            extensionSliders[i].SetEnabled(true);
            extensionSliders[i].SetValueWithoutNotify(Mathf.Clamp01(controller.TargetExtension01));
        }

        RefreshEndEffectorPistonReadouts();
    }

    private void RefreshEndEffectorPistonReadouts()
    {
        bool controllerAssigned = endEffectorTravelController != null && endEffectorTravelController.isActiveAndEnabled;
        for (int i = 0; i < EndEffectorPistonCount; i++)
        {
            Slider slider = endEffectorPistonSliders[i];
            Label valueLabel = endEffectorPistonValueLabels[i];
            if (slider == null || valueLabel == null)
            {
                continue;
            }

            slider.SetEnabled(controllerAssigned);
            if (!controllerAssigned)
            {
                slider.SetValueWithoutNotify(0f);
                valueLabel.text = "-- / --";
                continue;
            }

            float travel = i == 0
                ? endEffectorTravelController.Piston1TravelZ
                : endEffectorTravelController.Piston2TravelZ;
            slider.SetValueWithoutNotify(Mathf.Clamp(travel, Zero, ActuatorTravelSliderController.MaximumPistonTravel));
            valueLabel.text = $"{travel:0.00} / {ActuatorTravelSliderController.MaximumPistonTravel:0.00}";
        }
    }

    private PistonExtensionController GetController(int index)
    {
        PistonExtensionController controller = pistonControllers != null && index >= 0 && index < pistonControllers.Length
            ? pistonControllers[index]
            : null;
        return controller != null && controller.isActiveAndEnabled ? controller : null;
    }

    private static bool SetControllerTarget(PistonExtensionController controller, float value)
    {
        if (controller == null || float.IsNaN(value) || float.IsInfinity(value))
        {
            return false;
        }

        controller.SetExtension(value);
        return true;
    }

    private static void EnsureEventSystem(GameObject owner)
    {
        Type eventSystemType = FindType(EventSystemTypeName);
        if (eventSystemType == null || FindSceneComponent(eventSystemType) != null)
        {
            return;
        }

        owner.AddComponent(eventSystemType);
        Type inputModuleType = FindType(InputModuleTypeName);
        if (inputModuleType != null && FindSceneComponent(inputModuleType) == null)
        {
            owner.AddComponent(inputModuleType);
        }
    }

    private static Type FindType(string fullTypeName)
    {
        Type foundType = Type.GetType(fullTypeName);
        if (foundType != null)
        {
            return foundType;
        }

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            foundType = assemblies[i].GetType(fullTypeName);
            if (foundType != null)
            {
                return foundType;
            }
        }

        return null;
    }

    private static Component FindSceneComponent(Type componentType)
    {
        UnityEngine.Object[] components = Resources.FindObjectsOfTypeAll(componentType);
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i] as Component;
            if (component != null && component.gameObject.scene.IsValid())
            {
                return component;
            }
        }

        return null;
    }

    private void CleanupRuntimePanel()
    {
        StopMotionPlayback();
        StopAllTargetsAtCurrent();
        if (heldTarget != null)
        {
            ReleaseSuctionTarget();
        }

        rootElement = null;
        panelElement = null;
        visibilityButton = null;
        suctionButton = null;
        suctionStatusLabel = null;
        motionStatusLabel = null;
        for (int i = 0; i < PistonCount; i++)
        {
            percentageLabels[i] = null;
            progressFills[i] = null;
            extensionSliders[i] = null;
        }

        uiDocument = null;
        if (runtimeChild != null)
        {
            Destroy(runtimeChild);
            runtimeChild = null;
        }
    }
}
