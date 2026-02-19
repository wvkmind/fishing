using UnityEngine;

namespace MultiplayerFishing
{
    /// <summary>
    /// Attached to the player during Displaying state.
    /// - IK: raises left hand to "hold fish" position
    /// - Fish: follows left hand every frame (NOT parented, to avoid Mirror sync conflicts)
    ///   Rotated so model X+ points up (fish head up, body hangs down)
    /// </summary>
    public class DisplayFishHold : MonoBehaviour
    {
        private Animator _animator;
        private Transform _fishTransform;
        private Transform _leftHandBone;
        private Transform _headBone;
        private bool _active;

        private Vector3 _originalScale;

        public void Setup(GameObject fishGO)
        {
            _animator = GetComponent<Animator>();
            if (_animator == null)
            {
                Debug.LogWarning("[DisplayFishHold] No Animator found");
                return;
            }

            _headBone = _animator.GetBoneTransform(HumanBodyBones.Head);
            _leftHandBone = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _fishTransform = fishGO.transform;
            _originalScale = fishGO.transform.localScale;
            _active = true;

            Debug.Log($"[DisplayFishHold] Setup fish={fishGO.name} originalScale={_originalScale} leftHand={_leftHandBone != null} head={_headBone != null}");
        }

        public void Cleanup()
        {
            _active = false;
            _fishTransform = null;
        }

        private void LateUpdate()
        {
            if (!_active || _fishTransform == null || _leftHandBone == null) return;

            // Fish directly below left hand palm, shifted right
            _fishTransform.position = _leftHandBone.position + Vector3.down * 0.05f + transform.right * 0.15f;

            // Scale: keep original prefab scale, multiply by 0.25
            _fishTransform.localScale = _originalScale * 0.25f;

            // Fish head (X+) points up — same rotation that was working before
            _fishTransform.rotation = Quaternion.LookRotation(transform.forward, Vector3.up)
                                    * Quaternion.Euler(0f, 0f, 90f);
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (!_active || _animator == null || _headBone == null) return;

            // Same position validated with IKTest
            Vector3 target = _headBone.position + Vector3.up * 0.1f + transform.forward * 0.35f;

            _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 1f);
            _animator.SetIKPosition(AvatarIKGoal.LeftHand, target);

            Vector3 elbowHint = _headBone.position - transform.right * 0.3f + Vector3.down * 0.2f;
            _animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 1f);
            _animator.SetIKHintPosition(AvatarIKHint.LeftElbow, elbowHint);
        }

        private void OnDisable()
        {
            _active = false;
        }
    }
}
