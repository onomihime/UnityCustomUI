

using UnityEngine;

namespace Modules.CustomUI
{
    public class PanelToggler : MonoBehaviour
    {
        public void Toggle()
        {
            gameObject.ToggleActive();
        }

        public void Toggle(GameObject target)
        {
            target.ToggleActive();
        }
    }
}