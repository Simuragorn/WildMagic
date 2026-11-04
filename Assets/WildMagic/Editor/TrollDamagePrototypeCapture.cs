using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace WildMagic.Prototype.Editor
{
    public static class TrollDamagePrototypeCapture
    {
        private const int Width = 1280;
        private const int Height = 720;

        [MenuItem("Wild Magic/Capture Troll Damage Prototype")]
        public static void CaptureMenu()
        {
            string path = Capture();
            EditorUtility.RevealInFinder(path);
        }

        public static void CaptureFromCommandLine()
        {
            try
            {
                Capture();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static string Capture()
        {
            GameObject prefab = Resources.Load<GameObject>("Models/TrollArmLayers");
            if (prefab == null)
                throw new InvalidOperationException("TrollArmLayers could not be loaded from Resources.");

            Shader damageShader = Shader.Find("WildMagic/LayeredDamageCutout");
            if (damageShader == null)
                throw new InvalidOperationException("LayeredDamageCutout shader was not imported.");

            GameObject root = new("Troll Damage Capture Root");
            RenderTexture target = new(Width, Height, 24, RenderTextureFormat.ARGB32);
            Texture2D image = new(Width, Height, TextureFormat.RGBA32, false);
            RenderTexture? previousActive = RenderTexture.active;

            try
            {
                GameObject arm = UnityEngine.Object.Instantiate(prefab, root.transform);
                arm.transform.localScale = Vector3.one * 2.35f;
                arm.transform.rotation = Quaternion.Euler(0f, 168f, 0f);

                Renderer skin = FindRenderer(arm, "Arm_L_Skin");
                Renderer flesh = FindRenderer(arm, "Arm_L_Flesh");
                Renderer bone = FindRenderer(arm, "Arm_L_Bone");
                arm.transform.position += new Vector3(0f, 1.62f, 0f) - skin.bounds.center;
                Debug.Log($"Imported troll arm bounds: center={skin.bounds.center}, size={skin.bounds.size}, " +
                          $"rootScale={arm.transform.localScale}, skinScale={skin.transform.lossyScale}");

                Material skinMaterial = CreateDamageMaterial(
                    damageShader,
                    new Color(0.25f, 0.48f, 0.20f),
                    new Color(0.07f, 0.025f, 0.012f));
                Material fleshMaterial = CreateDamageMaterial(
                    damageShader,
                    new Color(0.63f, 0.045f, 0.035f),
                    new Color(0.16f, 0.012f, 0.008f));
                Material boneMaterial = CreateDamageMaterial(
                    damageShader,
                    new Color(0.78f, 0.68f, 0.48f),
                    new Color(0.28f, 0.18f, 0.08f));
                skin.sharedMaterial = skinMaterial;
                flesh.sharedMaterial = fleshMaterial;
                bone.sharedMaterial = boneMaterial;

                Material floorMaterial = CreateLitMaterial(new Color(0.055f, 0.07f, 0.085f));
                GameObject floor = CreateCube(root.transform, new Vector3(0f, -0.14f, 0f),
                    new Vector3(10f, 0.25f, 7f), floorMaterial);
                floor.name = "Floor";
                GameObject wall = CreateCube(root.transform, new Vector3(0f, 2.5f, 1.9f),
                    new Vector3(10f, 5f, 0.18f), CreateLitMaterial(new Color(0.04f, 0.052f, 0.068f)));
                wall.name = "Back Wall";

                GameObject cameraObject = new("Capture Camera");
                cameraObject.transform.SetParent(root.transform);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.fieldOfView = 38f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.018f, 0.024f, 0.032f);
                camera.aspect = Width / (float)Height;
                camera.targetTexture = target;
                camera.GetUniversalAdditionalCameraData().SetRenderer(1);
                Vector3 lookTarget = skin.bounds.center + new Vector3(0f, -0.08f, 0f);
                camera.transform.position = lookTarget + new Vector3(0.15f, 0.05f, -6.3f);
                camera.transform.LookAt(lookTarget);

                MeshCollider captureCollider = skin.gameObject.AddComponent<MeshCollider>();
                captureCollider.sharedMesh = skin.GetComponent<MeshFilter>().sharedMesh;
                Physics.SyncTransforms();
                Ray woundRay = camera.ViewportPointToRay(new Vector3(0.5f, 0.52f, 0f));
                if (!captureCollider.Raycast(woundRay, out RaycastHit woundHit, 20f))
                    throw new InvalidOperationException("Capture ray did not hit the troll arm.");

                Vector3 skinDamagePoint = skin.transform.InverseTransformPoint(woundHit.point);
                Vector3 fleshDamagePoint = flesh.transform.InverseTransformPoint(woundHit.point);
                Vector3 boneDamagePoint = bone.transform.InverseTransformPoint(woundHit.point);
                skinMaterial.SetFloat("_RequireReveal", 0f);
                skinMaterial.SetInt("_DamageCount", 1);
                skinMaterial.SetVectorArray("_DamagePoints", DamageArray(skinDamagePoint, 0.064f));
                fleshMaterial.SetFloat("_RequireReveal", 1f);
                fleshMaterial.SetInt("_RevealCount", 1);
                fleshMaterial.SetVectorArray("_RevealPoints", DamageArray(fleshDamagePoint, 0.082f));
                fleshMaterial.SetInt("_DamageCount", 1);
                fleshMaterial.SetVectorArray("_DamagePoints", DamageArray(fleshDamagePoint, 0.035f));
                boneMaterial.SetFloat("_RequireReveal", 1f);
                boneMaterial.SetInt("_RevealCount", 1);
                boneMaterial.SetVectorArray("_RevealPoints", DamageArray(boneDamagePoint, 0.058f));
                boneMaterial.SetInt("_DamageCount", 0);

                GameObject keyObject = new("Key Light");
                keyObject.transform.SetParent(root.transform);
                Light key = keyObject.AddComponent<Light>();
                key.type = LightType.Directional;
                key.color = new Color(1f, 0.91f, 0.78f);
                key.intensity = 1.55f;
                keyObject.transform.rotation = Quaternion.Euler(42f, -32f, 0f);

                GameObject rimObject = new("Rim Light");
                rimObject.transform.SetParent(root.transform);
                Light rim = rimObject.AddComponent<Light>();
                rim.type = LightType.Point;
                rim.color = new Color(0.24f, 0.55f, 1f);
                rim.intensity = 5.5f;
                rim.range = 8f;
                rimObject.transform.position = new Vector3(2.4f, 2.7f, 1.8f);

                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.22f, 0.28f, 0.34f);
                RenderSettings.ambientEquatorColor = new Color(0.10f, 0.13f, 0.16f);
                RenderSettings.ambientGroundColor = new Color(0.035f, 0.04f, 0.05f);

                RenderTexture.active = target;
                camera.Render();
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                image.Apply(false, false);

                string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Artifacts"));
                Directory.CreateDirectory(directory);
                string outputPath = Path.Combine(directory, "TrollDamagePrototypePreview.png");
                File.WriteAllBytes(outputPath, image.EncodeToPNG());
                Debug.Log($"Troll damage prototype captured: {outputPath}");

                UnityEngine.Object.DestroyImmediate(skinMaterial);
                UnityEngine.Object.DestroyImmediate(fleshMaterial);
                UnityEngine.Object.DestroyImmediate(boneMaterial);
                UnityEngine.Object.DestroyImmediate(floorMaterial);
                return outputPath;
            }
            finally
            {
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Vector4[] DamageArray(Vector3 point, float radius)
        {
            Vector4[] damage = new Vector4[32];
            damage[0] = new Vector4(point.x, point.y, point.z, radius);
            return damage;
        }

        private static Renderer FindRenderer(GameObject root, string name)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name == name)
                    return renderer;
            }
            throw new MissingComponentException(name);
        }

        private static Material CreateDamageMaterial(Shader shader, Color color, Color burn)
        {
            Material material = new(shader);
            material.SetColor("_BaseColor", color);
            material.SetColor("_BurnColor", burn);
            material.SetFloat("_BurnWidth", 0.03f);
            return material;
        }

        private static Material CreateLitMaterial(Color color)
        {
            Material material = new(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            return material;
        }

        private static GameObject CreateCube(
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(parent);
            cube.transform.position = position;
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube;
        }
    }
}
