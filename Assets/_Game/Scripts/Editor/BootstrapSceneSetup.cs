// BootstrapSceneSetup.cs — Editor menu item to create the Bootstrap scene
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

            // ─── Camera + Light (not provided by EmptyScene) ──────────────
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.transform.position = new Vector3(-2f, 20f, -15f);
            cam.transform.rotation = Quaternion.Euler(55, 0, 0);
            cam.fieldOfView = 60;
            camGo.AddComponent<UniversalAdditionalCameraData>();
            camGo.AddComponent<AudioListener>();

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            light.intensity = 1f;

            // ─── Network Manager ──────────────────────────────────────────
            var networkGo = new GameObject("NetworkManager");
            networkGo.AddComponent<NetworkManager>();
            networkGo.AddComponent<UnityTransport>();
            networkGo.AddComponent<NetworkBootstrapper>();

            // ─── GameManager ──────────────────────────────────────────────
            var gmGo = new GameObject("GameManager");
            gmGo.AddComponent<Unity.Netcode.NetworkObject>();
            var gm = gmGo.AddComponent<GameManager>();
            var gmSo = new SerializedObject(gm);
            var minProp = gmSo.FindProperty("minPlayersToStart");
            if (minProp != null) minProp.intValue = 1;
            gmSo.ApplyModifiedProperties();

            // ─── MapManager ───────────────────────────────────────────────
            var mapGo = new GameObject("MapManager");
            mapGo.AddComponent<MapManager>();

            // ─── DeckManager ──────────────────────────────────────────────
            var deckGo = new GameObject("DeckManager");
            deckGo.AddComponent<DeckManager>();

            // ─── EncounterResolver ────────────────────────────────────────
            var encounterGo = new GameObject("EncounterResolver");
            encounterGo.AddComponent<Unity.Netcode.NetworkObject>();
            encounterGo.AddComponent<EncounterResolver>();

            // ─── ShipMovement ─────────────────────────────────────────────
            var shipMoveGo = new GameObject("ShipMovement");
            shipMoveGo.AddComponent<Unity.Netcode.NetworkObject>();
            shipMoveGo.AddComponent<ShipMovement>();

            // ─── DebugGameUI ──────────────────────────────────────────────
            var debugUiGo = new GameObject("DebugGameUI");
            debugUiGo.AddComponent<DebugGameUI>();

            // ─── Map nodes ────────────────────────────────────────────────
            var mapParent = new GameObject("Map");
            MapBuilder.BuildNodes(mapParent.transform);

            // ─── Build settings ───────────────────────────────────────────
            var buildScenes = EditorBuildSettings.scenes;
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(buildScenes);
            list.Add(new EditorBuildSettingsScene(scene.path, true));
            EditorBuildSettings.scenes = list.ToArray();

            EditorSceneManager.SaveScene(scene, "Assets/_Game/Scenes/Bootstrap.unity");
            Debug.Log("[BootstrapSceneSetup] Bootstrap scene created with camera, light, map, and all managers.");
        }
    }
}
#endif
