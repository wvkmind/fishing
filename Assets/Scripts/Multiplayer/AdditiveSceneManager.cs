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
            if (!conn.isAuthenticated) return;

            string sceneName = msg.sceneName;
            Debug.Log($"[ASM] Player {conn} requesting '{sceneName}'");

            if (_playerSceneMap.TryGetValue(conn, out string current) && current == sceneName)
                return;

            if (!string.IsNullOrEmpty(current))
                RemovePlayerFromScene(conn, current);

            if (_loadedScenes.ContainsKey(sceneName))
                MovePlayerToScene(conn, sceneName);
            else
                StartCoroutine(LoadSceneAndMovePlayer(conn, sceneName));
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
        /// 客户端场景加载完成后的确认。此时才 RebuildObservers，
        /// 保证 spawn 消息到达时客户端场景已就绪。
        /// </summary>
        private void OnSceneReady(NetworkConnectionToClient conn, SceneReadyMessage msg)
        {
            if (conn.identity == null) return;

            Debug.Log($"[ASM] Player {conn} scene ready: '{msg.sceneName}'");

            // RebuildObservers 让 SceneInterestManagement 把同场景的玩家互相加入 observer
            NetworkServer.RebuildObservers(conn.identity, false);

            // 也 rebuild 同场景其他玩家，让他们能看到新来的人
            if (_loadedScenes.TryGetValue(msg.sceneName, out var si))
            {
                foreach (var other in si.players)
                {
                    if (other != conn && other.identity != null)
                        NetworkServer.RebuildObservers(other.identity, false);
                }
            }
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
        /// 服务器端移动玩家到目标场景。
        /// 注意：这里不调用 RebuildObservers，等客户端发 SceneReadyMessage 后再 rebuild。
        /// </summary>
        private void MovePlayerToScene(NetworkConnectionToClient conn, string sceneName)
        {
            if (!_loadedScenes.TryGetValue(sceneName, out var si))
            {
                Debug.LogError($"[ASM] Cannot move player: scene '{sceneName}' not loaded");
                return;
            }

            var identity = conn.identity;
            if (identity == null)
            {
                Debug.LogError($"[ASM] Cannot move player: no identity");
                return;
            }

            // 移动玩家到目标场景
            SceneManager.MoveGameObjectToScene(identity.gameObject, si.scene);

            // 传送到出生点
            var spawnPos = FindSpawnInScene(si.scene);
            var cc = identity.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            identity.transform.position = spawnPos;
            if (cc != null) cc.enabled = true;

            // 移动玩家拥有的其他对象（浮标、鱼等）
            foreach (var owned in conn.owned)
                if (owned != null && owned.gameObject.scene != si.scene)
                    SceneManager.MoveGameObjectToScene(owned.gameObject, si.scene);

            // 更新映射
            si.players.Add(conn);
            _playerSceneMap[conn] = sceneName;

            // 通知客户端加载场景（客户端加载完后会发 SceneReadyMessage 回来）
            conn.Send(new LoadSceneMessage { sceneName = sceneName });

            Debug.Log($"[ASM] Player {conn} moved to '{sceneName}', count={si.players.Count}");
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
