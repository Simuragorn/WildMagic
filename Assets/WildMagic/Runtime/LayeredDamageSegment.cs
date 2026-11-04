using System;
using System.Collections.Generic;
using UnityEngine;

namespace WildMagic.Prototype
{
    /// <summary>
    /// A single anatomical segment made from nested skin, flesh and bone meshes.
    /// Damage is stored in each layer's object space, so holes follow it after detachment.
    /// </summary>
    public sealed class LayeredDamageSegment : MonoBehaviour
    {
        private const int MaxDamagePoints = 32;

        private readonly List<Vector4> skinDamage = new();
        private readonly List<Vector4> fleshDamage = new();
        private readonly List<Vector4> boneDamage = new();
        private readonly List<Vector4> fleshReveal = new();
        private readonly List<Vector4> boneReveal = new();
        private readonly Vector4[] skinDamageBuffer = new Vector4[MaxDamagePoints];
        private readonly Vector4[] fleshDamageBuffer = new Vector4[MaxDamagePoints];
        private readonly Vector4[] boneDamageBuffer = new Vector4[MaxDamagePoints];
        private readonly Vector4[] fleshRevealBuffer = new Vector4[MaxDamagePoints];
        private readonly Vector4[] boneRevealBuffer = new Vector4[MaxDamagePoints];

        private Renderer skinRenderer = null!;
        private Renderer fleshRenderer = null!;
        private Renderer boneRenderer = null!;
        private Material skinMaterial = null!;
        private Material fleshMaterial = null!;
        private Material boneMaterial = null!;
        private BoxCollider physicsCollider = null!;
        private Vector3[] skinVertices = Array.Empty<Vector3>();
        private int[] skinTriangles = Array.Empty<int>();
        private Rigidbody? detachedBody;
        private int hitCount;

        public event Action<string>? StatusChanged;

        public bool IsDetached => detachedBody != null;
        public int HitCount => hitCount;
        public string StatusText { get; private set; } = "Кожа не повреждена";

        public void Initialize(
            Renderer skin,
            Renderer flesh,
            Renderer bone,
            Material skinDamageMaterial,
            Material fleshDamageMaterial,
            Material boneDamageMaterial)
        {
            skinRenderer = skin;
            fleshRenderer = flesh;
            boneRenderer = bone;
            skinMaterial = skinDamageMaterial;
            fleshMaterial = fleshDamageMaterial;
            boneMaterial = boneDamageMaterial;

            skinRenderer.sharedMaterial = skinMaterial;
            fleshRenderer.sharedMaterial = fleshMaterial;
            boneRenderer.sharedMaterial = boneMaterial;

            Mesh skinMesh = skinRenderer.GetComponent<MeshFilter>().sharedMesh;
            skinVertices = skinMesh.vertices;
            skinTriangles = skinMesh.triangles;

            physicsCollider = gameObject.AddComponent<BoxCollider>();
            ConfigurePhysicsCollider();
            physicsCollider.enabled = true;

            skinMaterial.SetFloat("_RequireReveal", 0f);
            fleshMaterial.SetFloat("_RequireReveal", 1f);
            boneMaterial.SetFloat("_RequireReveal", 1f);
            ApplyPoints(skinMaterial, "_DamagePoints", "_DamageCount", skinDamage, skinDamageBuffer);
            ApplyPoints(fleshMaterial, "_DamagePoints", "_DamageCount", fleshDamage, fleshDamageBuffer);
            ApplyPoints(boneMaterial, "_DamagePoints", "_DamageCount", boneDamage, boneDamageBuffer);
            ApplyPoints(fleshMaterial, "_RevealPoints", "_RevealCount", fleshReveal, fleshRevealBuffer);
            ApplyPoints(boneMaterial, "_RevealPoints", "_RevealCount", boneReveal, boneRevealBuffer);
            SetStatus("Кожа не повреждена");
        }

        /// <summary>
        /// Refines the broad box-collider hit against the original skin triangles. This keeps
        /// post-detachment shots attached to the visible moving mesh without a costly MeshCollider.
        /// </summary>
        public bool TryGetSurfacePoint(Ray worldRay, out Vector3 worldPoint)
        {
            Transform skinTransform = skinRenderer.transform;
            Vector3 localOrigin = skinTransform.InverseTransformPoint(worldRay.origin);
            Vector3 localDirection = skinTransform.InverseTransformPoint(
                worldRay.origin + worldRay.direction) - localOrigin;
            if (localDirection.sqrMagnitude < 0.000001f)
            {
                worldPoint = default;
                return false;
            }
            localDirection.Normalize();

            bool found = false;
            float closestWorldDistance = float.PositiveInfinity;
            Vector3 closestPoint = default;
            for (int index = 0; index < skinTriangles.Length; index += 3)
            {
                if (!RayIntersectsTriangle(
                        localOrigin,
                        localDirection,
                        skinVertices[skinTriangles[index]],
                        skinVertices[skinTriangles[index + 1]],
                        skinVertices[skinTriangles[index + 2]],
                        out float distance))
                {
                    continue;
                }

                Vector3 candidate = skinTransform.TransformPoint(localOrigin + localDirection * distance);
                float worldDistance = (candidate - worldRay.origin).sqrMagnitude;
                if (worldDistance >= closestWorldDistance)
                    continue;

                closestWorldDistance = worldDistance;
                closestPoint = candidate;
                found = true;
            }

            worldPoint = closestPoint;
            return found;
        }

        public DamageResult ApplyHit(Vector3 worldPoint, Vector3 incomingDirection)
        {
            hitCount++;

            float skinRadius = IsDetached ? 0.17f : 0.14f;
            AddPoint(skinRenderer, skinMaterial, skinDamage, skinDamageBuffer,
                "_DamagePoints", "_DamageCount", worldPoint, skinRadius);
            AddPoint(fleshRenderer, fleshMaterial, fleshReveal, fleshRevealBuffer,
                "_RevealPoints", "_RevealCount", worldPoint, skinRadius * 1.28f);

            bool fleshWasDamaged = hitCount >= 2;
            if (fleshWasDamaged)
            {
                float fleshRadius = IsDetached ? 0.115f : 0.082f;
                AddPoint(fleshRenderer, fleshMaterial, fleshDamage, fleshDamageBuffer,
                    "_DamagePoints", "_DamageCount", worldPoint, fleshRadius);
                AddPoint(boneRenderer, boneMaterial, boneReveal, boneRevealBuffer,
                    "_RevealPoints", "_RevealCount", worldPoint, fleshRadius * 1.55f);
            }

            bool boneWasDamaged = IsDetached && hitCount >= 4;
            if (boneWasDamaged)
            {
                AddPoint(boneRenderer, boneMaterial, boneDamage, boneDamageBuffer,
                    "_DamagePoints", "_DamageCount", worldPoint, 0.055f);
            }

            bool detachedNow = false;
            if (hitCount == 1)
            {
                SetStatus("Кожа пробита — видна плоть");
            }
            else if (hitCount == 2)
            {
                SetStatus("Плоть повреждена — обнажена кость");
            }
            else if (!IsDetached)
            {
                Detach(worldPoint, incomingDirection);
                detachedNow = true;
                SetStatus("Часть тела отсечена и получила физику");
            }
            else
            {
                SetStatus(boneWasDamaged
                    ? "Разрушение продолжается — повреждена даже кость"
                    : "Отсечённая часть продолжает разрушаться");
                detachedBody!.AddForceAtPosition(
                    incomingDirection.normalized * 1.4f,
                    worldPoint,
                    ForceMode.Impulse);
            }

            return new DamageResult(fleshWasDamaged, boneWasDamaged, detachedNow);
        }

        private static void AddPoint(
            Renderer targetRenderer,
            Material targetMaterial,
            List<Vector4> points,
            Vector4[] buffer,
            string arrayProperty,
            string countProperty,
            Vector3 worldPoint,
            float worldRadius)
        {
            Transform targetTransform = targetRenderer.transform;
            Vector3 localPoint = targetTransform.InverseTransformPoint(worldPoint);
            Vector3 scale = targetTransform.lossyScale;
            float averageScale = Mathf.Max(0.0001f,
                (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f);
            Vector4 newPoint = new(localPoint.x, localPoint.y, localPoint.z, worldRadius / averageScale);
            AddOrMergePermanentPoint(points, newPoint);
            ApplyPoints(targetMaterial, arrayProperty, countProperty, points, buffer);
        }

        private static void AddOrMergePermanentPoint(List<Vector4> points, Vector4 newPoint)
        {
            int nearestIndex = -1;
            float nearestDistance = float.PositiveInfinity;
            for (int index = 0; index < points.Count; index++)
            {
                Vector3 existingCenter = points[index];
                float distance = Vector3.Distance(existingCenter, newPoint);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = index;
                }

                float overlapDistance = (points[index].w + newPoint.w) * 0.72f;
                if (distance > overlapDistance)
                    continue;

                points[index] = MergeSpheres(points[index], newPoint, 0.08f);
                return;
            }

            if (points.Count < MaxDamagePoints)
            {
                points.Add(newPoint);
                return;
            }

            // Never discard an old wound. Once the fixed shader buffer is full, combine the
            // nearest wound and the new one into a sphere that contains both damaged regions.
            points[nearestIndex] = MergeSpheres(points[nearestIndex], newPoint, 0f);
        }

        private static Vector4 MergeSpheres(Vector4 first, Vector4 second, float repeatedHitGrowth)
        {
            Vector3 firstCenter = first;
            Vector3 secondCenter = second;
            float distance = Vector3.Distance(firstCenter, secondCenter);

            if (distance + second.w <= first.w)
            {
                first.w += Mathf.Min(first.w, second.w) * repeatedHitGrowth;
                return first;
            }
            if (distance + first.w <= second.w)
            {
                second.w += Mathf.Min(first.w, second.w) * repeatedHitGrowth;
                return second;
            }

            float radius = (distance + first.w + second.w) * 0.5f;
            Vector3 center = distance > 0.00001f
                ? firstCenter + (secondCenter - firstCenter) * ((radius - first.w) / distance)
                : firstCenter;
            radius += Mathf.Min(first.w, second.w) * repeatedHitGrowth;
            return new Vector4(center.x, center.y, center.z, radius);
        }

        private static void ApplyPoints(
            Material material,
            string arrayProperty,
            string countProperty,
            List<Vector4> points,
            Vector4[] buffer)
        {
            Array.Clear(buffer, 0, buffer.Length);
            for (int index = 0; index < points.Count; index++)
                buffer[index] = points[index];

            material.SetInt(countProperty, points.Count);
            material.SetVectorArray(arrayProperty, buffer);
        }

        private void ConfigurePhysicsCollider()
        {
            Bounds worldBounds = skinRenderer.bounds;
            worldBounds.Encapsulate(fleshRenderer.bounds);
            worldBounds.Encapsulate(boneRenderer.bounds);

            physicsCollider.center = transform.InverseTransformPoint(worldBounds.center);
            Vector3 scale = transform.lossyScale;
            physicsCollider.size = new Vector3(
                worldBounds.size.x / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
                worldBounds.size.y / Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
                worldBounds.size.z / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));
        }

        private void Detach(Vector3 hitPoint, Vector3 incomingDirection)
        {
            transform.SetParent(null, true);

            detachedBody = gameObject.AddComponent<Rigidbody>();
            detachedBody.mass = 3.2f;
            detachedBody.linearDamping = 0.22f;
            detachedBody.angularDamping = 0.12f;
            detachedBody.interpolation = RigidbodyInterpolation.Interpolate;
            detachedBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            detachedBody.AddForceAtPosition(
                incomingDirection.normalized * 3.2f + Vector3.up * 1.1f,
                hitPoint,
                ForceMode.Impulse);
            detachedBody.AddTorque(new Vector3(0.4f, -0.2f, 1.1f), ForceMode.Impulse);
        }

        private static bool RayIntersectsTriangle(
            Vector3 origin,
            Vector3 direction,
            Vector3 vertex0,
            Vector3 vertex1,
            Vector3 vertex2,
            out float distance)
        {
            const float epsilon = 0.000001f;
            Vector3 edge1 = vertex1 - vertex0;
            Vector3 edge2 = vertex2 - vertex0;
            Vector3 p = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (Mathf.Abs(determinant) < epsilon)
            {
                distance = 0f;
                return false;
            }

            float inverseDeterminant = 1f / determinant;
            Vector3 t = origin - vertex0;
            float u = Vector3.Dot(t, p) * inverseDeterminant;
            if (u < 0f || u > 1f)
            {
                distance = 0f;
                return false;
            }

            Vector3 q = Vector3.Cross(t, edge1);
            float v = Vector3.Dot(direction, q) * inverseDeterminant;
            if (v < 0f || u + v > 1f)
            {
                distance = 0f;
                return false;
            }

            distance = Vector3.Dot(edge2, q) * inverseDeterminant;
            return distance > epsilon;
        }

        private void SetStatus(string value)
        {
            StatusText = value;
            StatusChanged?.Invoke(value);
        }

        public readonly struct DamageResult
        {
            public DamageResult(bool fleshDamaged, bool boneDamaged, bool detached)
            {
                FleshDamaged = fleshDamaged;
                BoneDamaged = boneDamaged;
                Detached = detached;
            }

            public bool FleshDamaged { get; }
            public bool BoneDamaged { get; }
            public bool Detached { get; }
        }
    }
}
