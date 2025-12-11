using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;


namespace Our_Assets.Scripts
{
    /// <summary>
    /// Загружает новую сцену при телепортации на этот объект с эффектом затемнения.
    /// Требует наличия ScreenFader в сцене (или создаст его, если не найдет, но лучше добавить заранее).
    /// </summary>
    [RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.BaseTeleportationInteractable))]
    public class SceneTeleporter : MonoBehaviour
    {
        [Tooltip("Имя сцены, которую нужно загрузить")]
        [SerializeField] private string targetSceneName;

        private UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.BaseTeleportationInteractable _teleportInteractable;

        private void Awake()
        {
            _teleportInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.BaseTeleportationInteractable>();
        }

        private void OnEnable()
        {
            if (_teleportInteractable != null)
            {
                _teleportInteractable.teleporting.AddListener(OnTeleporting);
            }
        }

        private void OnDisable()
        {
            if (_teleportInteractable != null)
            {
                _teleportInteractable.teleporting.RemoveListener(OnTeleporting);
            }
        }

        private void OnTeleporting(UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportingEventArgs args)
        {
            if (!string.IsNullOrEmpty(targetSceneName))
            {
                StartCoroutine(LoadSceneRoutine());
            }
            else
            {
                Debug.LogWarning($"[SceneTeleporter] Не указано имя сцены на объекте {gameObject.name}!");
            }
        }

        private IEnumerator LoadSceneRoutine()
        {
            // Пытаемся найти ScreenFader, если статической ссылки нет
            if (ScreenFader.Instance == null)
            {
                var fader = FindFirstObjectByType<ScreenFader>();
                if (fader == null)
                {
                    // Если совсем нет, создаем временный (хотя он должен быть DontDestroyOnLoad)
                    GameObject go = new GameObject("ScreenFader_AutoCreated");
                    go.AddComponent<ScreenFader>();
                    // Ждем кадр инициализации
                    yield return null;
                }
            }

            if (ScreenFader.Instance != null)
            {
                yield return ScreenFader.Instance.FadeOut();
            }
            else
            {
                // Если что-то пошло не так, просто небольшая задержка
                yield return new WaitForSeconds(0.5f);
            }

            SceneManager.LoadScene(targetSceneName);
        }
    }
}
