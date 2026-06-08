// DebugGameUI.cs — IMGUI debug panel with Outer Rim rule actions
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

            GUILayout.BeginArea(new Rect(10, 10, 380, 750));
            try
            {
                int clientCount = 0;
                try { clientCount = net.IsServer ? net.ConnectedClients.Count : 0; }
                catch { clientCount = 0; }

                GUILayout.Label($"Status: {(net.IsConnectedClient ? "CONNECTED" : "OFFLINE")} | Clients: {clientCount}");

                if (!net.IsConnectedClient)
                {
                    GUILayout.Space(10);
                    if (GUILayout.Button("START HOST"))
                    {
                        var bootstrapper = FindObjectOfType<NetworkBootstrapper>();
                        if (bootstrapper != null) bootstrapper.StartHost();
                        else statusMessage = "No NetworkBootstrapper!";
                    }

                    GUILayout.Label("Join Code:");
                    joinCodeInput = GUILayout.TextField(joinCodeInput, GUILayout.Width(200));
                    if (GUILayout.Button("JOIN GAME") && !string.IsNullOrEmpty(joinCodeInput))
                    {
                        var bootstrapper = FindObjectOfType<NetworkBootstrapper>();
                        if (bootstrapper != null) bootstrapper.StartClient(joinCodeInput);
                    }

                    if (!string.IsNullOrEmpty(statusMessage)) GUILayout.Label(statusMessage);
                }
                else
                {
                    var gm = GameManager.Instance;
                    if (gm == null) { GUILayout.Label("Waiting for GameManager..."); return; }

                    var ap = gm.GetActivePlayer();
                    bool isMyTurn = ap != null && ap.OwnerClientId == net.LocalClientId;

                    GUILayout.Label($"Turn: {gm.CurrentTurnNumber} | Phase: {gm.CurrentPhase}");
                    GUILayout.Label(isMyTurn ? "*** YOUR TURN ***" : "(waiting)");

                    if (ap != null)
                    {
                        GUILayout.Label($"Node: {ap.CurrentNodeId.Value} | Speed: {ap.Speed.Value} | Fame: {ap.Fame.Value}/{10}");
                        GUILayout.Label($"Credits: {ap.Credits.Value} | Health: {ap.Health.Value}/{ap.MaxHealth.Value} | Ship: {ap.ShipHealth.Value}/{ap.MaxShipHealth.Value}");
                        if (ap.IsDefeated.Value)
                            GUILayout.Label("*** DEFEATED — must Heal next turn ***");
                    }

                    GUILayout.Space(10);

                    switch (gm.CurrentPhase)
                    {
                        case GamePhase.PlanningPhase:
                            if (isMyTurn)
                            {
                                GUILayout.Label("--- PLANNING (choose 1) ---");
                                if (ap != null && ap.IsDefeated.Value)
                                    GUILayout.Label("You are defeated — Heal is chosen automatically.");
                                else
                                {
                                    if (GUILayout.Button("MOVE SHIP")) gm.SubmitPlanningChoiceServerRpc(PlanningChoice.MoveShip);
                                    if (GUILayout.Button("HEAL (full repair)")) gm.SubmitPlanningChoiceServerRpc(PlanningChoice.HealDamage);
                                    if (GUILayout.Button("COLLECT CREDITS (+2000)")) gm.SubmitPlanningChoiceServerRpc(PlanningChoice.CollectCredits);
                                }
                            }
                            break;

                        case GamePhase.ActionPhase:
                            if (isMyTurn && ap != null)
                            {
                                GUILayout.Label("--- ACTION (any/all) ---");

                                // Movement
                                GUILayout.Label("Move to node ID:");
                                nodeIdInput = GUILayout.TextField(nodeIdInput, GUILayout.Width(80));
                                if (GUILayout.Button("MOVE TO NODE") && int.TryParse(nodeIdInput, out int destId))
                                { gm.ConfirmShipMovementServerRpc(destId); nodeIdInput = ""; }

                                if (MapManager.Instance != null)
                                {
                                    var reachable = MapManager.Instance.GetReachableNodes(ap.CurrentNodeId.Value, ap.Speed.Value);
                                    GUILayout.Label($"Reachable: [{string.Join(", ", reachable)}]");
                                }

                                GUILayout.Space(5);

                                // Market actions (on planets)
                                if (GUILayout.Button("BUY GEAR (if on planet)")) gm.BuyCardServerRpc(MarketDeckType.Gear, 0);
                                if (GUILayout.Button("BUY MOD (if on planet)")) gm.BuyCardServerRpc(MarketDeckType.Mods, 0);

                                GUILayout.Space(5);

                                // Deliver / Complete
                                if (GUILayout.Button("DELIVER CARGO (+2000cr, +1 fame)")) gm.DeliverCargoServerRpc(0);
                                if (GUILayout.Button("COMPLETE BOUNTY (+5000cr, +2 fame)")) gm.CompleteBountyServerRpc(0);

                                GUILayout.Space(5);

                                if (GUILayout.Button("END ACTION → Encounter")) gm.EndActionPhaseServerRpc();
                            }
                            break;

                        case GamePhase.EncounterPhase:
                            GUILayout.Label("--- ENCOUNTER ---");
                            GUILayout.Label("Resolving encounter... (auto)");
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
            finally { GUILayout.EndArea(); }
        }
    }
}
