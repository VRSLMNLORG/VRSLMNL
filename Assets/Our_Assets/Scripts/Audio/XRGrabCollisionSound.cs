using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Звук столкновений специально для объектов с XRGrabInteractable.
///
/// Идея:
/// - Вешаем на тот же объект, где стоит XRGrabInteractable (и Rigidbody).
/// - При столкновении (OnCollisionEnter) считаем силу удара по relativeVelocity.
/// - Проигрываем звук только если удар достаточно сильный.
///
/// Как использовать:
/// 1. Добавьте этот скрипт на префаб/объект с XRGrabInteractable.
/// 2. Убедитесь, что есть Rigidbody и Collider (НЕ триггер).
/// 3. В инспекторе задайте collisionClip и отрегулируйте пороги скорости.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(XRGrabInteractable))]
[RequireComponent(typeof(AudioSource))]
public class XRGrabCollisionSound : MonoBehaviour
{
    [Header("Звук захвата")]
    [Tooltip("Звук, который будет воспроизводиться при захвате объекта (когда игрок берет его в руку).")]
    [SerializeField] private AudioClip grabSound;

    [Range(0f, 1f)]
    [Tooltip("Громкость звука захвата.")]
    [SerializeField] private float grabSoundVolume = 0.6f;

    [Tooltip("Использовать размер объекта для изменения pitch звука захвата.")]
    [SerializeField] private bool useSizeBasedGrabPitch = true;

    [Tooltip("Звук захвата не зависит от расстояния (2D звук). Если выключено, используется 3D звук как для столкновений.")]
    [SerializeField] private bool grabSoundIs2D = true;

    [Header("Основной звук")]
    [Tooltip("Звук, который будет воспроизводиться при ударе.")]
    [SerializeField] private AudioClip collisionClip;

    [Tooltip("Минимальная скорость столкновения, при которой начинается звук.")]
    [SerializeField] private float minSpeedForSound = 0.5f;

    [Tooltip("Скорость, при которой громкость достигает максимума.")]
    [SerializeField] private float maxSpeedForMaxVolume = 5f;

    [Range(0f, 1f)]
    [Tooltip("Минимальная громкость звука.")]
    [SerializeField] private float minVolume = 0.2f;

    [Range(0f, 1f)]
    [Tooltip("Максимальная громкость звука.")]
    [SerializeField] private float maxVolume = 1f;

    [Header("Влияние размера на громкость")]
    [Tooltip("Увеличивать громкость для больших объектов (больше объект = громче звук).")]
    [SerializeField] private bool useSizeBasedVolume = true;

    [Range(0.5f, 2f)]
    [Tooltip("Множитель громкости для маленьких объектов (minObjectSize).")]
    [SerializeField] private float minSizeVolumeMultiplier = 0.7f;

    [Range(0.5f, 2f)]
    [Tooltip("Множитель громкости для больших объектов (maxObjectSize).")]
    [SerializeField] private float maxSizeVolumeMultiplier = 1.5f;

    [Header("Когда играть звук")]
    [Tooltip("Играть звук, когда объект держат в руках.")]
    [SerializeField] private bool playWhileHeld = true;

    [Tooltip("Играть звук, когда объект уже отпущен и летит/падает.")]
    [SerializeField] private bool playWhenReleased = true;

    [Header("Высота тона (Pitch)")]
    [Tooltip("Использовать размер объекта для изменения pitch (больше объект = ниже звук).")]
    [SerializeField] private bool useSizeBasedPitch = true;

    [Tooltip("Вычислять размер динамически при каждом столкновении (если объект меняет размер во время выполнения).")]
    [SerializeField] private bool dynamicSizeUpdate = true;

    [Tooltip("Минимальный размер объекта (для расчета pitch).")]
    [SerializeField] private float minObjectSize = 0.1f;

    [Tooltip("Максимальный размер объекта (для расчета pitch).")]
    [SerializeField] private float maxObjectSize = 2f;

    [Tooltip("Минимальный pitch (для больших объектов).")]
    [Range(0.1f, 2f)]
    [SerializeField] private float minPitch = 0.7f;

    [Tooltip("Максимальный pitch (для маленьких объектов).")]
    [Range(0.1f, 2f)]
    [SerializeField] private float maxPitch = 1.3f;

    [Tooltip("Добавлять случайное изменение pitch поверх размера (для разнообразия).")]
    [SerializeField] private bool addRandomJitter = true;

    [Range(0f, 0.3f)]
    [Tooltip("Амплитуда случайного отклонения питча (+/- от размера).")]
    [SerializeField] private float pitchJitter = 0.05f;

    [Tooltip("Минимальное время между звуками (сек), чтобы не было спама при дрожании.")]
    [SerializeField] private float cooldown = 0.05f;

    [Tooltip("Задержка перед воспроизведением звуков столкновений после захвата объекта (сек). Предотвращает перекрытие звука захвата.")]
    [SerializeField] private float collisionDelayAfterGrab = 0.3f;

    [Tooltip("Слои, столкновения с которыми учитываются (по умолчанию все).")]
    [SerializeField] private LayerMask hitLayers = ~0;

    [Header("Предотвращение дублирования звуков")]
    [Tooltip("Если у другого объекта тоже есть XRGrabCollisionSound, играть звук только одному из них.")]
    [SerializeField] private bool preventDuplicateSounds = true;

    [Tooltip("Критерий выбора: кто играет звук при столкновении двух объектов с этим компонентом.")]
    [SerializeField] private DuplicateResolutionMode duplicateResolution = DuplicateResolutionMode.ByMass;

    private enum DuplicateResolutionMode
    {
        ByMass,           // Объект с большей массой
        BySpeed,          // Объект с большей скоростью в момент столкновения
        ByInstanceID      // Объект с меньшим InstanceID (детерминированный выбор)
    }

    private XRGrabInteractable _grab;
    private Rigidbody _rb;
    private AudioSource _audioSource; // Для звуков столкновений (3D)
    private AudioSource _grabAudioSource; // Для звука захвата (2D или 3D)
    private bool _isHeld;
    private float _lastPlayTime;
    private float _lastGrabTime;
    private float _basePitch = 1f;
    private float _objectSize;

    private void Awake()
    {
        _grab = GetComponent<XRGrabInteractable>();
        _rb = GetComponent<Rigidbody>();
        _audioSource = GetComponent<AudioSource>();

        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();

        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 1f; // 3D звук для столкновений

        _basePitch = _audioSource.pitch;

        // Создаем отдельный AudioSource для звука захвата
        _grabAudioSource = gameObject.AddComponent<AudioSource>();
        _grabAudioSource.playOnAwake = false;
        _grabAudioSource.spatialBlend = grabSoundIs2D ? 0f : 1f; // 2D или 3D в зависимости от настройки

        // Вычисляем размер объекта (используем средний размер из scale)
        CalculateObjectSize();

        // Подписываемся на события захвата/отпуска
        _grab.selectEntered.AddListener(OnGrabbed);
        _grab.selectExited.AddListener(OnReleased);
    }

    private void CalculateObjectSize()
    {
        // Используем средний размер из localScale
        Vector3 scale = transform.localScale;
        _objectSize = (scale.x + scale.y + scale.z) / 3f;

        // Альтернатива: можно использовать размер коллайдера
        // Collider col = GetComponent<Collider>();
        // if (col != null)
        // {
        //     Bounds bounds = col.bounds;
        //     _objectSize = (bounds.size.x + bounds.size.y + bounds.size.z) / 3f;
        // }
    }

    private void OnDestroy()
    {
        if (_grab != null)
        {
            _grab.selectEntered.RemoveListener(OnGrabbed);
            _grab.selectExited.RemoveListener(OnReleased);
        }
    }

    private void OnGrabbed(SelectEnterEventArgs args)
    {
        _isHeld = true;
        _lastGrabTime = Time.time; // Запоминаем время захвата для задержки звуков столкновений

        // Воспроизводим звук захвата, если он задан
        if (grabSound != null && _grabAudioSource != null)
        {
            // Обновляем размер объекта для расчета pitch
            float grabPitch = _basePitch;
            if (useSizeBasedGrabPitch)
            {
                CalculateObjectSize();
                float sizeT = Mathf.InverseLerp(minObjectSize, maxObjectSize, _objectSize);
                sizeT = Mathf.Clamp01(sizeT);
                grabPitch = Mathf.Lerp(maxPitch, minPitch, sizeT);
            }

            // Устанавливаем pitch и воспроизводим звук через отдельный AudioSource
            _grabAudioSource.pitch = grabPitch;
            _grabAudioSource.PlayOneShot(grabSound, grabSoundVolume);
        }
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        _isHeld = false;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collisionClip == null)
            return;

        // Фильтр по слоям
        if ((hitLayers.value & (1 << collision.gameObject.layer)) == 0)
            return;

        // Фильтр по состоянию (держат/не держат)
        if (_isHeld && !playWhileHeld)
            return;
        if (!_isHeld && !playWhenReleased)
            return;

        // Задержка после захвата (чтобы звук захвата успел проиграться)
        if (_isHeld && Time.time - _lastGrabTime < collisionDelayAfterGrab)
            return;

        // Cooldown
        if (Time.time - _lastPlayTime < cooldown)
            return;

        float speed = collision.relativeVelocity.magnitude;
        if (speed < minSpeedForSound)
            return;

        // Проверка на дублирование звуков: если у другого объекта тоже есть этот компонент
        if (preventDuplicateSounds)
        {
            var otherSoundPlayer = collision.gameObject.GetComponent<XRGrabCollisionSound>();
            if (otherSoundPlayer != null)
            {
                // Определяем, кто должен играть звук
                bool shouldPlay = ShouldPlaySound(otherSoundPlayer, collision, speed);
                if (!shouldPlay)
                    return; // Другой объект будет играть звук
            }
        }

        // Если включено динамическое обновление, пересчитываем размер при каждом столкновении
        if (dynamicSizeUpdate && (useSizeBasedPitch || useSizeBasedVolume))
        {
            CalculateObjectSize();
        }

        // Нормализуем размер объекта (0 = маленький, 1 = большой) - используется и для pitch, и для volume
        float sizeT = 0f;
        if (useSizeBasedPitch || useSizeBasedVolume)
        {
            sizeT = Mathf.InverseLerp(minObjectSize, maxObjectSize, _objectSize);
            sizeT = Mathf.Clamp01(sizeT);
        }

        // Нормализуем скорость для громкости
        float t = Mathf.InverseLerp(minSpeedForSound, maxSpeedForMaxVolume, speed);
        float volume = Mathf.Lerp(minVolume, maxVolume, Mathf.Clamp01(t));

        // Применяем множитель размера к громкости
        if (useSizeBasedVolume)
        {
            float sizeVolumeMultiplier = Mathf.Lerp(minSizeVolumeMultiplier, maxSizeVolumeMultiplier, sizeT);
            volume *= sizeVolumeMultiplier;
            volume = Mathf.Clamp01(volume); // Ограничиваем максимальной громкостью
        }

        // Питч на основе размера объекта
        if (useSizeBasedPitch)
        {

            // Большой объект = низкий pitch, маленький = высокий pitch
            float sizeBasedPitch = Mathf.Lerp(maxPitch, minPitch, sizeT);

            // Добавляем случайное отклонение, если включено
            if (addRandomJitter)
            {
                float jitter = Random.Range(-pitchJitter, pitchJitter);
                _audioSource.pitch = sizeBasedPitch + jitter;
            }
            else
            {
                _audioSource.pitch = sizeBasedPitch;
            }
        }
        else
        {
            // Старый режим: только рандом или базовый pitch
            if (addRandomJitter)
            {
                float delta = Random.Range(-pitchJitter, pitchJitter);
                _audioSource.pitch = _basePitch + delta;
            }
            else
            {
                _audioSource.pitch = _basePitch;
            }
        }

        _audioSource.PlayOneShot(collisionClip, volume);
        _lastPlayTime = Time.time;
    }

    /// <summary>
    /// Определяет, должен ли этот объект играть звук при столкновении с другим объектом, у которого тоже есть XRGrabCollisionSound.
    /// </summary>
    private bool ShouldPlaySound(XRGrabCollisionSound other, Collision collision, float speed)
    {
        switch (duplicateResolution)
        {
            case DuplicateResolutionMode.ByMass:
                // Играет объект с большей массой
                float myMass = _rb != null ? _rb.mass : 1f;
                float otherMass = collision.rigidbody != null ? collision.rigidbody.mass : 1f;
                // Если массы равны, используем InstanceID для детерминированного выбора
                if (Mathf.Approximately(myMass, otherMass))
                    return GetInstanceID() < other.GetInstanceID();
                return myMass > otherMass;

            case DuplicateResolutionMode.BySpeed:
                // Играет объект с большей скоростью в момент столкновения
                float mySpeed = _rb != null ? _rb.linearVelocity.magnitude : 0f;
                float otherSpeed = collision.rigidbody != null ? collision.rigidbody.linearVelocity.magnitude : 0f;
                // Если скорости равны, используем InstanceID
                if (Mathf.Approximately(mySpeed, otherSpeed))
                    return GetInstanceID() < other.GetInstanceID();
                return mySpeed > otherSpeed;

            case DuplicateResolutionMode.ByInstanceID:
                // Детерминированный выбор: объект с меньшим InstanceID
                return GetInstanceID() < other.GetInstanceID();

            default:
                return true;
        }
    }

    /// <summary>
    /// Принудительно обновить размер объекта (можно вызывать из других скриптов при изменении размера).
    /// </summary>
    public void RefreshObjectSize()
    {
        CalculateObjectSize();
    }
}


