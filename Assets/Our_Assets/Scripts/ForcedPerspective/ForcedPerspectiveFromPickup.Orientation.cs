using UnityEngine;

public partial class ForcedPerspectiveFromPickup
{
    private void UpdateOrientationSmooth()
    {
        if (!keepUpright) return;

        Quaternion target = ComputeUprightTargetRotation();
        if (_smoothingActive && yawFollowSpeed > 0f)
            transform.rotation = Quaternion.Slerp(transform.rotation, target, Mathf.Clamp01(Time.deltaTime * yawFollowSpeed));
        else
            transform.rotation = target;
    }

    private Quaternion ComputeUprightTargetRotation()
    {
        Vector3 flatFwd = Vector3.ProjectOnPlane(_cameraTransform.forward, Vector3.up);
        if (flatFwd.sqrMagnitude < 1e-6f) flatFwd = _lastFlatFwd;
        else _lastFlatFwd = flatFwd.normalized;

        Quaternion camYaw = Quaternion.LookRotation(_lastFlatFwd, Vector3.up);
        Quaternion targetYaw = followCameraYaw ? camYaw * _uprightYawOffset : _fixedUprightYaw;
        return targetYaw;
    } 
}
