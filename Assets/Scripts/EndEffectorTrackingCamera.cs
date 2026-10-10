using UnityEngine;

/// <summary>Keeps this camera at its authored position and aims it at one target.</summary>
[DisallowMultipleComponent]
public sealed class EndEffectorTrackingCamera : MonoBehaviour
{
    [SerializeField, Tooltip("The only transform this camera looks at.")]
    private Transform target;

    private void LateUpdate()
    {
        if (target == null)
            return;

        transform.LookAt(target, Vector3.up);
    }
}
