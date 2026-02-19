using NUnit.Framework;
using UnityEngine;

namespace MultiplayerFishing.Tests
{
    /// <summary>
    /// Property 4: 远程鱼竿弯曲值正确应用
    /// For any synced bend values (syncHorizontalBend, syncVerticalBend),
    /// after sufficient Lerp iterations the remote Animator parameters
    /// should converge to the synced values within tolerance.
    ///
    /// Validates: Requirements 4.2
    /// </summary>
    [TestFixture]
    public class RemoteBendPropertyTest
    {
        private GameObject _rodGo;
        private Animator _animator;
        private RuntimeAnimatorController _controller;

        [SetUp]
        public void SetUp()
        {
            _rodGo = new GameObject("TestRod");
            _animator = _rodGo.AddComponent<Animator>();

            // Create a minimal AnimatorController with HorizontalBend and VerticalBend params
            var controller = new UnityEditor.Animations.AnimatorController();
            controller.AddParameter("HorizontalBend", AnimatorControllerParameterType.Float);
            controller.AddParameter("VerticalBend", AnimatorControllerParameterType.Float);
            controller.AddLayer("Base");
            _animator.runtimeAnimatorController = controller;
            _controller = controller;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_rodGo);
            if (_controller != null)
                Object.DestroyImmediate(_controller);
        }

        /// <summary>
        /// Simulates the Lerp logic from NetworkFishingRod.ApplyRemoteBendValues.
        /// After enough iterations, the Animator values should converge to the target.
        /// </summary>
        private void SimulateLerpIterations(float targetH, float targetV, int iterations, float deltaTime)
        {
            const float LerpSpeed = 14f;
            for (int i = 0; i < iterations; i++)
            {
                float currentH = _animator.GetFloat("HorizontalBend");
                float currentV = _animator.GetFloat("VerticalBend");
                float smoothedH = Mathf.Lerp(currentH, targetH, deltaTime * LerpSpeed);
                float smoothedV = Mathf.Lerp(currentV, targetV, deltaTime * LerpSpeed);
                _animator.SetFloat("HorizontalBend", smoothedH);
                _animator.SetFloat("VerticalBend", smoothedV);
            }
        }

        [Test, Repeat(100)]
        public void Property_RemoteBendValuesConvergeToSyncedValues()
        {
            // Generate random target bend values in [-1, 1] range
            float targetH = Random.Range(-1f, 1f);
            float targetV = Random.Range(-1f, 1f);

            // Start from random initial values
            float initH = Random.Range(-1f, 1f);
            float initV = Random.Range(-1f, 1f);
            _animator.SetFloat("HorizontalBend", initH);
            _animator.SetFloat("VerticalBend", initV);

            // Simulate 60 frames at ~16ms (roughly 1 second of Lerp)
            SimulateLerpIterations(targetH, targetV, 60, 0.016f);

            float resultH = _animator.GetFloat("HorizontalBend");
            float resultV = _animator.GetFloat("VerticalBend");

            // After 60 iterations with LerpSpeed=14, values should be very close
            float tolerance = 0.01f;
            Assert.AreEqual(targetH, resultH, tolerance,
                $"HorizontalBend should converge to {targetH} from {initH}, got {resultH}");
            Assert.AreEqual(targetV, resultV, tolerance,
                $"VerticalBend should converge to {targetV} from {initV}, got {resultV}");
        }
    }
}
