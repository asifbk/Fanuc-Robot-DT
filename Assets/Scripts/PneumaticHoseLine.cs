using System;
using UnityEngine;

/// <summary>Builds a flexible pneumatic tube mesh along animated robot route anchors.</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class PneumaticHoseLine : MonoBehaviour
{
    private const float DefaultHoseRadius = 0.006f;
    private const float MinimumHoseRadius = 0.001f;
    private const float MaximumHoseRadius = 0.03f;
    private const int DefaultRadialSegments = 8;
    private const int MinimumRadialSegments = 4;
    private const int MaximumRadialSegments = 16;
    private const int DefaultSamplesPerSegment = 6;
    private const int MinimumSamplesPerSegment = 2;
    private const int MaximumSamplesPerSegment = 12;
    private const float CatmullRomHalf = 0.5f;
    private const float CircleRadians = 6.28318530718f;
    private const float ParallelThreshold = 0.95f;
    private const float DirectionSquaredTolerance = 0.00000001f;

    [SerializeField, Tooltip("Ordered from the base mounting point to the pneumatic fitting.")]
    private Transform[] routeAnchors = Array.Empty<Transform>();
    [SerializeField, Tooltip("Per-anchor offsets in each anchor's local coordinates.")]
    private Vector3[] localOffsets = Array.Empty<Vector3>();
    [SerializeField, Min(MinimumHoseRadius)] private float hoseRadius = DefaultHoseRadius;
    [SerializeField, Range(MinimumRadialSegments, MaximumRadialSegments)] private int radialSegments = DefaultRadialSegments;
    [SerializeField, Range(MinimumSamplesPerSegment, MaximumSamplesPerSegment)] private int samplesPerSegment = DefaultSamplesPerSegment;

    [NonSerialized] private Mesh generatedMesh;
    [NonSerialized] private MeshFilter meshFilter;
    [NonSerialized] private Vector3[] routePoints;
    [NonSerialized] private Vector3[] sampledPoints;
    [NonSerialized] private Vector3[] vertices;
    [NonSerialized] private Vector2[] uv;
    [NonSerialized] private int[] triangles;

    private void OnEnable()
    {
        RebuildTube();
    }

    private void LateUpdate()
    {
        RebuildTube();
    }

    private void OnValidate()
    {
        hoseRadius = Mathf.Clamp(hoseRadius, MinimumHoseRadius, MaximumHoseRadius);
        radialSegments = Mathf.Clamp(radialSegments, MinimumRadialSegments, MaximumRadialSegments);
        samplesPerSegment = Mathf.Clamp(samplesPerSegment, MinimumSamplesPerSegment, MaximumSamplesPerSegment);
        RebuildTube();
    }

    private void OnDestroy()
    {
        if (generatedMesh == null) return;
        if (Application.isPlaying) Destroy(generatedMesh);
        else DestroyImmediate(generatedMesh);
    }

    private void RebuildTube()
    {
        if (!HasValidRoute()) return;
        EnsureBuffers();
        for (int anchorIndex = 0; anchorIndex < routeAnchors.Length; anchorIndex++)
            routePoints[anchorIndex] = routeAnchors[anchorIndex].TransformPoint(localOffsets[anchorIndex]);

        SampleRoute();
        BuildTubeMesh();
    }

    private bool HasValidRoute()
    {
        if (routeAnchors == null || localOffsets == null || routeAnchors.Length < 2 || localOffsets.Length != routeAnchors.Length)
            return false;
        for (int anchorIndex = 0; anchorIndex < routeAnchors.Length; anchorIndex++)
            if (routeAnchors[anchorIndex] == null) return false;
        return true;
    }

    private void EnsureBuffers()
    {
        int sampleCount = (routeAnchors.Length - 1) * samplesPerSegment + 1;
        int vertexCount = sampleCount * radialSegments;
        int triangleIndexCount = (sampleCount - 1) * radialSegments * 6;
        if (routePoints == null || routePoints.Length != routeAnchors.Length) routePoints = new Vector3[routeAnchors.Length];
        if (sampledPoints == null || sampledPoints.Length != sampleCount) sampledPoints = new Vector3[sampleCount];
        if (vertices == null || vertices.Length != vertexCount) vertices = new Vector3[vertexCount];
        if (uv == null || uv.Length != vertexCount) uv = new Vector2[vertexCount];
        if (triangles == null || triangles.Length != triangleIndexCount) triangles = BuildTriangleIndices(sampleCount);

        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
        if (generatedMesh == null)
        {
            generatedMesh = new Mesh
            {
                name = "Pneumatic Hose 2 Generated Mesh",
                hideFlags = HideFlags.DontSave
            };
            generatedMesh.MarkDynamic();
            meshFilter.sharedMesh = generatedMesh;
        }
    }

    private void SampleRoute()
    {
        int outputIndex = 0;
        for (int segmentIndex = 0; segmentIndex < routePoints.Length - 1; segmentIndex++)
        {
            Vector3 previousPoint = routePoints[Mathf.Max(segmentIndex - 1, 0)];
            Vector3 startPoint = routePoints[segmentIndex];
            Vector3 endPoint = routePoints[segmentIndex + 1];
            Vector3 nextPoint = routePoints[Mathf.Min(segmentIndex + 2, routePoints.Length - 1)];
            for (int sampleIndex = 0; sampleIndex < samplesPerSegment; sampleIndex++)
            {
                float t = sampleIndex / (float)samplesPerSegment;
                sampledPoints[outputIndex++] = CatmullRom(previousPoint, startPoint, endPoint, nextPoint, t);
            }
        }
        sampledPoints[outputIndex] = routePoints[routePoints.Length - 1];
    }

    private void BuildTubeMesh()
    {
        int sampleCount = sampledPoints.Length;
        for (int ringIndex = 0; ringIndex < sampleCount; ringIndex++)
        {
            Vector3 previousPoint = sampledPoints[Mathf.Max(ringIndex - 1, 0)];
            Vector3 nextPoint = sampledPoints[Mathf.Min(ringIndex + 1, sampleCount - 1)];
            Vector3 tangent = nextPoint - previousPoint;
            if (tangent.sqrMagnitude < DirectionSquaredTolerance) tangent = Vector3.forward;
            tangent.Normalize();

            Vector3 referenceAxis = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > ParallelThreshold ? Vector3.right : Vector3.up;
            Vector3 normal = Vector3.Cross(tangent, referenceAxis).normalized;
            Vector3 binormal = Vector3.Cross(tangent, normal).normalized;
            for (int sideIndex = 0; sideIndex < radialSegments; sideIndex++)
            {
                float angle = CircleRadians * sideIndex / radialSegments;
                Vector3 offset = hoseRadius * (normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle));
                int vertexIndex = ringIndex * radialSegments + sideIndex;
                vertices[vertexIndex] = transform.InverseTransformPoint(sampledPoints[ringIndex] + offset);
                uv[vertexIndex] = new Vector2(sideIndex / (float)radialSegments, ringIndex / (float)(sampleCount - 1));
            }
        }

        generatedMesh.Clear();
        generatedMesh.vertices = vertices;
        generatedMesh.uv = uv;
        generatedMesh.triangles = triangles;
        generatedMesh.RecalculateNormals();
        generatedMesh.RecalculateBounds();
        meshFilter.sharedMesh = generatedMesh;
    }

    private int[] BuildTriangleIndices(int sampleCount)
    {
        int[] indices = new int[(sampleCount - 1) * radialSegments * 6];
        int outputIndex = 0;
        for (int ringIndex = 0; ringIndex < sampleCount - 1; ringIndex++)
        {
            for (int sideIndex = 0; sideIndex < radialSegments; sideIndex++)
            {
                int nextSideIndex = (sideIndex + 1) % radialSegments;
                int current = ringIndex * radialSegments + sideIndex;
                int next = ringIndex * radialSegments + nextSideIndex;
                int upperCurrent = current + radialSegments;
                int upperNext = next + radialSegments;
                indices[outputIndex++] = current;
                indices[outputIndex++] = next;
                indices[outputIndex++] = upperCurrent;
                indices[outputIndex++] = next;
                indices[outputIndex++] = upperNext;
                indices[outputIndex++] = upperCurrent;
            }
        }
        return indices;
    }

    private static Vector3 CatmullRom(Vector3 previous, Vector3 start, Vector3 end, Vector3 next, float t)
    {
        float tSquared = t * t;
        float tCubed = tSquared * t;
        return CatmullRomHalf * ((2f * start) + (-previous + end) * t
            + (2f * previous - 5f * start + 4f * end - next) * tSquared
            + (-previous + 3f * start - 3f * end + next) * tCubed);
    }
}
