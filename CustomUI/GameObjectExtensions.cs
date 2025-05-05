using UnityEngine;

namespace Modules.CustomUI
{
    
    public static class GameObjectExtensions
    {
        /// <summary>
        /// Sets the active state of the GameObject and all its children.
        /// </summary>
        /// <param name="gameObject">The GameObject to set active.</param>
        /// <param name="isActive">The active state to set.</param>
        public static void SetActiveRecursively(this GameObject gameObject, bool isActive)
        {
            gameObject.SetActive(isActive);
            foreach (Transform child in gameObject.transform)
            {
                child.gameObject.SetActiveRecursively(isActive);
            }
        }
        
        /// <summary>
        /// Toggles the active state of the GameObject
        /// </summary>
        public static void ToggleActive(this GameObject gameObject)
        {
            gameObject.SetActive(!gameObject.activeSelf);
        }
    }
    
}