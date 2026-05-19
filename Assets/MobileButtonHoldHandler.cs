using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class MobileButtonHoldHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    MobileActionButtonsController controller;

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
        ResolveController()?.HandleInteractButtonDown();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        ResolveController()?.HandleInteractButtonUp();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
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
