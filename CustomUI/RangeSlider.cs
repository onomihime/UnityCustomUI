// Copyright (c) 2025 onomihime (github.com/onomihime)
// Originally from: github.com/onomihime/UnityCustomUI
// Licensed under the MIT License. See the LICENSE file in the repository root for full license text.
// This file may be used in commercial projects provided the above copyright notice and this permission notice appear in all copies.

using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Modules.CustomUI
{
    [RequireComponent(typeof(RectTransform))]
    public class RangeSlider : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        // --- Inspector References ---
        [Header("UI Elements")] [SerializeField]
        private RectTransform backgroundRect;

        [SerializeField] private RectTransform fillAreaRect;
        [SerializeField] private Image fillImage;
        [SerializeField] private RectTransform handleMinRect;
        [SerializeField] private Image handleMinImage;
        [SerializeField] private RectTransform handleMaxRect;
        [SerializeField] private Image handleMaxImage;

        [Header("Settings")] [SerializeField] private float minValue = 0f;
        [SerializeField] private float maxValue = 100f;
        [SerializeField] private bool wholeNumbers = false;

        [Tooltip("The minimum difference allowed between the min and max values.")] [SerializeField]
        private float minRange = 10f; // <-- Added minRange setting

        [Header("Current Values")] [SerializeField]
        private float currentMinValue = 10f;

        [SerializeField] private float currentMaxValue = 90f;

        // --- Events ---
        [System.Serializable]
        public class RangeSliderChangedEvent : UnityEvent<float, float>
        {
        }

        public RangeSliderChangedEvent onValueChanged = new RangeSliderChangedEvent();

        // --- Internal State ---
        private RectTransform parentRect;

        private enum DragTarget
        {
            None,
            MinHandle,
            MaxHandle,
            Fill
        }

        private DragTarget currentDragTarget = DragTarget.None;
        private float dragOffset;
        private float dragFillStartMinVal;
        private float dragFillStartMaxVal;
        private Vector2 dragFillStartPosition;

        // --- Public Properties ---
        public float MinValue
        {
            get => currentMinValue;
            set => SetMinValue(value, true);
        }

        public float MaxValue
        {
            get => currentMaxValue;
            set => SetMaxValue(value, true);
        }

        public float AbsoluteMinValue
        {
            get => minValue;
            set
            {
                minValue = value;
                ValidateValues();
                UpdateVisuals();
            }
        }

        public float AbsoluteMaxValue
        {
            get => maxValue;
            set
            {
                maxValue = value;
                ValidateValues();
                UpdateVisuals();
            }
        }

        // Property for minRange
        public float MinRange
        {
            get => minRange;
            set
            {
                minRange = Mathf.Max(0, value); // Ensure non-negative
                // Re-validate and update if the range changed
                ValidateValues();
                UpdateVisuals();
            }
        }


        void Awake()
        {
            parentRect = GetComponent<RectTransform>();
            ValidateValues(); // Initial validation now includes minRange check
            UpdateVisuals();
        }

        void OnEnable()
        {
            ValidateValues();
            UpdateVisuals();
        }

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (!parentRect) parentRect = GetComponent<RectTransform>();
            // Ensure minRange is non-negative in editor
            minRange = Mathf.Max(0, minRange);
            ValidateValues(); // Validate values including minRange check
            UpdateVisuals();
        }
#endif

        // --- Main Logic ---

        private void ValidateValues()
        {
            // 1. Clamp individual values to absolute min/max
            currentMinValue = Mathf.Clamp(currentMinValue, minValue, maxValue);
            currentMaxValue = Mathf.Clamp(currentMaxValue, minValue, maxValue);

            // 2. Ensure min <= max
            if (currentMinValue > currentMaxValue) currentMinValue = currentMaxValue;
            if (currentMaxValue < currentMinValue) currentMaxValue = currentMinValue; // Redundant but safe

            // 3. Enforce minimum range
            if (currentMaxValue - currentMinValue < minRange)
            {
                // Try adjusting max first
                currentMaxValue = currentMinValue + minRange;
                // If max hit the boundary, adjust min instead
                if (currentMaxValue > maxValue)
                {
                    currentMaxValue = maxValue;
                    currentMinValue = maxValue - minRange;
                    // Re-clamp min just in case minRange > (maxValue - minValue)
                    currentMinValue = Mathf.Clamp(currentMinValue, minValue, maxValue);
                }
            }

            // 4. Apply whole numbers if needed
            if (wholeNumbers)
            {
                currentMinValue = Mathf.Round(currentMinValue);
                currentMaxValue = Mathf.Round(currentMaxValue);
                // Re-check minRange after rounding, as rounding might violate it slightly
                if (currentMaxValue - currentMinValue < minRange)
                {
                    // Prioritize keeping min value, adjust max (could also do other strategies)
                    currentMaxValue = currentMinValue + minRange;
                    currentMaxValue = Mathf.Clamp(currentMaxValue, minValue, maxValue); // Clamp again
                    // If max is now clamped, min might need adjusting if minRange is large
                    if (currentMaxValue == maxValue)
                    {
                        currentMinValue = Mathf.Max(minValue, maxValue - minRange);
                        currentMinValue = Mathf.Round(currentMinValue); // Round again
                    }
                }
            }
        }


        private void UpdateVisuals()
        {
            if (!parentRect || !handleMinRect || !handleMaxRect || !fillAreaRect) return;

            float range = maxValue - minValue;
            if (range <= 0) range = 1f;

            float normalizedMin = (currentMinValue - minValue) / range;
            float normalizedMax = (currentMaxValue - minValue) / range;

            handleMinRect.anchorMin = new Vector2(normalizedMin, 0);
            handleMinRect.anchorMax = new Vector2(normalizedMin, 1);
            handleMinRect.anchoredPosition = Vector2.zero;

            handleMaxRect.anchorMin = new Vector2(normalizedMax, 0);
            handleMaxRect.anchorMax = new Vector2(normalizedMax, 1);
            handleMaxRect.anchoredPosition = Vector2.zero;

            fillAreaRect.anchorMin = new Vector2(normalizedMin, fillAreaRect.anchorMin.y);
            fillAreaRect.anchorMax = new Vector2(normalizedMax, fillAreaRect.anchorMax.y);
            fillAreaRect.offsetMin = new Vector2(0, fillAreaRect.offsetMin.y);
            fillAreaRect.offsetMax = new Vector2(0, fillAreaRect.offsetMax.y);
        }

        // --- Input Handling ---

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!MayDrag(eventData)) return;

            Vector2 localCursor;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, eventData.position,
                    eventData.pressEventCamera, out localCursor))
                return;

            // Determine what was clicked
            // Prioritize handles if they overlap
            if (RectTransformUtility.RectangleContainsScreenPoint(handleMinRect, eventData.position,
                    eventData.pressEventCamera))
            {
                currentDragTarget = DragTarget.MinHandle;
                float handleValue = GetValueFromPosition(localCursor);
                dragOffset = handleValue - currentMinValue;
                handleMinRect.SetAsLastSibling(); // Bring handle to front visually while dragging
            }
            else if (RectTransformUtility.RectangleContainsScreenPoint(handleMaxRect, eventData.position,
                         eventData.pressEventCamera))
            {
                currentDragTarget = DragTarget.MaxHandle;
                float handleValue = GetValueFromPosition(localCursor);
                dragOffset = handleValue - currentMaxValue;
                handleMaxRect.SetAsLastSibling(); // Bring handle to front
            }
            else if (RectTransformUtility.RectangleContainsScreenPoint(fillAreaRect, eventData.position,
                         eventData.pressEventCamera))
            {
                currentDragTarget = DragTarget.Fill;
                dragFillStartMinVal = currentMinValue;
                dragFillStartMaxVal = currentMaxValue;
                dragFillStartPosition = localCursor;
            }
            else
            {
                currentDragTarget = DragTarget.None;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!MayDrag(eventData) || currentDragTarget == DragTarget.None) return;

            Vector2 localCursor;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, eventData.position,
                    eventData.pressEventCamera, out localCursor))
                return;

            float newValue = GetValueFromPosition(localCursor);

            switch (currentDragTarget)
            {
                case DragTarget.MinHandle:
                    SetMinValue(newValue - dragOffset, true); // Offset ensures handle follows cursor correctly
                    break;
                case DragTarget.MaxHandle:
                    SetMaxValue(newValue - dragOffset, true);
                    break;
                case DragTarget.Fill:
                    float startValueAtDragPos = GetValueFromPosition(dragFillStartPosition);
                    float valueDelta = newValue - startValueAtDragPos;

                    float newMin = dragFillStartMinVal + valueDelta;
                    float newMax = dragFillStartMaxVal + valueDelta;
                    float currentRange = dragFillStartMaxVal - dragFillStartMinVal; // Preserve the range

                    // Clamp based on boundaries
                    if (newMin < minValue)
                    {
                        newMin = minValue;
                        newMax = minValue + currentRange;
                    }

                    if (newMax > maxValue)
                    {
                        newMax = maxValue;
                        newMin = maxValue - currentRange;
                    }

                    // Clamp again just to be safe (especially if range > total slider range)
                    newMin = Mathf.Clamp(newMin, minValue, maxValue - currentRange); // Ensure min allows for range
                    newMax = Mathf.Clamp(newMax, minValue + currentRange, maxValue); // Ensure max allows for range


                    SetMinMaxValues(newMin, newMax, true); // Use the method that sets both
                    break;
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            currentDragTarget = DragTarget.None;
        }

        // --- Value Calculation ---

        private float GetValueFromPosition(Vector2 localPosition)
        {
            // Calculate raw normalized position (0 to 1)
            // Ensure width is not zero to avoid division issues
            float parentWidth = parentRect.rect.width;
            float normalizedPos = (parentWidth > 0) ? Mathf.Clamp01(localPosition.x / parentWidth) : 0;

            // Lerp to value space
            float value = Mathf.Lerp(minValue, maxValue, normalizedPos);

            // Apply whole number snapping *here* before returning,
            // so dragging feels snappy if wholeNumbers is true.
            if (wholeNumbers)
            {
                value = Mathf.Round(value);
            }

            return value;
        }

        // --- Setters ---

        private void SetMinValue(float input, bool sendCallback)
        {
            // Clamp to absolute min/max and apply whole numbers *first*
            float newValue = Mathf.Clamp(input, minValue, maxValue);
            if (wholeNumbers) newValue = Mathf.Round(newValue);

            // Apply the minRange constraint: Cannot go higher than max value minus the min range
            newValue = Mathf.Clamp(newValue, minValue, currentMaxValue - minRange); // <-- Key change

            // Only update and invoke if the value actually changed
            if (Mathf.Abs(currentMinValue - newValue) > float.Epsilon) // Use Epsilon for float comparison
            {
                currentMinValue = newValue;
                UpdateVisuals();
                if (sendCallback)
                    onValueChanged.Invoke(currentMinValue, currentMaxValue);
            }
        }

        private void SetMaxValue(float input, bool sendCallback)
        {
            // Clamp to absolute min/max and apply whole numbers *first*
            float newValue = Mathf.Clamp(input, minValue, maxValue);
            if (wholeNumbers) newValue = Mathf.Round(newValue);

            // Apply the minRange constraint: Cannot go lower than min value plus the min range
            newValue = Mathf.Clamp(newValue, currentMinValue + minRange, maxValue); // <-- Key change

            // Only update and invoke if the value actually changed
            if (Mathf.Abs(currentMaxValue - newValue) > float.Epsilon) // Use Epsilon for float comparison
            {
                currentMaxValue = newValue;
                UpdateVisuals();
                if (sendCallback)
                    onValueChanged.Invoke(currentMinValue, currentMaxValue);
            }
        }

        // Sets both values simultaneously, includes validation and minRange enforcement
        public void SetMinMaxValues(float newMin, float newMax, bool sendCallback)
        {
            // Store original values to check for change
            float previousMin = currentMinValue;
            float previousMax = currentMaxValue;

            // Set internal fields directly first (don't use properties here to avoid recursive calls)
            currentMinValue = newMin;
            currentMaxValue = newMax;

            // Perform all validation steps, including minRange enforcement
            ValidateValues();

            // Check if either value actually changed after validation
            bool minChanged = Mathf.Abs(currentMinValue - previousMin) > float.Epsilon;
            bool maxChanged = Mathf.Abs(currentMaxValue - previousMax) > float.Epsilon;

            if (minChanged || maxChanged)
            {
                UpdateVisuals();
                if (sendCallback)
                    onValueChanged.Invoke(currentMinValue, currentMaxValue);
            }
        }


        private bool MayDrag(PointerEventData eventData)
        {
            return gameObject.activeSelf && enabled && eventData.button == PointerEventData.InputButton.Left;
        }

        // --- Public API for Customization ---
        public void SetFillColor(Color color)
        {
            if (fillImage) fillImage.color = color;
        }

        public void SetFillSprite(Sprite sprite)
        {
            if (fillImage) fillImage.sprite = sprite;
        }

        public void SetMinHandleColor(Color color)
        {
            if (handleMinImage) handleMinImage.color = color;
        }

        public void SetMinHandleSprite(Sprite sprite)
        {
            if (handleMinImage) handleMinImage.sprite = sprite;
        }
        // ... Add methods for Max Handle, Background etc.
    }
}