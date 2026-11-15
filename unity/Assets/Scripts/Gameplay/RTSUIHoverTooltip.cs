using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CSE377.Gameplay
{
    /// <summary>
    /// UI ögelerine fareyle üzerine gelindiğinde (Hover) RTS HUD Controller üzerinden
    /// detaylı alt metin (tooltip) gösterilmesini sağlar.
    /// </summary>
    public class RTSUIHoverTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public string title;
        [TextArea(2, 6)]
        public string description;

        public Func<string> dynamicTitleGetter;
        public Func<string> dynamicDescGetter;

        public void SetTooltip(string t, string d)
        {
            title = t;
            description = d;
        }

        public void SetDynamicTooltip(Func<string> tGetter, Func<string> dGetter)
        {
            dynamicTitleGetter = tGetter;
            dynamicDescGetter = dGetter;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            string t = dynamicTitleGetter != null ? dynamicTitleGetter() : title;
            string d = dynamicDescGetter != null ? dynamicDescGetter() : description;
            if (RTSHUDController.Instance != null)
            {
                RTSHUDController.Instance.ShowTooltip(t, d, transform as RectTransform, dynamicTitleGetter, dynamicDescGetter);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (RTSHUDController.Instance != null)
            {
                RTSHUDController.Instance.HideTooltip();
            }
        }

        private void OnDisable()
        {
            if (RTSHUDController.Instance != null)
            {
                RTSHUDController.Instance.HideTooltip();
            }
        }
    }
}
