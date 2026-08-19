using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Core;

namespace TacticalSoccer.UI
{
    /// <summary>
    /// Ties one Text to one localisation key: writes it on the way in, and
    /// rewrites it whenever the language changes underneath it.
    ///
    /// This is what makes the change "hot". Without it, switching language would
    /// only take effect on screens built after the switch — the options panel
    /// itself, being on screen at that exact moment, would be the one screen
    /// still in the old language.
    ///
    /// Subscribing in OnEnable rather than in Start is the point: every menu in
    /// this game is a panel that gets deactivated, and a component on a
    /// deactivated GameObject receives nothing at all. A hidden panel therefore
    /// misses the event — and does not need it, because OnEnable writes the
    /// current text the next time it is shown.
    /// </summary>
    [RequireComponent(typeof(Text))]
    public class LocalizedText : MonoBehaviour
    {
        [Tooltip("Key into the language files. The Spanish file is the reference: " +
                 "every key exists there, and a key missing from a translation " +
                 "falls back to showing the key itself.")]
        public string key;

        private Text target;

        private void Awake()
        {
            target = GetComponent<Text>();
        }

        private void OnEnable()
        {
            LocalizationManager.OnLanguageChanged += Refresh;

            Refresh();
        }

        private void OnDisable()
        {
            LocalizationManager.OnLanguageChanged -= Refresh;
        }

        /// <summary>
        /// Points this label at a different key and redraws it. For captions that
        /// change with the state of the game rather than with the language — the
        /// full-time button that reads PLAY AGAIN after a friendly and NEXT ROUND
        /// after a won tournament round.
        /// </summary>
        public void SetKey(string newKey)
        {
            key = newKey;

            Refresh();
        }

        public void Refresh()
        {
            if (target == null)
            {
                // Refresh can be reached from Write below before Awake has run,
                // on a component added to a panel that has never been enabled.
                target = GetComponent<Text>();
            }

            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            LocalizationManager.Write(target, key);
        }

        /// <summary>
        /// Writes a key onto a Text whether or not it has one of these, and
        /// leaves it able to follow the language afterwards if it does.
        ///
        /// The fallback branch is not defensive padding: it is what keeps a
        /// controller working against a scene that has not been regenerated
        /// since these components were introduced. The text is right either way;
        /// only the hot swap needs the component.
        /// </summary>
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
