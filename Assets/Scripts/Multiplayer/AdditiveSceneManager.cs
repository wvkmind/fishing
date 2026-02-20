using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MultiplayerFishing
{
    /// <summary>
    /// 服务器端 Additive Scene Manager。
    /// 管理多个游戏场景的动态加载/卸载，以及玩家在场景间的移动。
    /// 服务器始终保持 LobbyScene 作为基础场景，游戏地图以 Additive 方式加载。
    /// 配合 SceneInterestManagement 实现跨场景玩家隔离。
    /// </summary>
    public class AdditiveSceneManager : MonoBehaviour
    {
        /// <summary>
        /// 单例引用，供其他组件访问。
        /// </summary>
        public static AdditiveSceneManager Instance { get; private set; }

        /// <summary>
        /// 已加载的游戏场景及其玩家列表。
        /// Key = sceneName, Value = (Scene handle, 玩家连接集合)
        /// </summary>
        private readonly Dictionary<string, SceneInstance> _loadedScenes
            = new Dictionary<string, SceneInstance>();

        /// <summary>
        /// 玩家当前所在的场景名。Key = NetworkConnection
        /// </summary>
        private readonly Dictionary<NetworkConnection, string> _playerSceneMap
            = new Dictionary<NetworkConnection, string>();

        /// <summary>
        /// 客户端请求进入场景的网络消息。
        /// </summary>
        public struct EnterSceneMessage : NetworkMessage
        {
            public string sceneName;
        }

        /// <summary>
        /// 服务器通知客户端加载场景的网络消息。
        /// </summary>
        public struct LoadSceneMessage : NetworkMessage
        {
            public string sceneName;
        }

        /// <summary>
        /// 服务器通知客户端卸载场景的网络消息。
        /// </summary>
        public struct UnloadSceneMessage : NetworkMessage
        {
            public string sceneName;
        }

        /// <summary>
        /// 客户端请求离开当前场景（回大厅），不断开连接。
        /// </summary>
        public struct LeaveSceneMessage : NetworkMessage { }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        /// <summary>
        /// 服务器启动时注册消息处理器。由 HeadlessAutoStart 调用。
        /// </summary>
        public void ServerSetup()
        {
            NetworkServer.RegisterHandler<EnterSceneMessage>(OnEnterSceneRequest);
            NetworkServer.RegisterHandler<LeaveSceneMessage>(OnLeaveSceneRequest);
            Debug.Log("[AdditiveSceneManager] Server setup complete, handler registered");
        }

        /// <summary>
        /// 处理客户端的进入场景请求。
        /// 如果场景未加载则先加载，然后将玩家移入。
        /// </summary>
        private void OnEnterSceneRequest(NetworkConnectionToClient conn, EnterSceneMessage msg)
        {
            if (!conn.isAuthenticated)
            {
                Debug.LogWarning($"[AdditiveSceneManager] Unauthenticated request from {conn}, ignoring");
                return;
            }

            string sceneName = msg.sceneName;
            Debug.Log($"[AdditiveSceneManager] Player {conn} requesting scene '{sceneName}'");

            // 如果玩家已在目标场景，忽略
            if (_playerSceneMap.TryGetValue(conn, out string currentScene) && currentScene == sceneName)
            {
                Debug.Log($"[AdditiveSceneManager] Player already in '{sceneName}', ignoring");
                return;
            }

            // 先从当前场景移出（如果有的话）
            if (!string.IsNullOrEmpty(currentScene))
                RemovePlayerFromScene(conn, currentScene);

            // 加载目标场景（如果未加载）并移入玩家
            if (_loadedScenes.ContainsKey(sceneName))
            {
                MovePlayerToScene(conn, sceneName);
            }
            else
            {
                StartCoroutine(LoadSceneAndMovePlayer(conn, sceneName));
            }
        }

        /// <summary>
        /// 处理客户端的离开场景请求（回大厅）。
        /// 将玩家从当前地图场景移回 LobbyScene，不断开连接。
        /// </summary>
        private void OnLeaveSceneRequest(NetworkConnectionToClient conn, LeaveSceneMessage msg)
        {
            if (!conn.isAuthenticated) return;

            if (_playerSceneMap.TryGetValue(conn, out string currentScene))
            {
                Debug.Log($"[AdditiveSceneManager] Player {conn} leaving '{currentScene}' → lobby");
                RemovePlayerFromScene(conn, currentScene);

                // 将玩家移回 LobbyScene
                var lobbyScene = SceneManager.GetSceneByName("LobbyScene");
                if (lobbyScene.IsValid() && conn.identity != null)
                {
                    SceneManager.MoveGameObjectToScene(conn.identity.gameObject, lobbyScene);
                    foreach (var owned in conn.owned)
                    {
                        if (owned != null && owned.gameObject.scene != lobbyScene)
                            SceneManager.MoveGameObjectToScene(owned.gameObject, lobbyScene);
                    }
                    NetworkServer.RebuildObservers(conn.identity, false);
                }

                conn.Send(new UnloadSceneMessage { sceneName = currentScene });
            }
        }

        /// <summary>
        /// 异步加载场景（Additive），然后将玩家移入。
        /// </summary>
        private IEnumerator LoadSceneAndMovePlayer(NetworkConnectionToClient conn, string sceneName)
        {
            Debug.Log($"[AdditiveSceneManager] Loading scene '{sceneName}' additively...");

            var asyncOp = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (asyncOp == null)
            {
                Debug.LogError($"[AdditiveSceneManager] Failed to load scene '{sceneName}'");
                yield break;
            }

            while (!asyncOp.isDone)
                yield return null;

            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid())
            {
                Debug.LogError($"[AdditiveSceneManager] Scene '{sceneName}' loaded but invalid");
                yield break;
            }

            _loadedScenes[sceneName] = new SceneInstance
            {
                scene = scene,
                players = new HashSet<NetworkConnection>()
            };

            Debug.Log($"[AdditiveSceneManager] Scene '{sceneName}' loaded successfully");

            // 将场景中已有的 NetworkIdentity 对象重建 observers
            // （场景物体如 WaterZone 等需要对进入的玩家可见）
            foreach (var go in scene.GetRootGameObjects())
            {
                var identities = go.GetComponentsInChildren<NetworkIdentity>(true);
                foreach (var ni in identities)
                {
                    if (ni.isServer)
                        NetworkServer.RebuildObservers(ni, false);
                }
            }

            MovePlayerToScene(conn, sceneName);
        }

        /// <summary>
        /// 将玩家的 NetworkIdentity 移动到目标场景。
        /// </summary>
        private void MovePlayerToScene(NetworkConnectionToClient conn, string sceneName)
        {
            if (!_loadedScenes.TryGetValue(sceneName, out var sceneInstance))
            {
                Debug.LogError($"[AdditiveSceneManager] Cannot move player: scene '{sceneName}' not loaded");
                return;
            }

            var playerIdentity = conn.identity;
            if (playerIdentity == null)
            {
                Debug.LogError($"[AdditiveSceneManager] Cannot move player: no identity on connection");
                return;
            }

            // 先移动玩家 GameObject 到目标场景（服务器端先完成移动）
            SceneManager.MoveGameObjectToScene(playerIdentity.gameObject, sceneInstance.scene);

            // 将玩家传送到目标场景的出生点
            var spawnPos = FindSpawnInScene(sceneInstance.scene);
            var cc = playerIdentity.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            playerIdentity.transform.position = spawnPos;
            if (cc != null) cc.enabled = true;
            Debug.Log($"[AdditiveSceneManager] Teleported player to {spawnPos} in '{sceneName}'");

            // 同时移动玩家拥有的其他对象（如钓鱼浮标、鱼等）
            foreach (var owned in conn.owned)
            {
                if (owned != null && owned.gameObject.scene != sceneInstance.scene)
                    SceneManager.MoveGameObjectToScene(owned.gameObject, sceneInstance.scene);
            }

            // 更新映射
            sceneInstance.players.Add(conn);
            _playerSceneMap[conn] = sceneName;

            // 重建 observers 让 SceneInterestManagement 生效
            NetworkServer.RebuildObservers(playerIdentity, false);

            // 最后通知客户端加载场景（服务器端移动已完成）
            conn.Send(new LoadSceneMessage { sceneName = sceneName });

            Debug.Log($"[AdditiveSceneManager] Player {conn} moved to '{sceneName}', " +
                      $"players in scene: {sceneInstance.players.Count}");
        }

        /// <summary>
        /// 将玩家从场景中移出。
        /// </summary>
        private void RemovePlayerFromScene(NetworkConnection conn, string sceneName)
        {
            if (!_loadedScenes.TryGetValue(sceneName, out var sceneInstance))
                return;

            sceneInstance.players.Remove(conn);
            _playerSceneMap.Remove(conn);

            Debug.Log($"[AdditiveSceneManager] Player {conn} removed from '{sceneName}', " +
                      $"remaining: {sceneInstance.players.Count}");

            // 如果场景没有玩家了，可以选择卸载（节省内存）
            if (sceneInstance.players.Count == 0)
            {
                StartCoroutine(UnloadEmptyScene(sceneName));
            }
        }

        /// <summary>
        /// 卸载没有玩家的场景。延迟一小段时间以防玩家快速切换。
        /// </summary>
        private IEnumerator UnloadEmptyScene(string sceneName)
        {
            // 等待一段时间，防止玩家快速切换导致频繁加载/卸载
            yield return new WaitForSeconds(30f);

            // 再次检查是否仍然为空
            if (_loadedScenes.TryGetValue(sceneName, out var sceneInstance)
                && sceneInstance.players.Count == 0)
            {
                Debug.Log($"[AdditiveSceneManager] Unloading empty scene '{sceneName}'");

                // 销毁场景中的所有 NetworkIdentity 对象
                foreach (var go in sceneInstance.scene.GetRootGameObjects())
                {
                    var identities = go.GetComponentsInChildren<NetworkIdentity>(true);
                    foreach (var ni in identities)
                    {
                        if (ni.isServer && ni.connectionToClient == null) // 只销毁非玩家对象
                            NetworkServer.Destroy(ni.gameObject);
                    }
                }

                var asyncOp = SceneManager.UnloadSceneAsync(sceneInstance.scene);
                if (asyncOp != null)
                {
                    while (!asyncOp.isDone)
                        yield return null;
                }

                _loadedScenes.Remove(sceneName);
                Debug.Log($"[AdditiveSceneManager] Scene '{sceneName}' unloaded");
            }
        }

        /// <summary>
        /// 玩家断开连接时清理。由 HeadlessAutoStart 或 NetworkManager 调用。
        /// </summary>
        public void OnPlayerDisconnected(NetworkConnection conn)
        {
            if (_playerSceneMap.TryGetValue(conn, out string sceneName))
            {
                RemovePlayerFromScene(conn, sceneName);
            }
        }

        /// <summary>
        /// 获取指定场景的玩家数量。
        /// </summary>
        public int GetPlayerCount(string sceneName)
        {
            if (_loadedScenes.TryGetValue(sceneName, out var sceneInstance))
                return sceneInstance.players.Count;
            return 0;
        }

        /// <summary>
        /// 获取玩家当前所在的场景名。
        /// </summary>
        public string GetPlayerScene(NetworkConnection conn)
        {
            _playerSceneMap.TryGetValue(conn, out string sceneName);
            return sceneName;
        }

        /// <summary>
        /// 场景实例数据。
        /// </summary>
        private class SceneInstance
        {
            public Scene scene;
            public HashSet<NetworkConnection> players;
        }

        /// <summary>
        /// 在场景中查找 NetworkStartPosition 作为出生点。
        /// </summary>
        private Vector3 FindSpawnInScene(Scene scene)
        {
            foreach (var rootGo in scene.GetRootGameObjects())
            {
                var nsp = rootGo.GetComponent<NetworkStartPosition>();
                if (nsp != null)
                    return nsp.transform.position;
            }
            return new Vector3(98.1f, 4.215f, 66.77f); // fallback
        }
    }
}
