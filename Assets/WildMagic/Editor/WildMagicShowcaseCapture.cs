using System.IO;
using UnityEditor;
using UnityEngine;

namespace WildMagic.Prototype.Editor
{
    public static class WildMagicShowcaseCapture
    {
        private const int Width = 1920;
        private const int Height = 1080;

        [MenuItem("Wild Magic/Capture Visual Prototype")]
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
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static string Capture()
        {
            WildMagicShowcase showcase = WildMagicShowcase.CreateScene();

            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Artifacts"));
            Directory.CreateDirectory(directory);
            string outputPath = Path.Combine(directory, "WildMagicVisualPreview.png");
            string movementPath = Path.Combine(directory, "WildMagicMovementPreview.png");

            RenderTexture target = new(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                name = "WildMagic Preview Target",
                antiAliasing = 1
            };
            Texture2D image = new(Width, Height, TextureFormat.RGBA32, false);
            RenderTexture? previousActive = RenderTexture.active;
            RenderTexture? previousTarget = showcase.SceneCamera.targetTexture;

            try
            {
                showcase.SceneCamera.aspect = Width / (float)Height;
                showcase.SceneCamera.targetTexture = target;
                RenderTexture.active = target;

                CaptureFrame(showcase, image, outputPath, 7.35f);
                CaptureFrame(showcase, image, movementPath, 10.75f);
                Debug.Log($"Wild Magic visual prototype captured: {outputPath}");
            }
            finally
            {
                showcase.SceneCamera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(showcase.gameObject);
            }

            return outputPath;
        }

        private static void CaptureFrame(WildMagicShowcase showcase, Texture2D image, string path, float time)
        {
            showcase.SetPreviewTime(time);
            showcase.SceneCamera.Render();
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            image.Apply(false, false);
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
    }
}
