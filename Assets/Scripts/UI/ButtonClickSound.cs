using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TacticalSoccer.UI
{
    // Reproduce el sonido de click al pulsar el botón al que está enganchado.
    [RequireComponent(typeof(Button))]
    public class ButtonClickSound : MonoBehaviour, IPointerClickHandler
    {
        public void OnPointerClick(PointerEventData eventData)
        {
            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.PlayClick();
            }
        }
    }
}
