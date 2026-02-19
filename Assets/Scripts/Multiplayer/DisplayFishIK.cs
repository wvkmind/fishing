using UnityEngine;

namespace MultiplayerFishing
{
    /// <summary>
    /// IK controller for the "displaying caught fish" pose.
    /// Works like HandIK for the fishing rod: hands are pulled TO the fish.
    /// The fish is placed at a fixed display position (chest area),
    /// and IK targets are calculated relative to the fish's bounds.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class DisplayFishIK : MonoBehaviour
    {
        [Header("IK Weight")]
        [SerializeField] [Range(0f, 1f)] private float _ikWeight = 1f;
        [SerializeField] [Range(0f, 1f)] private float _rotationWeight = 0.5f;

        /// <summary>
        /// How far left hand grips from fish center (ratio of fish height).
        /// Positive = toward head (upper part).
        /// </summary>
        [Header("Grip Offsets (ratio of fish height)")]
        [SerializeField] private float _leftHandRatio = 0.25f;
        [SerializeField] private float _rightHandRatio = -0.25f;

        private Animator _animator;
        private Transform _fishTransform;
        private float _fishHeight;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            enabled = false; // off by default
        }

        /// <summary>
        /// Set the display fish reference. Call this after instantiating the fish.
        /// </summary>
        public void SetFish(Transform fish, float fishHeight)
        {
            _fishTransform = fish;
            _fishHeight = fishHeight;
        }

        public void ClearFish()
        {
            _fishTransform = null;
            _fishHeight = 0f;
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (_animator == null || _fishTransform == null) return;

            Vector3 fishCenter = _fishTransform.position;

            // Left hand target: fish center, nudge left and slightly down
            Vector3 leftTarget = fishCenter + Vector3.down * 0.05f + transform.right * -0.14f;

            // Left hand only
            _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, _ikWeight);
            _animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, _rotationWeight);
            _animator.SetIKPosition(AvatarIKGoal.LeftHand, leftTarget);

            // Right hand: natural animation
            _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
            _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
        }
    }
}
