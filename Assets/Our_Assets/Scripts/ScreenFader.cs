using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Our_Assets.Scripts
{
    /// <summary>
    /// Управляет затемнением экрана (Fade In/Out).
    /// Должен быть в сцене (желательно один). Не уничтожается при загрузке сцен.
    /// Адаптировано для VR (использует ScreenSpaceCamera).
    /// </summary>
    public class ScreenFader : MonoBehaviour
    {
        public static ScreenFader Instance { get; private set; }

        [Tooltip("Длительность затемнения в секундах.")]
        public float fadeDuration = 1.0f;
        
        [Tooltip("Цвет затемнения.")]
        public Color fadeColor = Color.black;

        [Tooltip("Насколько увеличить затемнение за пределы экрана (для VR). 0 = ровно по экрану, 0.5 = на 50% больше.")]
        public float overscan = 0.5f;

        [Tooltip("Расстояние от камеры до затемнения.")]
        public float planeDistance = 0.1f;

        private CanvasGroup _canvasGroup;
        private Canvas _canvas;
        private Image _image;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            SetupFadeUI();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            AssignCamera();
            // Гарантируем, что экран черный перед началом осветления
            if (_canvasGroup != null) _canvasGroup.alpha = 1f;
            StartCoroutine(FadeIn());
        }

        private void Update()
        {
            // Постоянно проверяем камеру, так как в VR она может теряться или пересоздаваться
            if (_canvas != null && _canvas.worldCamera == null)
            {
                AssignCamera();
            }
        }

        private void AssignCamera()
        {
            if (_canvas != null && _canvas.worldCamera == null)
            {
                _canvas.worldCamera = Camera.main;
                if (_canvas.worldCamera != null)
                {
                    _canvas.planeDistance = planeDistance;
                }
            }
        }

        private void SetupFadeUI()
        {
            if (_canvasGroup != null) return;

            // Создаем Canvas для фейда программно
            GameObject canvasObj = new GameObject("ScreenFaderCanvas");
            canvasObj.transform.SetParent(transform);
            
            _canvas = canvasObj.AddComponent<Canvas>();
            // Для VR лучше использовать ScreenSpaceCamera
            _canvas.renderMode = RenderMode.ScreenSpaceCamera; 
            _canvas.sortingOrder = 32767; // Поверх всего
            
            // Пытаемся сразу найти камеру
            AssignCamera();

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            _canvasGroup = canvasObj.AddComponent<CanvasGroup>();
            _canvasGroup.blocksRaycasts = false; 
            _canvasGroup.alpha = 0f; 

            GameObject imageObj = new GameObject("FadeImage");
            imageObj.transform.SetParent(canvasObj.transform, false);
            
            _image = imageObj.AddComponent<Image>();
            _image.color = fadeColor;
            _image.raycastTarget = false;
            
            RectTransform rt = _image.rectTransform;
            // Делаем изображение больше экрана, чтобы покрыть периферийное зрение в VR
            rt.anchorMin = new Vector2(-overscan, -overscan);
            rt.anchorMax = new Vector2(1f + overscan, 1f + overscan);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
        }

        public IEnumerator FadeOut()
        {
            // Убедимся, что камера назначена перед фейдом (на случай если она потерялась)
            AssignCamera();

            if (_canvasGroup == null) yield break;
            
            // Обновляем цвет на случай изменения в инспекторе
            if (_image != null) _image.color = fadeColor;

            float timer = 0f;
            float startAlpha = _canvasGroup.alpha;
            
            while (timer < fadeDuration)
            {
                timer += Time.deltaTime;
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, 1f, timer / fadeDuration);
                yield return null;
            }
            _canvasGroup.alpha = 1f;
        }

        public IEnumerator FadeIn()
        {
            // Небольшая задержка, чтобы сцена успела инициализироваться и VR-шлем стабилизировался
            yield return new WaitForSeconds(0.5f);

            AssignCamera();

            if (_canvasGroup == null) yield break;

            if (_image != null) _image.color = fadeColor;

            float timer = 0f;
            float startAlpha = _canvasGroup.alpha;

            while (timer < fadeDuration)
            {
                timer += Time.deltaTime;
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, timer / fadeDuration);
                yield return null;
            }
            _canvasGroup.alpha = 0f;
        }
    }
}
