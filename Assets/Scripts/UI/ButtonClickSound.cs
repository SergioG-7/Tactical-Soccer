using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TacticalSoccer.UI
{
    /// <summary>
    /// Plays the menu click sound whenever its Button is clicked.
    ///
    /// A component rather than a listener added straight to the Button at scene-
    /// generation time: the generator runs in the Editor, outside Play Mode, and
    /// `Button.onClick.AddListener(someMethod)` there only ever adds a plain
    /// delegate — never serialized into the saved scene, same reason every other
    /// controller in this project wires its own buttons from Start() and not
    /// from the generator. A COMPONENT is a real serialized object, so this is
    /// the one piece of the click sound the generator can attach directly and
    /// have it actually survive into the saved scene.
    ///
    /// Listens through IPointerClickHandler rather than Button.onClick, and
    /// deliberately so: nearly every controller in this project wires its
    /// button's REAL behaviour with `onClick.RemoveAllListeners()` followed by
    /// `AddListener(...)` in its own Start() — a pattern that exists so a second
    /// Play session's listeners cannot double up on the first's, not to make
    /// room for this component. Unity gives no ordering guarantee between two
    /// components' Start() on the same object, so a listener this component put
    /// on `onClick` could be wiped the instant a later Start() ran. The pointer
    /// handler is a completely separate delivery path — the EventSystem calls
    /// every IPointerClickHandler on the clicked object directly — so it cannot
    /// be cleared by anything wiring the Button's own click event.
    ///
    /// Not used on anything that fires DURING live play (duel actions, penalty
    /// direction) — only on menu-style buttons (title, options, developer menu,
    /// formation, substitutions, player edit...). The generator opts each button
    /// in individually rather than this component going in blind.
    /// </summary>
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
