using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class MobileButtonHoldHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    MobileActionButtonsController controller;
    bool isPointerPressed;

    void Awake()
    {
        if (controller == null)
        {
            controller = MobileActionButtonsController.Instance;
        }
    }

    public void SetController(MobileActionButtonsController mobileActionButtonsController)
    {
        controller = mobileActionButtonsController;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (isPointerPressed)
        {
            return;
        }

        isPointerPressed = true;
        ResolveController()?.HandleInteractButtonDown();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!isPointerPressed)
        {
            return;
        }

        isPointerPressed = false;
        ResolveController()?.HandleInteractButtonUp();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isPointerPressed)
        {
            return;
        }

        isPointerPressed = false;
        ResolveController()?.HandleInteractButtonUp();
    }

    MobileActionButtonsController ResolveController()
    {
        if (controller == null)
        {
            controller = MobileActionButtonsController.Instance;
        }

        return controller;
    }
}
