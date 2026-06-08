// BootstrapSceneSetup.cs — Editor menu item to create the Bootstrap scene
// with all required GameObjects (NetworkManager, GameManager, MapManager, etc.)
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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

            // ─── Network Manager (hosts NGO + Relay transport) ──────────────
            var networkGo = new GameObject("NetworkManager");
            networkGo.AddComponent<NetworkManager>();
            networkGo.AddComponent<UnityTransport>();
            networkGo.AddComponent<NetworkBootstrapper>();

            // ─── GameManager (server-authoritative state machine) ────────────
            var gmGo = new GameObject("GameManager");
            gmGo.AddComponent<Unity.Netcode.NetworkObject>(); // must be added BEFORE NetworkBehaviour
            var gm = gmGo.AddComponent<GameManager>();

            // Set minPlayersToStart to 1 for solo testing
            var gmSo = new SerializedObject(gm);
            var minProp = gmSo.FindProperty("minPlayersToStart");
            if (minProp != null) minProp.intValue = 1;
            gmSo.ApplyModifiedProperties();

            // ─── MapManager (BFS pathfinding, node lookup) ──────────────────
            var mapGo = new GameObject("MapManager");
            mapGo.AddComponent<MapManager>();

            // ─── DeckManager (card market) ──────────────────────────────────
            var deckGo = new GameObject("DeckManager");
            deckGo.AddComponent<DeckManager>();

            // ─── EncounterResolver ──────────────────────────────────────────
            var encounterGo = new GameObject("EncounterResolver");
            encounterGo.AddComponent<Unity.Netcode.NetworkObject>();
            encounterGo.AddComponent<EncounterResolver>();

            // ─── ShipMovement ───────────────────────────────────────────────
            var shipMoveGo = new GameObject("ShipMovement");
            shipMoveGo.AddComponent<Unity.Netcode.NetworkObject>();
            shipMoveGo.AddComponent<ShipMovement>();

            // ─── DebugGameUI (IMGUI debug panel) ────────────────────────────
            var debugUiGo = new GameObject("DebugGameUI");
            debugUiGo.AddComponent<DebugGameUI>();

            // ─── Map (parent for map nodes) ─────────────────────────────────
            var mapParent = new GameObject("Map");

            // ─── Build map nodes via MapBuilder ─────────────────────────────
            MapBuilder.BuildNodes(mapParent.transform);

            // ─── Camera setup for isometric overhead view ───────────────────
            var cam = Camera.main;
            if (cam != null)
            {
                // Map center is roughly at average of all node positions
                cam.transform.position = new Vector3(-2f, 20, -15);
                cam.transform.rotation = Quaternion.Euler(55, 0, 0);
                cam.fieldOfView = 60;
            }

            // ─── Add scene to build settings ────────────────────────────────
            var buildScenes = EditorBuildSettings.scenes;
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(buildScenes);
            list.Add(new EditorBuildSettingsScene(scene.path, true));
            EditorBuildSettings.scenes = list.ToArray();

            // ─── Save ──────────────────────────────────────────────────────
            EditorSceneManager.SaveScene(scene, "Assets/_Game/Scenes/Bootstrap.unity");
            Debug.Log("[BootstrapSceneSetup] Bootstrap scene created with all managers + map.");
        }
    }
}
#endif
