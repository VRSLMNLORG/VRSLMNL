using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Global component that counts the number of objects connected to XR Socket Interactors.
/// Tracks only sockets specified in the manual list.
/// Displays the count in a TextMeshPro component.
/// </summary>
public class SocketConnectionCounter : MonoBehaviour
{
    [Header("UI Display")]
    [Tooltip("TextMeshPro component to display the connection count. If null, will try to find TextMeshProUGUI component on this GameObject.")]
    public TextMeshProUGUI uiText;

    [Tooltip("Text format. {0} = connected count, {1} = total sockets, {2} = empty sockets.")]
    public string displayFormat = "Connected: {0} / {1} (Empty: {2})";

    [Header("Socket Detection")]
    [Tooltip("List of sockets to track. Drag XR Socket Interactor objects here.")]
    public XRSocketInteractor[] specificSockets = new XRSocketInteractor[0];

    [Header("Update Settings")]
    [Tooltip("Update frequency in seconds. 0 = every frame.")]
    [Min(0f)] public float updateInterval = 0.1f;

    [Header("Events")]
    [Tooltip("Invoked when all sockets are connected (all items placed).")]
    public UnityEvent OnAllSocketsConnected;

    [Tooltip("Invoked when at least one socket becomes disconnected (item removed).")]
    public UnityEvent OnSocketDisconnected;

    [Header("Debug")]
    [Tooltip("Show debug information in console.")]
    public bool debugLog = false;

    private List<XRSocketInteractor> _trackedSockets = new List<XRSocketInteractor>();
    private float _lastUpdateTime;
    private int _lastConnectedCount = -1;
    private bool _allSocketsWereConnected = false;

    private void Awake()
    {
        // Try to find TextMeshPro component if not assigned
        if (uiText == null)
        {
            uiText = GetComponent<TextMeshProUGUI>();
            if (uiText == null)
            {
                uiText = GetComponentInChildren<TextMeshProUGUI>();
            }
        }

        if (uiText == null && debugLog)
        {
            Debug.LogWarning($"[SocketConnectionCounter] No TextMeshProUGUI component found. Please assign one in the inspector.", this);
        }
    }

    private void Start()
    {
        FindAndTrackSockets();
        // Initialize connection state
        UpdateDisplay();
    }

    private void OnEnable()
    {
        FindAndTrackSockets();
        SubscribeToSocketEvents();
    }

    private void OnDisable()
    {
        UnsubscribeFromSocketEvents();
    }

    private void Update()
    {
        // Update UI at specified interval
        if (updateInterval > 0f)
        {
            if (Time.time - _lastUpdateTime < updateInterval)
                return;
        }

        _lastUpdateTime = Time.time;
        UpdateDisplay();
    }

    /// <summary>
    /// Finds and tracks sockets from the manual list.
    /// </summary>
    private void FindAndTrackSockets()
    {
        _trackedSockets.Clear();

        // Use only manual list
        if (specificSockets != null && specificSockets.Length > 0)
        {
            foreach (XRSocketInteractor socket in specificSockets)
            {
                if (socket != null)
                {
                    _trackedSockets.Add(socket);
                }
            }

            if (debugLog)
            {
                Debug.Log($"[SocketConnectionCounter] Tracking {_trackedSockets.Count} socket(s) from manual list.", this);
            }
        }
        else
        {
            if (debugLog)
            {
                Debug.LogWarning($"[SocketConnectionCounter] No sockets specified in the list! Please add sockets to track.", this);
            }
        }

        // Remove null references
        _trackedSockets.RemoveAll(s => s == null);

        if (debugLog && _trackedSockets.Count > 0)
        {
            Debug.Log($"[SocketConnectionCounter] Total tracked sockets: {_trackedSockets.Count}", this);
        }
    }

    /// <summary>
    /// Subscribes to socket events for real-time updates.
    /// </summary>
    private void SubscribeToSocketEvents()
    {
        foreach (XRSocketInteractor socket in _trackedSockets)
        {
            if (socket != null)
            {
                socket.selectEntered.AddListener(OnSocketConnected);
                socket.selectExited.AddListener(HandleSocketItemDisconnected);
            }
        }
    }

    /// <summary>
    /// Unsubscribes from socket events.
    /// </summary>
    private void UnsubscribeFromSocketEvents()
    {
        foreach (XRSocketInteractor socket in _trackedSockets)
        {
            if (socket != null)
            {
                socket.selectEntered.RemoveListener(OnSocketConnected);
                socket.selectExited.RemoveListener(HandleSocketItemDisconnected);
            }
        }
    }

    /// <summary>
    /// Called when an object is connected to a socket.
    /// </summary>
    private void OnSocketConnected(SelectEnterEventArgs args)
    {
        if (debugLog)
        {
            GameObject obj = GetGameObjectFromInteractable(args.interactableObject);
            string objName = obj != null ? obj.name : args.interactableObject.ToString();
            Debug.Log($"[SocketConnectionCounter] Object '{objName}' connected to socket.", this);
        }

        // Update immediately on connection change
        UpdateDisplay();
    }

    /// <summary>
    /// Called when an object is disconnected from a socket.
    /// </summary>
    private void HandleSocketItemDisconnected(SelectExitEventArgs args)
    {
        if (debugLog)
        {
            GameObject obj = GetGameObjectFromInteractable(args.interactableObject);
            string objName = obj != null ? obj.name : args.interactableObject.ToString();
            Debug.Log($"[SocketConnectionCounter] Object '{objName}' disconnected from socket.", this);
        }

        // Update immediately on connection change
        UpdateDisplay();
    }

    /// <summary>
    /// Counts how many sockets have connected objects.
    /// </summary>
    private int CountConnectedSockets()
    {
        int count = 0;
        foreach (XRSocketInteractor socket in _trackedSockets)
        {
            if (socket != null && socket.hasSelection)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Updates the UI display with current connection count.
    /// </summary>
    private void UpdateDisplay()
    {
        int connectedCount = CountConnectedSockets();
        int totalSockets = _trackedSockets.Count;
        int emptySockets = totalSockets - connectedCount;

        // Check if all sockets are connected
        bool allConnected = totalSockets > 0 && connectedCount >= totalSockets;

        // Check if count changed
        bool countChanged = connectedCount != _lastConnectedCount;

        // Update UI text
        if (uiText != null)
        {
            // Only update if count changed (optimization)
            if (countChanged || updateInterval == 0f)
            {
                string displayText = string.Format(displayFormat, connectedCount, totalSockets, emptySockets);
                uiText.text = displayText;

                if (debugLog)
                {
                    Debug.Log($"[SocketConnectionCounter] Updated display: {displayText}", this);
                }
            }
        }

        // Trigger events when state changes
        if (countChanged)
        {
            _lastConnectedCount = connectedCount;

            // Trigger OnAllSocketsConnected when all sockets become connected
            if (allConnected && !_allSocketsWereConnected)
            {
                _allSocketsWereConnected = true;
                OnAllSocketsConnected?.Invoke();

                if (debugLog)
                {
                    Debug.Log($"[SocketConnectionCounter] All sockets connected! OnAllSocketsConnected event invoked.", this);
                }
            }
            // Trigger OnSocketDisconnected when at least one socket becomes disconnected
            else if (!allConnected && _allSocketsWereConnected)
            {
                _allSocketsWereConnected = false;
                OnSocketDisconnected?.Invoke();

                if (debugLog)
                {
                    Debug.Log($"[SocketConnectionCounter] Socket disconnected. OnSocketDisconnected event invoked.", this);
                }
            }
        }
    }

    /// <summary>
    /// Gets GameObject from IXRSelectInteractable.
    /// </summary>
    private GameObject GetGameObjectFromInteractable(IXRSelectInteractable interactable)
    {
        if (interactable == null)
            return null;

        if (interactable is MonoBehaviour monoBehaviour)
        {
            return monoBehaviour.gameObject;
        }

        if (interactable.transform != null)
        {
            return interactable.transform.gameObject;
        }

        if (interactable is Component component)
        {
            return component.gameObject;
        }

        return null;
    }

    /// <summary>
    /// Manually refresh the socket list (useful if sockets are added/removed at runtime).
    /// </summary>
    public void RefreshSocketList()
    {
        UnsubscribeFromSocketEvents();
        FindAndTrackSockets();
        SubscribeToSocketEvents();
        UpdateDisplay();
    }

    /// <summary>
    /// Gets the current count of connected sockets.
    /// </summary>
    public int GetConnectedCount()
    {
        return CountConnectedSockets();
    }

    /// <summary>
    /// Gets the total count of tracked sockets.
    /// </summary>
    public int GetTotalCount()
    {
        return _trackedSockets.Count;
    }
}

