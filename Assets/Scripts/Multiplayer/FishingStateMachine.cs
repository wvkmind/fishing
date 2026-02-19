using System;
using System.Collections.Generic;
using UnityEngine;
using FishingGameTool.Fishing;
using FishingGameTool.Fishing.Float;
using FishingGameTool.Fishing.BaitData;
using FishingGameTool.Fishing.LootData;

namespace MultiplayerFishing
{
    /// <summary>
    /// Configuration snapshot read once from FishingSystem Inspector values.
    /// Decouples the state machine from the plugin components.
    /// </summary>
    public struct FishingConfig
    {
        public float spawnFloatDelay;
        public float catchDistance;
        public float catchCheckInterval;
        public float returnSpeedWithoutLoot;
        public float maxLineLength;
        public float maxLineLoad;
        public float overLoadDuration;
        public float baseAttractSpeed;
        public Vector2 angleRange;
        public bool isLineBreakable;
        public LayerMask fishingLayer;
        public LootCatchType lootCatchType;
    }

    /// <summary>
    /// Runtime context passed each Tick from the controller.
    /// Contains live data the state machine cannot own (transforms, physics).
    /// </summary>
    public struct FloatContext
    {
        public Vector3 floatPosition;
        public Vector3 playerPosition;
        /// <summary>Rod's transform.forward — used for line load angle calculation.</summary>
        public Vector3 rodForward;
        /// <summary>Rod's transform.position — used for line load angle calculation.</summary>
        public Vector3 rodPosition;
        /// <summary>Rod tip (line attachment) position — used for line length calculation.</summary>
        public Vector3 lineAttachmentPosition;
        public SubstrateType substrate;
        public Transform floatTransform;
        public bool attractInput;
    }

    /// <summary>
    /// Server-side fishing state machine. Pure C# — no MonoBehaviour, no Mirror.
    /// All Unity interactions go through callbacks; controller handles networking.
    ///
    /// Preserves every original FishingSystem behavior:
    ///   AttractFloat (4 branches), CheckingLoot, ChooseLoot, AttractWithLoot,
    ///   LineLengthLimitation, CalculateLineLoad, GrabLoot, CastingDelay,
    ///   ForceStopFishing, FixLine, AddBait.
    /// </summary>
    public class FishingStateMachine
    {
        // ── Config (immutable after construction) ──
        private readonly FishingConfig _cfg;

        // ── Callbacks ──
        public Action<FishingState> OnStateChanged;
        public Action OnRequestDestroyFloat;
        public Action<FishingLootData, float> OnLootSelected;
        public Action<GameObject> OnLootGrabbed;
        public Action OnLineBroken;

        // ── State ──
        private FishingState _state = FishingState.Idle;
        private float _castingTimer;
        private float _catchCheckTimer;
        private bool _caughtLoot;
        private FishingLootData _caughtLootData;
        private float _lootWeight;
        private FishingBaitData _bait;

        // Line load (server calculates, controller syncs to clients)
        private float _lineLoad;
        private float _overLoad;
        private float _attractFloatSpeed;
        private bool _hasLandedOnWater; // true after float first touches water

        // Loot fight
        private float _finalSpeed;
        private float _randomSpeedChangerTimer = 2f;
        private float _randomSpeedChanger = 1f;
        private readonly FishingFloatPathfinder _pathfinder = new FishingFloatPathfinder();

        // ── Public read-only accessors ──
        public FishingState State => _state;
        public float LineLoad => _lineLoad;
        public float OverLoad => _overLoad;
        public bool CaughtLoot => _caughtLoot;
        public FishingLootData CaughtLootData => _caughtLootData;
        public float LootWeight => _lootWeight;
        public FishingBaitData Bait => _bait;
        public bool HasLandedOnWater => _hasLandedOnWater;

        // ── Constructor ──
        public FishingStateMachine(FishingConfig config)
        {
            _cfg = config;
            _catchCheckTimer = config.catchCheckInterval;
        }

        // ══════════════════════════════════════════════════════════════
        //  Public API (called by NetworkFishingController)
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Start casting. Called when server receives CmdCast.
        /// Returns the spawnFloatDelay so the controller can run a coroutine.
        /// </summary>
        public float BeginCast()
        {
            if (_state != FishingState.Idle) return -1f;
            SetState(FishingState.Casting);
            _castingTimer = _cfg.spawnFloatDelay;
            return _cfg.spawnFloatDelay;
        }

        /// <summary>
        /// Called by controller after the float is actually spawned.
        /// Transitions Casting → Floating.
        /// </summary>
        public void OnFloatSpawned()
        {
            if (_state != FishingState.Casting) return;
            _catchCheckTimer = _cfg.catchCheckInterval;
            SetState(FishingState.Floating);
        }

        /// <summary>
        /// Main tick — call every server frame with current context.
        /// </summary>
        public void Tick(float deltaTime, FloatContext ctx)
        {
            switch (_state)
            {
                case FishingState.Floating:
                    TickFloating(deltaTime, ctx);
                    break;
                case FishingState.Hooked:
                    TickHooked(deltaTime, ctx);
                    break;
            }
        }

        /// <summary>Force stop (e.g. player presses cancel).</summary>
        public void ForceStop()
        {
            if (_state == FishingState.Idle) return;
            CleanupAndIdle();
            OnRequestDestroyFloat?.Invoke();
        }

        /// <summary>Fix a broken line.</summary>
        public void FixLine()
        {
            if (_state == FishingState.LineBroken)
                SetState(FishingState.Idle);
        }

        /// <summary>Set bait (placeholder for future inventory integration).</summary>
        public void SetBait(FishingBaitData baitData)
        {
            _bait = baitData;
        }

        /// <summary>Enter Displaying state (fish caught, showing to players).</summary>
        public void SetDisplaying()
        {
            SetState(FishingState.Displaying);
        }

        /// <summary>Dismiss display fish, return to Idle.</summary>
        public void DismissDisplay()
        {
            if (_state == FishingState.Displaying)
            {
                ResetFishingData();
                SetState(FishingState.Idle);
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  Floating state — loot check, no-loot attract, line limit
        // ══════════════════════════════════════════════════════════════

        private void TickFloating(float deltaTime, FloatContext ctx)
        {
            // Track first water contact — float starts InAir after being cast
            if (!_hasLandedOnWater && ctx.substrate == SubstrateType.Water)
                _hasLandedOnWater = true;

            // Loot check (water only)
            if (ctx.substrate == SubstrateType.Water && !_caughtLoot)
            {
                _catchCheckTimer -= deltaTime;
                if (_catchCheckTimer <= 0f)
                {
                    _caughtLoot = FishingLootCalculator.RollCatchCheck(
                        _bait, null,
                        ctx.playerPosition, ctx.floatPosition);
                    _catchCheckTimer = _cfg.catchCheckInterval;

                    if (_caughtLoot)
                    {
                        Debug.Log($"[StateMachine] Catch check HIT → Hooked");
                        SetState(FishingState.Hooked);
                        return;
                    }
                }
            }

            // Line length limitation (non-water only)
            TickLineLengthLimitation(ctx);

            if (!ctx.attractInput) return;

            // ── Attract without loot ──
            float dist = Vector3.Distance(ctx.playerPosition, ctx.floatPosition);

            if (ctx.substrate == SubstrateType.InAir)
            {
                // InAir: destroy only if float has already been on water
                // (prevents destroying float while it's still flying after cast)
                if (_hasLandedOnWater)
                {
                    CleanupAndIdle();
                    OnRequestDestroyFloat?.Invoke();
                }
                return;
            }

            if (ctx.substrate == SubstrateType.Land)
            {
                // Land: pull back with velocity
                float speed = _cfg.returnSpeedWithoutLoot * 120f * deltaTime;
                Vector3 dir = (ctx.playerPosition - ctx.floatPosition).normalized;
                var rb = ctx.floatTransform.GetComponent<Rigidbody>();
                if (rb != null) rb.linearVelocity = dir * speed;

                if (dist <= _cfg.catchDistance)
                {
                    CleanupAndIdle();
                    OnRequestDestroyFloat?.Invoke();
                }
                return;
            }

            if (ctx.substrate == SubstrateType.Water)
            {
                // Water: pathfinder pull back
                _pathfinder.FloatBehavior(
                    null, ctx.floatTransform, ctx.playerPosition,
                    _cfg.maxLineLength, _cfg.returnSpeedWithoutLoot,
                    true, _cfg.fishingLayer);

                if (dist <= _cfg.catchDistance)
                {
                    CleanupAndIdle();
                    OnRequestDestroyFloat?.Invoke();
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  Hooked state — choose loot, fight, line load, grab
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Called once when entering Hooked to provide the loot list from the water object.
        /// Must be called by the controller right after state transitions to Hooked.
        /// </summary>
        public void ProvideLootList(List<FishingLootData> lootList)
        {
            if (_state != FishingState.Hooked || _caughtLootData != null) return;

            Debug.Log($"[StateMachine] ProvideLootList count={lootList?.Count ?? 0}");

            // Mirror original while loop: keep trying until we get a loot or exhaust list
            while (_caughtLootData == null)
            {
                _caughtLootData = FishingLootCalculator.ChooseLoot(_bait, lootList);
                if (_caughtLootData != null)
                {
                    _lootWeight = UnityEngine.Random.Range(
                        _caughtLootData._weightRange._minWeight,
                        _caughtLootData._weightRange._maxWeight);
                    _bait = null;
                    Debug.Log($"[StateMachine] Loot chosen: {_caughtLootData._lootName} weight={_lootWeight:F2}");
                    OnLootSelected?.Invoke(_caughtLootData, _lootWeight);
                }
            }
        }

        private void TickHooked(float deltaTime, FloatContext ctx)
        {
            if (_caughtLootData == null) return; // waiting for ProvideLootList

            // Line length limitation
            TickLineLengthLimitation(ctx);

            // Calculate line load + overLoad (replaces FishingRod.CalculateLineLoad)
            TickLineLoad(deltaTime, ctx);

            // Check line break (overLoad accumulated inside TickLineLoad)
            if (_overLoad >= _cfg.overLoadDuration && _cfg.isLineBreakable)
            {
                Debug.Log($"[StateMachine] LINE BROKEN overLoad={_overLoad:F2}/{_cfg.overLoadDuration:F2}");
                OnRequestDestroyFloat?.Invoke();
                OnLineBroken?.Invoke();
                ResetFishingData();
                SetState(FishingState.LineBroken);
                return;
            }

            // Loot fight movement
            float lootSpeed = FishingLootCalculator.CalcLootSpeed(
                _caughtLootData, _lootWeight,
                ref _randomSpeedChangerTimer, ref _randomSpeedChanger);

            float targetSpeed = ctx.attractInput
                ? FishingLootCalculator.CalcFinalAttractSpeed(lootSpeed, _attractFloatSpeed, _caughtLootData)
                : lootSpeed;

            _finalSpeed = Mathf.Lerp(_finalSpeed, targetSpeed, 3f * deltaTime);

            _pathfinder.FloatBehavior(
                _caughtLootData, ctx.floatTransform, ctx.playerPosition,
                _cfg.maxLineLength, _finalSpeed,
                ctx.attractInput, _cfg.fishingLayer);

            // Check grab distance
            if (ctx.attractInput)
            {
                float dist = Vector3.Distance(ctx.playerPosition, ctx.floatPosition);
                if (dist <= _cfg.catchDistance)
                {
                    GrabLoot(ctx.floatPosition, ctx.playerPosition);
                    return;
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  Line load calculation (replaces FishingRod.CalculateLineLoad)
        // ══════════════════════════════════════════════════════════════

        private void TickLineLoad(float deltaTime, FloatContext ctx)
        {
            if (_caughtLootData == null) return;

            // Original: Vector3.Angle(fishingRod.transform.forward, floatPos - rodPos)
            Vector3 dir = ctx.floatPosition - ctx.rodPosition;
            float angle = Vector3.Angle(ctx.rodForward, dir);
            angle = Mathf.Min(angle, _cfg.angleRange.y);

            int lootTier = (int)_caughtLootData._lootTier;

            if (ctx.attractInput)
            {
                float loadDecreaseFactor = 4f;
                float calcWeight = (_lootWeight - (_lootWeight / lootTier));
                calcWeight = calcWeight <= 0f ? 1f : calcWeight;

                _lineLoad += ((angle * calcWeight) * deltaTime) / loadDecreaseFactor;
                _lineLoad = Mathf.Min(_lineLoad, _cfg.maxLineLoad);
            }
            else
            {
                _overLoad = 0f;
                _lineLoad -= 5f * deltaTime;
                _lineLoad = Mathf.Max(_lineLoad, 0f);
            }

            // Overload accumulation (original uses == for exact max comparison)
            if (_lineLoad == _cfg.maxLineLoad)
            {
                _overLoad += deltaTime;
            }

            // Calculate attract speed (same formula as FishingRod)
            float normalizeAngle = angle / _cfg.angleRange.y;
            float attractBonus = CalcAttractBonus(_lineLoad, _cfg.maxLineLoad, lootTier);
            _attractFloatSpeed = _cfg.baseAttractSpeed + normalizeAngle * attractBonus;
        }

        private static float CalcAttractBonus(float currentLineLoad, float maxLineLoad, int lootTier)
        {
            float[] multipliers = { 0.2f, 0.4f, 0.6f, 0.8f, 1.0f };
            float x = Mathf.InverseLerp(0f, maxLineLoad, currentLineLoad);
            return Mathf.Lerp(1f, currentLineLoad * multipliers[lootTier], x);
        }

        // ══════════════════════════════════════════════════════════════
        //  Line length limitation
        // ══════════════════════════════════════════════════════════════

        private void TickLineLengthLimitation(FloatContext ctx)
        {
            // Original uses _lineStatus._currentLineLength which is distance from
            // _lineAttachment (rod tip) to float, NOT player to float.
            float lineLength = Vector3.Distance(ctx.lineAttachmentPosition, ctx.floatPosition);
            if (lineLength > _cfg.maxLineLength && ctx.substrate != SubstrateType.Water)
            {
                Vector3 dir = (ctx.playerPosition - ctx.floatPosition).normalized;
                var rb = ctx.floatTransform.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    float speed = (lineLength - _cfg.maxLineLength) / Time.deltaTime;
                    rb.linearVelocity = dir * Mathf.Clamp(speed, -5f, 5f);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  Grab loot
        // ══════════════════════════════════════════════════════════════

        private void GrabLoot(Vector3 floatPosition, Vector3 playerPosition)
        {
            Debug.Log($"[StateMachine] GrabLoot loot={_caughtLootData?._lootName} catchType={_cfg.lootCatchType}");

            // Notify controller to spawn the ground-drop fish (no intermediate fly animation)
            OnLootGrabbed?.Invoke(null);
            OnRequestDestroyFloat?.Invoke();
            // Don't CleanupAndIdle here — controller will enter Displaying state.
            // State machine stays in current state; controller manages transition.
        }

        // ══════════════════════════════════════════════════════════════
        //  Helpers
        // ══════════════════════════════════════════════════════════════

        private void SetState(FishingState newState)
        {
            if (_state == newState) return;
            var old = _state;
            _state = newState;
            Debug.Log($"[StateMachine] {old} → {newState}");
            OnStateChanged?.Invoke(newState);
        }

        private void CleanupAndIdle()
        {
            ResetFishingData();
            SetState(FishingState.Idle);
        }

        private void ResetFishingData()
        {
            _caughtLoot = false;
            _caughtLootData = null;
            _lootWeight = 0f;
            _lineLoad = 0f;
            _overLoad = 0f;
            _attractFloatSpeed = 0f;
            _hasLandedOnWater = false;
            _finalSpeed = 0f;
            _randomSpeedChangerTimer = 2f;
            _randomSpeedChanger = 1f;
            _pathfinder.ClearPathData();
        }
    }
}
