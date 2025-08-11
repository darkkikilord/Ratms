using UnityEngine;
using UnityEngine.EventSystems;

namespace Ratms.UI
{
    public class UISelectableSfx : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
    {
        [SerializeField] private AudioSource sfxSource; // Output = SFX (en tu AudioMixer)
        [SerializeField] private AudioClip hoverClip;
        [SerializeField] private AudioClip clickClip;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (hoverClip && sfxSource) sfxSource.PlayOneShot(hoverClip);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (clickClip && sfxSource) sfxSource.PlayOneShot(clickClip);
        }
    }
}
