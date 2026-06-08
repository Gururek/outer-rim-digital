// GameHUD.cs — Phase 3: proper HUD overlay showing game state, ship stats, reputation
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

namespace OuterRim
{
    public class GameHUD : MonoBehaviour
    {
        [Header("References")]
        private Canvas canvas;
        private PlayerState localPlayer;

        // Top bar elements
        private Text fameText;
        private Text creditsText;
        private Text phaseText;
        private Text turnText;

        // Left panel - ship stats
        private Text shipNameText;
        private Text hyperdriveText;
        private Text hullText;
        private Text combatText;
        private Text cargoText;
        private Text crewText;

        // Right panel - reputation
        private Text huttRepText;
        private Text syndicateRepText;
        private Text imperialRepText;
        private Text rebelRepText;

        private void Start()
        {
            CreateHUD();
            InvokeRepeating(nameof(RefreshDisplay), 0.2f, 0.3f);
        }

        private void CreateHUD()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;
            gameObject.AddComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
            gameObject.AddComponent<GraphicRaycaster>();

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // ─── TOP BAR ────────────────────────────────────────────────────
            var topBar = CreatePanel("TopHUD", new Vector2(0, 1), new Vector2(1, 0), 50);
            topBar.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.1f, 0.9f);

            fameText = CreateLabel("Fame", new Vector2(0.02f, 0.5f), topBar.transform, font, 24, Color.yellow);
            creditsText = CreateLabel("Credits", new Vector2(0.15f, 0.5f), topBar.transform, font, 24, Color.white);
            phaseText = CreateLabel("Phase", new Vector2(0.35f, 0.5f), topBar.transform, font, 18, Color.cyan);
            turnText = CreateLabel("Turn", new Vector2(0.55f, 0.5f), topBar.transform, font, 18, Color.gray);

            // ─── LEFT PANEL — SHIP STATS ────────────────────────────────────
            var leftPanel = CreatePanel("ShipPanel", new Vector2(0, 0.5f), new Vector2(0, 0.8f), 180);
            leftPanel.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.1f, 0.85f);

            var leftY = 0.95f;
            shipNameText = CreateLabel("Ship", new Vector2(0.1f, leftY), leftPanel.transform, font, 18, Color.cyan);
            hyperdriveText = CreateLabel("HD", new Vector2(0.1f, leftY - 0.08f), leftPanel.transform, font, 16, Color.white);
            hullText = CreateLabel("Hull", new Vector2(0.1f, leftY - 0.16f), leftPanel.transform, font, 16, Color.white);
            combatText = CreateLabel("Combat", new Vector2(0.1f, leftY - 0.24f), leftPanel.transform, font, 16, Color.white);
            cargoText = CreateLabel("Cargo", new Vector2(0.1f, leftY - 0.32f), leftPanel.transform, font, 16, Color.white);
            crewText = CreateLabel("Crew", new Vector2(0.1f, leftY - 0.40f), leftPanel.transform, font, 16, Color.white);

            // ─── RIGHT PANEL — REPUTATION ───────────────────────────────────
            var rightPanel = CreatePanel("RepPanel", new Vector2(1, 0.5f), new Vector2(0, 0.5f), 160);
            rightPanel.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.1f, 0.85f);

            var rightY = 0.95f;
            huttRepText = CreateLabel("Hutt", new Vector2(0.1f, rightY), rightPanel.transform, font, 16, Color.yellow);
            syndicateRepText = CreateLabel("Syndicate", new Vector2(0.1f, rightY - 0.1f), rightPanel.transform, font, 16, Color.red);
            imperialRepText = CreateLabel("Imperial", new Vector2(0.1f, rightY - 0.2f), rightPanel.transform, font, 16, Color.blue);
            rebelRepText = CreateLabel("Rebel", new Vector2(0.1f, rightY - 0.3f), rightPanel.transform, font, 16, new Color(1f, 0.5f, 0.5f));
        }

        private GameObject CreatePanel(string name, Vector2 anchor, Vector2 size, float heightOrWidth)
        {
            var go = new GameObject(name);
            go.transform.SetParent(canvas.transform, false);
            var rt = go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();

            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(anchor.x, anchor.y > 0.5f ? 1 : 0);

            if (size.x > 0) // Horizontal bar
            {
                rt.anchorMax = new Vector2(1, anchor.y);
                rt.sizeDelta = new Vector2(0, heightOrWidth);
            }
            else // Vertical panel
            {
                rt.anchorMin = new Vector2(anchor.x, 0);
                rt.sizeDelta = new Vector2(heightOrWidth, 0);
            }

            return go;
        }

        private Text CreateLabel(string name, Vector2 anchor, Transform parent, Font font, int size, Color color)
        {
            var go = new GameObject($"Label_{name}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            var txt = go.AddComponent<Text>();

            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(300, size + 4);

            txt.font = font;
            txt.fontSize = size;
            txt.color = color;
            txt.alignment = TextAnchor.MiddleLeft;

            return txt;
        }

        private void RefreshDisplay()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient) return;

            // Find local player
            if (localPlayer == null)
            {
                foreach (var ps in FindObjectsOfType<PlayerState>())
                    if (ps.IsOwner) { localPlayer = ps; break; }
            }
            if (localPlayer == null) return;

            var gm = GameManager.Instance;
            if (gm == null) return;

            // Top bar
            fameText.text = $"★ {localPlayer.Fame.Value}/{gm.FameRequirement}";
            creditsText.text = $"$ {localPlayer.Credits.Value:N0}";
            phaseText.text = $"{gm.CurrentPhase}";
            turnText.text = $"Turn {gm.CurrentTurnNumber}";

            bool isMyTurn = gm.GetActivePlayer()?.OwnerClientId == NetworkManager.Singleton.LocalClientId;
            phaseText.color = isMyTurn ? Color.green : Color.cyan;

            // Ship stats
            shipNameText.text = $"🚀 SHIP";
            hyperdriveText.text = $"Hyperdrive: {localPlayer.Hyperdrive.Value}";
            hullText.text = $"Hull: {localPlayer.ShipHealth.Value}/{localPlayer.MaxShipHealth.Value}";
            combatText.text = $"Combat: {localPlayer.ShipCombatValue.Value}";
            cargoText.text = $"Cargo: {localPlayer.CargoUsed.Value}/{localPlayer.CargoSlots.Value}";
            crewText.text = $"Crew: {localPlayer.CrewUsed.Value}/{localPlayer.CrewSlots.Value}";

            // Reputation
            huttRepText.text = $"Hutt: {FormatRep(localPlayer.HuttRep.Value)}";
            syndicateRepText.text = $"Syndicate: {FormatRep(localPlayer.SyndicateRep.Value)}";
            imperialRepText.text = $"Imperial: {FormatRep(localPlayer.ImperialRep.Value)}";
            rebelRepText.text = $"Rebel: {FormatRep(localPlayer.RebelRep.Value)}";

            RepaintRepColor(huttRepText, localPlayer.HuttRep.Value, Color.yellow);
            RepaintRepColor(syndicateRepText, localPlayer.SyndicateRep.Value, Color.red);
            RepaintRepColor(imperialRepText, localPlayer.ImperialRep.Value, Color.blue);
            RepaintRepColor(rebelRepText, localPlayer.RebelRep.Value, new Color(1f, 0.5f, 0.5f));
        }

        private string FormatRep(ReputationStatus rep) => rep switch
        {
            ReputationStatus.Positive => "▲ Friendly",
            ReputationStatus.Neutral  => "■ Neutral",
            ReputationStatus.Negative => "▼ Hostile",
            _ => "?"
        };

        private void RepaintRepColor(Text label, ReputationStatus rep, Color baseColor)
        {
            label.color = rep switch
            {
                ReputationStatus.Positive => Color.green,
                ReputationStatus.Neutral  => baseColor * 0.7f,
                ReputationStatus.Negative => Color.red,
                _ => baseColor
            };
        }
    }
}
