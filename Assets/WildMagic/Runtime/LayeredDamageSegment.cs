using System;
using System.Collections.Generic;
using UnityEngine;

namespace WildMagic.Prototype
{
    /// <summary>
    /// A single anatomical segment made from nested skin, flesh and bone meshes.
    /// Damage is stored in object space, so holes follow the segment after detachment.
    /// </summary>
    public sealed class LayeredDamageSegment : MonoBehaviour
    {
        private const int MaxDamagePoints = 12;

        private readonly List<Vector4> skinDamage = new();
        private readonly List<Vector4> fleshDamage = new();

        private Renderer skinRenderer = null!;
        private Renderer fleshRenderer = null!;
        private Renderer boneRenderer = null!;
        private Material skinMaterial = null!;
        private Material fleshMaterial = null!;
        private MeshCollider skinCollider = null!;
        private BoxCollider physicsCollider = null!;
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
            Material boneMaterial)
        {
            skinRenderer = skin;
            fleshRenderer = flesh;
            boneRenderer = bone;
            skinMaterial = skinDamageMaterial;
            fleshMaterial = fleshDamageMaterial;

            skinRenderer.sharedMaterial = skinMaterial;
            fleshRenderer.sharedMaterial = fleshMaterial;
            boneRenderer.sharedMaterial = boneMaterial;

            MeshFilter skinFilter = skinRenderer.GetComponent<MeshFilter>();
            skinCollider = skinRenderer.gameObject.AddComponent<MeshCollider>();
            skinCollider.sharedMesh = skinFilter.sharedMesh;
            skinCollider.convex = false;

            physicsCollider = gameObject.AddComponent<BoxCollider>();
            ConfigurePhysicsCollider();
            physicsCollider.enabled = false;

            ApplyDamagePoints(skinMaterial, skinDamage);
            ApplyDamagePoints(fleshMaterial, fleshDamage);
            SetStatus("Кожа не повреждена");
        }

        public DamageResult ApplyHit(Vector3 worldPoint, Vector3 incomingDirection)
        {
            hitCount++;

            float skinRadius = IsDetached ? 0.16f : 0.14f;
            AddDamagePoint(skinRenderer, skinMaterial, skinDamage, worldPoint, skinRadius);

            bool fleshWasDamaged = hitCount >= 2;
            if (fleshWasDamaged)
            {
                float fleshRadius = IsDetached ? 0.105f : 0.082f;
                AddDamagePoint(fleshRenderer, fleshMaterial, fleshDamage, worldPoint, fleshRadius);
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
                SetStatus("Рука отсечена и получила физику");
            }
            else
            {
                SetStatus("Отсечённая часть продолжает разрушаться");
                detachedBody!.AddForceAtPosition(
                    incomingDirection.normalized * 1.4f,
                    worldPoint,
                    ForceMode.Impulse);
            }

            return new DamageResult(fleshWasDamaged, detachedNow);
        }

        private void AddDamagePoint(
            Renderer targetRenderer,
            Material targetMaterial,
            List<Vector4> points,
            Vector3 worldPoint,
            float worldRadius)
        {
            if (points.Count >= MaxDamagePoints)
                points.RemoveAt(0);

            Transform targetTransform = targetRenderer.transform;
            Vector3 localPoint = targetTransform.InverseTransformPoint(worldPoint);
            Vector3 scale = targetTransform.lossyScale;
            float averageScale = Mathf.Max(0.0001f,
                (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f);
            points.Add(new Vector4(localPoint.x, localPoint.y, localPoint.z, worldRadius / averageScale));
            ApplyDamagePoints(targetMaterial, points);
        }

        private static void ApplyDamagePoints(Material material, List<Vector4> points)
        {
            Vector4[] values = new Vector4[MaxDamagePoints];
            for (int index = 0; index < points.Count; index++)
                values[index] = points[index];

            material.SetInt("_DamageCount", points.Count);
            material.SetVectorArray("_DamagePoints", values);
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
            skinCollider.enabled = false;
            physicsCollider.enabled = true;

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

        private void SetStatus(string value)
        {
            StatusText = value;
            StatusChanged?.Invoke(value);
        }

        public readonly struct DamageResult
        {
            public DamageResult(bool fleshDamaged, bool detached)
            {
                FleshDamaged = fleshDamaged;
                Detached = detached;
            }

            public bool FleshDamaged { get; }
            public bool Detached { get; }
        }
    }
}
