using UnityEngine;
using FishingGameTool.Fishing;
using FishingGameTool.Fishing.Rod;
using FishingGameTool.Fishing.Line;
using FishingGameTool.Fishing.LootData;

namespace MultiplayerFishing
{
    /// <summary>
    /// One-way bridge: reads network state, writes to FishingGameTool plugin components.
    /// Drives ONLY visual/animation fields — no UI fields.
    ///
    /// UI is handled separately by FishingUI (reads SyncVars directly).
    ///
    /// Drives: FishingRod (bending, line rendering), HandIK (animation params),
    ///         cast animation flag.
    /// </summary>
    public class FishingPresenter
    {
        private readonly FishingSystem _fs;
        private readonly FishingRod _rod;
        private readonly FishingLineStatus _lineStatus;

        public FishingPresenter(FishingSystem fishingSystem)
        {
            _fs = fishingSystem;
            _rod = fishingSystem._fishingRod;
            _lineStatus = _rod._lineStatus;

            // Zero out all UI fields so SimpleUIManager (if still enabled) shows nothing
            _fs._castInput = false;
            _fs._currentCastForce = 0f;
            _fs._advanced._caughtLoot = false;
            _fs._advanced._caughtLootData = null;
            _lineStatus._currentLineLoad = 0f;
            _lineStatus._currentOverLoad = 0f;
            _lineStatus._isLineBroken = false;
        }

        private FishingState _lastLoggedState = FishingState.Idle;

        /// <summary>
        /// Called every frame on ALL clients for ALL players.
        /// Writes only animation/visual fields — never UI fields.
        /// </summary>
        public void Apply(
            FishingState state,
            bool attractInput,
            Transform floatTransform)
        {
            if (state != _lastLoggedState)
            {
                Debug.Log($"[Presenter] Apply state={state} attract={attractInput} float={floatTransform != null}");
                _lastLoggedState = state;
            }

            // Cast animation flag (HandIK reads this)
            _fs._castFloat = (state == FishingState.Casting);

            // Attract input (HandIK animation)
            _fs._attractInput = attractInput;

            // Float reference (line rendering + rod bending)
            _rod._fishingFloat = floatTransform;

            // Loot caught for rod bending + HandIK
            // safeHooked: only true when we have loot data AND float exists
            bool hooked = (state == FishingState.Hooked);
            bool hasLootData = (_fs._advanced._caughtLootData != null);
            bool safeHooked = hooked && hasLootData && floatTransform != null;
            _rod.LootCaught(safeHooked);

            // _advanced._caughtLoot drives HandIK's CaughtLoot animation param
            // and the angle calculation that reads _fishingFloat.position
            _fs._advanced._caughtLoot = safeHooked;

            // Line length for line rendering
            if (floatTransform != null && _rod._line._lineAttachment != null)
                _lineStatus._currentLineLength = Vector3.Distance(
                    _rod._line._lineAttachment.position, floatTransform.position);
        }

        /// <summary>
        /// Set loot data on FishingSystem for HandIK angle calculation.
        /// HandIK reads _advanced._caughtLootData indirectly via _caughtLoot flag.
        /// </summary>
        public void ApplyLootData(string lootName, int lootTier, string lootDescription)
        {
            Debug.Log($"[Presenter] ApplyLootData: {lootName} tier={lootTier}");
            if (_reusableLootData == null)
                _reusableLootData = ScriptableObject.CreateInstance<FishingLootData>();

            _reusableLootData._lootName = lootName;
            _reusableLootData._lootTier = (LootTier)lootTier;
            _reusableLootData._lootDescription = lootDescription;

            _fs._advanced._caughtLootData = _reusableLootData;
        }

        // Reusable SO instance to avoid per-catch allocation/leak
        private FishingLootData _reusableLootData;

        /// <summary>
        /// Clear loot data when returning to Idle.
        /// </summary>
        public void ClearLootData()
        {
            Debug.Log("[Presenter] ClearLootData");
            _fs._advanced._caughtLootData = null;
        }
    }
}
