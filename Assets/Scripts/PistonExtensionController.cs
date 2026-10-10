using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public class PistonExtensionController : MonoBehaviour
{
    private const float DefaultStrokeLength = 0.024f;
    private const float DefaultMovementSpeed = 0.02f;

    [SerializeField] private Transform movingRod;
    [SerializeField] private Vector3 retractedLocalPosition = Vector3.zero;
    [SerializeField] private Vector3 extensionAxis = Vector3.down;
    [SerializeField, Min(0)] private float strokeLength = DefaultStrokeLength;
    [SerializeField, Range(0, 1)] private float extension01 = 0;
    [SerializeField, Min(0)] private float movementSpeed = DefaultMovementSpeed;

    private float currentExtension01;
    private bool safetyInterlocked;

    /// <summary>Gets whether DCS currently blocks piston extension commands.</summary>
    public bool IsSafetyInterlocked => safetyInterlocked;

    /// <summary>Gets the actual normalized extension, independently of the target.</summary>
    public float CurrentExtension01 => currentExtension01;

    /// <summary>Gets the normalized extension currently commanded by the controller.</summary>
    public float TargetExtension01 => extension01;

    private void OnEnable()
    {
        ValidateNumericFields();
        currentExtension01 = extension01;
        ApplyRodPosition();
    }

    private void OnValidate()
    {
        // Validation may run during loading or off the main thread: no transforms here.
        ValidateNumericFields();
    }

    private void Update()
    {
        ValidateNumericFields();
        // Edit previews, zero-length strokes, and zero speed snap to the target.
        if (!Application.IsPlaying(gameObject) || strokeLength == 0 || movementSpeed == 0)
            currentExtension01 = extension01;
        else
            currentExtension01 = Mathf.MoveTowards(currentExtension01, extension01,
                movementSpeed / strokeLength * Time.deltaTime);

        ApplyRodPosition();
    }

    /// <summary>Sets the clamped target extension; play mode moves toward it smoothly.</summary>
    public void SetExtension(float normalizedExtension)
    {
        if (IsMotionBlocked) return;
        extension01 = ClampExtension(normalizedExtension);
    }

    /// <summary>Targets the fully extended position.</summary>
    public void Extend()
    {
        SetExtension(1);
    }

    /// <summary>Targets the fully retracted position.</summary>
    public void Retract()
    {
        SetExtension(0);
    }

    /// <summary>Sets both target and actual extension immediately, bypassing movement speed.</summary>
    public void SetExtensionImmediate(float normalizedExtension)
    {
        if (IsMotionBlocked) return;
        extension01 = ClampExtension(normalizedExtension);
        currentExtension01 = extension01;
        ValidateNumericFields();
        ApplyRodPosition();
    }

    /// <summary>Enables or clears the DCS motion interlock; locking cancels extension targets at the current position.</summary>
    public void SetSafetyInterlocked(bool isInterlocked)
    {
        safetyInterlocked = isInterlocked;
        if (safetyInterlocked)
        {
            extension01 = currentExtension01;
        }
    }

    private bool IsMotionBlocked => safetyInterlocked && Application.IsPlaying(gameObject);

    private void ApplyRodPosition()
    {
        // Never move the controller itself or a rod without a local reference frame.
        if (movingRod == null || movingRod == transform || movingRod.parent == null)
            return;

        Vector3 rodLocalPosition = retractedLocalPosition
            + extensionAxis.normalized * strokeLength * currentExtension01;
        if (IsFinite(rodLocalPosition) && !movingRod.localPosition.Equals(rodLocalPosition))
            movingRod.localPosition = rodLocalPosition;
    }

    private void ValidateNumericFields()
    {
        strokeLength = NonNegativeFinite(strokeLength);
        movementSpeed = NonNegativeFinite(movementSpeed);
        extension01 = ClampExtension(extension01);
        if (!IsFinite(retractedLocalPosition))
            retractedLocalPosition = Vector3.zero;
        if (!IsFinite(extensionAxis))
            extensionAxis = Vector3.down;
    }

    private static float ClampExtension(float value)
    {
        return IsFinite(value) ? Mathf.Clamp01(value) : 0;
    }

    private static float NonNegativeFinite(float value)
    {
        return IsFinite(value) ? Mathf.Max(0, value) : 0;
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
