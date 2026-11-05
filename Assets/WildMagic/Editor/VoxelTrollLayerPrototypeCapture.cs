using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WildMagic.Prototype.Editor
{
    public static class VoxelTrollLayerPrototypeCapture
    {
        private const int Width = 1280;
        private const int Height = 900;

        [MenuItem("Wild Magic/Capture Voxel Troll Skeleton")]
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
                GameObject root = new("Voxel Troll Skeleton Capture");
                VoxelTrollLayerPrototype prototype = root.AddComponent<VoxelTrollLayerPrototype>();
                if (prototype.Skeleton == null)
                {
                    MethodInfo? buildScene = typeof(VoxelTrollLayerPrototype).GetMethod(
                        "BuildScene",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    if (buildScene == null)
                        throw new MissingMethodException("VoxelTrollLayerPrototype.BuildScene");
                    buildScene.Invoke(prototype, null);
                }

                Camera camera = root.GetComponentInChildren<Camera>(true);
                VoxelDestructibleBody? skeleton = prototype.Skeleton;
                if (camera == null || skeleton == null)
                    throw new MissingComponentException("Voxel troll prototype camera or skeleton");
                if (skeleton.VoxelCount < 1000)
                    throw new InvalidOperationException($"Skeleton is too sparse: {skeleton.VoxelCount} voxels.");

                camera.aspect = Width / (float)Height;
                camera.targetTexture = target;
                RenderTexture.active = target;
                camera.Render();
                camera.Render();
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                image.Apply(false, false);

                string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Artifacts"));
                Directory.CreateDirectory(directory);
                string outputPath = Path.Combine(directory, "VoxelTrollSkeletonPrototype.png");
                File.WriteAllBytes(outputPath, image.EncodeToPNG());
                Debug.Log($"VOXEL_TROLL_SKELETON_VALIDATION_PASSED: voxels={skeleton.VoxelCount}");
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
    }
}
