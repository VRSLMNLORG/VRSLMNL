using UnityEngine;

public class FollowMirror : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public Transform objectToFolllow;
    public Transform mirror;
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 objectToFollowLocal = mirror.InverseTransformPoint(objectToFolllow.position);
        transform.position = mirror.TransformPoint(new Vector3(objectToFollowLocal.x, objectToFollowLocal.y, -objectToFollowLocal.z));

        transform.localRotation = objectToFolllow.localRotation;
        
    }
}
