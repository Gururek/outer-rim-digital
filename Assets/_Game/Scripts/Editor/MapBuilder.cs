// MapBuilder.cs — Editor tool to build the 10-node test map with faction-colored spheres.
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace OuterRim
{
    public static class MapBuilder
    {
        public static void BuildNodes(Transform parent)
        {
            var nodes = new (string name, MapNodeType type, FactionType faction, int[] connections, float x, float z)[]
            {
                ("Tatooine",   MapNodeType.Planet,   FactionType.Hutts,     new[]{1, 2},    -8f,  0f),
                ("Mos Espa",   MapNodeType.NavPoint,  FactionType.Hutts,     new[]{0, 3},    -5f,  3f),
                ("Coruscant",  MapNodeType.Planet,    FactionType.Authority, new[]{0, 4, 5}, -3f, -4f),
                ("Nal Hutta",  MapNodeType.Planet,    FactionType.Hutts,     new[]{1, 6},     0f,  5f),
                ("Corellia",   MapNodeType.Planet,    FactionType.Syndicate, new[]{2, 7},     4f, -5f),
                ("Kuat",       MapNodeType.NavPoint,  FactionType.Authority, new[]{2, 8},    -1f, -8f),
                ("Ryloth",     MapNodeType.NavPoint,  FactionType.Syndicate, new[]{3, 9},     6f,  3f),
                ("Ord Mantell",MapNodeType.Planet,    FactionType.Syndicate, new[]{4, 9},     8f, -2f),
                ("Mandalore",  MapNodeType.Planet,    FactionType.Rebels,    new[]{5, 9},     3f, -10f),
                ("Dantooine",  MapNodeType.NavPoint,  FactionType.Rebels,    new[]{6, 7, 8}, 11f, 0f),
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

                // ─── Visual sphere ──────────────────────────────────────
                var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                visual.name = "Visual";
                visual.transform.SetParent(nodeGo.transform);
                visual.transform.localPosition = Vector3.zero;

                // Keep collider as trigger for 3D click detection (OnMouseDown)
                var collider = visual.GetComponent<Collider>();
                if (collider != null) collider.isTrigger = true;

                float scale = type == MapNodeType.Planet ? 0.8f : 0.3f;
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

                // ─── Click handler ───────────────────────────────────────
                var clickHandler = nodeGo.AddComponent<NodeClickHandler>();
                clickHandler.NodeId = i;

                // ─── Label (TextMesh) ────────────────────────────────────
                var label = new GameObject("Label");
                label.transform.SetParent(nodeGo.transform);
                label.transform.localPosition = new Vector3(0, scale + 0.2f, 0);
                var textMesh = label.AddComponent<TextMesh>();
                textMesh.text = $"{name}\n({connections.Length} links)";
                textMesh.fontSize = 14;
                textMesh.anchor = TextAnchor.MiddleCenter;
                textMesh.color = Color.white;
            }
        }
    }
}
#endif
