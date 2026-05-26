using UnityEngine;

/// <summary>
/// Attached to BOTH the Survivor and Monster player prefabs. Only does anything on
/// IsOwner clients (Monster will never call StartSpectating on itself).
///
/// During MonsterPreview, the local survivor's camera is detached from their own player
/// and smoothly orbits 3rd-person behind the monster. On exit, the camera is re-parented
/// back to its original transform.
///
/// We detach rather than reparenting under the monster's transform because (a) the monster's
/// transform is owner-authoritative via ClientNetworkTransform, and (b) the survivor's own
/// transform shouldn't influence the spectator camera.
/// </summary>
public class SpectatorCameraController : MonoBehaviour
{
    [Header("Spectator Camera")]
    [Tooltip("Local-space offset from the monster: x=side, y=height, z=behind (negative).")]
    public Vector3 spectatorOffset = new Vector3(0f, 3f, -6f);

    [Tooltip("Higher = snappier follow. 6-10 is comfortable.")]
    public float followSmoothing = 8f;

    [Tooltip("Look at this height above the monster's pivot (chest, not feet).")]
    public float lookAtHeightOffset = 1.5f;

    private Transform _cameraTransform;
    private Transform _target;
    private Transform _origParent;
    private Vector3 _origLocalPos;
    private Quaternion _origLocalRot;
    private bool _isSpectating;

    public void StartSpectating(Transform monsterTransform)
    {
        if (monsterTransform == null)
        {
            Debug.LogWarning("[SpectatorCamera] StartSpectating called with null target.");
            return;
        }
        if (_isSpectating) return; // idempotent

        // Resolve camera transform from MainPlayerScript (it's the assigned cameraTransform there).
        var main = GetComponent<MainPlayerScript>();
        if (main == null || main.cameraTransform == null)
        {
            Debug.LogWarning("[SpectatorCamera] No MainPlayerScript or cameraTransform — cannot spectate.");
            return;
        }
        _cameraTransform = main.cameraTransform;

        // Cache parent + local pose so StopSpectating can restore exactly.
        _origParent = _cameraTransform.parent;
        _origLocalPos = _cameraTransform.localPosition;
        _origLocalRot = _cameraTransform.localRotation;

        // Detach (keep world pose) so neither the survivor's nor the monster's transform fights us.
        _cameraTransform.SetParent(null, true);

        _target = monsterTransform;
        _isSpectating = true;
    }

    public void StopSpectating()
    {
        if (!_isSpectating) return;

        if (_cameraTransform != null && _origParent != null)
        {
            _cameraTransform.SetParent(_origParent, false);
            _cameraTransform.localPosition = _origLocalPos;
            _cameraTransform.localRotation = _origLocalRot;
        }
        _target = null;
        _isSpectating = false;
    }

    private void LateUpdate()
    {
        if (!_isSpectating || _target == null || _cameraTransform == null) return;

        // Desired position: behind-and-above the monster, in the monster's local frame.
        Vector3 desiredPos = _target.position + _target.TransformDirection(spectatorOffset);

        _cameraTransform.position = Vector3.Lerp(
            _cameraTransform.position,
            desiredPos,
            Time.deltaTime * followSmoothing
        );

        Vector3 lookAt = _target.position + Vector3.up * lookAtHeightOffset;
        Quaternion desiredRot = Quaternion.LookRotation(lookAt - _cameraTransform.position);
        _cameraTransform.rotation = Quaternion.Slerp(
            _cameraTransform.rotation,
            desiredRot,
            Time.deltaTime * followSmoothing
        );
    }
}
