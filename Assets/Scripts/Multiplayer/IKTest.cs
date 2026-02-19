using UnityEngine;

namespace MultiplayerFishing
{
    /// <summary>
    /// 本地测试用：按J键切换提鱼姿势+生成鱼模型。
    /// 挂在角色上（有Animator的物体），Inspector里拖一个鱼prefab进去。
    /// </summary>
    public class IKTest : MonoBehaviour
    {
        [Header("拖一个鱼Prefab进来")]
        public GameObject fishPrefab;

        [Header("调参数")]
        public float scaleMul = 0.25f;
        public float downOffset = 0.05f;
        public float rightOffset = 0.15f;
        public float upOffset = 0.1f;
        public float forwardOffset = 0.35f;

        private Animator _animator;
        private bool _raising;
        private GameObject _fishInstance;
        private Vector3 _originalScale;
        private Transform _leftHandBone;
        private Transform _headBone;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            if (_animator != null)
            {
                _headBone = _animator.GetBoneTransform(HumanBodyBones.Head);
                _leftHandBone = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.J))
            {
                _raising = !_raising;
                if (_raising && fishPrefab != null && _fishInstance == null)
                {
                    _fishInstance = Instantiate(fishPrefab);
                    _originalScale = _fishInstance.transform.localScale;
                    // 去掉物理
                    var rb = _fishInstance.GetComponent<Rigidbody>();
                    if (rb != null) Destroy(rb);
                    var col = _fishInstance.GetComponent<Collider>();
                    if (col != null) Destroy(col);
                }
                if (!_raising && _fishInstance != null)
                {
                    Destroy(_fishInstance);
                    _fishInstance = null;
                }
            }
        }

        private void LateUpdate()
        {
            if (!_raising || _fishInstance == null || _leftHandBone == null) return;

            _fishInstance.transform.position = _leftHandBone.position
                + Vector3.down * downOffset
                + transform.right * rightOffset;

            _fishInstance.transform.localScale = _originalScale * scaleMul;

            _fishInstance.transform.rotation = Quaternion.LookRotation(transform.forward, Vector3.up)
                                             * Quaternion.Euler(0f, 0f, 90f);
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (_animator == null || _headBone == null)  return;

            if (_raising)
            {
                Vector3 target = _headBone.position + Vector3.up * upOffset + transform.forward * forwardOffset;
                _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 1f);
                _animator.SetIKPosition(AvatarIKGoal.LeftHand, target);

                Vector3 elbowHint = _headBone.position - transform.right * 0.3f + Vector3.down * 0.2f;
                _animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 1f);
                _animator.SetIKHintPosition(AvatarIKHint.LeftElbow, elbowHint);
            }
            else
            {
                _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
                _animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
            }
        }
    }
}
