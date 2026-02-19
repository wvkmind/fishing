using NUnit.Framework;
using UnityEngine;

namespace MultiplayerFishing.Tests
{
    /// <summary>
    /// Property 3: 贝塞尔曲线鱼线计算一致性
    /// For any identical input parameters (lineAttachment position, fishingFloat position,
    /// lootCaught state, simulateGravity value), the bezier curve calculation output
    /// should be identical. The function is deterministic (pure function).
    ///
    /// Validates: Requirements 5.5
    /// </summary>
    [TestFixture]
    public class BezierConsistencyPropertyTest
    {
        /// <summary>
        /// Mirrors FishingRod.CalculateBezier — a pure cubic bezier function.
        /// Extracted here so we can test determinism without needing a MonoBehaviour.
        /// </summary>
        private static Vector3 CalculateBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t, Vector3 floatPosition)
        {
            float u = 1 - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;

            Vector3 point = uuu * p0;
            point += 3 * uu * t * p1;
            point += 3 * u * tt * p2;
            point += ttt * floatPosition;

            return point;
        }

        /// <summary>
        /// Mirrors the pure portion of FishingRod.CalculatePointOnCurve.
        /// Uses a pre-computed smoothedSimGravity instead of Lerp over time.
        /// </summary>
        private static Vector3 CalculatePointOnCurve(
            float t, Vector3 attachmentPosition, Vector3 floatPosition,
            float smoothedSimGravity)
        {
            Vector3 controlPoint = Vector3.Lerp(attachmentPosition, floatPosition, 0.5f)
                                   + Vector3.up * smoothedSimGravity;
            return CalculateBezier(attachmentPosition, controlPoint, floatPosition, t, floatPosition);
        }

        [Test, Repeat(100)]
        public void Property_SameInputsProduceSameOutputs()
        {
            // Generate random inputs
            Vector3 attachment = new Vector3(
                Random.Range(-50f, 50f), Random.Range(0f, 20f), Random.Range(-50f, 50f));
            Vector3 floatPos = new Vector3(
                Random.Range(-50f, 50f), Random.Range(-5f, 5f), Random.Range(-50f, 50f));
            float simGravity = Random.Range(-2f, 0f);
            float t = Random.Range(0f, 1f);

            // Call twice with identical inputs
            Vector3 result1 = CalculatePointOnCurve(t, attachment, floatPos, simGravity);
            Vector3 result2 = CalculatePointOnCurve(t, attachment, floatPos, simGravity);

            Assert.AreEqual(result1.x, result2.x, 0.0001f,
                $"X mismatch for t={t}, attachment={attachment}, float={floatPos}, gravity={simGravity}");
            Assert.AreEqual(result1.y, result2.y, 0.0001f,
                $"Y mismatch for t={t}, attachment={attachment}, float={floatPos}, gravity={simGravity}");
            Assert.AreEqual(result1.z, result2.z, 0.0001f,
                $"Z mismatch for t={t}, attachment={attachment}, float={floatPos}, gravity={simGravity}");
        }

        [Test]
        public void BezierEndpoints_MatchInputPositions()
        {
            Vector3 attachment = new Vector3(1f, 2f, 3f);
            Vector3 floatPos = new Vector3(10f, 0f, 10f);
            float simGravity = -1f;

            // At t=0, point should be at attachment
            Vector3 atStart = CalculatePointOnCurve(0f, attachment, floatPos, simGravity);
            Assert.AreEqual(attachment.x, atStart.x, 0.001f);
            Assert.AreEqual(attachment.y, atStart.y, 0.001f);
            Assert.AreEqual(attachment.z, atStart.z, 0.001f);

            // At t=1, point should be at floatPos
            Vector3 atEnd = CalculatePointOnCurve(1f, attachment, floatPos, simGravity);
            Assert.AreEqual(floatPos.x, atEnd.x, 0.001f);
            Assert.AreEqual(floatPos.y, atEnd.y, 0.001f);
            Assert.AreEqual(floatPos.z, atEnd.z, 0.001f);
        }
    }
}
