using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WildMagic.Prototype.Editor
{
    public static class VoxelDamagePrototypeCapture
    {
        private const int Width = 1280;
        private const int Height = 720;

        [MenuItem("Wild Magic/Capture Voxel Damage Prototype")]
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
            Scene previousScene = SceneManager.GetActiveScene();
            string previousScenePath = previousScene.path;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderTexture target = new(Width, Height, 24, RenderTextureFormat.ARGB32);
            Texture2D image = new(Width, Height, TextureFormat.RGBA32, false);
            RenderTexture previousTarget = RenderTexture.active;

            try
            {
                GameObject root = new("Voxel Capture Root");
                VoxelDamagePrototypeGame game = root.AddComponent<VoxelDamagePrototypeGame>();
                if (root.GetComponentInChildren<Camera>(true) == null)
                {
                    MethodInfo? buildScene = typeof(VoxelDamagePrototypeGame).GetMethod(
                        "BuildScene",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    if (buildScene == null)
                        throw new MissingMethodException("VoxelDamagePrototypeGame.BuildScene");
                    buildScene.Invoke(game, null);
                }

                Camera camera = root.GetComponentInChildren<Camera>(true);
                VoxelDestructibleBody body = root.GetComponentInChildren<VoxelDestructibleBody>(true);
                if (camera == null || body == null)
                    throw new MissingComponentException("Voxel prototype camera or body");
                camera.aspect = Width / (float)Height;
                camera.targetTexture = target;

                string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Artifacts"));
                Directory.CreateDirectory(directory);
                string intactPath = Path.Combine(directory, "VoxelDamagePrototypeIntact.png");
                Render(camera, target, image, intactPath);

                Bounds bounds = body.WorldBounds;
                int cloth = 0;
                int skin = 0;
                int flesh = 0;
                int bone = 0;
                Vector3 direction = Vector3.forward;

                // Peel the upper-arm layers from the camera-facing surface inward.
                float woundX = bounds.min.x + bounds.size.x * 0.23f;
                for (int depth = 0; depth < 5; depth++)
                {
                    Vector3 point = new(
                        woundX,
                        bounds.center.y,
                        bounds.min.z + 0.07f + depth * 0.105f);
                    VoxelDestructibleBody.DamageReport wound = body.DamageWorld(point, 0.14f, direction);
                    cloth += wound.Cloth;
                    skin += wound.Skin;
                    flesh += wound.Flesh;
                    bone += wound.Bone;
                }

                // Remove a complete cross-section: connectivity, not a hit counter, must detach it.
                Vector3 cutPoint = new(
                    bounds.center.x + bounds.size.x * 0.12f,
                    bounds.center.y,
                    bounds.center.z);
                VoxelDestructibleBody.DamageReport cut = body.DamageWorld(cutPoint, 0.57f, direction);
                cloth += cut.Cloth;
                skin += cut.Skin;
                flesh += cut.Flesh;
                bone += cut.Bone;

                if (cloth == 0 || skin == 0 || flesh == 0 || bone == 0)
                    throw new InvalidOperationException(
                        $"Not all volume layers were damaged: cloth={cloth}, skin={skin}, " +
                        $"flesh={flesh}, bone={bone}.");
                if (cut.FragmentCount == 0 || cut.DetachedVoxels == 0)
                    throw new InvalidOperationException("The severed voxel cross-section stayed connected.");

                foreach (VoxelDestructibleBody fragment in
                         UnityEngine.Object.FindObjectsByType<VoxelDestructibleBody>(FindObjectsSortMode.None))
                {
                    if (!fragment.IsAnchored)
                        fragment.transform.position += new Vector3(0.15f, -0.55f, 0.18f);
                }

                string outputPath = Path.Combine(directory, "VoxelDamagePrototypePreview.png");
                Render(camera, target, image, outputPath);
                Debug.Log(
                    $"VOXEL_DAMAGE_VALIDATION_PASSED: cloth={cloth}, skin={skin}, flesh={flesh}, " +
                    $"bone={bone}, fragments={cut.FragmentCount}, detached={cut.DetachedVoxels}");
                camera.targetTexture = null;
                return outputPath;
            }
            finally
            {
                RenderTexture.active = previousTarget;
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(target);
                if (!string.IsNullOrEmpty(previousScenePath))
                    EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);
            }
        }

        private static void Render(
            Camera camera,
            RenderTexture target,
            Texture2D image,
            string outputPath)
        {
            RenderTexture.active = target;
            // The first render can be spent warming URP shader variants in a clean batch project.
            camera.Render();
            camera.Render();
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            image.Apply(false, false);
            File.WriteAllBytes(outputPath, image.EncodeToPNG());
        }
    }
}
