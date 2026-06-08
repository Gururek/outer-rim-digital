// DebugGameUI.cs — IMGUI debug panel with Outer Rim rule actions
using UnityEngine;
using Unity.Netcode;

namespace OuterRim
{
    public class DebugGameUI : MonoBehaviour
    {
        private string joinCodeInput = "";
        private string nodeIdInput = "";
        private string tradeTargetInput = "";
        private string tradeAmountInput = "1000";
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
                        var b = FindObjectOfType<NetworkBootstrapper>();
                        if (b != null) b.StartHost(); else statusMessage = "No NetworkBootstrapper!";
                    }
                    GUILayout.Label("Join Code:");
                    joinCodeInput = GUILayout.TextField(joinCodeInput, GUILayout.Width(200));
                    if (GUILayout.Button("JOIN GAME") && !string.IsNullOrEmpty(joinCodeInput))
                        FindObjectOfType<NetworkBootstrapper>()?.StartClient(joinCodeInput);
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
                        GUILayout.Label($"Cr: {ap.Credits.Value} | HP: {ap.Health.Value}/{ap.MaxHealth.Value} | Ship: {ap.ShipHealth.Value}/{ap.MaxShipHealth.Value}");
                        if (ap.IsDefeated.Value) GUILayout.Label("*** DEFEATED ***");
                    }

                    GUILayout.Space(10);

                    switch (gm.CurrentPhase)
                    {
                        case GamePhase.PlanningPhase:
                            if (isMyTurn && ap != null && !ap.IsDefeated.Value)
                            {
                                GUILayout.Label("--- PLANNING (choose 1) ---");
                                if (GUILayout.Button("MOVE SHIP")) gm.SubmitPlanningChoiceServerRpc(PlanningChoice.MoveShip);
                                if (GUILayout.Button("HEAL (full repair)")) gm.SubmitPlanningChoiceServerRpc(PlanningChoice.HealDamage);
                                if (GUILayout.Button("COLLECT CREDITS (+2000)")) gm.SubmitPlanningChoiceServerRpc(PlanningChoice.CollectCredits);
                            }
                            break;

                        case GamePhase.ActionPhase:
                            if (isMyTurn && ap != null)
                            {
                                GUILayout.Label("--- ACTION (any/all) ---");

                                // Movement
                                GUILayout.BeginHorizontal();
                                nodeIdInput = GUILayout.TextField(nodeIdInput, GUILayout.Width(60));
                                if (GUILayout.Button("MOVE", GUILayout.Width(80)))
                                { if (int.TryParse(nodeIdInput, out int d)) { gm.ConfirmShipMovementServerRpc(d); nodeIdInput = ""; } }
                                GUILayout.EndHorizontal();

                                if (MapManager.Instance != null)
                                {
                                    var r = MapManager.Instance.GetReachableNodes(ap.CurrentNodeId.Value, ap.Speed.Value);
                                    GUILayout.Label($"Reachable: [{string.Join(", ", r)}]");
                                }

                                GUILayout.Space(5);

                                // Market
                                GUILayout.BeginHorizontal();
                                if (GUILayout.Button("Buy Gear")) gm.BuyCardServerRpc(MarketDeckType.Gear, 0);
                                if (GUILayout.Button("Buy Mod")) gm.BuyCardServerRpc(MarketDeckType.Mods, 0);
                                if (GUILayout.Button("Buy Cargo")) gm.BuyCardServerRpc(MarketDeckType.Cargo, 0);
                                GUILayout.EndHorizontal();

                                GUILayout.Space(5);

                                // Deliver / Bounty
                                GUILayout.BeginHorizontal();
                                if (GUILayout.Button("Deliver (+1f)")) gm.DeliverCargoServerRpc(0);
                                if (GUILayout.Button("Bounty (+2f)")) gm.CompleteBountyServerRpc(0);
                                GUILayout.EndHorizontal();

                                GUILayout.Space(5);

                                // Trade (Outer Rim: trade with player in same space)
                                GUILayout.Label("--- TRADE ---");
                                GUILayout.BeginHorizontal();
                                GUILayout.Label("Target ID:", GUILayout.Width(60));
                                tradeTargetInput = GUILayout.TextField(tradeTargetInput, GUILayout.Width(40));
                                GUILayout.Label("Cr:", GUILayout.Width(25));
                                tradeAmountInput = GUILayout.TextField(tradeAmountInput, GUILayout.Width(60));
                                GUILayout.EndHorizontal();
                                if (GUILayout.Button("SEND CREDITS") && ulong.TryParse(tradeTargetInput, out ulong tid) && int.TryParse(tradeAmountInput, out int amt))
                                    gm.TradeCreditsServerRpc(tid, amt);

                                GUILayout.Space(5);
                                if (GUILayout.Button("END ACTION → Encounter")) gm.EndActionPhaseServerRpc();
                            }
                            break;

                        case GamePhase.EncounterPhase:
                            GUILayout.Label("--- ENCOUNTER (auto) ---");
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
