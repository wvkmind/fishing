using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Mirror;
using kcp2k;
using FishingGameTool.Fishing;
using FishingGameTool.Fishing.Rod;
using FishingGameTool.Fishing.Float;
using FishingGameTool.Example;
using MultiplayerFishing;

/// <summary>
/// Multiplayer setup. Run steps in order with DemoScene open.
/// Step 1: Create network float prefab
/// Step 2: Create player prefab (from scene character, reparents TPP Camera + UI)
/// Step 3: Create Lobby + Game scenes
/// </summary>
public static class MultiplayerSetupEditor
{
    private const string NetworkFloatPrefabPath = "Assets/Prefabs/Multiplayer/NetworkFishingFloat.prefab";
    private const string PlayerPrefabPath = "Assets/Prefabs/Multiplayer/PlayerPrefab.prefab";
    private const string LobbyScenePath = "Assets/Scenes/LobbyScene.unity";
    private const string GameScenePath = "Assets/Scenes/GameScene.unity";

    [MenuItem("Tools/Multiplayer Fishing Setup/1. Create Network Float Prefab")]
    public static void CreateNetworkFloatPrefab()
    {
        var orig = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/FishingGameTool/Example/Prefabs/FishingFloat/FishingFloat.prefab");
        if (orig == null) { Debug.LogError("FishingFloat.prefab not found."); return; }

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(orig);
        PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        if (!inst.GetComponent<NetworkIdentity>()) inst.AddComponent<NetworkIdentity>();

        // Float is server-authoritative (server runs physics), so NetworkTransform
        // must sync ServerToClient. Also use Reliable for smooth movement.
        var existingNT = inst.GetComponent<NetworkTransformUnreliable>();
        if (existingNT != null) Object.DestroyImmediate(existingNT);
        var floatNT = inst.GetComponent<NetworkTransformReliable>();
        if (floatNT == null) floatNT = inst.AddComponent<NetworkTransformReliable>();
        floatNT.syncDirection = SyncDirection.ServerToClient;

        if (!inst.GetComponent<NetworkFishingFloat>()) inst.AddComponent<NetworkFishingFloat>();

        EnsureDir("Assets/Prefabs/Multiplayer");
        PrefabUtility.SaveAsPrefabAsset(inst, NetworkFloatPrefabPath);
        Object.DestroyImmediate(inst);
        Debug.Log("[OK] Network Float prefab created.");
    }

    [MenuItem("Tools/Multiplayer Fishing Setup/2. Create Player Prefab (DemoScene must be open)")]
    public static void CreatePlayerPrefab()
    {
        var charMove = Object.FindAnyObjectByType<CharacterMovement>();
        if (charMove == null) { Debug.LogError("Open DemoScene first!"); return; }

        var playerGo = charMove.gameObject;
        if (PrefabUtility.IsPartOfAnyPrefab(playerGo))
            PrefabUtility.UnpackPrefabInstance(playerGo, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        // --- Reparent scene objects under player so they're included in the prefab ---

        // TPP Camera (root scene object → child of player)
        var tppCam = Object.FindAnyObjectByType<TPPCamera>();
        if (tppCam != null && tppCam.transform.root != playerGo.transform)
            tppCam.transform.SetParent(playerGo.transform, true);

        // SimpleUIManager / Canvas (root scene object → child of player)
        var uiMgr = Object.FindAnyObjectByType<SimpleUIManager>();
        if (uiMgr != null)
        {
            var uiRoot = uiMgr.transform.root;
            if (uiRoot != playerGo.transform)
                uiRoot.SetParent(playerGo.transform, true);
        }

        // InteractionMark
        var interSys = playerGo.GetComponent<InteractionSystem>();
        if (interSys != null && interSys._interactionMark != null)
        {
            var markRoot = interSys._interactionMark.transform.root;
            if (markRoot != playerGo.transform)
                markRoot.SetParent(playerGo.transform, true);
        }

        // --- Add network components ---
        AddNetworkComponents(playerGo);

        // --- Save as prefab ---
        EnsureDir("Assets/Prefabs/Multiplayer");
        PrefabUtility.SaveAsPrefabAsset(playerGo, PlayerPrefabPath);

        Debug.Log("[OK] Player prefab created. Do NOT save DemoScene (Ctrl+Z or reload).");
    }

    [MenuItem("Tools/Multiplayer Fishing Setup/3. Create Lobby and Game Scenes")]
    public static void CreateScenes()
    {
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (playerPrefab == null) { Debug.LogError("Run Step 2 first!"); return; }

        // --- Game Scene ---
        EditorSceneManager.OpenScene("Assets/FishingGameTool/Example/Scenes/DemoScene.unity", OpenSceneMode.Single);

        // Record position before destroying
        var charMove = Object.FindAnyObjectByType<CharacterMovement>();
        Vector3 spawnPos = Vector3.zero;
        Quaternion spawnRot = Quaternion.identity;

        if (charMove != null)
        {
            spawnPos = charMove.transform.position + Vector3.up * 0.5f;
            spawnRot = charMove.transform.rotation;

            // Remove TPP Camera if separate
            var tppCam = Object.FindAnyObjectByType<TPPCamera>();
            if (tppCam != null && tppCam.transform.root != charMove.transform)
                Object.DestroyImmediate(tppCam.gameObject);

            // Remove UI Canvas if separate
            var uiMgr = Object.FindAnyObjectByType<SimpleUIManager>();
            if (uiMgr != null && uiMgr.transform.root != charMove.transform)
                Object.DestroyImmediate(uiMgr.transform.root.gameObject);

            // Remove InteractionMark if separate
            var interSys = charMove.GetComponent<InteractionSystem>();
            if (interSys != null && interSys._interactionMark != null)
            {
                var markRoot = interSys._interactionMark.transform.root;
                if (markRoot != charMove.transform)
                    Object.DestroyImmediate(markRoot.gameObject);
            }

            Object.DestroyImmediate(charMove.gameObject);
        }

        // Spawn point
        var spawnGo = new GameObject("SpawnPoint");
        spawnGo.transform.position = spawnPos;
        spawnGo.transform.rotation = spawnRot;
        spawnGo.AddComponent<NetworkStartPosition>();

        // ItemInfoBinder to rebind ItemInfo._characterMovement at runtime
        new GameObject("ItemInfoBinder").AddComponent<ItemInfoBinder>();

        EnsureDir("Assets/Scenes");
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), GameScenePath);
        Debug.Log($"  Game scene: {GameScenePath}");

        // --- Lobby Scene ---
        var lobbyScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var nmGo = new GameObject("NetworkManager");
        var nm = nmGo.AddComponent<NetworkManager>();
        nmGo.AddComponent<HeadlessAutoStart>();
        nmGo.AddComponent<LobbyUI>();
        var transport = nmGo.AddComponent<KcpTransport>();
        nm.transport = transport;
        nm.playerPrefab = playerPrefab;
        nm.offlineScene = LobbyScenePath;
        nm.onlineScene = GameScenePath;

        var netFloat = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkFloatPrefabPath);
        if (netFloat != null) nm.spawnPrefabs.Add(netFloat);

        // Screen logger + frame limiter (DontDestroyOnLoad, survives scene transitions)
        var loggerGo = new GameObject("ScreenLogger");
        loggerGo.AddComponent<ScreenLogger>();
        loggerGo.AddComponent<ServerFrameLimiter>();

        EditorSceneManager.SaveScene(lobbyScene, LobbyScenePath);
        Debug.Log($"  Lobby scene: {LobbyScenePath}");

        // Add to Build Settings
        AddToBuildSettings(LobbyScenePath, GameScenePath);
        Debug.Log("[OK] All done. Open LobbyScene and press Play!");
    }

    [MenuItem("Tools/Multiplayer Fishing Setup/4. Fix Existing Prefabs (NetworkTransform upgrade)")]
    public static void FixExistingPrefabs()
    {
        int fixes = 0;

        // Fix NetworkFishingFloat prefab: Unreliable → Reliable, syncDirection = ServerToClient
        var floatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkFloatPrefabPath);
        if (floatPrefab != null)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(floatPrefab);
            bool changed = false;

            var oldNT = inst.GetComponent<NetworkTransformUnreliable>();
            if (oldNT != null) { Object.DestroyImmediate(oldNT); changed = true; }

            var reliableNT = inst.GetComponent<NetworkTransformReliable>();
            if (reliableNT == null) { reliableNT = inst.AddComponent<NetworkTransformReliable>(); changed = true; }
            if (reliableNT.syncDirection != SyncDirection.ServerToClient)
            { reliableNT.syncDirection = SyncDirection.ServerToClient; changed = true; }

            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(inst, NetworkFloatPrefabPath);
                Debug.Log("[Fix] Float prefab: switched to NetworkTransformReliable (ServerToClient)");
                fixes++;
            }
            Object.DestroyImmediate(inst);
        }

        // Fix PlayerPrefab: Unreliable → Reliable, add FishingUI, disable SimpleUIManager
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (playerPrefab != null)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            bool changed = false;

            var oldNT = inst.GetComponent<NetworkTransformUnreliable>();
            if (oldNT != null) { Object.DestroyImmediate(oldNT); changed = true; }

            var reliableNT = inst.GetComponent<NetworkTransformReliable>();
            if (reliableNT == null) { reliableNT = inst.AddComponent<NetworkTransformReliable>(); changed = true; }

            // Add FishingUI if missing
            if (inst.GetComponent<FishingUI>() == null)
            {
                inst.AddComponent<FishingUI>();
                Debug.Log("[Fix] Player prefab: added FishingUI component");
                changed = true;
            }

            // Fix NetworkAnimator (must be ClientToServer + clientAuthority for player anims)
            var netAnim = inst.GetComponent<NetworkAnimator>();
            if (netAnim != null)
            {
                if (netAnim.syncDirection != SyncDirection.ClientToServer)
                {
                    netAnim.syncDirection = SyncDirection.ClientToServer;
                    Debug.Log("[Fix] Player prefab: NetworkAnimator syncDirection → ClientToServer");
                    changed = true;
                }
                if (!netAnim.clientAuthority)
                {
                    netAnim.clientAuthority = true;
                    Debug.Log("[Fix] Player prefab: NetworkAnimator clientAuthority → true");
                    changed = true;
                }
            }

            // Disable SimpleUIManager (replaced by FishingUI)
            var simpleUI = inst.GetComponentInChildren<SimpleUIManager>(true);
            if (simpleUI != null && simpleUI.enabled)
            {
                simpleUI.enabled = false;
                Debug.Log("[Fix] Player prefab: disabled SimpleUIManager");
                changed = true;
            }

            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(inst, PlayerPrefabPath);
                Debug.Log("[Fix] Player prefab: updated");
                fixes++;
            }
            Object.DestroyImmediate(inst);
        }

        if (fixes == 0)
            Debug.Log("[Fix] No changes needed — prefabs already up to date.");
        else
            Debug.Log($"[OK] Fixed {fixes} prefab(s). Rebuild your client.");
    }

    [MenuItem("Tools/Multiplayer Fishing Setup/5. Fix Lobby Scene (LobbyUI)")]
    public static void FixLobbyScene()
    {
        // Open LobbyScene
        var scene = EditorSceneManager.OpenScene(LobbyScenePath, OpenSceneMode.Single);

        var nm = Object.FindAnyObjectByType<NetworkManager>();
        if (nm == null) { Debug.LogError("No NetworkManager in LobbyScene!"); return; }

        bool changed = false;

        // Remove NetworkManagerHUD (replaced by LobbyUI)
        var hud = nm.GetComponent<NetworkManagerHUD>();
        if (hud != null) { Object.DestroyImmediate(hud); changed = true; Debug.Log("[Fix] Removed NetworkManagerHUD"); }

        // Add LobbyUI if missing
        if (nm.GetComponent<LobbyUI>() == null)
        { nm.gameObject.AddComponent<LobbyUI>(); changed = true; Debug.Log("[Fix] Added LobbyUI"); }

        if (changed)
        {
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[OK] LobbyScene updated. Rebuild client.");
        }
        else
        {
            Debug.Log("[Fix] LobbyScene already up to date.");
        }
    }

    // ─────────────── Helpers ───────────────

    private static void AddNetworkComponents(GameObject go)
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

        if (!go.GetComponent<NetworkIdentity>()) go.AddComponent<NetworkIdentity>();

        // Use NetworkTransformReliable for player — CharacterController movement
        // needs reliable sync to avoid teleporting/snapping issues.
        var existingNTU = go.GetComponent<NetworkTransformUnreliable>();
        if (existingNTU != null) Object.DestroyImmediate(existingNTU);
        if (!go.GetComponent<NetworkTransformReliable>()) go.AddComponent<NetworkTransformReliable>();

        var netAnim = go.GetComponent<NetworkAnimator>();
        if (!netAnim) netAnim = go.AddComponent<NetworkAnimator>();
        var anim = go.GetComponent<Animator>();
        if (anim) netAnim.animator = anim;
        // Player animations are driven by owning client (CharacterMovement sets Walk param)
        netAnim.syncDirection = SyncDirection.ClientToServer;
        netAnim.clientAuthority = true;

        var setup = go.GetComponent<NetworkPlayerSetup>();
        if (!setup) setup = go.AddComponent<NetworkPlayerSetup>();
        WireSetup(go, setup, flags);

        var fc = go.GetComponent<NetworkFishingController>();
        if (!fc) fc = go.AddComponent<NetworkFishingController>();
        var fs = go.GetComponent<FishingSystem>();
        if (fs) typeof(NetworkFishingController).GetField("_fishingSystem", flags)?.SetValue(fc, fs);
        var nfp = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkFloatPrefabPath);
        if (nfp) typeof(NetworkFishingController).GetField("_networkFloatPrefab", flags)?.SetValue(fc, nfp);

        // FishingUI — replaces SimpleUIManager for multiplayer
        if (!go.GetComponent<FishingUI>()) go.AddComponent<FishingUI>();

        // Disable SimpleUIManager if present (FishingUI replaces it)
        var simpleUI = go.GetComponentInChildren<SimpleUIManager>(true);
        if (simpleUI != null) simpleUI.enabled = false;

        var rod = go.GetComponentInChildren<FishingRod>();
        if (rod)
        {
            var nr = rod.GetComponent<NetworkFishingRod>();
            if (!nr) nr = rod.gameObject.AddComponent<NetworkFishingRod>();
            typeof(NetworkFishingRod).GetField("_fishingRod", flags)?.SetValue(nr, rod);
            typeof(NetworkFishingRod).GetField("_networkFishingController", flags)?.SetValue(nr, fc);
        }
    }

    private static void WireSetup(GameObject go, NetworkPlayerSetup setup, System.Reflection.BindingFlags flags)
    {
        var t = typeof(NetworkPlayerSetup);
        t.GetField("_characterMovement", flags)?.SetValue(setup, go.GetComponent<CharacterMovement>());
        t.GetField("_interactionSystem", flags)?.SetValue(setup, go.GetComponent<InteractionSystem>());
        t.GetField("_simpleUIManager", flags)?.SetValue(setup, go.GetComponentInChildren<SimpleUIManager>(true));
        t.GetField("_audioListener", flags)?.SetValue(setup, go.GetComponentInChildren<AudioListener>(true));
        foreach (var cam in go.GetComponentsInChildren<Camera>(true))
        {
            if (cam.GetComponent<TPPCamera>()) t.GetField("_tppCamera", flags)?.SetValue(setup, cam);
            else if (cam.GetComponent<FPPCameraSystem>()) t.GetField("_fppCamera", flags)?.SetValue(setup, cam);
        }
    }

    private static void AddToBuildSettings(params string[] paths)
    {
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (var p in paths)
        {
            bool exists = false;
            foreach (var s in list) if (s.path == p) { exists = true; break; }
            if (!exists) list.Add(new EditorBuildSettingsScene(p, true));
        }
        EditorBuildSettings.scenes = list.ToArray();
    }

    private static void EnsureDir(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parts = path.Split('/');
        var cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            var next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}
