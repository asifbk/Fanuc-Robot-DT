using UnityEngine;
using UnityEngine.Serialization;

/// <summary>Provides smooth local-axis travel controls for the end-effector pistons and actuator barrel.</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class ActuatorTravelSliderController : MonoBehaviour
{
    public const float MaximumPistonTravel = 0.6f;
    public const float MaximumBarrelTravel = 46f;
    private const float DefaultPistonSpeed = 0.25f;
    private const float DefaultPiston2TravelScale = 100f;
    private const float DefaultBarrelSpeed = 20f;
    private const float PositionSquaredTolerance = 0.00000001f;
    private const float Zero = 0f;

    [Header("Piston 1 · Local Z")]
    [SerializeField] private Transform piston1;
    [SerializeField] private Vector3 piston1StartLocalPosition;
    [SerializeField, FormerlySerializedAs("piston1TravelX"), Range(Zero, MaximumPistonTravel)] private float piston1TravelZ;
    [SerializeField, Min(Zero)] private float piston1Speed = DefaultPistonSpeed;

    [Header("Piston 2 · Local Z")]
    [SerializeField] private Transform piston2;
    [SerializeField] private Vector3 piston2StartLocalPosition;
    [SerializeField, FormerlySerializedAs("piston2TravelX"), Range(Zero, MaximumPistonTravel)] private float piston2TravelZ;
    [SerializeField, Min(Zero)] private float piston2Speed = DefaultPistonSpeed;
    [SerializeField, Min(Zero), Tooltip("Compensates Piston 2 travel for its imported 100x mesh scale so the slider produces a visible stroke.")]
    private float piston2TravelScale = DefaultPiston2TravelScale;

    [Header("Barrel · Local X")]
    [SerializeField] private Transform barrel;
    [SerializeField] private Vector3 barrelStartLocalPosition;
    [SerializeField, Range(Zero, MaximumBarrelTravel)] private float barrelTravelX;
    [SerializeField, Min(Zero)] private float barrelSpeed = DefaultBarrelSpeed;

    /// <summary>Gets the Transform driven by piston 1.</summary>
    public Transform Piston1Target => piston1;

    /// <summary>Gets the commanded local-Z travel of piston 1.</summary>
    public float Piston1TravelZ => piston1TravelZ;

    /// <summary>Gets the commanded local-Z travel of piston 2.</summary>
    public float Piston2TravelZ => piston2TravelZ;

    /// <summary>Sets piston 1's local-Z travel within its configured range.</summary>
    public void SetPiston1TravelZ(float travel)
    {
        piston1TravelZ = Mathf.Clamp(travel, Zero, MaximumPistonTravel);
    }

    /// <summary>Sets piston 1's local-Z travel and immediately applies its transform position.</summary>
    public void SetPiston1TravelZImmediate(float travel)
    {
        SetPiston1TravelZ(travel);
        MoveAlongLocalAxis(piston1, piston1StartLocalPosition, Vector3.forward, piston1TravelZ, piston1Speed, Zero);
    }

    /// <summary>Sets piston 2's local-Z travel within its configured range.</summary>
    public void SetPiston2TravelZ(float travel)
    {
        piston2TravelZ = Mathf.Clamp(travel, Zero, MaximumPistonTravel);
    }

    /// <summary>Sets piston 2's local-Z travel and immediately applies its transform position.</summary>
    public void SetPiston2TravelZImmediate(float travel)
    {
        SetPiston2TravelZ(travel);
        Vector3 piston2ParentLocalAxis = piston2 != null
            ? piston2.localRotation * Vector3.forward
            : Vector3.forward;
        MoveAlongLocalAxis(piston2, piston2StartLocalPosition, piston2ParentLocalAxis,
            piston2TravelZ * piston2TravelScale, piston2Speed * piston2TravelScale, Zero);
    }

    private void OnValidate()
    {
        piston1TravelZ = Mathf.Clamp(piston1TravelZ, Zero, MaximumPistonTravel);
        piston2TravelZ = Mathf.Clamp(piston2TravelZ, Zero, MaximumPistonTravel);
        barrelTravelX = Mathf.Clamp(barrelTravelX, Zero, MaximumBarrelTravel);
        piston1Speed = Mathf.Max(Zero, piston1Speed);
        piston2Speed = Mathf.Max(Zero, piston2Speed);
        piston2TravelScale = Mathf.Max(Zero, piston2TravelScale);
        barrelSpeed = Mathf.Max(Zero, barrelSpeed);
    }

    private void Update()
    {
        bool isPlaying = Application.IsPlaying(gameObject);
        float deltaTime = isPlaying ? Time.deltaTime : Zero;

        MoveAlongLocalAxis(piston1, piston1StartLocalPosition, Vector3.forward, piston1TravelZ, piston1Speed, deltaTime);
        Vector3 piston2ParentLocalAxis = piston2 != null
            ? piston2.localRotation * Vector3.forward
            : Vector3.forward;
        MoveAlongLocalAxis(piston2, piston2StartLocalPosition, piston2ParentLocalAxis,
            piston2TravelZ * piston2TravelScale, piston2Speed * piston2TravelScale, deltaTime);
        MoveAlongLocalAxis(barrel, barrelStartLocalPosition, Vector3.right, barrelTravelX, barrelSpeed, deltaTime);
    }

    private static void MoveAlongLocalAxis(Transform target, Vector3 startPosition, Vector3 localAxis,
        float travel, float speed, float deltaTime)
    {
        if (!target) return;

        Vector3 targetPosition = startPosition + localAxis * travel;
        Vector3 nextPosition = deltaTime > Zero
            ? Vector3.MoveTowards(target.localPosition, targetPosition, speed * deltaTime)
            : targetPosition;

        if ((target.localPosition - nextPosition).sqrMagnitude > PositionSquaredTolerance)
            target.localPosition = nextPosition;
    }
}
