using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Self-contained Play Mode teach pendant for an uncalibrated six-axis simulation.</summary>
[DisallowMultipleComponent]
public sealed class RobotTeachPendant : MonoBehaviour
{
    private const float RefreshInterval = 0.1f, MinimumPercent = 5f, MaximumPercent = 100f;
    private const float PanelWidth = 320f, LimitTolerance = 0.1f;
    private const int NoJoint = -1, NoPointer = -1, PrimaryButton = 0, WristStart = 3, SortingOrder = 100;
    private const string PanelSettingsResourcePath = "UI/RobotTeachPendantPanelSettings";
    private const string SignedAngleFormat = "+0.0;-0.0;0.0";
    private const string EventSystemTypeName = "UnityEngine.EventSystems.EventSystem, UnityEngine.UI";
    private const string InputModuleTypeName = "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem";
    private static readonly Color Charcoal = new Color(0.075f, 0.085f, 0.10f);
    private static readonly Color RowColor = new Color(0.11f, 0.125f, 0.145f);
    private static readonly Color ButtonColor = new Color(0.18f, 0.20f, 0.23f);
    private static readonly Color Gold = new Color(0.94f, 0.73f, 0.32f);
    private static readonly Color TextColor = new Color(0.91f, 0.93f, 0.95f);
    private static readonly Color Muted = new Color(0.59f, 0.64f, 0.70f);
    private static readonly Color StopColor = new Color(0.58f, 0.16f, 0.18f);

    [SerializeField, Tooltip("Six-axis simulation controller operated by this runtime pendant.")]
    private RobotSixAxisController controller;
    [SerializeField, Tooltip("Show the pendant initially. Its toggle remains available when hidden.")]
    private bool visibleByDefault = true;

    private readonly Label[] jointNames = new Label[RobotSixAxisController.JointCount];
    private readonly Label[] angles = new Label[RobotSixAxisController.JointCount];
    private readonly Label[] limits = new Label[RobotSixAxisController.JointCount];
    private readonly VisualElement[] rows = new VisualElement[RobotSixAxisController.JointCount];
    private readonly Button[] minus = new Button[RobotSixAxisController.JointCount];
    private readonly Button[] plus = new Button[RobotSixAxisController.JointCount];
    private GameObject documentObject;
    private UIDocument document;
    private PanelSettings panelSettings;
    private VisualElement pendant;
    private Label status, speedLabel, toolPosition, toolRotation;
    private Button toggle, home, heldButton;
    private Slider speed;
    private RobotSixAxisController heldController;
    private int heldJoint = NoJoint, heldPointer = NoPointer;
    private float jogSign, nextRefresh;
    private bool visible;

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        if (!documentObject)
        {
            panelSettings = Resources.Load<PanelSettings>(PanelSettingsResourcePath);
            if (panelSettings == null)
            {
                Debug.LogError("Missing robot pendant panel settings resource: " + PanelSettingsResourcePath, this);
                return;
            }

            documentObject = new GameObject("Robot Teach Pendant UI");
            documentObject.SetActive(false);
            documentObject.transform.SetParent(transform, false);
            document = documentObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.sortingOrder = SortingOrder;
            EnsureInputRouting();
        }
        documentObject.SetActive(true);
        BuildInterface();
        visible = visibleByDefault;
        ApplyVisibility();
        RefreshReadouts();
    }

    private void Update()
    {
        if (document == null) return;
        if (heldJoint != NoJoint)
        {
            if (!Application.isFocused || !controller || controller != heldController || !controller.IsConfigured)
                StopMotion();
            else
                controller.JogJoint(heldJoint, jogSign * controller.JogSpeedDegreesPerSecond * Time.unscaledDeltaTime);
        }


        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + RefreshInterval;
        RefreshReadouts();
    }

    private void OnDisable()
    {
        StopMotion();
        if (documentObject) documentObject.SetActive(false);
    }

    private void OnApplicationFocus(bool focused) { if (!focused) StopMotion(); }
    private void OnApplicationPause(bool paused) { if (paused) StopMotion(); }

    private void OnDestroy()
    {
        StopMotion();
        if (documentObject) Destroy(documentObject);
    }

    private void EnsureInputRouting()
    {
        // Unity 2022.3 needs a UI input module for new-input-only projects. Discover it
        // optionally, without package references or external action assets; preserve existing routing.
        System.Type eventSystemType = System.Type.GetType(EventSystemTypeName);
        System.Type inputModuleType = System.Type.GetType(InputModuleTypeName);
        if (eventSystemType == null || inputModuleType == null || FindAnyObjectByType(eventSystemType) != null) return;
        documentObject.AddComponent(eventSystemType);
        documentObject.AddComponent(inputModuleType); // OnEnable supplies the package's built-in default actions.
    }

    private void BuildInterface()
    {
        VisualElement root = document.rootVisualElement;
        root.Clear();
        root.pickingMode = PickingMode.Ignore;
        root.style.position = Position.Absolute;
        root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
        root.style.alignItems = Align.FlexEnd;
        root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = root.style.paddingBottom = 12;
        root.style.unityFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        root.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            StopMotion();
            evt.StopPropagation();
        }, TrickleDown.TrickleDown);

        toggle = MakeButton("", () => { StopMotion(); visible = !visible; ApplyVisibility(); });
        toggle.style.height = 26;
        toggle.style.marginBottom = 6;
        root.Add(toggle);
        pendant = new VisualElement();
        Surface(pendant, Charcoal, 12);
        pendant.style.width = PanelWidth;
        pendant.style.maxWidth = Length.Percent(100);
        pendant.style.height = Length.Percent(100);
        pendant.style.minHeight = 0;
        pendant.style.flexShrink = 1;
        Border(pendant, Gold);
        root.Add(pendant);
        pendant.Add(MakeLabel("ROBOT JOG", Gold, 20));
        pendant.Add(MakeLabel("LR Mate-inspired · SIMULATION", Muted, 11));
        Label warning = MakeLabel("Imported reference pose — not calibrated robot angles", Gold, 11);
        warning.style.marginTop = warning.style.marginBottom = 6;
        pendant.Add(warning);
        status = MakeLabel("", Muted, 11);
        pendant.Add(status);

        VisualElement commands = new VisualElement();
        commands.style.flexDirection = FlexDirection.Row;
        commands.style.marginTop = commands.style.marginBottom = 8;
        Button stop = MakeButton("STOP", StopMotion);
        stop.style.backgroundColor = StopColor;
        stop.style.height = 40;
        stop.style.fontSize = 17;
        stop.style.flexGrow = 1;
        stop.tooltip = "Cancel held jogging and all outstanding targets.";
        home = MakeButton("HOME", () => { StopMotion(); if (controller && controller.IsConfigured) controller.Home(); });
        home.style.width = 85;
        home.style.height = 40;
        home.style.marginLeft = 6;
        home.tooltip = "Smoothly approach the imported reference pose, within simulation limits.";
        commands.Add(stop);
        commands.Add(home);
        pendant.Add(commands);

        ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.flexGrow = scroll.style.flexShrink = 1;
        scroll.style.minHeight = 0;
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
        scroll.contentViewport.style.flexGrow = scroll.contentViewport.style.flexShrink = 1;
        scroll.contentViewport.style.minHeight = scroll.contentViewport.style.minWidth = 0;
        scroll.contentViewport.style.overflow = Overflow.Hidden;
        scroll.contentContainer.style.flexShrink = 0;
        VisualElement scrollLayout = scroll.Q<VisualElement>(className: "unity-scroll-view__content-and-vertical-scroll-container");
        if (scrollLayout != null) { scrollLayout.style.flexDirection = FlexDirection.Row; scrollLayout.style.flexGrow = 1; scrollLayout.style.minHeight = 0; }
        StyleScrollbar(scroll.verticalScroller);
        pendant.Add(scroll);
        for (int index = 0; index < RobotSixAxisController.JointCount; index++)
        {
            if (index == 0 || index == WristStart)
                scroll.Add(MakeLabel(index == 0 ? "MAJOR AXES · J1–J3" : "WRIST · J4–J6", Gold, 10));
            BuildJointRow(scroll, index);
        }
        speedLabel = MakeLabel("", Gold, 11);
        speedLabel.style.marginTop = 8;
        scroll.Add(speedLabel);
        speed = new Slider(MinimumPercent, MaximumPercent);
        Surface(speed, Color.clear, 0);
        speed.style.height = 24;
        StyleSlider(speed);
        speed.RegisterValueChangedCallback(evt =>
        {
            if (controller) controller.SpeedOverride = evt.newValue / MaximumPercent;
            UpdateSpeedLabel();
        });
        scroll.Add(speed);
        scroll.Add(MakeLabel("TOOL POSE · Base-local units", Gold, 11));
        toolPosition = MakeLabel("", TextColor, 11);
        toolRotation = MakeLabel("", Muted, 11);
        scroll.Add(toolPosition);
        scroll.Add(toolRotation);
    }

    private void BuildJointRow(VisualElement parent, int index)
    {
        VisualElement row = new VisualElement();
        Surface(row, RowColor, 5);
        Border(row, RowColor);
        row.style.flexDirection = FlexDirection.Row;
        row.style.marginTop = 4;
        row.style.marginBottom = 2;
        VisualElement readout = new VisualElement();
        readout.style.flexGrow = 1;
        readout.style.minWidth = 0;
        jointNames[index] = MakeLabel("", TextColor, 11);
        angles[index] = MakeLabel("", TextColor, 11);
        limits[index] = MakeLabel("", Muted, 9);
        readout.Add(jointNames[index]);
        readout.Add(angles[index]);
        readout.Add(limits[index]);
        row.Add(readout);
        minus[index] = MakeJogButton(index, -1f, "− HOLD");
        plus[index] = MakeJogButton(index, 1f, "+ HOLD");
        row.Add(minus[index]);
        row.Add(plus[index]);
        rows[index] = row;
        parent.Add(row);
    }

    private Button MakeJogButton(int index, float sign, string text)
    {
        Button button = MakeButton(text, null);
        button.style.width = 54;
        button.style.height = 40;
        button.style.paddingLeft = button.style.paddingRight = 2;
        button.style.marginLeft = 4;
        button.tooltip = "Hold to jog; releasing immediately cancels outstanding motion.";
        button.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != PrimaryButton || heldJoint != NoJoint || !Application.isFocused || !controller || !controller.IsConfigured) return;
            evt.StopImmediatePropagation();
            StopMotion();
            heldController = controller;
            heldJoint = index;
            jogSign = sign;
            heldPointer = evt.pointerId;
            heldButton = button;
            button.Focus();
            button.CapturePointer(heldPointer);
            nextRefresh = 0;
        }, TrickleDown.TrickleDown);
        button.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (heldButton != button || evt.pointerId != heldPointer) return;
            evt.StopImmediatePropagation();
            StopHeldMotion();
        }, TrickleDown.TrickleDown);
        button.RegisterCallback<PointerCaptureOutEvent>(evt => { if (heldButton == button && evt.pointerId == heldPointer) StopHeldMotion(); });
        button.RegisterCallback<PointerCancelEvent>(evt => { if (heldButton == button && evt.pointerId == heldPointer) StopHeldMotion(); });
        return button;
    }

    private void StopMotion()
    {
        // Edit Mode instances must never change serialized motion targets.
        if (!panelSettings && !heldController) return;
        StopHeldMotion();
        if (controller && controller.IsConfigured) controller.StopMotion();
        nextRefresh = 0;
    }

    private void StopHeldMotion()
    {
        Button captured = heldButton;
        int pointer = heldPointer;
        RobotSixAxisController previousController = heldController;
        heldJoint = NoJoint;
        heldPointer = NoPointer;
        heldButton = null;
        heldController = null;
        jogSign = 0;
        if (previousController && previousController.IsConfigured) previousController.StopMotion();
        if (captured != null && captured.HasPointerCapture(pointer)) captured.ReleasePointer(pointer);
        nextRefresh = 0;
    }

    private void ApplyVisibility()
    {
        pendant.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        toggle.text = visible ? "HIDE PENDANT" : "SHOW ROBOT JOG";
    }

    private void RefreshReadouts()
    {
        if (status == null) return;
        bool configured = controller && controller.IsConfigured;
        status.text = !configured ? "● CONFIGURATION MISSING · Jog / Home disabled"
            : heldJoint != NoJoint ? "● HOLDING · release to stop"
            : controller.IsMoving ? "● MOVING TO TARGET" : "● READY · hold − / + to jog";
        status.style.color = configured ? Muted : Gold;
        home.SetEnabled(configured);
        speed.SetEnabled(configured);
        home.style.opacity = speed.style.opacity = configured ? 1f : 0.35f;
        for (int index = 0; index < RobotSixAxisController.JointCount; index++)
        {
            minus[index].SetEnabled(configured);
            plus[index].SetEnabled(configured);
            minus[index].style.opacity = plus[index].style.opacity = configured ? 1f : 0.35f;
            jointNames[index].text = controller ? controller.GetJointLabel(index) : $"J{index + 1}";
            if (!configured) { angles[index].text = "ACT —   TGT —"; limits[index].text = "Simulation limits unavailable"; Border(rows[index], RowColor); angles[index].style.color = Muted; continue; }
            float actual = controller.GetCurrentAngle(index);
            Vector2 bounds = controller.GetLimits(index);
            bool atLimit = actual <= bounds.x + LimitTolerance || actual >= bounds.y - LimitTolerance;
            angles[index].text = $"ACT {Angle(actual)}°   TGT {Angle(controller.GetTargetAngle(index))}°";
            limits[index].text = $"Limits {Angle(bounds.x)}° … {Angle(bounds.y)}°{(atLimit ? " · LIMIT" : "")}";
            angles[index].style.color = atLimit ? Gold : TextColor;
            Border(rows[index], atLimit || heldJoint == index ? Gold : RowColor);
        }
        speed.SetValueWithoutNotify(controller ? controller.SpeedOverride * MaximumPercent : MinimumPercent);
        UpdateSpeedLabel();
        if (configured && controller.TryGetToolPoseInBase(out Vector3 position, out Vector3 rotation))
        {
            toolPosition.text = string.Format(CultureInfo.InvariantCulture, "X {0:+0.000;-0.000;0.000}   Y {1:+0.000;-0.000;0.000}   Z {2:+0.000;-0.000;0.000}", position.x, position.y, position.z);
            toolRotation.text = $"Euler °   X {Angle(rotation.x)}   Y {Angle(rotation.y)}   Z {Angle(rotation.z)}";
        }
        else { toolPosition.text = "Tool frame unavailable"; toolRotation.text = "Euler °   —"; }
    }

    private void UpdateSpeedLabel() => speedLabel.text = $"SPEED OVERRIDE · {speed.value.ToString("0", CultureInfo.InvariantCulture)}%";
    private static string Angle(float value) => value.ToString(SignedAngleFormat, CultureInfo.InvariantCulture);

    private static Label MakeLabel(string text, Color color, int fontSize)
    {
        Label label = new Label(text) { pickingMode = PickingMode.Ignore };
        Surface(label, Color.clear, 0);
        label.style.color = color;
        label.style.fontSize = fontSize;
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.flexShrink = 0;
        return label;
    }

    private static Button MakeButton(string text, System.Action clicked)
    {
        Button button = new Button { text = text };
        if (clicked != null) button.clicked += clicked;
        Surface(button, ButtonColor, 6);
        Border(button, ButtonColor);
        button.style.color = TextColor;
        button.style.fontSize = 11;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.style.flexShrink = 0;
        button.RegisterCallback<PointerEnterEvent>(_ => button.style.borderTopColor = button.style.borderBottomColor = button.style.borderLeftColor = button.style.borderRightColor = Gold);
        button.RegisterCallback<PointerLeaveEvent>(_ => Border(button, ButtonColor));
        return button;
    }

    private static void Surface(VisualElement element, Color background, int padding)
    {
        element.style.backgroundColor = background;
        element.style.paddingLeft = element.style.paddingRight = element.style.paddingTop = element.style.paddingBottom = padding;
        element.style.marginLeft = element.style.marginRight = element.style.marginTop = element.style.marginBottom = 0;
        element.style.borderTopLeftRadius = element.style.borderTopRightRadius = element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = 4;
    }

    private static void Border(VisualElement element, Color color)
    {
        element.style.borderLeftWidth = element.style.borderRightWidth = element.style.borderTopWidth = element.style.borderBottomWidth = 1;
        element.style.borderLeftColor = element.style.borderRightColor = element.style.borderTopColor = element.style.borderBottomColor = color;
    }

    private static void StyleSlider(Slider slider)
    {
        VisualElement input = slider.Q<VisualElement>(className: "unity-base-slider__input");
        if (input != null) { input.style.flexGrow = 1; input.style.minWidth = 0; }
        VisualElement container = slider.Q<VisualElement>("unity-drag-container");
        if (container != null) { container.style.flexGrow = 1; container.style.height = 24; }
        VisualElement track = slider.Q<VisualElement>("unity-tracker");
        if (track != null) { Surface(track, ButtonColor, 0); track.style.position = Position.Absolute; track.style.left = track.style.right = 0; track.style.top = 10; track.style.height = 4; }
        VisualElement thumb = slider.Q<VisualElement>("unity-dragger");
        if (thumb != null) { Surface(thumb, Gold, 0); thumb.style.position = Position.Absolute; thumb.style.top = 4; thumb.style.width = thumb.style.height = 16; }
    }

    private static void StyleScrollbar(Scroller scroller)
    {
        scroller.style.width = 8;
        scroller.style.marginLeft = 4;
        scroller.style.flexDirection = FlexDirection.Column;
        scroller.lowButton.style.display = scroller.highButton.style.display = DisplayStyle.None;
        scroller.slider.style.flexGrow = 1;
        VisualElement input = scroller.slider.Q<VisualElement>(className: "unity-base-slider__input");
        if (input != null) { input.style.flexGrow = 1; input.style.minHeight = 0; }
        VisualElement container = scroller.slider.Q<VisualElement>("unity-drag-container");
        if (container != null) { container.style.flexGrow = 1; container.style.width = 8; }
        VisualElement thumb = scroller.slider.Q<VisualElement>("unity-dragger");
        if (thumb != null) { Surface(thumb, Muted, 0); thumb.style.position = Position.Absolute; thumb.style.width = 8; }
    }
}
