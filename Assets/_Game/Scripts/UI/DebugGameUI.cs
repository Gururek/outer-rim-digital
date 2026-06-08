// DebugGameUI.cs — Minimal IMGUI debug panel for game loop testing.
// No Canvas/EventSystem needed. Attach to a plain GameObject in the scene.
using UnityEngine;
using Unity.Netcode;

namespace OuterRim
{
    public class DebugGameUI : MonoBehaviour
    {
        private string joinCodeInput = "";
        private string nodeIdInput = "";
        private string statusMessage = "";

        private void OnGUI()
        {
            var net = NetworkManager.Singleton;
            if (net == null) return;

            GUILayout.BeginArea(new Rect(10, 10, 360, 700));
            try
            {
                // Connection status
                int clientCount = 0;
                try { clientCount = net.IsServer ? net.ConnectedClients.Count : 0; }
                catch { clientCount = 0; }

                GUILayout.Label($"Status: {(net.IsConnectedClient ? "CONNECTED" : "OFFLINE")}");
                GUILayout.Label($"Clients: {clientCount}");

                if (!net.IsConnectedClient)
                {
                    GUILayout.Space(10);
                    if (GUILayout.Button("START HOST"))
                    {
                        var bootstrapper = FindObjectOfType<NetworkBootstrapper>();
                        if (bootstrapper != null)
                        {
                            bootstrapper.StartHost();
                        }
                        else
                        {
                            statusMessage = "No NetworkBootstrapper in scene!";
                        }
                    }

                    GUILayout.Label("Join Code:");
                    joinCodeInput = GUILayout.TextField(joinCodeInput, GUILayout.Width(200));
                    if (GUILayout.Button("JOIN GAME") && !string.IsNullOrEmpty(joinCodeInput))
                    {
                        var bootstrapper = FindObjectOfType<NetworkBootstrapper>();
                        if (bootstrapper != null)
                        {
                            bootstrapper.StartClient(joinCodeInput);
                        }
                    }

                    if (!string.IsNullOrEmpty(statusMessage))
                        GUILayout.Label(statusMessage);
                }
                else
                {
                    var gm = GameManager.Instance;
                    if (gm == null)
                    {
                        GUILayout.Label("Waiting for GameManager...");
                        return;
                    }

                    var ap = gm.GetActivePlayer();
                    bool isMyTurn = ap != null && ap.OwnerClientId == net.LocalClientId;

                    GUILayout.Label($"Turn: {gm.CurrentTurnNumber} | Phase: {gm.CurrentPhase}");
                    GUILayout.Label(isMyTurn ? "*** YOUR TURN ***" : "(waiting)");

                    if (ap != null)
                    {
                        GUILayout.Label($"Current Node: {ap.CurrentNodeId.Value} | Speed: {ap.Speed.Value}");
                        GUILayout.Label($"Fame: {ap.Fame.Value} | Credits: {ap.Credits.Value}");
                        GUILayout.Label($"Health: {ap.Health.Value}/{ap.MaxHealth.Value} | Ship: {ap.ShipHealth.Value}/{ap.MaxShipHealth.Value}");
                    }

                    GUILayout.Space(10);

                    // Phase-specific UI
                    switch (gm.CurrentPhase)
                    {
                        case GamePhase.PlanningPhase:
                            if (isMyTurn)
                            {
                                GUILayout.Label("--- PLANNING ---");
                                if (GUILayout.Button("MOVE SHIP"))
                                    gm.SubmitPlanningChoiceServerRpc(PlanningChoice.MoveShip);
                                if (GUILayout.Button("HEAL"))
                                    gm.SubmitPlanningChoiceServerRpc(PlanningChoice.HealDamage);
                                if (GUILayout.Button("COLLECT CREDITS (2000)"))
                                    gm.SubmitPlanningChoiceServerRpc(PlanningChoice.CollectCredits);
                            }
                            break;

                        case GamePhase.ActionPhase:
                            if (isMyTurn && ap != null)
                            {
                                GUILayout.Label("--- ACTION ---");
                                GUILayout.Label("Move to node (ID):");
                                nodeIdInput = GUILayout.TextField(nodeIdInput, GUILayout.Width(80));

                                if (GUILayout.Button("MOVE TO NODE") && int.TryParse(nodeIdInput, out int destId))
                                {
                                    gm.ConfirmShipMovementServerRpc(destId);
                                    nodeIdInput = "";
                                }

                                // Show reachable nodes
                                if (MapManager.Instance != null)
                                {
                                    var reachable = MapManager.Instance.GetReachableNodes(ap.CurrentNodeId.Value, ap.Speed.Value);
                                    GUILayout.Label($"Reachable: [{string.Join(", ", reachable)}]");
                                }

                                if (GUILayout.Button("END ACTION"))
                                    gm.EndActionPhaseServerRpc();
                            }
                            break;

                        case GamePhase.EncounterPhase:
                            GUILayout.Label("--- ENCOUNTER ---");
                            GUILayout.Label("Encounter resolving... (auto)");
                            break;

                        case GamePhase.CheckingWinCondition:
                            GUILayout.Label("--- WIN CHECK ---");
                            break;

                        case GamePhase.GameOver:
                            GUILayout.Label("=== GAME OVER ===");
                            break;
                    }
                }
            }
            finally
            {
                GUILayout.EndArea();
            }
        }
    }
}
