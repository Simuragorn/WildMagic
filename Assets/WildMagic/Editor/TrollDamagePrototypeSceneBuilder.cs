using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

namespace WildMagic.Prototype.Editor
{
    public static class TrollDamagePrototypeSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/TrollDamagePrototype.unity";
        private const string PipelinePath = "Assets/Settings/UniversalRP.asset";
        private const string Renderer3DPath = "Assets/Settings/Universal3DRenderer.asset";
        private const string ModelPath = "Assets/WildMagic/Resources/Models/TrollArmLayers.fbx";
        private const string ModularModelPath = "Assets/WildMagic/Resources/Models/ModularTrollLayers.fbx";

        [MenuItem("Wild Magic/Create Troll Damage Prototype Scene")]
        public static void CreateScene()
        {
            Ensure3DRenderer();
            EnsureModelReadable(ModelPath);
            EnsureModelReadable(ModularModelPath);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "TrollDamagePrototype";
            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureSceneInBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void EnsureSceneInBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = new(EditorBuildSettings.scenes);
            int existingIndex = scenes.FindIndex(scene => scene.path == ScenePath);
            if (existingIndex >= 0)
            {
                scenes[existingIndex] = new EditorBuildSettingsScene(ScenePath, true);
            }
            else
            {
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void Ensure3DRenderer()
        {
            UniversalRendererData renderer3D =
                AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Renderer3DPath);
            if (renderer3D == null)
            {
                renderer3D = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer3D.name = "Universal 3D Renderer";
                AssetDatabase.CreateAsset(renderer3D, Renderer3DPath);
            }

            UniversalRenderPipelineAsset pipeline =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            SerializedObject serializedPipeline = new(pipeline);
            SerializedProperty rendererList = serializedPipeline.FindProperty("m_RendererDataList");

            bool alreadyAdded = false;
            for (int index = 0; index < rendererList.arraySize; index++)
            {
                if (rendererList.GetArrayElementAtIndex(index).objectReferenceValue == renderer3D)
                {
                    alreadyAdded = true;
                    break;
                }
            }

            if (!alreadyAdded)
            {
                int index = rendererList.arraySize;
                rendererList.InsertArrayElementAtIndex(index);
                rendererList.GetArrayElementAtIndex(index).objectReferenceValue = renderer3D;
                serializedPipeline.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(pipeline);
            }

            AssetDatabase.SaveAssets();
        }

        private static void EnsureModelReadable(string modelPath)
        {
            ModelImporter importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null || importer.isReadable)
                return;

            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        public static void CreateSceneForAutomation()
        {
            CreateScene();
            EditorApplication.Exit(0);
        }
    }
}
