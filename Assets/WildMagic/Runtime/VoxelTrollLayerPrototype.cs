using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace WildMagic.Prototype
{
    /// <summary>
    /// Visual approval scene for the voxel troll. Layers are built from the inside out;
    /// this first revision deliberately contains only the skeleton.
    /// </summary>
    public sealed class VoxelTrollLayerPrototype : MonoBehaviour
    {
        private const float DesignScale = 2f;
        private const float VoxelSize = 0.0525f;

        private readonly List<Material> generatedMaterials = new();
        private Transform? displayRoot;
        private Font? uiFont;
        private GUIStyle? titleStyle;
        private GUIStyle? bodyStyle;

        public VoxelDestructibleBody? Skeleton { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (SceneManager.GetActiveScene().name != "VoxelTrollLayerPrototype")
                return;
            if (FindFirstObjectByType<VoxelTrollLayerPrototype>() != null)
                return;
            new GameObject("[Voxel Troll Layer Prototype]").AddComponent<VoxelTrollLayerPrototype>();
        }

        private void Awake()
        {
            BuildScene();
        }

        private void Update()
        {
            if (displayRoot == null)
                return;

            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            float direction = 0f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
                direction += 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
                direction -= 1f;
            displayRoot.Rotate(Vector3.up, direction * 42f * Time.deltaTime, Space.World);

            if (keyboard.rKey.wasPressedThisFrame)
                displayRoot.rotation = Quaternion.identity;
        }

        private void OnDestroy()
        {
            foreach (Material material in generatedMaterials)
            {
                if (material != null)
                    Destroy(material);
            }
        }

        private void BuildScene()
        {
            if (displayRoot != null)
                return;

            uiFont = Resources.Load<Font>("RobotoMono-Regular");
            BuildEnvironment();
            BuildSkeleton();
        }

        private void BuildEnvironment()
        {
            Camera camera = new GameObject("Prototype Camera").AddComponent<Camera>();
            camera.transform.SetParent(transform, false);
            camera.transform.position = new Vector3(0.15f, 1.85f, -11.2f);
            camera.transform.LookAt(new Vector3(0f, 1.55f, 0f));
            camera.orthographic = false;
            camera.fieldOfView = 31f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.075f, 0.085f, 0.105f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 40f;
            UniversalAdditionalCameraData cameraData = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = false;

            Light key = new GameObject("Key Light").AddComponent<Light>();
            key.transform.SetParent(transform, false);
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.91f, 0.78f);
            key.intensity = 1.7f;
            key.transform.rotation = Quaternion.Euler(38f, -32f, 0f);

            Light fill = new GameObject("Fill Light").AddComponent<Light>();
            fill.transform.SetParent(transform, false);
            fill.type = LightType.Directional;
            fill.color = new Color(0.48f, 0.65f, 1f);
            fill.intensity = 0.72f;
            fill.transform.rotation = Quaternion.Euler(25f, 145f, 0f);

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Prototype Floor";
            floor.transform.SetParent(transform, false);
            floor.transform.position = new Vector3(0f, -0.93f, 0f);
            floor.transform.localScale = new Vector3(1.25f, 1f, 1.25f);
            floor.GetComponent<Renderer>().sharedMaterial = CreateMaterial(
                "Prototype Floor",
                new Color(0.10f, 0.115f, 0.14f),
                0.02f);
        }

        private void BuildSkeleton()
        {
            displayRoot = new GameObject("Approved Troll - Layer 1 Skeleton").transform;
            displayRoot.SetParent(transform, false);
            displayRoot.rotation = Quaternion.Euler(0f, -14f, 0f);

            Dictionary<Vector3Int, VoxelMaterial> cells = new();

            // Skull and jaw. The skull intentionally has no ear protrusions.
            AddEllipsoid(cells, new Vector3(0f, 39f, 0f), new Vector3(5.2f, 5.1f, 4.0f));
            AddEllipsoid(cells, new Vector3(0f, 34.6f, -0.1f), new Vector3(4.2f, 2.8f, 3.25f));
            CarveEllipsoid(cells, new Vector3(-2.05f, 39.6f, 0f), new Vector3(1.65f, 1.7f, 8f));
            CarveEllipsoid(cells, new Vector3(2.05f, 39.6f, 0f), new Vector3(1.65f, 1.7f, 8f));
            CarveEllipsoid(cells, new Vector3(0f, 37.15f, 0f), new Vector3(0.82f, 1.4f, 8f));
            CarveBox(cells, new Vector3Int(-3, 34, -6), new Vector3Int(3, 35, 6));
            for (int tooth = -2; tooth <= 2; tooth += 2)
                AddCapsule(cells, new Vector3(tooth, 34f, -3.15f), new Vector3(tooth, 35f, -3.15f), 0.38f);

            // Neck, spine and sternum form the central load-bearing chain.
            AddCapsule(cells, new Vector3(0f, 31f, 0.8f), new Vector3(0f, 14f, 0.8f), 1.25f);
            AddCapsule(cells, new Vector3(0f, 30f, -2.1f), new Vector3(0f, 20.5f, -2.1f), 0.9f);
            AddEllipsoid(cells, new Vector3(0f, 31.2f, 0.1f), new Vector3(2.15f, 2.25f, 2f));

            BuildRibCage(cells);
            BuildShouldersAndArms(cells);
            BuildPelvisAndLegs(cells);

            Material[] materials =
            {
                CreateMaterial("Troll Cloth Placeholder", new Color(0.24f, 0.16f, 0.13f), 0.04f),
                CreateMaterial("Troll Skin Placeholder", new Color(0.30f, 0.53f, 0.17f), 0.05f),
                CreateMaterial("Troll Flesh Placeholder", new Color(0.60f, 0.08f, 0.075f), 0.03f),
                CreateMaterial("Troll Bone", new Color(0.78f, 0.69f, 0.50f), 0.11f)
            };

            GameObject skeletonObject = new("Troll Skeleton Voxels");
            skeletonObject.transform.SetParent(displayRoot, false);
            Skeleton = skeletonObject.AddComponent<VoxelDestructibleBody>();
            Skeleton.Initialize(cells, VoxelSize, Vector3.zero, materials, null, true);
        }

        private static void BuildRibCage(Dictionary<Vector3Int, VoxelMaterial> cells)
        {
            for (int rib = 0; rib < 5; rib++)
            {
                float y = 28.4f - rib * 2.25f;
                float width = 6.4f + rib * 0.2f;
                float drop = 0.9f + rib * 0.12f;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3[] path =
                    {
                        new(0f, y, 0.8f),
                        new(side * 2.7f, y + 0.35f, 0.25f),
                        new(side * width, y - 0.15f, -0.25f),
                        new(side * (width - 0.55f), y - drop, -1.35f),
                        new(side * 1.25f, y - drop - 0.55f, -2.05f)
                    };
                    AddPath(cells, path, 0.56f);
                }
            }

            AddCapsule(cells, new Vector3(-6.9f, 29.1f, 0f), new Vector3(0f, 28.7f, -1.5f), 0.66f);
            AddCapsule(cells, new Vector3(6.9f, 29.1f, 0f), new Vector3(0f, 28.7f, -1.5f), 0.66f);
        }

        private static void BuildShouldersAndArms(Dictionary<Vector3Int, VoxelMaterial> cells)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 shoulder = new(side * 8.0f, 28.4f, 0f);
                Vector3 elbow = new(side * 9.2f, 19.2f, 0.15f);
                Vector3 wrist = new(side * 9.65f, 10.4f, -0.05f);
                AddEllipsoid(cells, shoulder, new Vector3(2.0f, 2.0f, 2.0f));
                AddCapsule(cells, shoulder, elbow, 1.35f);
                AddEllipsoid(cells, elbow, new Vector3(1.65f, 1.55f, 1.55f));
                AddCapsule(cells, elbow, wrist, 1.1f);
                AddEllipsoid(cells, wrist, new Vector3(1.35f, 1.25f, 1.25f));

                Vector3 palm = new(side * 9.8f, 7.25f, -0.05f);
                AddEllipsoid(cells, palm, new Vector3(1.65f, 2.15f, 1.2f));
                for (int finger = 0; finger < 4; finger++)
                {
                    float outward = side * (-1.6f + finger * 1.05f);
                    Vector3 top = new(side * 9.8f + outward, 5.8f, -0.15f);
                    Vector3 bottom = new(side * 9.9f + outward, 2.7f + Mathf.Abs(finger - 1.5f) * 0.25f, -0.25f);
                    AddCapsule(cells, top, bottom, 0.48f);
                    AddEllipsoid(cells, bottom, new Vector3(0.58f, 0.6f, 0.58f));
                }

                Vector3 thumbTop = new(side * 8.55f, 7.5f, -0.05f);
                Vector3 thumbBottom = new(side * 7.75f, 5.45f, -0.45f);
                AddCapsule(cells, thumbTop, thumbBottom, 0.62f);
            }
        }

        private static void BuildPelvisAndLegs(Dictionary<Vector3Int, VoxelMaterial> cells)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3[] pelvicArch =
                {
                    new(0f, 15.8f, 0.8f),
                    new(side * 3.2f, 17.4f, 0.25f),
                    new(side * 5.5f, 15.6f, -0.15f),
                    new(side * 4.4f, 12.3f, -0.8f),
                    new(side * 1.35f, 12.2f, -1.55f)
                };
                AddPath(cells, pelvicArch, 0.92f);
            }
            AddEllipsoid(cells, new Vector3(0f, 14.2f, 0.65f), new Vector3(1.75f, 2.35f, 1.75f));

            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 hip = new(side * 3.8f, 12.5f, 0f);
                Vector3 knee = new(side * 4.15f, 4.3f, 0.1f);
                Vector3 ankle = new(side * 4.35f, -4.3f, -0.05f);
                AddEllipsoid(cells, hip, new Vector3(1.75f, 1.75f, 1.75f));
                AddCapsule(cells, hip, knee, 1.45f);
                AddEllipsoid(cells, knee, new Vector3(1.75f, 1.55f, 1.65f));
                AddCapsule(cells, knee, ankle, 1.18f);
                AddEllipsoid(cells, ankle, new Vector3(1.45f, 1.35f, 1.35f));

                Vector3 heel = new(side * 4.35f, -6.25f, 0.45f);
                AddEllipsoid(cells, heel, new Vector3(2.2f, 1.9f, 2.35f));
                for (int toe = 0; toe < 4; toe++)
                {
                    float lateral = side * (-1.25f + toe * 0.82f);
                    Vector3 toeStart = new(side * 4.35f + lateral, -6.55f, -0.75f);
                    Vector3 toeEnd = new(side * 4.35f + lateral, -6.65f, -3.35f + Mathf.Abs(toe - 1.5f) * 0.25f);
                    AddCapsule(cells, toeStart, toeEnd, 0.58f);
                }
            }
        }

        private static void AddPath(
            Dictionary<Vector3Int, VoxelMaterial> cells,
            IReadOnlyList<Vector3> points,
            float radius)
        {
            for (int index = 0; index < points.Count - 1; index++)
                AddCapsule(cells, points[index], points[index + 1], radius);
        }

        private static void AddCapsule(
            Dictionary<Vector3Int, VoxelMaterial> cells,
            Vector3 start,
            Vector3 end,
            float radius)
        {
            start *= DesignScale;
            end *= DesignScale;
            radius *= DesignScale;
            Vector3 minimum = Vector3.Min(start, end) - Vector3.one * radius;
            Vector3 maximum = Vector3.Max(start, end) + Vector3.one * radius;
            Vector3 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;

            ForEachCoordinate(minimum, maximum, coordinate =>
            {
                Vector3 point = coordinate;
                float t = lengthSquared < 0.0001f
                    ? 0f
                    : Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSquared);
                Vector3 nearest = start + segment * t;
                if ((point - nearest).sqrMagnitude <= radius * radius)
                    cells[coordinate] = VoxelMaterial.Bone;
            });
        }

        private static void AddEllipsoid(
            Dictionary<Vector3Int, VoxelMaterial> cells,
            Vector3 center,
            Vector3 radii)
        {
            center *= DesignScale;
            radii *= DesignScale;
            ForEachCoordinate(center - radii, center + radii, coordinate =>
            {
                Vector3 delta = (Vector3)coordinate - center;
                float normalized =
                    delta.x * delta.x / (radii.x * radii.x) +
                    delta.y * delta.y / (radii.y * radii.y) +
                    delta.z * delta.z / (radii.z * radii.z);
                if (normalized <= 1f)
                    cells[coordinate] = VoxelMaterial.Bone;
            });
        }

        private static void CarveEllipsoid(
            Dictionary<Vector3Int, VoxelMaterial> cells,
            Vector3 center,
            Vector3 radii)
        {
            center *= DesignScale;
            radii *= DesignScale;
            List<Vector3Int> removed = new();
            foreach (Vector3Int coordinate in cells.Keys)
            {
                Vector3 delta = (Vector3)coordinate - center;
                float normalized =
                    delta.x * delta.x / (radii.x * radii.x) +
                    delta.y * delta.y / (radii.y * radii.y) +
                    delta.z * delta.z / (radii.z * radii.z);
                if (normalized <= 1f)
                    removed.Add(coordinate);
            }
            foreach (Vector3Int coordinate in removed)
                cells.Remove(coordinate);
        }

        private static void CarveBox(
            Dictionary<Vector3Int, VoxelMaterial> cells,
            Vector3Int minimum,
            Vector3Int maximum)
        {
            Vector3 scaledMinimum = (Vector3)minimum * DesignScale;
            Vector3 scaledMaximum = (Vector3)maximum * DesignScale;
            for (int x = Mathf.FloorToInt(scaledMinimum.x); x <= Mathf.CeilToInt(scaledMaximum.x); x++)
            for (int y = Mathf.FloorToInt(scaledMinimum.y); y <= Mathf.CeilToInt(scaledMaximum.y); y++)
            for (int z = Mathf.FloorToInt(scaledMinimum.z); z <= Mathf.CeilToInt(scaledMaximum.z); z++)
                cells.Remove(new Vector3Int(x, y, z));
        }

        private static void ForEachCoordinate(Vector3 minimum, Vector3 maximum, Action<Vector3Int> action)
        {
            for (int x = Mathf.FloorToInt(minimum.x); x <= Mathf.CeilToInt(maximum.x); x++)
            for (int y = Mathf.FloorToInt(minimum.y); y <= Mathf.CeilToInt(maximum.y); y++)
            for (int z = Mathf.FloorToInt(minimum.z); z <= Mathf.CeilToInt(maximum.z); z++)
                action(new Vector3Int(x, y, z));
        }

        private Material CreateMaterial(string materialName, Color color, float smoothness)
        {
            Shader shader = Shader.Find("WildMagic/Voxel Prototype Lit")
                ?? Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard");
            Material material = new(shader)
            {
                name = materialName,
                color = color
            };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            generatedMaterials.Add(material);
            return material;
        }

        private void OnGUI()
        {
            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                font = uiFont,
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            bodyStyle ??= new GUIStyle(GUI.skin.label)
            {
                font = uiFont,
                fontSize = 14,
                normal = { textColor = new Color(0.80f, 0.84f, 0.91f) }
            };

            GUI.Box(new Rect(18f, 18f, 490f, 92f), GUIContent.none);
            GUI.Label(new Rect(34f, 29f, 450f, 30f), "ТРОЛЛЬ — СЛОЙ 1/4: СКЕЛЕТ", titleStyle);
            GUI.Label(new Rect(34f, 66f, 450f, 24f), "A/D или стрелки — вращать    R — вид спереди", bodyStyle);
        }
    }
}
