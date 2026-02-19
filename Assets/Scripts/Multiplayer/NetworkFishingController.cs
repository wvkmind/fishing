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
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public class NetworkFishingController : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private FishingSystem _fishingSystem;

        [Header("Network Float Prefab")]
        [SerializeField] private GameObject _networkFloatPrefab;

        [Header("Fish Display")]
        [SerializeField] private FishDatabase _fishDatabase;

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
        private GameObject _serverDroppedFish; // fish on the ground (server-spawned NetworkObject)

        // Throttled sync
        private float _lastSyncedLineLoad;
        private float _lastSyncedOverLoad;
        private float _syncTimer;
        private float _floatDiagTimer;

        // ── All clients ──
        private FishingPresenter _presenter;
        private FishingUI _fishingUI;

        // ── Authority client input ──
        private bool _isCharging;
        private float _currentCastForce;
        private bool _wasAttracting;
        private bool _castSent;

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
            _stateMachine.OnStateChanged = OnServerStateChanged;
            _stateMachine.OnRequestDestroyFloat = OnServerDestroyFloat;
            _stateMachine.OnLootSelected = OnServerLootSelected;
            _stateMachine.OnLootGrabbed = OnServerLootGrabbed;
            _stateMachine.OnLineBroken = OnServerLineBroken;

            _fishingSystem._fishingRod.OnLineBreak = () => _stateMachine.ForceStop();

            if (Application.isBatchMode)
                DisableServerVisuals();
        }

        private void DisableServerVisuals()
        {
            var rod = _fishingSystem._fishingRod;
            if (rod != null) rod.enabled = false;

            var handIK = GetComponentInChildren<FishingGameTool.Example.HandIK>(true);
            if (handIK != null) handIK.enabled = false;

            var tppCam = GetComponentInChildren<FishingGameTool.Example.TPPCamera>(true);
            if (tppCam != null) tppCam.enabled = false;

            var lineRenderers = GetComponentsInChildren<LineRenderer>(true);
            foreach (var lr in lineRenderers) lr.enabled = false;

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

            var rod = _fishingSystem._fishingRod;
            if (rod != null)
                _rodGameObject = rod.gameObject;
            _handIK = GetComponentInChildren<FishingGameTool.Example.HandIK>(true);

            ApplyRodVisuals(syncRodEquipped);

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

            if (_spawnedFloat == null) return;

            _floatDiagTimer -= Time.deltaTime;
            if (_floatDiagTimer <= 0f)
            {
                var rb = _spawnedFloat.GetComponent<Rigidbody>();
                Debug.Log($"[NFC][Server] FloatDiag pos={ctx.floatPosition:F2} sub={ctx.substrate} vel={rb?.linearVelocity:F2} state={syncState} landed={_stateMachine.HasLandedOnWater} netId={netId}");
                _floatDiagTimer = 0.5f;
            }

            if (_stateMachine.State == FishingState.Hooked && _stateMachine.CaughtLootData == null)
            {
                List<FishingLootData> lootList = fishingFloat.GetLootDataFormWaterObject();
                _stateMachine.ProvideLootList(lootList);
            }

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
            _presenter.Apply(syncState, syncAttractInput, ActiveFloatTransform);

            if (isOwned && _fishingUI != null)
                _fishingUI.UpdateUI(_isCharging, _currentCastForce);
        }

        // ══════════════════════════════════════════════════════════════
        //  Client input (authority only)
        // ══════════════════════════════════════════════════════════════

        private void HandleLocalInput()
        {
            if (_fishingSystem == null) return;
            if (_fishingUI != null && _fishingUI.IsMenuOpen) return;

            // ── Rod equip toggle (F key) — works in any state ──
            if (Input.GetKeyDown(KeyCode.F))
            {
                if (syncState == FishingState.Idle || syncState == FishingState.Displaying)
                {
                    CmdToggleRod();
                    return;
                }
            }

            // ── Pick up fish on ground (E key) ──
            if (Input.GetKeyDown(KeyCode.E))
            {
                if (syncState == FishingState.Displaying)
                {
                    CmdPickupFish();
                    return;
                }
            }

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
                _isCharging = true;

            if (_isCharging)
            {
                if (castInput)
                {
                    _currentCastForce += _fishingSystem._forceChargeRate * Time.deltaTime;
                    _currentCastForce = Mathf.Min(_currentCastForce, _fishingSystem._maxCastForce);
                }
                else
                {
                    CmdCast(_currentCastForce, transform.forward + Vector3.up);
                    _currentCastForce = 0f;
                    _isCharging = false;
                    _castSent = true;
                }
            }

            if (_castSent && syncState != FishingState.Idle)
                _castSent = false;
        }

        // ══════════════════════════════════════════════════════════════
        //  Commands
        // ══════════════════════════════════════════════════════════════

        [Command]
        private void CmdToggleRod()
        {
            if (_stateMachine != null
                && _stateMachine.State != FishingState.Idle
                && _stateMachine.State != FishingState.Displaying) return;
            syncRodEquipped = !syncRodEquipped;
            Debug.Log($"[NFC][Server] RodEquipped={syncRodEquipped} netId={netId}");
        }

        [Command]
        private void CmdCast(float force, Vector3 direction)
        {
            Debug.Log($"[NFC][Server] CmdCast force={force:F2} dir={direction} netId={netId}");
            if (_stateMachine == null) return;
            float delay = _stateMachine.BeginCast();
            if (delay < 0f) return;
            StartCoroutine(ServerCastCoroutine(force, direction, delay));
        }

        private IEnumerator ServerCastCoroutine(float force, Vector3 direction, float delay)
        {
            syncState = FishingState.Casting;
            Debug.Log($"[NFC][Server] CastCoroutine start, delay={delay:F2} netId={netId}");

            yield return new WaitForSeconds(delay);

            Vector3 spawnPoint = _fishingSystem._fishingRod._line._lineAttachment.position;
            _spawnedFloat = Instantiate(_networkFloatPrefab, spawnPoint, Quaternion.identity);

            var rb = _spawnedFloat.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.AddForce(direction * force, ForceMode.Impulse);
            }

            NetworkServer.Spawn(_spawnedFloat);
            Debug.Log($"[NFC][Server] Float spawned at {spawnPoint} force={force:F2} netId={netId}");

            _stateMachine.OnFloatSpawned();
            syncState = FishingState.Floating;
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

        [Command]
        private void CmdPickupFish()
        {
            Debug.Log($"[NFC][Server] CmdPickupFish netId={netId}");
            if (_stateMachine == null || _stateMachine.State != FishingState.Displaying) return;

            if (_serverDroppedFish != null)
            {
                NetworkServer.Destroy(_serverDroppedFish);
                _serverDroppedFish = null;
            }

            RpcCleanupFishHold();

            // Return to idle
            syncState = FishingState.Idle;
            syncLootName = null;
            syncLootTier = 0;
            syncLootDescription = null;
            _stateMachine.DismissDisplay();
            _presenter?.ClearLootData();

            Debug.Log($"[NFC][Server] Fish picked up, back to Idle netId={netId}");
        }

        [ClientRpc]
        private void RpcCleanupFishHold()
        {
            if (Application.isBatchMode) return;
            var hold = gameObject.GetComponent<DisplayFishHold>();
            if (hold != null)
            {
                hold.Cleanup();
                Destroy(hold);
            }
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
            Debug.Log($"[NFC][Server] LootGrabbed: syncLootName={syncLootName} fishDb={_fishDatabase != null} playerPos={transform.position} netId={netId}");
            if (lootObj != null)
                Destroy(lootObj, 0.1f);
            ServerSpawnHeldFish();
        }

        private void ServerSpawnHeldFish()
        {
            if (_fishDatabase == null) return;

            string lootName = syncLootName;
            var prefab = _fishDatabase.GetNetworkPrefab(lootName);
            if (prefab == null)
            {
                Debug.LogWarning($"[NFC][Server] No prefab for '{lootName}', skipping netId={netId}");
                return;
            }

            // Spawn at player position (will be re-parented to hand on clients)
            _serverDroppedFish = Instantiate(prefab, transform.position, Quaternion.identity);

            // Remove physics — fish is held, not dropped
            var rb = _serverDroppedFish.GetComponent<Rigidbody>();
            if (rb != null) Object.Destroy(rb);
            var col = _serverDroppedFish.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            NetworkServer.Spawn(_serverDroppedFish);
            RpcAttachFishToHand(_serverDroppedFish.GetComponent<NetworkIdentity>());

            // Enter Displaying state
            syncState = FishingState.Displaying;
            _stateMachine?.SetDisplaying();

            Debug.Log($"[NFC][Server] Fish '{lootName}' held by player netId={netId}");
        }

        [ClientRpc]
        private void RpcAttachFishToHand(NetworkIdentity fishIdentity)
        {
            if (Application.isBatchMode) return;
            Debug.Log($"[NFC][Client] RpcAttachFishToHand fishNull={fishIdentity == null} netId={netId}");
            if (fishIdentity == null) return;

            // Disable NetworkTransform on fish so LateUpdate can control position
            var nt = fishIdentity.GetComponent<Mirror.NetworkTransformUnreliable>();
            if (nt != null) nt.enabled = false;
            var ntBase = fishIdentity.GetComponent<Mirror.NetworkTransformBase>();
            if (ntBase != null) ntBase.enabled = false;

            var hold = gameObject.GetComponent<DisplayFishHold>();
            if (hold == null)
                hold = gameObject.AddComponent<DisplayFishHold>();
            hold.Setup(fishIdentity.gameObject);
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
            if (Application.isBatchMode) return;

            if (_rodGameObject != null)
                _rodGameObject.SetActive(equipped);

            if (_handIK != null)
                _handIK.enabled = equipped;
        }
    }
}
