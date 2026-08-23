using UnityEngine;

namespace TacticalSoccer.Visuals
{
    // Hace botar a un espectador en su asiento con una onda sinusoidal.
    public class SpectatorAnimator : MonoBehaviour
    {
        private float bounceSpeed;
        private float bounceHeight;
        private float timeOffset;
        private Vector3 startPos;

        // Genera valores aleatorios de velocidad, altura y desfase para que cada espectador bote a su ritmo.
        private void Awake()
        {
            startPos = transform.localPosition;

            bounceSpeed = Random.Range(5f, 10f);
            bounceHeight = Random.Range(0.2f, 0.5f);
            timeOffset = Random.Range(0f, 100f);
        }

        // Mueve al espectador hacia arriba y abajo siguiendo una onda sinusoidal.
        private void Update()
        {
            float yOffset = Mathf.Max(0f, Mathf.Sin((Time.time * bounceSpeed) + timeOffset) * bounceHeight);

            transform.localPosition = startPos + new Vector3(0f, yOffset, 0f);
        }
    }
}
