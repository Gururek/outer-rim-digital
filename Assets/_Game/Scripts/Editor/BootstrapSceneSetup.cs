// BootstrapSceneSetup.cs — V3: creates all manager GameObjects
// Force recompile — added using for clarity
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace OuterRim
{
    public static class BootstrapSceneSetup
    {
        [MenuItem("Tools/Outer Rim/Create Bootstrap Scene")]
        public static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Camera
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.transform.position = new Vector3(1f, 30f, -20f);
            cam.transform.rotation = Quaternion.Euler(55, 0, 0);
            cam.fieldOfView = 60;
            camGo.AddComponent<UniversalAdditionalCameraData>();
            camGo.AddComponent<AudioListener>();

            // Light
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            light.intensity = 1f;

            // NetworkManager
            var netGo = new GameObject("NetworkManager");
            netGo.AddComponent<NetworkManager>();
            netGo.AddComponent<UnityTransport>();
            netGo.AddComponent<NetworkBootstrapper>();

            // GameManager
            var gmGo = new GameObject("GameManager");
            gmGo.AddComponent<Unity.Netcode.NetworkObject>();
            var gm = gmGo.AddComponent<GameManager>();
            var gmSo = new SerializedObject(gm);
            var fp = gmSo.FindProperty("fameRequirement");
            if (fp != null) fp.intValue = 10;
            gmSo.ApplyModifiedProperties();

            // MapManager
            var mapGo = new GameObject("MapManager");
            mapGo.AddComponent<MapManager>();

            // DeckManager
            var deckGo = new GameObject("DeckManager");
            deckGo.AddComponent<DeckManager>();

            // DataBankManager
            var dbGo = new GameObject("DataBankManager");
            dbGo.AddComponent<DataBankManager>();

            // PatrolManager
            var patrolGo = new GameObject("PatrolManager");
            patrolGo.AddComponent<PatrolManager>();

            // EncounterResolver
            var encGo = new GameObject("EncounterResolver");
            encGo.AddComponent<Unity.Netcode.NetworkObject>();
            encGo.AddComponent<EncounterResolver>();

            // Visual systems
            {
                var hrGo = new GameObject("HyperspaceLaneRenderer");
                hrGo.transform.SetParent(mapGo.transform);
                hrGo.AddComponent<HyperspaceLaneRenderer>();
            }
            {
                var svmGo = new GameObject("ShipVisualManager");
                svmGo.AddComponent<Unity.Netcode.NetworkObject>();
                svmGo.AddComponent<ShipVisualManager>();
            }

            // ShipMovement
            var smGo = new GameObject("ShipMovement");
            smGo.AddComponent<Unity.Netcode.NetworkObject>();
            smGo.AddComponent<ShipMovement>();

            // CombatResolver
            var crGo = new GameObject("CombatResolver");
            crGo.AddComponent<Unity.Netcode.NetworkObject>();
            crGo.AddComponent<CombatResolver>();

            // GameUIManager — UI Toolkit (UIDocument) unified UI
            var uiGo = new GameObject("GameUIManager");
            var uiDoc = uiGo.AddComponent<UnityEngine.UIElements.UIDocument>();
            uiDoc.panelSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(
                "Assets/_Game/UI/GameUIPanelSettings.asset");
            uiDoc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>(
                "Assets/_Game/UI/GameUI.uxml");
            uiGo.AddComponent<GameUIManager>();
            // NOTE: UI Toolkit does not require a uGUI EventSystem for input.

            // Map
            var mapParent = new GameObject("Map");
            MapBuilder.BuildNodes(mapParent.transform);

            // Build settings
            var bs = EditorBuildSettings.scenes;
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(bs);
            list.Add(new EditorBuildSettingsScene(scene.path, true));
            EditorBuildSettings.scenes = list.ToArray();

            EditorSceneManager.SaveScene(scene, "Assets/_Game/Scenes/Bootstrap.unity");
            Debug.Log("[Bootstrap] V3 scene created with GameUIManager (unified UI).");
        }
    }
}
#endif