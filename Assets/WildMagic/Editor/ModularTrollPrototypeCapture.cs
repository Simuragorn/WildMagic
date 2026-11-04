using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WildMagic.Prototype.Editor
{
    public static class ModularTrollPrototypeCapture
    {
        private const int Width = 1280;
        private const int Height = 720;

        [MenuItem("Wild Magic/Capture Modular Troll Prototype")]
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
            if (previousScene.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new OperationCanceledException("Capture was cancelled because the current scene was not saved.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderTexture target = new(Width, Height, 24, RenderTextureFormat.ARGB32);
            Texture2D image = new(Width, Height, TextureFormat.RGBA32, false);
            RenderTexture previousTarget = RenderTexture.active;

            try
            {
                GameObject root = new("Modular Troll Capture Root");
                TrollDamagePrototypeGame game = root.AddComponent<TrollDamagePrototypeGame>();
                if (root.GetComponentInChildren<Camera>(true) == null)
                {
                    MethodInfo? buildScene = typeof(TrollDamagePrototypeGame).GetMethod(
                        "BuildScene",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    if (buildScene == null)
                        throw new MissingMethodException("TrollDamagePrototypeGame.BuildScene");
                    buildScene.Invoke(game, null);
                }

                Camera camera = root.GetComponentInChildren<Camera>(true);
                if (camera == null)
                    throw new MissingComponentException("Prototype Camera");
                camera.aspect = Width / (float)Height;
                camera.targetTexture = target;

                string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Artifacts"));
                Directory.CreateDirectory(directory);
                RenderTexture.active = target;
                camera.Render();
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                image.Apply(false, false);
                string intactPath = Path.Combine(directory, "HighFidelityModularTrollIntactPreview.png");
                File.WriteAllBytes(intactPath, image.EncodeToPNG());
                Debug.Log($"Intact modular troll captured: {intactPath}");

                LayeredDamageSegment[] segments = root.GetComponentsInChildren<LayeredDamageSegment>(true);
                foreach (LayeredDamageSegment segment in segments)
                {
                    if (!segment.name.StartsWith("Head", StringComparison.Ordinal))
                        continue;

                    Renderer headSkin = null!;
                    foreach (Renderer renderer in segment.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer.name == "Head_Skin")
                        {
                            headSkin = renderer;
                            break;
                        }
                    }
                    if (headSkin == null)
                        throw new MissingComponentException("Head_Skin");

                    Vector3 direction = (headSkin.bounds.center - camera.transform.position).normalized;
                    Ray woundRay = new(camera.transform.position, direction);
                    if (!segment.TryGetSurfacePoint(woundRay, out Vector3 woundPoint))
                        throw new InvalidOperationException("The capture ray did not hit the troll head.");
                    segment.ApplyHit(woundPoint, direction);
                    segment.ApplyHit(woundPoint, direction);
                    break;
                }

                RenderTexture.active = target;
                camera.Render();
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                image.Apply(false, false);

                string outputPath = Path.Combine(directory, "ModularTrollPrototypePreview.png");
                File.WriteAllBytes(outputPath, image.EncodeToPNG());
                Debug.Log($"Modular troll prototype captured: {outputPath}");
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
