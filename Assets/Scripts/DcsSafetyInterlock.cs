using System.Collections.Generic;
using UnityEngine;

/// <summary>Monitors a DCS workcell volume, displays its boundary, and interlocks motion on intrusion.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
public sealed class DcsSafetyInterlock : MonoBehaviour
{
    private const string MissingControllerMessage = "DCS is enabled, but no RobotSixAxisController was found.";
    private const int EnvelopeEdgeCount = 12;
    private const float EnvelopeLineWidth = 0.025f;
    private const float Zero = 0f;
    private static readonly int[] EnvelopeEdgeCorners =
    {
        0, 1, 1, 2, 2, 3, 3, 0,
        4, 5, 5, 6, 6, 7, 7, 4,
        0, 4, 1, 5, 2, 6, 3, 7
    };

    [SerializeField, Tooltip("Enable DCS automatically when Play Mode starts.")]
    private bool initiallyEnabled = true;

    [SerializeField, Tooltip("Objects that are allowed inside the guarded workcell, such as conveyor workpieces.")]
    private Transform[] ignoredIntruderRoots = new Transform[0];

    private readonly HashSet<Collider> intruders = new HashSet<Collider>();
    private readonly Vector3[] envelopeCorners = new Vector3[8];
    private readonly LineRenderer[] envelopeLines = new LineRenderer[EnvelopeEdgeCount];
    private RobotSixAxisController robotController;
    private PistonExtensionController[] pistonControllers = new PistonExtensionController[0];
    private BoxCollider detectionVolume;
    private Rigidbody zoneRigidbody;
    private Material envelopeMaterial;
    private bool dcsEnabled;
    private bool tripped;

    /// <summary>Whether DCS is currently monitoring its trigger volume.</summary>
    public bool IsEnabled => dcsEnabled;

    /// <summary>Whether an intrusion has latched the motion interlock.</summary>
    public bool IsTripped => tripped;

    /// <summary>Whether the robot controller required to stop motion is available.</summary>
    public bool HasController => robotController != null;

    private void Awake()
    {
        detectionVolume = GetComponent<BoxCollider>();
        detectionVolume.isTrigger = true;
        zoneRigidbody = GetComponent<Rigidbody>();
        zoneRigidbody.isKinematic = true;
        zoneRigidbody.useGravity = false;
        ResolveControllers();
        CreateEnvelopeVisuals();
    }

    private void Start()
    {
        SetDcsEnabled(initiallyEnabled);
    }

    private void Update()
    {
        intruders.RemoveWhere(IsDestroyedCollider);
        if (dcsEnabled)
        {
            UpdateEnvelopeVisuals();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsIntruder(other))
        {
            return;
        }

        intruders.Add(other);
        if (dcsEnabled)
        {
            TripSafetyInterlock();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        intruders.Remove(other);
    }

    private void OnDestroy()
    {
        ApplyMotionInterlock(false);
        if (envelopeMaterial == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(envelopeMaterial);
        }
        else
        {
            DestroyImmediate(envelopeMaterial);
        }
    }

    /// <summary>Enables or disables DCS; re-enabling while an intruder remains in the zone immediately trips the interlock.</summary>
    public void SetDcsEnabled(bool enabled)
    {
        intruders.RemoveWhere(IsDestroyedCollider);
        dcsEnabled = enabled;
        SetEnvelopeVisible(dcsEnabled);

        if (!dcsEnabled)
        {
            tripped = false;
            ApplyMotionInterlock(false);
            return;
        }

        if (intruders.Count > 0)
        {
            TripSafetyInterlock();
            return;
        }

        tripped = false;
        ApplyMotionInterlock(false);
        UpdateEnvelopeVisuals();
    }

    /// <summary>Toggles DCS monitoring from a runtime control.</summary>
    public void ToggleDcs()
    {
        SetDcsEnabled(!dcsEnabled);
    }

    /// <summary>Returns whether a proposed robot tool movement remains inside the enabled DCS envelope.</summary>
    public bool IsToolMovementInsideEnvelope(Vector3 currentWorldPosition, Vector3 proposedWorldPosition)
    {
        if (!dcsEnabled)
        {
            return true;
        }

        if (detectionVolume == null)
        {
            return false;
        }

        Bounds bounds = detectionVolume.bounds;
        if (bounds.Contains(proposedWorldPosition))
        {
            return true;
        }

        if (!bounds.Contains(currentWorldPosition))
        {
            float currentOutsideDistance = (bounds.ClosestPoint(currentWorldPosition) - currentWorldPosition).sqrMagnitude;
            float proposedOutsideDistance = (bounds.ClosestPoint(proposedWorldPosition) - proposedWorldPosition).sqrMagnitude;
            return proposedOutsideDistance < currentOutsideDistance;
        }

        return false;
    }

    private bool IsIntruder(Collider other)
    {
        if (!other || other.isTrigger)
        {
            return false;
        }

        ResolveControllers();
        if (robotController == null)
        {
            return true;
        }

        Transform intruderTransform = other.transform;
        Transform protectedRoot = robotController.transform;
        if (intruderTransform == protectedRoot || intruderTransform.IsChildOf(protectedRoot))
        {
            return false;
        }

        for (int index = 0; index < ignoredIntruderRoots.Length; index++)
        {
            Transform ignoredRoot = ignoredIntruderRoots[index];
            if (ignoredRoot != null && (intruderTransform == ignoredRoot || intruderTransform.IsChildOf(ignoredRoot)))
            {
                return false;
            }
        }

        return true;
    }

    private void ResolveControllers()
    {
        if (robotController == null)
        {
            robotController = FindFirstObjectByType<RobotSixAxisController>();
            if (robotController != null)
            {
                pistonControllers = robotController.GetComponentsInChildren<PistonExtensionController>(true);
            }
        }
    }

    private void TripSafetyInterlock()
    {
        if (tripped)
        {
            return;
        }

        tripped = true;
        ResolveControllers();
        ApplyMotionInterlock(true);
        UpdateEnvelopeVisuals();
        if (robotController == null)
        {
            Debug.LogWarning(MissingControllerMessage, this);
        }
    }

    private void ApplyMotionInterlock(bool isLocked)
    {
        ResolveControllers();
        if (robotController != null)
        {
            robotController.SetSafetyInterlocked(isLocked);
        }

        for (int index = 0; index < pistonControllers.Length; index++)
        {
            if (pistonControllers[index] != null)
            {
                pistonControllers[index].SetSafetyInterlocked(isLocked);
            }
        }
    }

    private void CreateEnvelopeVisuals()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            return;
        }

        envelopeMaterial = new Material(shader)
        {
            name = "DCS Envelope Unlit Material"
        };
        for (int edgeIndex = 0; edgeIndex < EnvelopeEdgeCount; edgeIndex++)
        {
            GameObject edgeObject = new GameObject("DCS Envelope Edge " + (edgeIndex + 1));
            edgeObject.transform.SetParent(transform, false);
            LineRenderer line = edgeObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.widthMultiplier = EnvelopeLineWidth;
            line.sharedMaterial = envelopeMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            envelopeLines[edgeIndex] = line;
        }
    }

    private void SetEnvelopeVisible(bool isVisible)
    {
        for (int edgeIndex = 0; edgeIndex < envelopeLines.Length; edgeIndex++)
        {
            if (envelopeLines[edgeIndex] != null)
            {
                envelopeLines[edgeIndex].enabled = isVisible;
            }
        }
    }

    private void UpdateEnvelopeVisuals()
    {
        if (detectionVolume == null || envelopeMaterial == null)
        {
            return;
        }

        Bounds bounds = detectionVolume.bounds;
        Vector3 minimum = bounds.min;
        Vector3 maximum = bounds.max;
        envelopeCorners[0] = new Vector3(minimum.x, minimum.y, minimum.z);
        envelopeCorners[1] = new Vector3(maximum.x, minimum.y, minimum.z);
        envelopeCorners[2] = new Vector3(maximum.x, minimum.y, maximum.z);
        envelopeCorners[3] = new Vector3(minimum.x, minimum.y, maximum.z);
        envelopeCorners[4] = new Vector3(minimum.x, maximum.y, minimum.z);
        envelopeCorners[5] = new Vector3(maximum.x, maximum.y, minimum.z);
        envelopeCorners[6] = new Vector3(maximum.x, maximum.y, maximum.z);
        envelopeCorners[7] = new Vector3(minimum.x, maximum.y, maximum.z);

        Color envelopeColor = tripped ? new Color(1f, 0.12f, 0.08f, 1f) : new Color(0.2f, 1f, 0.42f, 1f);
        if (envelopeMaterial.HasProperty("_Color"))
        {
            envelopeMaterial.color = envelopeColor;
        }

        for (int edgeIndex = 0; edgeIndex < EnvelopeEdgeCount; edgeIndex++)
        {
            LineRenderer line = envelopeLines[edgeIndex];
            if (line == null)
            {
                continue;
            }

            line.startColor = envelopeColor;
            line.endColor = envelopeColor;
            line.SetPosition(0, envelopeCorners[EnvelopeEdgeCorners[edgeIndex * 2]]);
            line.SetPosition(1, envelopeCorners[EnvelopeEdgeCorners[edgeIndex * 2 + 1]]);
        }
    }

    private static bool IsDestroyedCollider(Collider collider)
    {
        return !collider;
    }
}
