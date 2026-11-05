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

                LayeredDamageSegment damagedSegment = null!;
                Vector3 damagedPoint = default;
                Vector3 damagedDirection = default;
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
                    damagedSegment = segment;
                    damagedPoint = woundPoint;
                    damagedDirection = direction;
                    break;
                }

                if (damagedSegment == null)
                    throw new MissingComponentException("Head damage segment");

                // A third hit in the face must deepen that wound, not detach the whole head.
                damagedSegment.ApplyHit(damagedPoint, damagedDirection);
                if (damagedSegment.IsDetached)
                    throw new InvalidOperationException("A hit away from the neck detached the head.");

                RenderTexture.active = target;
                camera.Render();
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                image.Apply(false, false);

                string outputPath = Path.Combine(directory, "ModularTrollPrototypePreview.png");
                File.WriteAllBytes(outputPath, image.EncodeToPNG());
                Debug.Log($"Modular troll prototype captured: {outputPath}");

                if (!damagedSegment.TryGetJointSurfacePoint(out Vector3 jointHitPoint))
                    throw new InvalidOperationException("The troll neck has no joint surface point.");
                Vector3 jointDirection = (jointHitPoint - camera.transform.position).normalized;
                for (int hit = 0; hit < 10 && !damagedSegment.IsDetached; hit++)
                    damagedSegment.ApplyHit(jointHitPoint, jointDirection);
                if (!damagedSegment.IsDetached)
                    throw new InvalidOperationException(
                        $"Repeated neck damage did not detach the head. " +
                        $"Surface-to-joint distance: " +
                        $"{Vector3.Distance(jointHitPoint, damagedSegment.JointWorldPosition):F3}; " +
                        $"status: {damagedSegment.StatusText}");

                damagedSegment.transform.position += new Vector3(0.55f, 0.58f, 0f);
                camera.Render();
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                image.Apply(false, false);
                string detachedPath = Path.Combine(directory, "HighFidelityModularTrollDetachedPreview.png");
                File.WriteAllBytes(detachedPath, image.EncodeToPNG());
                Debug.Log($"Detached modular troll captured: {detachedPath}");
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

        public static void ValidateJointRulesFromCommandLine()
        {
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                GameObject root = new("Modular Troll Joint Validation Root");
                TrollDamagePrototypeGame game = root.AddComponent<TrollDamagePrototypeGame>();
                MethodInfo? buildScene = typeof(TrollDamagePrototypeGame).GetMethod(
                    "BuildScene",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (buildScene == null)
                    throw new MissingMethodException("TrollDamagePrototypeGame.BuildScene");
                buildScene.Invoke(game, null);

                Camera camera = root.GetComponentInChildren<Camera>(true);
                if (camera == null)
                    throw new MissingComponentException("Prototype Camera");

                LayeredDamageSegment[] segments = root.GetComponentsInChildren<LayeredDamageSegment>(true);
                int validated = 0;
                foreach (LayeredDamageSegment segment in segments)
                {
                    if (!segment.HasJoint)
                        continue;

                    Renderer skin = null!;
                    foreach (Renderer renderer in segment.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer.name.EndsWith("_Skin", StringComparison.Ordinal))
                        {
                            skin = renderer;
                            break;
                        }
                    }
                    if (skin == null)
                        throw new MissingComponentException($"Skin for {segment.name}");

                    Vector3 bodyDirection = (skin.bounds.center - camera.transform.position).normalized;
                    if (!segment.TryGetSurfacePoint(
                            new Ray(camera.transform.position, bodyDirection),
                            out Vector3 bodyPoint))
                    {
                        throw new InvalidOperationException($"Could not hit the middle of {segment.name}.");
                    }
                    for (int hit = 0; hit < 6; hit++)
                        segment.ApplyHit(bodyPoint, bodyDirection);
                    if (segment.IsDetached)
                        throw new InvalidOperationException($"Body hits detached {segment.name} away from its joint.");

                    if (!segment.TryGetJointSurfacePoint(out Vector3 jointPoint))
                        throw new InvalidOperationException($"Could not find the joint surface of {segment.name}.");
                    Vector3 jointDirection = (jointPoint - camera.transform.position).normalized;
                    for (int hit = 0; hit < 10 && !segment.IsDetached; hit++)
                        segment.ApplyHit(jointPoint, jointDirection);
                    if (!segment.IsDetached)
                        throw new InvalidOperationException($"Joint damage did not detach {segment.name}.");

                    validated++;
                }

                if (validated != 5)
                    throw new InvalidOperationException($"Expected five detachable segments, validated {validated}.");
                Debug.Log("JOINT_SEVER_VALIDATION_PASSED: head, both arms and both legs");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }
    }
}
