using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace WildMagic.Prototype.Editor
{
    public static class VoxelTrollLayerPrototypeSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/VoxelTrollLayerPrototype.unity";

        [MenuItem("Wild Magic/Create Voxel Troll Layer Prototype Scene")]
        public static void CreateScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "VoxelTrollLayerPrototype";
            EditorSceneManager.SaveScene(scene, ScenePath);

            List<EditorBuildSettingsScene> scenes = new(EditorBuildSettings.scenes);
            int existingIndex = scenes.FindIndex(candidate => candidate.path == ScenePath);
            if (existingIndex >= 0)
                scenes[existingIndex] = new EditorBuildSettingsScene(ScenePath, true);
            else
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static void CreateSceneForAutomation()
        {
            CreateScene();
            EditorApplication.Exit(0);
        }
    }
}
