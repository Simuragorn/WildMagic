using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace WildMagic.Prototype
{
    /// <summary>
    /// First volumetric destruction prototype: a filled layered arm whose disconnected voxel
    /// islands become physical objects.
    /// </summary>
    public sealed class VoxelDamagePrototypeGame : MonoBehaviour
    {
        private readonly List<Material> generatedMaterials = new();
        private Camera sceneCamera = null!;
        private Material[] voxelMaterials = null!;
        private Font? uiFont;
        private GUIStyle? titleStyle;
        private GUIStyle? bodyStyle;
        private GUIStyle? statusStyle;
        private float damageRadius = 0.23f;
        private float nextShotTime;
        private string status = "Стреляйте в руку левой кнопкой мыши";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (SceneManager.GetActiveScene().name != "VoxelDamagePrototype")
                return;
            if (FindFirstObjectByType<VoxelDamagePrototypeGame>() != null)
                return;
            new GameObject("[Voxel Damage Prototype]").AddComponent<VoxelDamagePrototypeGame>();
        }

        private void Awake()
        {
            BuildScene();
        }

        private void Update()
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                SceneManager.LoadSceneAsync("VoxelDamagePrototype", LoadSceneMode.Single);
                enabled = false;
                return;
            }

            Mouse? mouse = Mouse.current;
            if (mouse == null)
                return;

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                damageRadius = Mathf.Clamp(damageRadius + Mathf.Sign(scroll) * 0.035f, 0.11f, 0.52f);

            if (mouse.leftButton.wasPressedThisFrame && Time.time >= nextShotTime)
                Shoot(mouse.position.ReadValue());
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
            foreach (Camera existingCamera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                existingCamera.gameObject.SetActive(false);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.24f, 0.28f, 0.34f);
            RenderSettings.ambientEquatorColor = new Color(0.11f, 0.13f, 0.16f);
            RenderSettings.ambientGroundColor = new Color(0.035f, 0.04f, 0.05f);
            RenderSettings.ambientIntensity = 1.35f;

            CreateCamera();
            CreateLighting();
            CreateEnvironment();
            CreateVoxelArm();

            uiFont = Resources.Load<Font>("RobotoMono-Regular");
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void CreateCamera()
        {
            GameObject cameraObject = new("Voxel Prototype Camera");
            cameraObject.transform.SetParent(transform, false);
            cameraObject.tag = "MainCamera";
            sceneCamera = cameraObject.AddComponent<Camera>();
            sceneCamera.fieldOfView = 39f;
            sceneCamera.nearClipPlane = 0.05f;
            sceneCamera.farClipPlane = 50f;
            sceneCamera.clearFlags = CameraClearFlags.SolidColor;
            sceneCamera.backgroundColor = new Color(0.016f, 0.021f, 0.03f);
            cameraObject.AddComponent<AudioListener>();
            sceneCamera.GetUniversalAdditionalCameraData().SetRenderer(1);
            cameraObject.transform.position = new Vector3(0.1f, 2.05f, -7.2f);
            cameraObject.transform.LookAt(new Vector3(0f, 1.45f, 0f));
        }

        private void CreateLighting()
        {
            GameObject keyObject = new("Voxel Key Light");
            keyObject.transform.SetParent(transform, false);
            keyObject.transform.rotation = Quaternion.Euler(42f, -32f, 0f);
            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.65f;
            key.color = new Color(1f, 0.84f, 0.68f);
            key.shadows = LightShadows.Soft;

            GameObject fillObject = new("Voxel Fill Light");
            fillObject.transform.SetParent(transform, false);
            fillObject.transform.position = new Vector3(-2.2f, 3.2f, -2.4f);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 8f;
            fill.intensity = 7.5f;
            fill.color = new Color(0.30f, 0.52f, 1f);
        }

        private void CreateEnvironment()
        {
            Material floorMaterial = CreateMaterial("Voxel Floor", new Color(0.07f, 0.09f, 0.12f));
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Voxel Laboratory Floor";
            floor.transform.SetParent(transform, false);
            floor.transform.position = new Vector3(0f, -0.18f, 0f);
            floor.transform.localScale = new Vector3(11f, 0.25f, 7f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;

            Material mountMaterial = CreateMaterial("Arm Mount", new Color(0.13f, 0.16f, 0.21f));
            GameObject mount = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mount.name = "Fixed Shoulder Mount";
            mount.transform.SetParent(transform, false);
            mount.transform.position = new Vector3(-2.15f, 1.45f, 0.12f);
            mount.transform.localScale = new Vector3(0.5f, 1.7f, 1.35f);
            mount.GetComponent<Renderer>().sharedMaterial = mountMaterial;
        }

        private void CreateVoxelArm()
        {
            voxelMaterials = new[]
            {
                CreateMaterial("Voxel Cloth", new Color(0.40f, 0.16f, 0.62f)),
                CreateMaterial("Voxel Skin", new Color(0.34f, 0.58f, 0.22f)),
                CreateMaterial("Voxel Flesh", new Color(0.67f, 0.035f, 0.035f)),
                CreateMaterial("Voxel Bone", new Color(0.84f, 0.74f, 0.52f))
            };

            const int length = 35;
            const int crossSize = 17;
            const float size = 0.105f;
            Dictionary<Vector3Int, VoxelMaterial> cells = new();
            HashSet<Vector3Int> anchors = new();
            float center = (crossSize - 1) * 0.5f;

            for (int x = 0; x < length; x++)
            {
                float radiusY;
                float radiusZ;
                bool clothRegion;
                if (x < 14)
                {
                    radiusY = Mathf.Lerp(6.2f, 5.25f, x / 13f);
                    radiusZ = Mathf.Lerp(5.5f, 4.65f, x / 13f);
                    clothRegion = true;
                }
                else if (x < 28)
                {
                    radiusY = Mathf.Lerp(4.7f, 3.75f, (x - 14) / 13f);
                    radiusZ = Mathf.Lerp(4.2f, 3.35f, (x - 14) / 13f);
                    clothRegion = false;
                }
                else
                {
                    float handCurve = 1f - Mathf.Abs(x - 31f) / 4f;
                    radiusY = 3.8f + Mathf.Max(0f, handCurve) * 1.25f;
                    radiusZ = 3.3f + Mathf.Max(0f, handCurve) * 0.75f;
                    clothRegion = false;
                }

                for (int y = 0; y < crossSize; y++)
                for (int z = 0; z < crossSize; z++)
                {
                    float dy = y - center;
                    float dz = z - center;
                    float normalized = Mathf.Sqrt(
                        dy * dy / (radiusY * radiusY)
                        + dz * dz / (radiusZ * radiusZ));
                    if (normalized > 1f)
                        continue;

                    float surfaceDepth = (1f - normalized) * Mathf.Min(radiusY, radiusZ);
                    float boneRadius = x < 15 ? 1.65f : 1.25f;
                    float radialCells = Mathf.Sqrt(dy * dy + dz * dz);
                    VoxelMaterial material;
                    if (radialCells <= boneRadius && x < 30)
                        material = VoxelMaterial.Bone;
                    else if (surfaceDepth < 0.95f)
                        material = clothRegion ? VoxelMaterial.Cloth : VoxelMaterial.Skin;
                    else if (surfaceDepth < (clothRegion ? 1.85f : 1.45f))
                        material = VoxelMaterial.Skin;
                    else
                        material = VoxelMaterial.Flesh;

                    Vector3Int coordinate = new(x, y, z);
                    cells.Add(coordinate, material);
                    if (x <= 1)
                        anchors.Add(coordinate);
                }
            }

            Vector3 origin = new(
                -1.9f,
                1.45f - center * size,
                -center * size);
            GameObject armObject = new("Layered voxel arm");
            armObject.transform.SetParent(transform, false);
            VoxelDestructibleBody arm = armObject.AddComponent<VoxelDestructibleBody>();
            arm.Initialize(cells, size, origin, voxelMaterials, anchors, true);
            status = $"Объёмная рука создана: {arm.VoxelCount} блоков";
        }

        private void Shoot(Vector2 screenPosition)
        {
            Ray ray = sceneCamera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 50f))
            {
                status = "Выстрел прошёл мимо";
                return;
            }

            VoxelDestructibleBody? target = hit.collider.GetComponentInParent<VoxelDestructibleBody>();
            if (target == null)
            {
                status = "Это не разрушаемое тело";
                return;
            }

            nextShotTime = Time.time + 0.10f;
            Vector3 damageCenter = hit.point + ray.direction * damageRadius * 0.42f;
            VoxelDestructibleBody.DamageReport report = target.DamageWorld(
                damageCenter,
                damageRadius,
                ray.direction);
            SpawnImpact(hit.point, hit.normal, report.Flesh > 0);

            status = $"Удалено {report.Removed}: ткань {report.Cloth}, кожа {report.Skin}, " +
                     $"плоть {report.Flesh}, кость {report.Bone}";
            if (report.FragmentCount > 0)
                status += $"  |  Отделено: {report.FragmentCount} частей ({report.DetachedVoxels} блоков)";
        }

        private void SpawnImpact(Vector3 position, Vector3 normal, bool blood)
        {
            GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.name = blood ? "Voxel Blood Impact" : "Voxel Magic Impact";
            flash.transform.position = position + normal * 0.02f;
            flash.transform.localScale = Vector3.one * (blood ? 0.095f : 0.075f);
            flash.GetComponent<Renderer>().sharedMaterial = blood
                ? voxelMaterials[(int)VoxelMaterial.Flesh - 1]
                : CreateTransientImpactMaterial();
            Destroy(flash.GetComponent<Collider>());
            Destroy(flash, 0.22f);
        }

        private Material CreateTransientImpactMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            Material material = new(shader)
            {
                name = "Transient Voxel Impact"
            };
            material.color = new Color(1f, 0.26f, 0.025f);
            Destroy(material, 0.3f);
            return material;
        }

        private Material CreateMaterial(string materialName, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new(shader)
            {
                name = materialName,
                color = color
            };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.08f);
            generatedMaterials.Add(material);
            return material;
        }

        private void OnGUI()
        {
            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                font = uiFont,
                fontSize = 23,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            bodyStyle ??= new GUIStyle(GUI.skin.label)
            {
                font = uiFont,
                fontSize = 15,
                wordWrap = true,
                normal = { textColor = new Color(0.82f, 0.86f, 0.92f) }
            };
            statusStyle ??= new GUIStyle(bodyStyle)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.76f, 0.32f) }
            };

            GUI.Box(new Rect(20f, 18f, 590f, 146f), GUIContent.none);
            GUI.Label(new Rect(36f, 28f, 550f, 32f), "ОБЪЁМНОЕ БЛОЧНОЕ ТЕЛО", titleStyle);
            GUI.Label(
                new Rect(36f, 63f, 550f, 48f),
                "ЛКМ — разрушить блоки    Колесо — изменить радиус    R — перезапустить",
                bodyStyle);
            GUI.Label(
                new Rect(36f, 112f, 550f, 25f),
                $"Радиус: {damageRadius:F2} м",
                bodyStyle);
            GUI.Label(new Rect(36f, 137f, 970f, 28f), status, statusStyle);

            GUI.Box(new Rect(Screen.width - 225f, 18f, 205f, 120f), GUIContent.none);
            DrawLegend(Screen.width - 205f, 35f, new Color(0.40f, 0.16f, 0.62f), "Ткань");
            DrawLegend(Screen.width - 205f, 58f, new Color(0.34f, 0.58f, 0.22f), "Кожа");
            DrawLegend(Screen.width - 205f, 81f, new Color(0.67f, 0.035f, 0.035f), "Плоть");
            DrawLegend(Screen.width - 205f, 104f, new Color(0.84f, 0.74f, 0.52f), "Кость");
        }

        private void DrawLegend(float x, float y, Color color, string label)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(x, y, 16f, 16f), Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.Label(new Rect(x + 25f, y - 3f, 130f, 22f), label, bodyStyle);
        }
    }
}
