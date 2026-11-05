using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WildMagic.Prototype
{
    public enum VoxelMaterial : byte
    {
        Air = 0,
        Cloth = 1,
        Skin = 2,
        Flesh = 3,
        Bone = 4
    }

    /// <summary>
    /// A filled destructible volume. Voxels are data only; exposed faces are combined into one mesh.
    /// Connectivity is recalculated after damage and disconnected islands become physical bodies.
    /// </summary>
    public sealed class VoxelDestructibleBody : MonoBehaviour
    {
        private static readonly Vector3Int[] Neighbours =
        {
            Vector3Int.right,
            Vector3Int.left,
            Vector3Int.up,
            Vector3Int.down,
            new(0, 0, 1),
            new(0, 0, -1)
        };

        private static readonly Vector3[] FaceNormals =
        {
            Vector3.right,
            Vector3.left,
            Vector3.up,
            Vector3.down,
            Vector3.forward,
            Vector3.back
        };

        private static readonly Vector3[] FaceAxesU =
        {
            Vector3.up,
            Vector3.up,
            Vector3.forward,
            Vector3.forward,
            Vector3.right,
            Vector3.right
        };

        private static readonly Vector3[] FaceAxesV =
        {
            Vector3.forward,
            Vector3.back,
            Vector3.right,
            Vector3.left,
            Vector3.up,
            Vector3.down
        };

        private readonly Dictionary<Vector3Int, VoxelMaterial> voxels = new();
        private readonly HashSet<Vector3Int> anchorVoxels = new();

        private MeshFilter meshFilter = null!;
        private MeshRenderer meshRenderer = null!;
        private MeshCollider? meshCollider;
        private BoxCollider? boxCollider;
        private Rigidbody? body;
        private Material[] materials = Array.Empty<Material>();
        private Mesh? generatedMesh;
        private Vector3 localOrigin;
        private float voxelSize;
        private bool anchored;

        public int VoxelCount => voxels.Count;
        public bool IsAnchored => anchored;
        public Bounds WorldBounds => meshRenderer.bounds;

        public void Initialize(
            Dictionary<Vector3Int, VoxelMaterial> source,
            float size,
            Vector3 origin,
            Material[] sharedMaterials,
            IEnumerable<Vector3Int>? anchors,
            bool isAnchored)
        {
            voxels.Clear();
            foreach ((Vector3Int coordinate, VoxelMaterial material) in source)
                voxels.Add(coordinate, material);

            anchorVoxels.Clear();
            if (anchors != null)
            {
                foreach (Vector3Int coordinate in anchors)
                    anchorVoxels.Add(coordinate);
            }

            voxelSize = size;
            localOrigin = origin;
            materials = sharedMaterials;
            anchored = isAnchored;

            meshFilter = gameObject.GetComponent<MeshFilter>();
            if (meshFilter == null)
                meshFilter = gameObject.AddComponent<MeshFilter>();
            meshRenderer = gameObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
                meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterials = materials;
            RebuildMeshAndCollider();
        }

        private void OnDestroy()
        {
            if (generatedMesh != null)
                Destroy(generatedMesh);
        }

        public DamageReport DamageWorld(Vector3 worldPoint, float worldRadius, Vector3 incomingDirection)
        {
            if (voxels.Count == 0)
                return default;

            Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
            float localRadius = worldRadius / AverageScale(transform.lossyScale);
            float radiusSquared = localRadius * localRadius;
            List<Vector3Int> removed = new();
            int cloth = 0;
            int skin = 0;
            int flesh = 0;
            int bone = 0;

            foreach ((Vector3Int coordinate, VoxelMaterial material) in voxels)
            {
                if ((VoxelCenter(coordinate) - localPoint).sqrMagnitude > radiusSquared)
                    continue;
                removed.Add(coordinate);
                switch (material)
                {
                    case VoxelMaterial.Cloth: cloth++; break;
                    case VoxelMaterial.Skin: skin++; break;
                    case VoxelMaterial.Flesh: flesh++; break;
                    case VoxelMaterial.Bone: bone++; break;
                }
            }

            if (removed.Count == 0 && TryFindNearestVoxel(localPoint, out Vector3Int nearest))
            {
                VoxelMaterial material = voxels[nearest];
                removed.Add(nearest);
                switch (material)
                {
                    case VoxelMaterial.Cloth: cloth++; break;
                    case VoxelMaterial.Skin: skin++; break;
                    case VoxelMaterial.Flesh: flesh++; break;
                    case VoxelMaterial.Bone: bone++; break;
                }
            }

            foreach (Vector3Int coordinate in removed)
                voxels.Remove(coordinate);

            SplitReport split = SplitDisconnected(worldPoint, incomingDirection);
            return new DamageReport(
                removed.Count,
                cloth,
                skin,
                flesh,
                bone,
                split.FragmentCount,
                split.DetachedVoxels);
        }

        private SplitReport SplitDisconnected(Vector3 hitPoint, Vector3 incomingDirection)
        {
            if (voxels.Count == 0)
            {
                RebuildMeshAndCollider();
                return default;
            }

            List<List<Vector3Int>> components = FindComponents();
            if (components.Count == 1)
            {
                RebuildMeshAndCollider();
                return default;
            }

            List<Vector3Int>? retained = null;
            if (anchored)
            {
                foreach (List<Vector3Int> component in components)
                {
                    foreach (Vector3Int coordinate in component)
                    {
                        if (!anchorVoxels.Contains(coordinate))
                            continue;
                        retained = component;
                        break;
                    }
                    if (retained != null)
                        break;
                }
            }
            else
            {
                foreach (List<Vector3Int> component in components)
                {
                    if (retained == null || component.Count > retained.Count)
                        retained = component;
                }
            }

            HashSet<Vector3Int> retainedSet = retained != null
                ? new HashSet<Vector3Int>(retained)
                : new HashSet<Vector3Int>();
            List<Dictionary<Vector3Int, VoxelMaterial>> detached = new();
            int detachedVoxels = 0;
            foreach (List<Vector3Int> component in components)
            {
                if (ReferenceEquals(component, retained))
                    continue;
                Dictionary<Vector3Int, VoxelMaterial> fragment = new(component.Count);
                foreach (Vector3Int coordinate in component)
                    fragment.Add(coordinate, voxels[coordinate]);
                detached.Add(fragment);
                detachedVoxels += fragment.Count;
            }

            if (retained != null)
            {
                List<Vector3Int> discard = new();
                foreach (Vector3Int coordinate in voxels.Keys)
                {
                    if (!retainedSet.Contains(coordinate))
                        discard.Add(coordinate);
                }
                foreach (Vector3Int coordinate in discard)
                    voxels.Remove(coordinate);
            }
            else
            {
                voxels.Clear();
            }

            RebuildMeshAndCollider();
            foreach (Dictionary<Vector3Int, VoxelMaterial> fragment in detached)
                SpawnFragment(fragment, hitPoint, incomingDirection);
            return new SplitReport(detached.Count, detachedVoxels);
        }

        private List<List<Vector3Int>> FindComponents()
        {
            HashSet<Vector3Int> unvisited = new(voxels.Keys);
            List<List<Vector3Int>> components = new();
            Queue<Vector3Int> queue = new();
            while (unvisited.Count > 0)
            {
                Vector3Int seed = default;
                foreach (Vector3Int candidate in unvisited)
                {
                    seed = candidate;
                    break;
                }

                List<Vector3Int> component = new();
                unvisited.Remove(seed);
                queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    Vector3Int coordinate = queue.Dequeue();
                    component.Add(coordinate);
                    foreach (Vector3Int direction in Neighbours)
                    {
                        Vector3Int neighbour = coordinate + direction;
                        if (!unvisited.Remove(neighbour))
                            continue;
                        queue.Enqueue(neighbour);
                    }
                }
                components.Add(component);
            }
            return components;
        }

        private void SpawnFragment(
            Dictionary<Vector3Int, VoxelMaterial> fragment,
            Vector3 hitPoint,
            Vector3 incomingDirection)
        {
            GameObject fragmentObject = new($"Voxel fragment ({fragment.Count})");
            fragmentObject.transform.SetPositionAndRotation(transform.position, transform.rotation);
            fragmentObject.transform.localScale = transform.lossyScale;
            VoxelDestructibleBody fragmentBody = fragmentObject.AddComponent<VoxelDestructibleBody>();
            fragmentBody.Initialize(fragment, voxelSize, localOrigin, materials, null, false);
            fragmentBody.EnablePhysics(hitPoint, incomingDirection);
        }

        private void EnablePhysics(Vector3 hitPoint, Vector3 incomingDirection)
        {
            body = gameObject.AddComponent<Rigidbody>();
            body.mass = Mathf.Clamp(voxels.Count * 0.012f, 0.12f, 18f);
            body.linearDamping = 0.18f;
            body.angularDamping = 0.12f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.AddForceAtPosition(
                incomingDirection.normalized * Mathf.Clamp(0.8f + voxels.Count * 0.002f, 0.8f, 3.2f)
                + Vector3.up * 0.35f,
                hitPoint,
                ForceMode.Impulse);
            body.AddTorque(UnityEngine.Random.insideUnitSphere * 0.45f, ForceMode.Impulse);
        }

        private void RebuildMeshAndCollider()
        {
            if (generatedMesh != null)
            {
                if (meshCollider != null)
                    meshCollider.sharedMesh = null;
                Destroy(generatedMesh);
            }

            generatedMesh = BuildMesh();
            meshFilter.sharedMesh = generatedMesh;
            meshRenderer.enabled = voxels.Count > 0;

            if (anchored)
            {
                if (boxCollider != null)
                    boxCollider.enabled = false;
                if (meshCollider == null)
                {
                    meshCollider = gameObject.GetComponent<MeshCollider>();
                    if (meshCollider == null)
                        meshCollider = gameObject.AddComponent<MeshCollider>();
                }
                meshCollider.convex = false;
                meshCollider.sharedMesh = generatedMesh;
                meshCollider.enabled = voxels.Count > 0;
            }
            else
            {
                if (meshCollider != null)
                    meshCollider.enabled = false;
                if (boxCollider == null)
                {
                    boxCollider = gameObject.GetComponent<BoxCollider>();
                    if (boxCollider == null)
                        boxCollider = gameObject.AddComponent<BoxCollider>();
                }
                boxCollider.center = generatedMesh.bounds.center;
                boxCollider.size = generatedMesh.bounds.size + Vector3.one * voxelSize * 0.08f;
                boxCollider.enabled = voxels.Count > 0;
            }
        }

        private Mesh BuildMesh()
        {
            Mesh mesh = new()
            {
                name = $"Voxel volume ({voxels.Count})",
                indexFormat = IndexFormat.UInt32
            };
            if (voxels.Count == 0)
                return mesh;

            List<Vector3> vertices = new();
            List<Vector3> normals = new();
            List<Vector2> uv = new();
            List<int>[] triangles =
            {
                new(),
                new(),
                new(),
                new()
            };

            float half = voxelSize * 0.5f;
            foreach ((Vector3Int coordinate, VoxelMaterial material) in voxels)
            {
                Vector3 center = VoxelCenter(coordinate);
                for (int face = 0; face < Neighbours.Length; face++)
                {
                    if (voxels.ContainsKey(coordinate + Neighbours[face]))
                        continue;

                    Vector3 normal = FaceNormals[face];
                    Vector3 axisU = FaceAxesU[face] * half;
                    Vector3 axisV = FaceAxesV[face] * half;
                    Vector3 faceCenter = center + normal * half;
                    int start = vertices.Count;
                    vertices.Add(faceCenter - axisU - axisV);
                    vertices.Add(faceCenter + axisU - axisV);
                    vertices.Add(faceCenter + axisU + axisV);
                    vertices.Add(faceCenter - axisU + axisV);
                    for (int corner = 0; corner < 4; corner++)
                        normals.Add(normal);
                    uv.Add(new Vector2(0f, 0f));
                    uv.Add(new Vector2(1f, 0f));
                    uv.Add(new Vector2(1f, 1f));
                    uv.Add(new Vector2(0f, 1f));

                    List<int> target = triangles[(int)material - 1];
                    target.Add(start);
                    target.Add(start + 1);
                    target.Add(start + 2);
                    target.Add(start);
                    target.Add(start + 2);
                    target.Add(start + 3);
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = triangles.Length;
            for (int index = 0; index < triangles.Length; index++)
                mesh.SetTriangles(triangles[index], index, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        private bool TryFindNearestVoxel(Vector3 localPoint, out Vector3Int coordinate)
        {
            coordinate = default;
            float closest = float.PositiveInfinity;
            bool found = false;
            foreach (Vector3Int candidate in voxels.Keys)
            {
                float distance = (VoxelCenter(candidate) - localPoint).sqrMagnitude;
                if (distance >= closest)
                    continue;
                closest = distance;
                coordinate = candidate;
                found = true;
            }
            return found;
        }

        private Vector3 VoxelCenter(Vector3Int coordinate) =>
            localOrigin + new Vector3(coordinate.x, coordinate.y, coordinate.z) * voxelSize;

        private static float AverageScale(Vector3 scale) => Mathf.Max(
            0.0001f,
            (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f);

        private readonly struct SplitReport
        {
            public SplitReport(int fragmentCount, int detachedVoxels)
            {
                FragmentCount = fragmentCount;
                DetachedVoxels = detachedVoxels;
            }

            public int FragmentCount { get; }
            public int DetachedVoxels { get; }
        }

        public readonly struct DamageReport
        {
            public DamageReport(
                int removed,
                int cloth,
                int skin,
                int flesh,
                int bone,
                int fragmentCount,
                int detachedVoxels)
            {
                Removed = removed;
                Cloth = cloth;
                Skin = skin;
                Flesh = flesh;
                Bone = bone;
                FragmentCount = fragmentCount;
                DetachedVoxels = detachedVoxels;
            }

            public int Removed { get; }
            public int Cloth { get; }
            public int Skin { get; }
            public int Flesh { get; }
            public int Bone { get; }
            public int FragmentCount { get; }
            public int DetachedVoxels { get; }
        }
    }
}
