// DebugGameUI.cs — V2 debug panel
using UnityEngine;
using Unity.Netcode;

namespace OuterRim
{
    public class DebugGameUI : MonoBehaviour
    {
        private string joinCodeInput = "", nodeIdInput = "", tradeTargetInput = "", tradeAmountInput = "1000", statusMessage = "";

        private void OnGUI()
        {
            var net = NetworkManager.Singleton;
            if (net == null) return;

            GUILayout.BeginArea(new Rect(10, 10, 380, 750));
            try
            {
                int cc = 0;
                try { cc = net.IsServer ? net.ConnectedClients.Count : 0; } catch { cc = 0; }
                GUILayout.Label($"Status: {(net.IsConnectedClient ? "CONNECTED" : "OFFLINE")} | Clients: {cc}");

                if (!net.IsConnectedClient)
                {
                    GUILayout.Space(10);
                    if (GUILayout.Button("START HOST"))
                        FindObjectOfType<NetworkBootstrapper>()?.StartHost();
                    GUILayout.Label("Join Code:");
                    joinCodeInput = GUILayout.TextField(joinCodeInput, GUILayout.Width(200));
                    if (GUILayout.Button("JOIN") && !string.IsNullOrEmpty(joinCodeInput))
                        FindObjectOfType<NetworkBootstrapper>()?.StartClient(joinCodeInput);
                    if (!string.IsNullOrEmpty(statusMessage)) GUILayout.Label(statusMessage);
                }
                else
                {
                    var gm = GameManager.Instance;
                    if (gm == null) { GUILayout.Label("Waiting..."); return; }
                    var ap = gm.GetActivePlayer();
                    bool myTurn = ap != null && ap.OwnerClientId == net.LocalClientId;

                    GUILayout.Label($"Turn {gm.CurrentTurnNumber} | {gm.CurrentPhase} | Fame: {ap?.Fame.Value}/{gm.FameRequirement}");
                    GUILayout.Label(myTurn ? "*** YOUR TURN ***" : "(waiting)");
                    if (ap != null)
                    {
                        GUILayout.Label($"Node: {ap.CurrentNodeId.Value} | HD: {ap.Hyperdrive.Value} | Cr: {ap.Credits.Value}");
                        GUILayout.Label($"HP: {ap.Health.Value}/{ap.MaxHealth.Value} | Ship: {ap.ShipHealth.Value}/{ap.MaxShipHealth.Value}");
                        if (ap.IsDefeated.Value) GUILayout.Label("*** DEFEATED ***");
                    }

                    GUILayout.Space(10);
                    switch (gm.CurrentPhase)
                    {
                        case GamePhase.PlanningPhase:
                            if (myTurn && ap != null && !ap.IsDefeated.Value)
                            {
                                GUILayout.Label("--- PLANNING (choose 1) ---");
                                if (GUILayout.Button("MOVE SHIP")) gm.SubmitPlanningChoiceServerRpc(PlanningChoice.MoveShip);
                                if (GUILayout.Button("RECOVER (full heal)")) gm.SubmitPlanningChoiceServerRpc(PlanningChoice.RecoverDamage);
                                if (GUILayout.Button("GAIN CREDITS (+2000)")) gm.SubmitPlanningChoiceServerRpc(PlanningChoice.GainCredits);
                            }
                            break;

                        case GamePhase.ActionPhase:
                            if (myTurn && ap != null)
                            {
                                GUILayout.Label("--- ACTION (any/all) ---");
                                GUILayout.BeginHorizontal();
                                nodeIdInput = GUILayout.TextField(nodeIdInput, GUILayout.Width(60));
                                if (GUILayout.Button("MOVE", GUILayout.Width(80)) && int.TryParse(nodeIdInput, out int d))
                                { gm.ConfirmMoveServerRpc(d); nodeIdInput = ""; }
                                GUILayout.EndHorizontal();
                                if (MapManager.Instance != null)
                                    GUILayout.Label($"Reachable: [{string.Join(", ", MapManager.Instance.GetReachableNodes(ap.CurrentNodeId.Value, ap.Hyperdrive.Value))}]");
                                GUILayout.Space(3);
                                GUILayout.BeginHorizontal();
                                if (GUILayout.Button("Buy Gear")) gm.BuyCardServerRpc(MarketDeckType.GearAndMod, 0);
                                if (GUILayout.Button("Buy Cargo")) gm.BuyCardServerRpc(MarketDeckType.Cargo, 0);
                                if (GUILayout.Button("Buy Ship")) gm.BuyCardServerRpc(MarketDeckType.Ship, 0);
                                GUILayout.EndHorizontal();
                                GUILayout.Space(3);
                                GUILayout.BeginHorizontal();
                                if (GUILayout.Button("Deliver (+1f)")) gm.DeliverCargoServerRpc(0);
                                if (GUILayout.Button("Bounty (+2f)")) gm.CompleteBountyServerRpc(0);
                                GUILayout.EndHorizontal();
                                GUILayout.Space(3);
                                GUILayout.Label("--- TRADE ---");
                                GUILayout.BeginHorizontal();
                                GUILayout.Label("ID:", GUILayout.Width(25));
                                tradeTargetInput = GUILayout.TextField(tradeTargetInput, GUILayout.Width(40));
                                GUILayout.Label("Cr:", GUILayout.Width(25));
                                tradeAmountInput = GUILayout.TextField(tradeAmountInput, GUILayout.Width(60));
                                GUILayout.EndHorizontal();
                                if (GUILayout.Button("SEND") && ulong.TryParse(tradeTargetInput, out ulong tid) && int.TryParse(tradeAmountInput, out int amt))
                                    gm.TradeCreditsServerRpc(tid, amt);
                                GUILayout.Space(5);
                                if (GUILayout.Button("END ACTION")) gm.EndActionPhaseServerRpc();
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
