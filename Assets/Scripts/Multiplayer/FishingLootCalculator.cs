using System.Collections.Generic;
using UnityEngine;
using FishingGameTool.Fishing.BaitData;
using FishingGameTool.Fishing.LootData;
using FishingGameTool.Fishing.CatchProbability;

namespace MultiplayerFishing
{
    /// <summary>
    /// Pure static calculation helpers for fishing loot probability and selection.
    /// No state, no MonoBehaviour — just math.
    /// </summary>
    public static class FishingLootCalculator
    {
        /// <summary>
        /// Rolls a catch check based on bait, probability data, and cast distance.
        /// </summary>
        public static bool RollCatchCheck(FishingBaitData baitData,
            CatchProbabilityData catchProbabilityData,
            Vector3 playerPosition, Vector3 floatPosition)
        {
            float distance = Vector3.Distance(playerPosition, floatPosition);
            float minSafeFishingDistanceFactor = 10f;

            int chanceVal = Random.Range(1, 100);

            int commonProb = 5;
            int uncommonProb = 12;
            int rareProb = 22;
            int epicProb = 35;
            int legendaryProb = 45;

            if (catchProbabilityData != null)
            {
                commonProb = catchProbabilityData._commonProbability;
                uncommonProb = catchProbabilityData._uncommonProbability;
                rareProb = catchProbabilityData._rareProbability;
                epicProb = catchProbabilityData._epicProbability;
                legendaryProb = catchProbabilityData._legendaryProbability;
                minSafeFishingDistanceFactor = catchProbabilityData._minSafeFishingDistanceFactor;
            }

            commonProb = AdjustByDistance(commonProb, distance, minSafeFishingDistanceFactor);
            uncommonProb = AdjustByDistance(uncommonProb, distance, minSafeFishingDistanceFactor);
            rareProb = AdjustByDistance(rareProb, distance, minSafeFishingDistanceFactor);
            epicProb = AdjustByDistance(epicProb, distance, minSafeFishingDistanceFactor);
            legendaryProb = AdjustByDistance(legendaryProb, distance, minSafeFishingDistanceFactor);

            if (baitData == null)
                return chanceVal <= commonProb;

            switch (baitData._baitTier)
            {
                case BaitTier.Uncommon:  return chanceVal <= uncommonProb;
                case BaitTier.Rare:      return chanceVal <= rareProb;
                case BaitTier.Epic:      return chanceVal <= epicProb;
                case BaitTier.Legendary: return chanceVal <= legendaryProb;
            }

            return false;
        }

        /// <summary>
        /// Selects a random loot from the available pool based on rarity and bait tier.
        /// Mirrors FishingSystem.ChooseFishingLoot exactly.
        /// </summary>
        public static FishingLootData ChooseLoot(FishingBaitData baitData, List<FishingLootData> lootDataList)
        {
            Debug.Log($"[ChooseLoot] Input list count={lootDataList?.Count ?? 0}");
            for (int d = 0; d < lootDataList.Count; d++)
                Debug.Log($"[ChooseLoot]   [{d}] {lootDataList[d]._lootName} rarity={lootDataList[d]._lootRarity} tier={lootDataList[d]._lootTier}");

            // Shuffle
            for (int i = 0; i < lootDataList.Count; i++)
            {
                FishingLootData temp = lootDataList[i];
                int randomIndex = Random.Range(i, lootDataList.Count);
                lootDataList[i] = lootDataList[randomIndex];
                lootDataList[randomIndex] = temp;
            }

            float totalRarity = 0f;
            foreach (var loot in lootDataList)
                totalRarity += loot._lootRarity;

            List<float> rarityPercents = new List<float>();
            foreach (var loot in lootDataList)
                rarityPercents.Add((loot._lootRarity / totalRarity) * 100f);

            // TODO: 鱼饵系统未实现，当前忽略baitTier过滤，所有鱼按rarity均等随机。
            // 实现鱼饵后恢复：baitTier >= (int)lootDataList[i]._lootTier 的检查。
            // 见 DESIGN.md 第十节。
            float chanceVal = Random.Range(1f, 100f);
            float accumulated = 0f;

            for (int i = 0; i < rarityPercents.Count; i++)
            {
                accumulated += rarityPercents[i];
                if (chanceVal <= accumulated)
                    return lootDataList[i];
            }

            return null;
        }

        /// <summary>
        /// Calculates the loot's autonomous swimming speed (fish fight behavior).
        /// </summary>
        public static float CalcLootSpeed(FishingLootData lootData, float lootWeight,
            ref float randomSpeedChangerTimer, ref float randomSpeedChanger)
        {
            float[] speedMultipliersByTier = { 1.0f, 1.5f, 2.0f, 2.5f, 3.0f };
            float baseSpeed = 1.4f;
            int tier = (int)lootData._lootTier;

            randomSpeedChangerTimer -= Time.deltaTime;
            if (randomSpeedChangerTimer < 0)
            {
                randomSpeedChanger = Random.Range(1f, 3f);
                randomSpeedChangerTimer = Random.Range(2f, 4f);
            }

            return (baseSpeed + lootWeight * 0.1f * speedMultipliersByTier[tier]) * randomSpeedChanger;
        }

        /// <summary>
        /// Calculates the final attract speed when fighting a caught loot.
        /// </summary>
        public static float CalcFinalAttractSpeed(float lootSpeed, float attractSpeed, FishingLootData lootData)
        {
            int tier = (int)lootData._lootTier;
            float[] speedFactorByTier = { 1.2f, 1.0f, 0.8f, 0.6f, 0.5f };

            float result = (attractSpeed - lootSpeed) * speedFactorByTier[tier];
            return result < 2f ? 2f : result;
        }

        private static int AdjustByDistance(float probability, float distance, float minSafeFishingDistanceFactor)
        {
            float x = Mathf.InverseLerp(0, minSafeFishingDistanceFactor, distance);
            float value = Mathf.Lerp(0.3f, 1f, x);
            return (int)(probability * value);
        }
    }
}
