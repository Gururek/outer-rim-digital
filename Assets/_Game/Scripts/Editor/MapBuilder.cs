// MapBuilder.cs — Builds the full Outer Rim map from TTS mod data
#if UNITY_EDITOR
using UnityEngine;
using System.Collections.Generic;

namespace OuterRim
{
    public static class MapBuilder
    {
        public static void BuildNodes(Transform parent)
        {
            var nodes = new (string name, MapNodeType type, FactionType faction, int[] connections, float x, float z)[]
            {
                ("Tatooine",       MapNodeType.Planet, FactionType.Hutts,     new[]{1,8,5},     -2.3f, -7.5f),
                ("Cantonica",      MapNodeType.Planet, FactionType.Syndicate, new[]{0,3,7},      3.0f, -4.0f),
                ("Nal Hutta",      MapNodeType.Planet, FactionType.Hutts,     new[]{0,3,8},      3.7f,  3.2f),
                ("Lothal",         MapNodeType.Planet, FactionType.Rebels,    new[]{2,1,7,4},   -3.5f,  3.6f),
                ("Mon Calamari",   MapNodeType.Planet, FactionType.Rebels,    new[]{7,3,9},      5.6f, -5.3f),
                ("Ryloth",         MapNodeType.Planet, FactionType.Hutts,     new[]{0,2,6},     -1.4f, -8.7f),
                ("Naboo",          MapNodeType.Planet, FactionType.Authority, new[]{5,10},        3.4f,  4.0f),
                ("Ord Mantell",    MapNodeType.Planet, FactionType.Syndicate, new[]{1,3,4},     -3.8f,  5.2f),
                ("Takodama",       MapNodeType.Planet, FactionType.Authority, new[]{6,11},       6.9f, -0.4f),
                ("Kessel",         MapNodeType.Planet, FactionType.Syndicate, new[]{4},          8.5f, -3.1f),
                ("The Ring",       MapNodeType.Planet, FactionType.Authority, new[]{8},         -6.2f, -7.1f),
            };

            for (int i = 0; i < nodes.Length; i++)
            {
                var (name, type, faction, connections, x, z) = nodes[i];

                var nodeGo = new GameObject($"Node_{i}_{name}");
                nodeGo.transform.SetParent(parent);
                nodeGo.transform.position = new Vector3(x, 0, z);

                var mn = nodeGo.AddComponent<MapNode>();
                mn.NodeId = i;
                mn.NodeName = name;
                mn.Type = type;
                mn.PlanetFactionType = faction;
                mn.ConnectedNodeIds = new List<int>(connections);

                // Visual sphere
                var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                visual.name = "Visual";
                visual.transform.SetParent(nodeGo.transform);
                visual.transform.localPosition = Vector3.zero;

                var collider = visual.GetComponent<Collider>();
                if (collider != null) collider.isTrigger = true;

                float scale = 0.6f;
                visual.transform.localScale = new Vector3(scale, scale, scale);

                var renderer = visual.GetComponent<Renderer>();
                var mat = new Material(renderer.sharedMaterial);
                mat.color = faction switch
                {
                    FactionType.Syndicate => new Color(1f, 0.5f, 0f),
                    FactionType.Authority => new Color(0.3f, 0.3f, 1f),
                    FactionType.Rebels    => new Color(1f, 0.2f, 0.2f),
                    FactionType.Hutts     => new Color(0.8f, 0.6f, 0f),
                    _                     => Color.gray
                };
                renderer.sharedMaterial = mat;

                // Click handler
                var clickHandler = nodeGo.AddComponent<NodeClickHandler>();
                clickHandler.NodeId = i;

                // Label
                var label = new GameObject("Label");
                label.transform.SetParent(nodeGo.transform);
                label.transform.localPosition = new Vector3(0, scale + 0.3f, 0);
                var textMesh = label.AddComponent<TextMesh>();
                textMesh.text = $"{i}: {name}";
                textMesh.fontSize = 12;
                textMesh.anchor = TextAnchor.MiddleCenter;
                textMesh.color = Color.white;
            }

            Debug.Log($"[MapBuilder] Built {nodes.Length} planet nodes.");
        }
    }
}
#endif
