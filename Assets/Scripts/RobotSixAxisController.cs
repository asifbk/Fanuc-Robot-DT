using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Six local rotational DOFs for an LR Mate-inspired simulation, not a calibrated robot controller.</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class RobotSixAxisController : MonoBehaviour
{
    public const int JointCount = 6;
    private const float Zero = 0f, Unit = 1f;
    private const float DefaultSpeed = 30f, DefaultOverride = 0.25f;
    private const float MinimumOverride = 0.05f, MaximumOverride = Unit;
    private const float AngleTolerance = 0.0001f, VectorSquaredTolerance = 0.00000001f;
    private const float RotationSquaredTolerance = 0.000000000001f;
    private const float Joint1Minimum = -170f, Joint1Maximum = 170f;
    private const float Joint2Minimum = -60f, Joint2Maximum = 140f;
    private const float Joint3Minimum = -142f, Joint3Maximum = 230f;
    private const float Joint4Minimum = -190f, Joint4Maximum = 190f;
    private const float Joint5Minimum = -120f, Joint5Maximum = 120f;
    private const float Joint6Minimum = -360f, Joint6Maximum = 360f;
    private const string LimitsTooltip = "Editable simulation bounds in degrees relative to the imported reference pose; not manufacturer-mastered joint limits.";
    private const string AxisTooltip = "Single rotational axis in the joint's zero-pose local coordinates. Normalized when applied.";
    private const string ZeroPoseTooltip = "Explicit imported zero-pose local Euler rotation. Applied before the joint's axis rotation.";

    [SerializeField, Tooltip("Fixed reference frame. This controller never changes its transform.")] private Transform baseReference;
    [SerializeField, Tooltip("Optional tool frame below Base, used to report a pose in Base coordinates.")] private Transform toolFrame;
    [SerializeField] private Transform joint1;
    [SerializeField] private Transform joint2;
    [SerializeField] private Transform joint3;
    [SerializeField] private Transform joint4;
    [SerializeField] private Transform joint5;
    [SerializeField] private Transform joint6;
    [SerializeField, Tooltip(AxisTooltip)] private Vector3 axis1 = Vector3.up;
    [SerializeField, Tooltip(AxisTooltip)] private Vector3 axis2 = Vector3.forward;
    [SerializeField, Tooltip(AxisTooltip)] private Vector3 axis3 = Vector3.forward;
    [SerializeField, Tooltip(AxisTooltip)] private Vector3 axis4 = Vector3.left;
    [SerializeField, Tooltip(AxisTooltip)] private Vector3 axis5 = Vector3.forward;
    [SerializeField, Tooltip(AxisTooltip)] private Vector3 axis6 = Vector3.down;
    [SerializeField, Tooltip(ZeroPoseTooltip)] private Vector3 zeroEuler1 = Vector3.zero;
    [SerializeField, Tooltip(ZeroPoseTooltip)] private Vector3 zeroEuler2 = Vector3.zero;
    [SerializeField, Tooltip(ZeroPoseTooltip)] private Vector3 zeroEuler3 = Vector3.zero;
    [SerializeField, Tooltip(ZeroPoseTooltip)] private Vector3 zeroEuler4 = Vector3.zero;
    [SerializeField, Tooltip(ZeroPoseTooltip)] private Vector3 zeroEuler5 = Vector3.zero;
    [SerializeField, Tooltip(ZeroPoseTooltip)] private Vector3 zeroEuler6 = Vector3.zero;
    [SerializeField, Range(Joint1Minimum, Joint1Maximum)] private float angle1Degrees = Zero;
    [SerializeField, Range(Joint2Minimum, Joint2Maximum)] private float angle2Degrees = Zero;
    [SerializeField, Range(Joint3Minimum, Joint3Maximum)] private float angle3Degrees = Zero;
    [SerializeField, Range(Joint4Minimum, Joint4Maximum)] private float angle4Degrees = Zero;
    [SerializeField, Range(Joint5Minimum, Joint5Maximum)] private float angle5Degrees = Zero;
    [SerializeField, Range(Joint6Minimum, Joint6Maximum)] private float angle6Degrees = Zero;
    [SerializeField, Tooltip(LimitsTooltip)] private Vector2 limits1 = new Vector2(Joint1Minimum, Joint1Maximum);
    [SerializeField, Tooltip(LimitsTooltip)] private Vector2 limits2 = new Vector2(Joint2Minimum, Joint2Maximum);
    [SerializeField, Tooltip(LimitsTooltip)] private Vector2 limits3 = new Vector2(Joint3Minimum, Joint3Maximum);
    [SerializeField, Tooltip(LimitsTooltip)] private Vector2 limits4 = new Vector2(Joint4Minimum, Joint4Maximum);
    [SerializeField, Tooltip(LimitsTooltip)] private Vector2 limits5 = new Vector2(Joint5Minimum, Joint5Maximum);
    [SerializeField, Tooltip(LimitsTooltip)] private Vector2 limits6 = new Vector2(Joint6Minimum, Joint6Maximum);
    [SerializeField, Min(Zero), Tooltip("Full-speed angular rate. Runtime movement uses unscaledDeltaTime, independently of timeScale.")]
    private float speedDegreesPerSecond = DefaultSpeed;
    [SerializeField, Range(MinimumOverride, MaximumOverride)] private float speedOverride = DefaultOverride;
    [SerializeField, Tooltip("Apply targets instantly in Edit Mode. Explicit immediate commands also work when preview is disabled.")]
    private bool enableEditModePreview = true;

    // Allocated once per component; reference checks and updates do not allocate each frame.
    [NonSerialized] private readonly float[] currentAngles = new float[JointCount];
    [NonSerialized] private readonly Transform[] cachedJoints = new Transform[JointCount];
    [NonSerialized] private readonly List<Transform> cachedAncestors = new List<Transform>();
    [NonSerialized] private readonly List<Transform> cachedParents = new List<Transform>();
    [NonSerialized] private Transform cachedBase;
    [NonSerialized] private bool signatureChecked, configurationValid, initialized;
    [NonSerialized] private bool safetyInterlocked;
    [NonSerialized] private DcsSafetyInterlock dcsInterlock;

    /// <summary>Whether DCS currently blocks new joint motion commands.</summary>
    public bool IsSafetyInterlocked => safetyInterlocked;

    /// <summary>Whether six unique joints form an ordered descendant chain below the fixed Base.</summary>
    public bool IsConfigured { get { EnsureState(); return configurationValid; } }
    /// <summary>Gets the fixed Base reference without modifying it.</summary>
    public Transform BaseReference => baseReference;
    /// <summary>Gets the optional tool pose reference.</summary>
    public Transform ToolFrame => toolFrame;
    /// <summary>Gets or sets the finite, bounded runtime speed multiplier.</summary>
    public float SpeedOverride
    {
        get => speedOverride;
        set => speedOverride = Mathf.Clamp(FiniteOr(value, DefaultOverride), MinimumOverride, MaximumOverride);
    }
    /// <summary>Angular rate to use for signed, held-button jog increments.</summary>
    public float JogSpeedDegreesPerSecond => speedDegreesPerSecond * speedOverride;
    /// <summary>Whether a valid playing-world controller has outstanding target motion.</summary>
    public bool IsMoving
    {
        get
        {
            EnsureState();
            if (!configurationValid || !Application.IsPlaying(gameObject)) return false;
            for (int index = 0; index < JointCount; index++)
                if (Mathf.Abs(currentAngles[index] - ReadTarget(index)) > AngleTolerance) return true;
            return false;
        }
    }

    private void Update()
    {
        EnsureState();
        bool isPlaying = Application.IsPlaying(gameObject);
        if (!configurationValid || (!isPlaying && !enableEditModePreview)) return;
        float maximumStep = JogSpeedDegreesPerSecond * Time.unscaledDeltaTime;
        for (int index = 0; index < JointCount; index++)
        {
            float target = ClampAngle(index, ReadTarget(index));
            WriteTarget(index, target);
            currentAngles[index] = ClampAngle(index, currentAngles[index]);
            float proposedAngle = isPlaying
                ? Mathf.MoveTowards(currentAngles[index], target, maximumStep)
                : target;
            TryApplyJointAngle(index, proposedAngle);
        }
    }

    private void OnValidate()
    {
        // Numeric validation only: Unity may invoke this outside the main thread.
        speedDegreesPerSecond = Mathf.Max(Zero, FiniteOr(speedDegreesPerSecond, DefaultSpeed));
        speedOverride = Mathf.Clamp(FiniteOr(speedOverride, DefaultOverride), MinimumOverride, MaximumOverride);
        ValidateJointNumbers(ref axis1, Vector3.up, ref zeroEuler1, ref limits1, Joint1Minimum, Joint1Maximum, ref angle1Degrees);
        ValidateJointNumbers(ref axis2, Vector3.forward, ref zeroEuler2, ref limits2, Joint2Minimum, Joint2Maximum, ref angle2Degrees);
        ValidateJointNumbers(ref axis3, Vector3.forward, ref zeroEuler3, ref limits3, Joint3Minimum, Joint3Maximum, ref angle3Degrees);
        ValidateJointNumbers(ref axis4, Vector3.left, ref zeroEuler4, ref limits4, Joint4Minimum, Joint4Maximum, ref angle4Degrees);
        ValidateJointNumbers(ref axis5, Vector3.forward, ref zeroEuler5, ref limits5, Joint5Minimum, Joint5Maximum, ref angle5Degrees);
        ValidateJointNumbers(ref axis6, Vector3.down, ref zeroEuler6, ref limits6, Joint6Minimum, Joint6Maximum, ref angle6Degrees);
    }

    /// <summary>Returns the current simulated angle for a zero-based joint; invalid indices throw.</summary>
    public float GetCurrentAngle(int zeroBasedJoint)
    {
        ValidateIndex(zeroBasedJoint);
        EnsureState();
        return currentAngles[zeroBasedJoint];
    }
    /// <summary>Returns the bounded target angle for a zero-based joint; invalid indices throw.</summary>
    public float GetTargetAngle(int zeroBasedJoint)
    {
        ValidateIndex(zeroBasedJoint);
        return ClampAngle(zeroBasedJoint, ReadTarget(zeroBasedJoint));
    }
    /// <summary>Returns editable simulation bounds, not calibrated manufacturer joint limits.</summary>
    public Vector2 GetLimits(int zeroBasedJoint)
    {
        ValidateIndex(zeroBasedJoint);
        return ReadLimits(zeroBasedJoint);
    }
    /// <summary>Returns a descriptive UI label for a zero-based joint; invalid indices throw.</summary>
    public string GetJointLabel(int zeroBasedJoint)
    {
        ValidateIndex(zeroBasedJoint);
        return zeroBasedJoint switch
        {
            0 => "J1 · Base swivel", 1 => "J2 · Shoulder", 2 => "J3 · Elbow",
            3 => "J4 · Forearm roll", 4 => "J5 · Wrist bend", _ => "J6 · Tool roll"
        };
    }
    /// <summary>Sets a bounded target. Playing worlds approach it using unscaled time; Edit Mode applies instantly on update.</summary>
    public void SetJointTarget(int zeroBasedJoint, float angleDegrees)
    {
        ValidateIndex(zeroBasedJoint);
        ValidateFinite(angleDegrees, nameof(angleDegrees));
        EnsureState();
        if (IsMotionBlocked) return;
        WriteTarget(zeroBasedJoint, ClampAngle(zeroBasedJoint, angleDegrees));
    }
    /// <summary>Sets both current and target angles immediately in Edit or Play Mode, if references are valid.</summary>
    public void SetJointImmediate(int zeroBasedJoint, float angleDegrees)
    {
        ValidateIndex(zeroBasedJoint);
        ValidateFinite(angleDegrees, nameof(angleDegrees));
        EnsureState();
        if (IsMotionBlocked) return;
        TryApplyJointAngle(zeroBasedJoint, ClampAngle(zeroBasedJoint, angleDegrees));
    }
    /// <summary>Jogs from the current simulated angle, avoiding queued motion and leaving other targets untouched.</summary>
    public void JogJoint(int zeroBasedJoint, float deltaDegrees)
    {
        ValidateIndex(zeroBasedJoint);
        ValidateFinite(deltaDegrees, nameof(deltaDegrees));
        EnsureState();
        if (IsMotionBlocked) return;
        WriteTarget(zeroBasedJoint, ClampAngle(zeroBasedJoint, currentAngles[zeroBasedJoint] + deltaDegrees));
    }
    /// <summary>Cancels all outstanding motion by setting every target to its current simulated angle.</summary>
    public void StopMotion()
    {
        EnsureState();
        for (int index = 0; index < JointCount; index++) WriteTarget(index, ClampAngle(index, currentAngles[index]));
    }
    /// <summary>Enables or clears the DCS motion interlock; locking immediately stops queued joint movement.</summary>
    public void SetSafetyInterlocked(bool isInterlocked)
    {
        safetyInterlocked = isInterlocked;
        if (safetyInterlocked && Application.IsPlaying(gameObject)) StopMotion();
    }
    /// <summary>Targets the explicit zero pose, clamped to each joint's editable simulation bounds.</summary>
    public void Home()
    {
        EnsureState();
        if (IsMotionBlocked) return;
        for (int index = 0; index < JointCount; index++) WriteTarget(index, ClampAngle(index, Zero));
    }
    /// <summary>Applies the bounded zero pose immediately in Edit or Play Mode when configured.</summary>
    public void HomeImmediate()
    {
        EnsureState();
        if (IsMotionBlocked) return;
        Home();
        for (int index = 0; index < JointCount; index++)
            TryApplyJointAngle(index, ReadTarget(index));
    }
    /// <summary>Reports tool position and Euler orientation relative to Base; returns false for missing or invalid references.</summary>
    public bool TryGetToolPoseInBase(out Vector3 position, out Vector3 eulerAngles)
    {
        position = eulerAngles = Vector3.zero;
        EnsureState();
        if (!configurationValid || !toolFrame || !toolFrame.IsChildOf(baseReference)) return false;
        position = baseReference.InverseTransformPoint(toolFrame.position);
        eulerAngles = (Quaternion.Inverse(baseReference.rotation) * toolFrame.rotation).eulerAngles;
        return true;
    }

    private bool IsMotionBlocked => safetyInterlocked && Application.IsPlaying(gameObject);

    private void EnsureState()
    {
        bool changed = !signatureChecked || cachedBase != baseReference || (configurationValid && !baseReference);
        for (int index = 0; index < JointCount; index++) changed |= cachedJoints[index] != ReadJoint(index);
        for (int index = 0; index < cachedAncestors.Count && !changed; index++)
            changed = !cachedAncestors[index] || cachedAncestors[index].parent != cachedParents[index];
        // Parent snapshots detect reparenting and destruction, including intervening link objects.
        // Rebuild and validate only when this reference/hierarchy signature changes.
        if (changed)
        {
            cachedAncestors.Clear();
            cachedParents.Clear();
            cachedBase = baseReference;
            configurationValid = baseReference;
            Transform previous = baseReference;
            for (int index = 0; index < JointCount; index++)
            {
                Transform joint = ReadJoint(index);
                cachedJoints[index] = joint;
                if (!joint || !previous || joint == previous || !joint.IsChildOf(previous)) configurationValid = false;
                for (Transform ancestor = joint; ancestor && ancestor != baseReference; ancestor = ancestor.parent)
                {
                    cachedAncestors.Add(ancestor);
                    cachedParents.Add(ancestor.parent);
                }
                previous = joint;
            }
            signatureChecked = true;
            initialized = false;
        }
        if (initialized) return;
        for (int index = 0; index < JointCount; index++) currentAngles[index] = ClampAngle(index, ReadTarget(index));
        initialized = true;
    }

    private bool TryApplyJointAngle(int index, float proposedAngle)
    {
        DcsSafetyInterlock activeDcs = dcsInterlock != null
            ? dcsInterlock
            : FindFirstObjectByType<DcsSafetyInterlock>();
        dcsInterlock = activeDcs;
        bool enforceEnvelope = activeDcs != null && activeDcs.IsEnabled;
        if (!configurationValid || (enforceEnvelope && !toolFrame))
        {
            WriteTarget(index, currentAngles[index]);
            return false;
        }

        float previousAngle = currentAngles[index];
        float boundedAngle = ClampAngle(index, proposedAngle);
        Vector3 previousToolPosition = enforceEnvelope ? toolFrame.position : Vector3.zero;
        currentAngles[index] = boundedAngle;
        ApplyJoint(index);

        if (enforceEnvelope && !activeDcs.IsToolMovementInsideEnvelope(previousToolPosition, toolFrame.position))
        {
            currentAngles[index] = previousAngle;
            ApplyJoint(index);
            WriteTarget(index, previousAngle);
            return false;
        }

        WriteTarget(index, boundedAngle);
        return true;
    }

    private void ApplyJoint(int index)
    {
        Quaternion desired = Quaternion.Euler(ReadZeroEuler(index)) * Quaternion.AngleAxis(currentAngles[index], ReadAxis(index).normalized);
        Quaternion actual = cachedJoints[index].localRotation;
        float sign = Quaternion.Dot(actual, desired) < Zero ? -Unit : Unit;
        float differenceSquared = Square(actual.x - desired.x * sign) + Square(actual.y - desired.y * sign)
            + Square(actual.z - desired.z * sign) + Square(actual.w - desired.w * sign);
        if (differenceSquared > RotationSquaredTolerance) cachedJoints[index].localRotation = desired;
    }

    private static void ValidateJointNumbers(ref Vector3 axis, Vector3 defaultAxis, ref Vector3 zeroEuler,
        ref Vector2 limits, float defaultMinimum, float defaultMaximum, ref float target)
    {
        if (!IsFinite(axis) || !IsFinite(axis.sqrMagnitude) || axis.sqrMagnitude < VectorSquaredTolerance) axis = defaultAxis;
        if (!IsFinite(zeroEuler)) zeroEuler = Vector3.zero;
        if (!IsFinite(limits.x) || !IsFinite(limits.y)) limits = new Vector2(defaultMinimum, defaultMaximum);
        if (limits.x > limits.y) limits = new Vector2(limits.y, limits.x);
        target = Mathf.Clamp(FiniteOr(target, Zero), limits.x, limits.y);
    }
    private float ClampAngle(int index, float angle)
    {
        Vector2 limits = ReadLimits(index);
        return Mathf.Clamp(angle, limits.x, limits.y);
    }
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    private static float FiniteOr(float value, float fallback) => IsFinite(value) ? value : fallback;
    private static float Square(float value) => value * value;
    private static void ValidateFinite(float value, string parameterName)
    {
        if (!IsFinite(value)) throw new ArgumentOutOfRangeException(parameterName, "Angle must be finite.");
    }
    private static void ValidateIndex(int index)
    {
        if (index < 0 || index >= JointCount) throw new ArgumentOutOfRangeException(nameof(index), index, "Joint index must be from zero through five.");
    }
    private Transform ReadJoint(int index) => index switch
    {
        0 => joint1, 1 => joint2, 2 => joint3, 3 => joint4, 4 => joint5, _ => joint6
    };
    private Vector3 ReadAxis(int index) => index switch
    {
        0 => axis1, 1 => axis2, 2 => axis3, 3 => axis4, 4 => axis5, _ => axis6
    };
    private Vector3 ReadZeroEuler(int index) => index switch
    {
        0 => zeroEuler1, 1 => zeroEuler2, 2 => zeroEuler3, 3 => zeroEuler4, 4 => zeroEuler5, _ => zeroEuler6
    };
    private Vector2 ReadLimits(int index) => index switch
    {
        0 => limits1, 1 => limits2, 2 => limits3, 3 => limits4, 4 => limits5, _ => limits6
    };
    private float ReadTarget(int index) => index switch
    {
        0 => angle1Degrees, 1 => angle2Degrees, 2 => angle3Degrees,
        3 => angle4Degrees, 4 => angle5Degrees, _ => angle6Degrees
    };
    private void WriteTarget(int index, float value)
    {
        switch (index)
        {
            case 0: angle1Degrees = value; break;
            case 1: angle2Degrees = value; break;
            case 2: angle3Degrees = value; break;
            case 3: angle4Degrees = value; break;
            case 4: angle5Degrees = value; break;
            default: angle6Degrees = value; break;
        }
    }
}
