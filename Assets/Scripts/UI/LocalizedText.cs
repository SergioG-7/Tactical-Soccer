using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Core;

namespace TacticalSoccer.UI
{
    // Enlaza un Text con una clave de idioma y lo actualiza cuando cambia el idioma.
    [RequireComponent(typeof(Text))]
    public class LocalizedText : MonoBehaviour
    {
        [Tooltip("Key into the language files. The Spanish file is the reference: " +
                 "every key exists there, and a key missing from a translation " +
                 "falls back to showing the key itself.")]
        public string key;

        private Text target;

        // Obtiene el componente Text.
        private void Awake()
        {
            target = GetComponent<Text>();
        }

        // Se suscribe al cambio de idioma y refresca el texto al activarse.
        private void OnEnable()
        {
            LocalizationManager.OnLanguageChanged += Refresh;

            Refresh();
        }

        // Se desuscribe del cambio de idioma al desactivarse.
        private void OnDisable()
        {
            LocalizationManager.OnLanguageChanged -= Refresh;
        }

        // Cambia la clave de este texto y lo redibuja.
        public void SetKey(string newKey)
        {
            key = newKey;

            Refresh();
        }

        // Vuelve a escribir el texto según la clave y el idioma actual.
        public void Refresh()
        {
            if (target == null)
            {
                target = GetComponent<Text>();
            }

            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            LocalizationManager.Write(target, key);
        }

        // Escribe una clave en un Text, tenga o no el componente LocalizedText.
        public static void Write(Text text, string key)
        {
            if (text == null)
            {
                return;
            }

            if (text.TryGetComponent(out LocalizedText localized))
            {
                localized.SetKey(key);
                return;
            }

            LocalizationManager.Write(text, key);
        }
    }
}
