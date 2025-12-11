using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace Our_Assets.Scripts
{
    /// <summary>
    /// Показывает информационный объект (например, Canvas с текстом) при наведении на интерактивный объект.
    /// </summary>
    [RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable))]
    public class HoverInfoDisplay : MonoBehaviour
    {
        [Tooltip("Объект, который нужно показывать при наведении (например, Canvas).")]
        [SerializeField] private GameObject infoObject;
        
        [Tooltip("Поворачивать ли объект лицом к игроку?")]
        [SerializeField] private bool lookAtPlayer = true;

        private UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable _interactable;
        private Transform _cameraTransform;

        private void Awake()
        {
            _interactable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>();
            
            if (infoObject != null)
                infoObject.SetActive(false);
                
            if (Camera.main != null)
                _cameraTransform = Camera.main.transform;
        }

        private void OnEnable()
        {
            if (_interactable != null)
            {
                _interactable.hoverEntered.AddListener(OnHoverEnter);
                _interactable.hoverExited.AddListener(OnHoverExit);
            }
        }

        private void OnDisable()
        {
            if (_interactable != null)
            {
                _interactable.hoverEntered.RemoveListener(OnHoverEnter);
                _interactable.hoverExited.RemoveListener(OnHoverExit);
            }
        }

        private void Update()
        {
            // Если камера потерялась (например, при смене сцен), ищем её снова
            if (_cameraTransform == null && Camera.main != null)
            {
                _cameraTransform = Camera.main.transform;
            }

            if (lookAtPlayer && infoObject != null && infoObject.activeSelf && _cameraTransform != null)
            {
                // Поворачиваем объект к камере
                infoObject.transform.LookAt(_cameraTransform);
                // Разворачиваем на 180, так как UI обычно смотрит "назад" при LookAt
                infoObject.transform.Rotate(0, 180, 0); 
            }
        }

        private void OnHoverEnter(HoverEnterEventArgs args)
        {
            if (infoObject != null)
                infoObject.SetActive(true);
        }

        private void OnHoverExit(HoverExitEventArgs args)
        {
            if (infoObject != null)
                infoObject.SetActive(false);
        }
    }
}
