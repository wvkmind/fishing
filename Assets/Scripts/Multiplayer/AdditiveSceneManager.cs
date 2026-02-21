using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MultiplayerFishing
{
    /// <summary>
    /// 服务器端 Additive Scene Manager。
    /// 玩家连接后在 LobbyScene，选择地图后进入对应游戏场景。
    /// 同场景玩家互相同步，不同场景互不影响。
    /// 配合 SceneInterestManagement 实现跨场景隔离。
    ///
    /// 关键时序（参考 Mirror 官方 MultipleAdditiveScenes 示例）：
    /// 1. 客户端发 EnterSceneMessage
    /// 2. 服务器加载场景（如果未加载），移动玩家 GameObject 到目标场景
    /// 3. 服务器发 LoadSceneMessage 给客户端
    /// 4. 客户端加载场景完成后发 SceneReadyMessage 回服务器
    /// 5. 服务器收到确认后 RebuildObservers → 触发 spawn 同步
    /// 这样保证客户端收到其他玩家的 spawn 消息时，场景已经加载好了。
    /// </summary>
    public class AdditiveSceneManager : MonoBehaviour
    {
        public static AdditiveSceneManager Instance { get; private set; }

        private readonly Dictionary<string, SceneInstance> _loadedScenes
            = new Dictionary<string, SceneInstance>();

        private readonly Dictionary<NetworkConnection, string> _playerSceneMap
            = new Dictionary<NetworkConnection, string>();

        // ── 网络消息 ──

        /// <summary>客户端 → 服务器：请求进入场景</summary>
        public struct EnterSceneMessage : NetworkMessage
        {
            public string sceneName;
        }

        /// <summary>服务器 → 客户端：加载场景</summary>
        public struct LoadSceneMessage : NetworkMessage
        {
            public string sceneName;
        }

        /// <summary>服务器 → 客户端：卸载场景</summary>
        public struct UnloadSceneMessage : NetworkMessage
        {
            public string sceneName;
        }

        /// <summary>客户端 → 服务器：请求离开场景回大厅</summary>
        public struct LeaveSceneMessage : NetworkMessage { }

        /// <summary>客户端 → 服务器：场景加载完成确认</summary>
        public struct SceneReadyMessage : NetworkMessage
        {
            public string sceneName;
        }

        // ── 生命周期 ──

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public void ServerSetup()
        {
            NetworkServer.RegisterHandler<EnterSceneMessage>(OnEnterSceneRequest);
            NetworkServer.RegisterHandler<LeaveSceneMessage>(OnLeaveSceneRequest);
            NetworkServer.RegisterHandler<SceneReadyMessage>(OnSceneReady);
            Debug.Log("[ASM] Server setup complete");
        }

        // ── 服务器消息处理 ──

        private void OnEnterSceneRequest(NetworkConnectionToClient conn, EnterSceneMessage msg)
        {
            if (!conn.isAuthenticated)
            {
                Debug.LogWarning($"[ASM] OnEnterSceneRequest: conn={conn} NOT authenticated, ignoring");
                return;
            }

            string sceneName = msg.sceneName;
            Debug.Log($"[ASM] OnEnterSceneRequest: conn={conn} netId={conn.identity?.netId} " +
                       $"requesting='{sceneName}' currentGOScene='{conn.identity?.gameObject.scene.name}'");

            if (_playerSceneMap.TryGetValue(conn, out string current) && current == sceneName)
            {
                Debug.Log($"[ASM] OnEnterSceneRequest: already mapped to '{sceneName}', skipping");
                return;
            }

            if (!string.IsNullOrEmpty(current))
            {
                Debug.Log($"[ASM] OnEnterSceneRequest: removing from old scene '{current}'");
                RemovePlayerFromScene(conn, current);
            }

            if (_loadedScenes.ContainsKey(sceneName))
            {
                Debug.Log($"[ASM] OnEnterSceneRequest: scene '{sceneName}' already loaded, moving player");
                MovePlayerToScene(conn, sceneName);
            }
            else
            {
                Debug.Log($"[ASM] OnEnterSceneRequest: scene '{sceneName}' not loaded, loading now...");
                StartCoroutine(LoadSceneAndMovePlayer(conn, sceneName));
            }
        }

        private void OnLeaveSceneRequest(NetworkConnectionToClient conn, LeaveSceneMessage msg)
        {
            if (!conn.isAuthenticated) return;

            if (_playerSceneMap.TryGetValue(conn, out string currentScene))
            {
                Debug.Log($"[ASM] Player {conn} leaving '{currentScene}' → lobby");
                RemovePlayerFromScene(conn, currentScene);

                var lobbyScene = SceneManager.GetSceneByName("LobbyScene");
                if (lobbyScene.IsValid() && conn.identity != null)
                {
                    SceneManager.MoveGameObjectToScene(conn.identity.gameObject, lobbyScene);
                    foreach (var owned in conn.owned)
                        if (owned != null && owned.gameObject.scene != lobbyScene)
                            SceneManager.MoveGameObjectToScene(owned.gameObject, lobbyScene);

                    NetworkServer.RebuildObservers(conn.identity, false);
                }

                conn.Send(new UnloadSceneMessage { sceneName = currentScene });
            }
        }

        /// <summary>
        /// 客户端场景加载完成后的确认。
        /// 此时才真正移动服务器端的 GameObject 到目标场景，
        /// SceneInterestManagement.LateUpdate 检测到 scene 变化后会自动 RebuildObservers，
        /// 此时客户端场景已就绪，spawn 消息到达后能正确显示。
        /// </summary>
        private void OnSceneReady(NetworkConnectionToClient conn, SceneReadyMessage msg)
        {
            var identity = conn.identity;
            if (identity == null)
            {
                Debug.LogError($"[ASM] OnSceneReady: conn={conn} has NO identity, aborting");
                return;
            }

            if (!_loadedScenes.TryGetValue(msg.sceneName, out var si))
            {
                Debug.LogError($"[ASM] OnSceneReady: scene '{msg.sceneName}' not in _loadedScenes");
                return;
            }

            Debug.Log($"[ASM] OnSceneReady: conn={conn} netId={identity.netId} sceneName='{msg.sceneName}' " +
                       $"currentScene='{identity.gameObject.scene.name}' targetScene='{si.scene.name}' " +
                       $"targetSceneValid={si.scene.IsValid()} playersInScene={si.players.Count}");

            // 现在才移动 GameObject 到目标场景
            SceneManager.MoveGameObjectToScene(identity.gameObject, si.scene);
            Debug.Log($"[ASM] OnSceneReady: MOVED netId={identity.netId} → scene='{identity.gameObject.scene.name}'");

            // 传送到出生点
            var spawnPos = FindSpawnInScene(si.scene);
            var cc = identity.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            identity.transform.position = spawnPos;
            if (cc != null) cc.enabled = true;
            Debug.Log($"[ASM] OnSceneReady: teleported netId={identity.netId} to {spawnPos}");

            // 移动玩家拥有的其他对象（浮标、鱼等）
            int movedOwned = 0;
            foreach (var owned in conn.owned)
            {
                if (owned != null && owned.gameObject.scene != si.scene)
                {
                    SceneManager.MoveGameObjectToScene(owned.gameObject, si.scene);
                    movedOwned++;
                }
            }
            Debug.Log($"[ASM] OnSceneReady: moved {movedOwned} owned objects to '{msg.sceneName}'");

            // 列出场景中所有 NetworkIdentity，方便排查
            int niCount = 0;
            foreach (var go in si.scene.GetRootGameObjects())
                foreach (var ni in go.GetComponentsInChildren<NetworkIdentity>(true))
                {
                    Debug.Log($"[ASM] OnSceneReady: sceneObj netId={ni.netId} name='{ni.name}' " +
                              $"isServer={ni.isServer} connToClient={ni.connectionToClient}");
                    niCount++;
                }
            Debug.Log($"[ASM] OnSceneReady: total NetworkIdentities in '{msg.sceneName}' = {niCount}");

            // RebuildObservers
            NetworkServer.RebuildObservers(identity, false);

            // 打印 rebuild 后的 observer 数量
            Debug.Log($"[ASM] OnSceneReady: RebuildObservers done for netId={identity.netId}, " +
                       $"observers={identity.observers?.Count ?? -1}");

            // 也 rebuild 场景中其他玩家的 observers，让他们能看到新来的人
            foreach (var go in si.scene.GetRootGameObjects())
            {
                foreach (var ni in go.GetComponentsInChildren<NetworkIdentity>(true))
                {
                    if (ni != identity && ni.isServer)
                    {
                        NetworkServer.RebuildObservers(ni, false);
                        Debug.Log($"[ASM] OnSceneReady: also rebuilt observers for netId={ni.netId} " +
                                  $"name='{ni.name}' observers={ni.observers?.Count ?? -1}");
                    }
                }
            }

            Debug.Log($"[ASM] OnSceneReady: COMPLETE — player netId={identity.netId} fully in '{msg.sceneName}'");
        }

        // ── 场景加载与玩家移动 ──

        private IEnumerator LoadSceneAndMovePlayer(NetworkConnectionToClient conn, string sceneName)
        {
            Debug.Log($"[ASM] Loading scene '{sceneName}' additively...");

            var asyncOp = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (asyncOp == null)
            {
                Debug.LogError($"[ASM] Failed to load scene '{sceneName}'");
                yield break;
            }

            while (!asyncOp.isDone)
                yield return null;

            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid())
            {
                Debug.LogError($"[ASM] Scene '{sceneName}' loaded but invalid");
                yield break;
            }

            _loadedScenes[sceneName] = new SceneInstance
            {
                scene = scene,
                players = new HashSet<NetworkConnection>()
            };

            Debug.Log($"[ASM] Scene '{sceneName}' loaded");

            // Rebuild 场景内已有的 NetworkIdentity（如 WaterZone 等场景物体）
            foreach (var go in scene.GetRootGameObjects())
                foreach (var ni in go.GetComponentsInChildren<NetworkIdentity>(true))
                    if (ni.isServer)
                        NetworkServer.RebuildObservers(ni, false);

            MovePlayerToScene(conn, sceneName);
        }

        /// <summary>
        /// 准备将玩家移入目标场景。
        /// 此时只更新映射、通知客户端加载场景，不移动 GameObject。
        /// 等客户端发 SceneReadyMessage 确认后才真正移动，
        /// 避免 SceneInterestManagement.LateUpdate 在客户端场景未就绪时触发 spawn。
        /// </summary>
        private void MovePlayerToScene(NetworkConnectionToClient conn, string sceneName)
        {
            if (!_loadedScenes.TryGetValue(sceneName, out var si))
            {
                Debug.LogError($"[ASM] MovePlayerToScene: scene '{sceneName}' not in _loadedScenes");
                return;
            }

            var identity = conn.identity;
            if (identity == null)
            {
                Debug.LogError($"[ASM] MovePlayerToScene: conn={conn} has no identity");
                return;
            }

            Debug.Log($"[ASM] MovePlayerToScene: conn={conn} netId={identity.netId} " +
                       $"currentScene='{identity.gameObject.scene.name}' → target='{sceneName}' " +
                       $"playersAlreadyInScene={si.players.Count}");

            // 先更新映射（但不移动 GameObject，避免触发 SceneInterestManagement）
            si.players.Add(conn);
            _playerSceneMap[conn] = sceneName;

            // 通知客户端加载场景（客户端加载完后会发 SceneReadyMessage 回来）
            conn.Send(new LoadSceneMessage { sceneName = sceneName });

            Debug.Log($"[ASM] MovePlayerToScene: sent LoadSceneMessage to conn={conn}, waiting for SceneReady...");
        }

        private void RemovePlayerFromScene(NetworkConnection conn, string sceneName)
        {
            if (!_loadedScenes.TryGetValue(sceneName, out var si))
                return;

            si.players.Remove(conn);
            _playerSceneMap.Remove(conn);

            Debug.Log($"[ASM] Player {conn} removed from '{sceneName}', remaining={si.players.Count}");

            if (si.players.Count == 0)
                StartCoroutine(UnloadEmptyScene(sceneName));
        }

        private IEnumerator UnloadEmptyScene(string sceneName)
        {
            yield return new WaitForSeconds(30f);

            if (_loadedScenes.TryGetValue(sceneName, out var si) && si.players.Count == 0)
            {
                Debug.Log($"[ASM] Unloading empty scene '{sceneName}'");

                foreach (var go in si.scene.GetRootGameObjects())
                    foreach (var ni in go.GetComponentsInChildren<NetworkIdentity>(true))
                        if (ni.isServer && ni.connectionToClient == null)
                            NetworkServer.Destroy(ni.gameObject);

                var op = SceneManager.UnloadSceneAsync(si.scene);
                if (op != null)
                    while (!op.isDone)
                        yield return null;

                _loadedScenes.Remove(sceneName);
                Debug.Log($"[ASM] Scene '{sceneName}' unloaded");
            }
        }

        // ── 公共接口 ──

        public void OnPlayerDisconnected(NetworkConnection conn)
        {
            if (_playerSceneMap.TryGetValue(conn, out string sceneName))
                RemovePlayerFromScene(conn, sceneName);
        }

        public int GetPlayerCount(string sceneName)
        {
            return _loadedScenes.TryGetValue(sceneName, out var si) ? si.players.Count : 0;
        }

        public string GetPlayerScene(NetworkConnection conn)
        {
            _playerSceneMap.TryGetValue(conn, out string sceneName);
            return sceneName;
        }

        // ── 内部类型 ──

        private class SceneInstance
        {
            public Scene scene;
            public HashSet<NetworkConnection> players;
        }

        private Vector3 FindSpawnInScene(Scene scene)
        {
            foreach (var rootGo in scene.GetRootGameObjects())
            {
                var nsp = rootGo.GetComponent<NetworkStartPosition>();
                if (nsp != null)
                    return nsp.transform.position;
            }
            return new Vector3(98.1f, 4.215f, 66.77f);
        }
    }
}
