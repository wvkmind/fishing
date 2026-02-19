using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using FishingGameTool.Fishing;
using FishingGameTool.Fishing.Float;
using FishingGameTool.Fishing.Rod;
using FishingGameTool.Fishing.LootData;

namespace MultiplayerFishing
{
    /// <summary>
    /// v2 Network controller — thin orchestrator.
    ///
    /// Responsibilities:
    ///   1. Lifecycle: disable FishingSystem, create state machine + presenter
    ///   2. Input: read Fire1/Fire2 on authority client, send Commands
    ///   3. Networking: Commands, ClientRpcs, SyncVars
    ///   4. Tick: drive state machine (server) + presenter (all clients)
    ///   5. Throttled sync: lineLoad/overLoad
    ///
    /// Does NOT contain any fishing logic — that lives in FishingStateMachine.
    /// Does NOT write plugin fields directly — that lives in FishingPresenter.
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public class NetworkFishingController : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private FishingSystem _fishingSystem;

        [Header("Network Float Prefab")]
        [SerializeField] private GameObject _networkFloatPrefab;

        // ── SyncVars ──
        [SyncVar] public FishingState syncState;
        [SyncVar] public bool syncAttractInput;
        [SyncVar] public float syncLineLoad;
        [SyncVar] public float syncOverLoad;
        [SyncVar] public string syncLootName;
        [SyncVar] public int syncLootTier;
        [SyncVar] public string syncLootDescription;
        [SyncVar(hook = nameof(OnRodEquippedChanged))] public bool syncRodEquipped;

        // ── Public accessor for other components (e.g. NetworkFishingRod) ──
        public Transform ActiveFloatTransform { get; private set; }

        // ── Server-only ──
        private FishingStateMachine _stateMachine;
        private GameObject _spawnedFloat;

        // Throttled sync
        private float _lastSyncedLineLoad;
        private float _lastSyncedOverLoad;
        private float _syncTimer;
        private float _floatDiagTimer; // periodic float diagnostic logging

        // ── All clients ──
        private FishingPresenter _presenter;
        private FishingUI _fishingUI; // local player only

        // ── Authority client input ──
        private bool _isCharging;
        private float _currentCastForce;
        private bool _wasAttracting;
        private bool _castSent; // prevents duplicate CmdCast before SyncVar updates

        // ── Rod equip/unequip ──
        private GameObject _rodGameObject;
        private FishingGameTool.Example.HandIK _handIK;

        // ══════════════════════════════════════════════════════════════
        //  Lifecycle
        // ══════════════════════════════════════════════════════════════

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (_fishingSystem == null) return;

            Debug.Log($"[NFC][Server] OnStartServer netId={netId}");
            _fishingSystem.enabled = false;

            var config = new FishingConfig
            {
                spawnFloatDelay = _fishingSystem._spawnFloatDelay,
                catchDistance = _fishingSystem._catchDistance,
                catchCheckInterval = _fishingSystem._advanced._catchCheckInterval,
                returnSpeedWithoutLoot = _fishingSystem._advanced._returnSpeedWithoutLoot,
                maxLineLength = _fishingSystem._fishingRod._lineStatus._maxLineLength,
                maxLineLoad = _fishingSystem._fishingRod._lineStatus._maxLineLoad,
                overLoadDuration = _fishingSystem._fishingRod._lineStatus._overLoadDuration,
                baseAttractSpeed = _fishingSystem._fishingRod._baseAttractSpeed,
                angleRange = _fishingSystem._fishingRod._angleRange,
                isLineBreakable = _fishingSystem._fishingRod._isLineBreakable,
                fishingLayer = _fishingSystem._fishingLayer,
                lootCatchType = _fishingSystem._lootCatchType
            };

            _stateMachine = new FishingStateMachine(config);

            // Wire callbacks
            _stateMachine.OnStateChanged = OnServerStateChanged;
            _stateMachine.OnRequestDestroyFloat = OnServerDestroyFloat;
            _stateMachine.OnLootSelected = OnServerLootSelected;
            _stateMachine.OnLootGrabbed = OnServerLootGrabbed;
            _stateMachine.OnLineBroken = OnServerLineBroken;

            // Register line break callback on FishingRod (prevents FindObjectsByType fallback)
            _fishingSystem._fishingRod.OnLineBreak = () => _stateMachine.ForceStop();

            // Disable visual-only components on dedicated server — they waste CPU
            // and can NullRef (no camera, no renderer needed)
            if (Application.isBatchMode)
            {
                DisableServerVisuals();
            }
        }

        /// <summary>
        /// Disables components that are purely visual/client-side.
        /// Called on dedicated server only.
        /// </summary>
        private void DisableServerVisuals()
        {
            // FishingRod: CalculateBend + LineRenderer — pure visuals
            var rod = _fishingSystem._fishingRod;
            if (rod != null) rod.enabled = false;

            // HandIK: animation IK + reads _fishingFloat.position (NullRef risk)
            var handIK = GetComponentInChildren<FishingGameTool.Example.HandIK>(true);
            if (handIK != null) handIK.enabled = false;

            // TPPCamera: follows target, reads mouse input
            var tppCam = GetComponentInChildren<FishingGameTool.Example.TPPCamera>(true);
            if (tppCam != null) tppCam.enabled = false;

            // NOTE: Do NOT disable Animator — NetworkAnimator needs it to relay
            // animation parameters between clients (Walk, CaughtLoot, etc.)

            // LineRenderer: no rendering on server
            var lineRenderers = GetComponentsInChildren<LineRenderer>(true);
            foreach (var lr in lineRenderers) lr.enabled = false;

            // SkinnedMeshRenderer: no rendering on server
            var skinRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var sr in skinRenderers) sr.enabled = false;

            Debug.Log($"[NFC][Server] Disabled visual components for headless mode netId={netId}");
        }


        public override void OnStartClient()
        {
            base.OnStartClient();
            if (_fishingSystem == null) return;

            Debug.Log($"[NFC][Client] OnStartClient netId={netId} isOwned={isOwned} isServer={isServer}");
            _fishingSystem.enabled = false;
            _presenter = new FishingPresenter(_fishingSystem);

            // Cache rod references for equip/unequip
            var rod = _fishingSystem._fishingRod;
            if (rod != null) _rodGameObject = rod.gameObject;
            _handIK = GetComponentInChildren<FishingGameTool.Example.HandIK>(true);

            // Start with rod unequipped (hidden)
            ApplyRodVisuals(syncRodEquipped);

            // Initialize FishingUI for local player only
            if (isOwned)
            {
                var fishingUI = GetComponentInChildren<FishingUI>(true);
                if (fishingUI != null)
                {
                    fishingUI.Initialize(
                        this,
                        _fishingSystem._maxCastForce,
                        _fishingSystem._fishingRod._lineStatus._maxLineLoad,
                        _fishingSystem._fishingRod._lineStatus._overLoadDuration);
                    _fishingUI = fishingUI;
                }
            }
        }

        private void Update()
        {
            if (isOwned)
                HandleLocalInput();

            if (isServer && _stateMachine != null && _spawnedFloat != null)
                ServerTick();

            if (_presenter != null)
                ClientPresent();
        }

        // ══════════════════════════════════════════════════════════════
        //  Server tick
        // ══════════════════════════════════════════════════════════════

        private void ServerTick()
        {
            if (_spawnedFloat == null) return;

            var fishingFloat = _spawnedFloat.GetComponent<FishingFloat>();
            var rodTransform = _fishingSystem._fishingRod.transform;
            var ctx = new FloatContext
            {
                floatPosition = _spawnedFloat.transform.position,
                playerPosition = transform.position,
                rodForward = rodTransform != null ? rodTransform.forward : transform.forward,
                rodPosition = rodTransform != null ? rodTransform.position : transform.position,
                lineAttachmentPosition = _fishingSystem._fishingRod._line._lineAttachment != null
                    ? _fishingSystem._fishingRod._line._lineAttachment.position
                    : transform.position,
                substrate = fishingFloat.CheckSurface(_fishingSystem._fishingLayer),
                floatTransform = _spawnedFloat.transform,
                attractInput = syncAttractInput
            };

            _stateMachine.Tick(Time.deltaTime, ctx);

            // Periodic float diagnostic (every 0.5s)
            _floatDiagTimer -= Time.deltaTime;
            if (_floatDiagTimer <= 0f)
            {
                var rb = _spawnedFloat.GetComponent<Rigidbody>();
                Debug.Log($"[NFC][Server] FloatDiag pos={ctx.floatPosition:F2} sub={ctx.substrate} vel={rb?.linearVelocity:F2} state={syncState} landed={_stateMachine.HasLandedOnWater} netId={netId}");
                _floatDiagTimer = 0.5f;
            }

            // Provide loot list when entering Hooked (if not yet provided)
            if (_stateMachine.State == FishingState.Hooked && _stateMachine.CaughtLootData == null)
            {
                List<FishingLootData> lootList = fishingFloat.GetLootDataFormWaterObject();
                _stateMachine.ProvideLootList(lootList);
            }

            // Throttled line load sync
            ThrottledSyncLineStatus(_stateMachine.LineLoad, _stateMachine.OverLoad);
        }

        private void ThrottledSyncLineStatus(float lineLoad, float overLoad)
        {
            _syncTimer -= Time.deltaTime;
            bool forceSync = _syncTimer <= 0f;
            bool lineLoadChanged = Mathf.Abs(lineLoad - _lastSyncedLineLoad) > 0.5f;
            bool overLoadChanged = Mathf.Abs(overLoad - _lastSyncedOverLoad) > 0.1f;

            if (forceSync || lineLoadChanged || overLoadChanged)
            {
                syncLineLoad = lineLoad;
                syncOverLoad = overLoad;
                _lastSyncedLineLoad = lineLoad;
                _lastSyncedOverLoad = overLoad;
                _syncTimer = 0.15f;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  Client presentation
        // ══════════════════════════════════════════════════════════════

        private void ClientPresent()
        {
            // Visual/animation (all clients, all players)
            _presenter.Apply(
                syncState,
                syncAttractInput,
                ActiveFloatTransform);

            // UI (local player only)
            if (isOwned && _fishingUI != null)
                _fishingUI.UpdateUI(_isCharging, _currentCastForce);
        }

        // ══════════════════════════════════════════════════════════════
        //  Client input (authority only)
        // ══════════════════════════════════════════════════════════════

        private void HandleLocalInput()
        {
            if (_fishingSystem == null) return;

            // Block input when ESC menu is open
            if (_fishingUI != null && _fishingUI.IsMenuOpen) return;

            // ── Rod equip toggle (F key) ──
            if (Input.GetKeyDown(KeyCode.F) && syncState == FishingState.Idle)
            {
                CmdToggleRod();
                return; // consume this frame's input
            }

            // ── All fishing input requires rod to be equipped ──
            if (!syncRodEquipped) return;

            // ── Attract (Fire2) ──
            bool attracting = Input.GetButton("Fire2");
            if (attracting && !_wasAttracting) CmdStartAttract();
            else if (!attracting && _wasAttracting) CmdStopAttract();
            _wasAttracting = attracting;

            // ── Cast (Fire1) ──
            bool castInput = Input.GetButton("Fire1");
            bool canCast = syncState == FishingState.Idle
                        && !_isCharging
                        && !_castSent;

            if (castInput && canCast)
            {
                _isCharging = true;
            }

            if (_isCharging)
            {
                if (castInput)
                {
                    _currentCastForce += _fishingSystem._forceChargeRate * Time.deltaTime;
                    _currentCastForce = Mathf.Min(_currentCastForce, _fishingSystem._maxCastForce);
                }
                else
                {
                    // Released — send cast command
                    CmdCast(_currentCastForce, transform.forward + Vector3.up);
                    _currentCastForce = 0f;
                    _isCharging = false;
                    _castSent = true; // block further casts until state changes
                }
            }

            // Reset cast lock when server confirms state moved past Idle.
            // Two-phase: first detect state left Idle, then allow re-cast when back to Idle.
            if (_castSent && syncState != FishingState.Idle)
                _castSent = false;
        }

        // ══════════════════════════════════════════════════════════════
        //  Commands
        // ══════════════════════════════════════════════════════════════

        [Command]
        private void CmdToggleRod()
        {
            // Can only toggle when idle (not mid-fishing)
            if (_stateMachine != null && _stateMachine.State != FishingState.Idle) return;
            syncRodEquipped = !syncRodEquipped;
            Debug.Log($"[NFC][Server] RodEquipped={syncRodEquipped} netId={netId}");
        }

        [Command]
        private void CmdCast(float force, Vector3 direction)
        {
            Debug.Log($"[NFC][Server] CmdCast force={force:F2} dir={direction} netId={netId}");
            if (_stateMachine == null) return;
            float delay = _stateMachine.BeginCast();
            if (delay < 0f) return; // not in Idle
            StartCoroutine(ServerCastCoroutine(force, direction, delay));
        }

        private IEnumerator ServerCastCoroutine(float force, Vector3 direction, float delay)
        {
            syncState = FishingState.Casting;
            Debug.Log($"[NFC][Server] CastCoroutine start, delay={delay:F2} netId={netId}");

            yield return new WaitForSeconds(delay);

            // Spawn float
            Vector3 spawnPoint = _fishingSystem._fishingRod._line._lineAttachment.position;
            _spawnedFloat = Instantiate(_networkFloatPrefab, spawnPoint, Quaternion.identity);

            // Ensure Rigidbody is non-kinematic BEFORE applying force
            // (OnStartServer sets this too, but it fires during NetworkServer.Spawn which is after AddForce)
            var rb = _spawnedFloat.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.AddForce(direction * force, ForceMode.Impulse);
            }

            NetworkServer.Spawn(_spawnedFloat);
            Debug.Log($"[NFC][Server] Float spawned at {spawnPoint} force={force:F2} dir={direction} rb.isKinematic={rb?.isKinematic} netId={netId}");

            // Tell state machine float is ready
            _stateMachine.OnFloatSpawned();
            syncState = FishingState.Floating;

            // Tell all clients about the float
            RpcOnFloatSpawned(_spawnedFloat.GetComponent<NetworkIdentity>());
        }

        [Command]
        public void CmdStartAttract()
        {
            Debug.Log($"[NFC][Server] CmdStartAttract netId={netId}");
            syncAttractInput = true;
        }

        [Command]
        public void CmdStopAttract()
        {
            Debug.Log($"[NFC][Server] CmdStopAttract netId={netId}");
            syncAttractInput = false;
        }

        [Command]
        public void CmdForceStop()
        {
            Debug.Log($"[NFC][Server] CmdForceStop netId={netId}");
            if (_stateMachine == null) return;
            _stateMachine.ForceStop();
        }

        [Command]
        public void CmdFixLine()
        {
            Debug.Log($"[NFC][Server] CmdFixLine netId={netId}");
            if (_stateMachine == null) return;
            _stateMachine.FixLine();
            syncState = _stateMachine.State;
            RpcOnLineFixed();
        }

        // ══════════════════════════════════════════════════════════════
        //  Server callbacks (from FishingStateMachine)
        // ══════════════════════════════════════════════════════════════

        private void OnServerStateChanged(FishingState newState)
        {
            Debug.Log($"[NFC][Server] StateChanged → {newState} netId={netId}");
            syncState = newState;

            if (newState == FishingState.Idle)
            {
                syncAttractInput = false;
                syncLineLoad = 0f;
                syncOverLoad = 0f;
                syncLootName = null;
                syncLootTier = 0;
                syncLootDescription = null;
                _presenter?.ClearLootData();
            }
        }

        private void OnServerDestroyFloat()
        {
            Debug.Log($"[NFC][Server] DestroyFloat netId={netId} floatExists={_spawnedFloat != null}");
            if (_spawnedFloat != null)
            {
                NetworkServer.Destroy(_spawnedFloat);
                _spawnedFloat = null;
            }
            RpcOnFloatDestroyed();
        }

        private void OnServerLootSelected(FishingLootData lootData, float weight)
        {
            Debug.Log($"[NFC][Server] LootSelected: {lootData._lootName} tier={lootData._lootTier} weight={weight:F2} netId={netId}");
            syncLootName = lootData._lootName;
            syncLootTier = (int)lootData._lootTier;
            syncLootDescription = lootData._lootDescription;

            RpcOnLootSelected(lootData._lootName, (int)lootData._lootTier, lootData._lootDescription);
        }

        private void OnServerLootGrabbed(GameObject lootObj)
        {
            Debug.Log($"[NFC][Server] LootGrabbed: {lootObj.name} netId={netId}");
            // Loot is visual-only (fish flies out and lands). No network sync needed.
            // Just let it exist on server, auto-destroy after 30s.
            Destroy(lootObj, 30f);
        }

        private void OnServerLineBroken()
        {
            Debug.Log($"[NFC][Server] LineBroken netId={netId}");
            RpcOnLineBroken();
        }

        // ══════════════════════════════════════════════════════════════
        //  ClientRpcs
        // ══════════════════════════════════════════════════════════════

        [ClientRpc]
        private void RpcOnFloatSpawned(NetworkIdentity floatIdentity)
        {
            Debug.Log($"[NFC][Rpc] FloatSpawned floatNull={floatIdentity == null} isServer={isServer} isOwned={isOwned} netId={netId}");
            if (floatIdentity == null) return;
            ActiveFloatTransform = floatIdentity.transform;
        }

        [ClientRpc]
        private void RpcOnFloatDestroyed()
        {
            Debug.Log($"[NFC][Rpc] FloatDestroyed isServer={isServer} isOwned={isOwned} netId={netId}");
            ActiveFloatTransform = null;
            // Tell FishingRod to clean up line rendering
            if (_fishingSystem != null && _fishingSystem._fishingRod != null)
                _fishingSystem._fishingRod.FinishFishing();
        }

        [ClientRpc]
        private void RpcOnLootSelected(string lootName, int lootTier, string lootDescription)
        {
            Debug.Log($"[NFC][Rpc] LootSelected: {lootName} tier={lootTier} isServer={isServer} isOwned={isOwned} netId={netId}");
            _presenter?.ApplyLootData(lootName, lootTier, lootDescription);
        }

        [ClientRpc]
        private void RpcOnLineFixed()
        {
            Debug.Log($"[NFC][Rpc] LineFixed isServer={isServer} isOwned={isOwned} netId={netId}");
        }

        [ClientRpc]
        private void RpcOnLineBroken()
        {
            Debug.Log($"[NFC][Rpc] LineBroken isServer={isServer} isOwned={isOwned} netId={netId}");
        }

        // ══════════════════════════════════════════════════════════════
        //  Rod equip/unequip visuals
        // ══════════════════════════════════════════════════════════════

        private void OnRodEquippedChanged(bool oldVal, bool newVal)
        {
            ApplyRodVisuals(newVal);
        }

        private void ApplyRodVisuals(bool equipped)
        {
            // Skip on dedicated server (visuals already disabled)
            if (Application.isBatchMode) return;

            if (_rodGameObject != null)
                _rodGameObject.SetActive(equipped);

            if (_handIK != null)
                _handIK.enabled = equipped;
        }
    }
}
