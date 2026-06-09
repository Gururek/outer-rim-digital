// ContactTokenVisual.cs — V2: 3D visual marker for contact tokens on planets
// Placed by EncounterResolver during game init, removed when token is collected.
using UnityEngine;

namespace OuterRim
{
    public class ContactTokenVisual : MonoBehaviour
    {
        [Header("Visual Settings")]
        [SerializeField] private float pulseSpeed = 2f;
        [SerializeField] private float pulseHeight = 0.3f;
        [SerializeField] private float hoverOffset = 1.5f;

        private ContactClass contactClass;
        private int databankCardNumber;
        private GameObject visualSphere;
        private Material sphereMaterial;
        private Vector3 basePosition;
        private float pulsePhase;

        public ContactClass TokenClass => contactClass;
        public int DatabankCardNumber => databankCardNumber;
        public bool IsRevealed { get; private set; }

        /// <summary>Initialize the token on a planet node.</summary>
        public void Initialize(int cardNumber, ContactClass tokenClass, Transform planetTransform)
        {
            databankCardNumber = cardNumber;
            contactClass = tokenClass;
            IsRevealed = false;

            // Position above the planet
            transform.SetParent(planetTransform);
            transform.localPosition = Vector3.up * hoverOffset;

            // Create visual sphere
            visualSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visualSphere.name = $"Token_{cardNumber}";
            visualSphere.transform.SetParent(transform);
            visualSphere.transform.localPosition = Vector3.zero;
            visualSphere.transform.localScale = Vector3.one * 0.5f;

            // Remove collider (visual only)
            var collider = visualSphere.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            // Color based on contact class
            var renderer = visualSphere.GetComponent<Renderer>();
            sphereMaterial = new Material(renderer.sharedMaterial);
            sphereMaterial.color = GetTokenColor(contactClass);
            renderer.sharedMaterial = sphereMaterial;

            // Make it emissive-ish (bright)
            sphereMaterial.EnableKeyword("_EMISSION");
            sphereMaterial.SetColor("_EmissionColor", sphereMaterial.color * 0.5f);

            basePosition = transform.localPosition;
            pulsePhase = Random.Range(0f, Mathf.PI * 2f); // Randomize start phase

            Debug.Log($"[ContactToken] Placed {contactClass} token (card #{cardNumber}) on {planetTransform.name}");
        }

        /// <summary>Reveal the token — show what it is. Called when player lands here.</summary>
        public void Reveal()
        {
            if (IsRevealed) return;
            IsRevealed = true;

            // Flash bright, then fade
            if (sphereMaterial != null)
            {
                sphereMaterial.color = Color.white;
                sphereMaterial.SetColor("_EmissionColor", Color.white);
            }
        }

        /// <summary>Remove the token visual entirely.</summary>
        public void Remove()
        {
            Debug.Log($"[ContactToken] Removing token (card #{databankCardNumber})");
            Destroy(gameObject);
        }

        private void Update()
        {
            // Pulsing animation
            if (visualSphere != null && !IsRevealed)
            {
                pulsePhase += Time.deltaTime * pulseSpeed;
                float offset = Mathf.Sin(pulsePhase) * pulseHeight;
                transform.localPosition = basePosition + Vector3.up * offset;

                // Subtle rotation
                visualSphere.transform.Rotate(Vector3.up, 30f * Time.deltaTime);
            }
        }

        private Color GetTokenColor(ContactClass cc) => cc switch
        {
            ContactClass.White  => new Color(0.9f, 0.9f, 0.9f),
            ContactClass.Green  => new Color(0.2f, 0.8f, 0.3f),
            ContactClass.Yellow => new Color(1f, 0.85f, 0.2f),
            ContactClass.Orange => new Color(1f, 0.4f, 0.1f),
            _ => Color.gray
        };
    }
}
