# 多人钓鱼系统 v2 架构设计

## 核心原则

1. **自己的状态机是唯一真相** — FishingGameTool 插件只做表现（动画、线渲染、浮标视觉）
2. **FishingSystem.enabled = false** — 永远不跑它的 Update/HandleInput/CastFloat/AttractFloat
3. **所有游戏逻辑跑在服务端** — 客户端只做输入发送 + 表现驱动
4. **不直接读写插件内部字段** — 通过 Presenter 层单向驱动插件组件
5. **自己的数据模型** — 不依赖 FishingSystem 的字段做状态存储，插件更新不影响核心逻辑

## 一、状态机

### 状态枚举

```
Idle → Charging → Casting → Floating → Hooked → Displaying → Idle
                                         ↓
                                     LineBroken → Idle
```

| 状态 | 描述 | 谁拥有 | 服务端行为 | 客户端表现 |
|------|------|--------|-----------|-----------|
| Idle | 待机 | 服务端 | 无 | 无浮标、无 UI |
| Charging | 蓄力中 | **纯客户端** | 无（不同步） | 蓄力条 UI |
| Casting | 抛竿延迟中 | 服务端 | _spawnFloatDelay 等待后生成浮标 | 抛竿动画 |
| Floating | 浮标在水面 | 服务端 | 定时 CheckLoot、LineLengthLimitation、无鱼收线 | 浮标可见、线渲染 |
| Hooked | 鱼上钩 | 服务端 | 选 loot、AttractWithLoot、CalculateLineLoad、距离够近 GrabLoot→Displaying | 鱼竿弯曲、线负载 UI |
| Displaying | 展示鱼 | 服务端 | 等待 E 键收鱼 | 左手IK提鱼姿势、鱼模型挂手上 |
| LineBroken | 断线 | 服务端 | 清理浮标、重置状态→Idle | 断线表现 |

### 设计决策：为什么去掉 Reeling 和 Caught

- **Reeling**：原版 FishingSystem 没有独立的"无鱼收线"状态。`_attractInput && !_caughtLoot` 时直接在 AttractFloat 里按地形类型（InAir/Land/Water）处理浮标回收。这是 Floating 状态的子行为，不需要独立状态。
- **Caught**：原版 GrabLoot 是瞬间完成的（Instantiate + AddForce），没有持续过程。Hooked 状态下距离够近时直接 GrabLoot 然后回 Idle，不需要中间状态。

### 状态转换条件

| 从 | 到 | 触发条件 |
|----|-----|---------|
| Idle | Casting | 服务端收到 CmdCast(force, direction) |
| Casting | Floating | _spawnFloatDelay 结束，浮标生成 |
| Floating | Hooked | CheckLoot 命中（服务端定时检测） |
| Floating | Idle | attractInput + 浮标回到 catchDistance 内（无鱼收回）|
| Floating | Idle | attractInput + InAir（浮标在空中，直接销毁）|
| Hooked | Displaying | attractInput + 浮标回到 catchDistance 内（GrabLoot → 提鱼展示）|
| Displaying | Idle | 按 E 键收鱼（CmdPickupFish）|
| Hooked | LineBroken | lineLoad 达到 maxLineLoad 且 overLoad 超时 |
| LineBroken | Idle | 调用 FixLine 或自动延迟重置 |

### Charging 的特殊处理

Charging 是纯客户端体验：
- 客户端按住 Fire1 → 本地进入 Charging，驱动蓄力条 UI
- 客户端松开 Fire1 → 发送 `CmdCast(force, direction)` 到服务端
- 服务端从不知道 Charging 状态，只收到最终的 cast 请求
- 这样做的原因：蓄力是纯 UI 反馈，不需要服务端验证，减少网络流量

## 二、文件结构

```
Assets/Scripts/Multiplayer/
├── FishingState.cs              — 状态枚举（含 Displaying 状态）
├── FishingStateMachine.cs       — 服务端状态机（纯 C# 类，不是 MonoBehaviour）
├── FishingLootCalculator.cs     — 纯静态计算（概率、稀有度、速度）
├── FishingPresenter.cs          — 表现层桥接：读网络状态，单向驱动 FishingSystem/FishingRod/HandIK
├── NetworkFishingController.cs  — 主控 NetworkBehaviour：输入、Command/Rpc、SyncVar、持有状态机和 Presenter
├── DisplayFishHold.cs           — Displaying 状态：左手IK提鱼 + 鱼模型跟随手骨
├── FishDatabase.cs              — ScriptableObject：loot名 → 网络prefab 映射
├── FishPickupLabel.cs           — [未使用] 原地面拾取"Press E"标签（已改为手持展示）
├── IKTest.cs                    — 开发调试用：按J键测试IK姿势和鱼位置
├── NetworkFishingFloat.cs       — 网络化浮标
├── NetworkFishingRod.cs         — 鱼竿弯曲同步
├── NetworkPlayerSetup.cs        — 玩家组件配置
├── ItemInfoBinder.cs            — 场景物品绑定
├── FishingUI.cs                 — 蓄力条、线负载条、ESC菜单
```

### 各文件职责边界

| 文件 | 知道什么 | 不知道什么 |
|------|---------|-----------|
| FishingState | 状态枚举定义 | 任何逻辑 |
| FishingStateMachine | 状态转换、钓鱼核心逻辑、FishingLootCalculator | Mirror、MonoBehaviour、FishingSystem 插件 |
| FishingLootCalculator | 概率数学、速度计算 | 状态、网络、Unity 组件（除了 Vector3/Random） |
| FishingPresenter | FishingSystem/FishingRod/FishingLineStatus/HandIK 的字段 | 网络、状态机逻辑 |
| NetworkFishingController | Mirror 网络、输入、状态机、Presenter | 钓鱼核心逻辑细节 |
| DisplayFishHold | Animator IK、左手骨骼、鱼模型 Transform | 网络、状态机、钓鱼逻辑 |
| FishingUI | SyncVar 读取、UI 构建、ESC 菜单 | 状态机、Presenter、插件内部 |
| FishDatabase | lootName → prefab 映射 | 网络、逻辑 |
| IKTest | Animator IK 调试（按J键测试） | 网络、状态机（纯本地调试工具） |

## 三、数据流

```
[客户端输入] → Command → [服务端 FishingStateMachine] → SyncVar/Rpc → [客户端 Presenter] → [FishingGameTool 组件]
```

### 同步数据 (SyncVars on NetworkFishingController)

```csharp
// 核心状态（服务端写，所有客户端读）
[SyncVar] FishingState syncState;           // 当前状态枚举

// Hooked 状态数据
[SyncVar] string syncLootName;              // 战利品名称
[SyncVar] int syncLootTier;                 // 战利品等级
[SyncVar] string syncLootDescription;       // 战利品描述
[SyncVar] float syncLineLoad;              // 线负载（降频同步）
[SyncVar] float syncOverLoad;              // 过载值（降频同步）

// 输入状态（服务端写，远程客户端读）
[SyncVar] bool syncAttractInput;           // 收线输入

// 装备状态
[SyncVar(hook=OnRodEquippedChanged)] bool syncRodEquipped;  // 鱼竿是否装备（F键切换）
```

### Presenter 驱动映射

Presenter 是单向的：只从网络状态写入插件字段，永远不从插件读取。

| 网络状态 | 驱动的插件字段 | 说明 |
|---------|---------------|------|
| state == Casting | FishingSystem._castFloat = true | 触发抛竿动画 |
| state >= Floating | FishingRod._fishingFloat = ActiveFloatTransform | 线渲染需要浮标引用 |
| state == Hooked | FishingRod.LootCaught(true) | 鱼竿弯曲 |
| state == Hooked | FishingSystem._advanced._caughtLoot = true | HandIK/角度计算 |
| state == Displaying | 清空所有钓鱼动画字段 | DisplayFishHold 独立处理 |
| syncAttractInput | FishingSystem._attractInput | HandIK 动画 |
| RpcOnLootSelected | FishingSystem._advanced._caughtLootData (复用 SO) | HandIK 角度计算 |
| 本地 Charging | FishingSystem._castInput = true | 蓄力条 UI（仅本地） |
| 本地 Charging | FishingSystem._currentCastForce | 蓄力条进度（仅本地） |

### 临时 ScriptableObject 的处理

v1 的问题：每次 RpcOnLootSelected 都 CreateInstance 一个 FishingLootData，没有销毁，内存泄漏。

v2 方案：
- Presenter 持有一个复用的 FishingLootData 实例（首次 ApplyLootData 时创建一次）
- 每次收到新 loot 数据时更新这个实例的字段，不重新创建
- ClearLootData 时设为 null（不销毁实例）

### UI 独立处理 (FishingUI)

Presenter 只驱动视觉/动画字段。UI 由 `FishingUI` 组件独立处理：
- 运行时动态创建 Canvas + 所有 UI 元素（不依赖预制件）
- 直接读取 `NetworkFishingController` 的 SyncVar（syncState、syncLineLoad、syncOverLoad 等）
- 蓄力条：本地 Charging 状态时显示
- 线负载条：Hooked 状态时显示，颜色从 cyan→yellow→red→dark red（过载）
- Loot 信息：当前隐藏（钓上来之前不显示鱼名，更有趣）
- ESC 菜单：Resume / Lobby / Quit

## 四、FishingStateMachine 设计

### 构造参数（配置数据，从 FishingSystem Inspector 读取一次）

```csharp
public struct FishingConfig
{
    public float spawnFloatDelay;       // FishingSystem._spawnFloatDelay
    public float catchDistance;         // FishingSystem._catchDistance
    public float catchCheckInterval;    // FishingSystem._advanced._catchCheckInterval
    public float returnSpeedWithoutLoot;// FishingSystem._advanced._returnSpeedWithoutLoot
    public float maxLineLength;         // FishingLineStatus._maxLineLength
    public float maxLineLoad;           // FishingLineStatus._maxLineLoad
    public float overLoadDuration;      // FishingLineStatus._overLoadDuration
    public float baseAttractSpeed;      // FishingRod._baseAttractSpeed
    public Vector2 angleRange;          // FishingRod._angleRange
    public bool isLineBreakable;        // FishingRod._isLineBreakable
    public LayerMask fishingLayer;      // FishingSystem._fishingLayer
    public LootCatchType lootCatchType; // FishingSystem._lootCatchType
}
```

### 回调（状态机通知 Controller）

```csharp
public Action<FishingState> OnStateChanged;           // 状态变化
public Action OnRequestDestroyFloat;                  // 请求销毁浮标
public Action<FishingLootData, float> OnLootSelected; // loot 选定（数据, 重量）
public Action<GameObject> OnLootGrabbed;              // loot 被抓取（null，controller 自行生成手持鱼）
public Action OnLineBroken;                           // 断线
```

注意：没有 `OnRequestSpawnFloat` 回调。抛竿流程是 `BeginCast()` 返回 delay 值，Controller 自己跑 `ServerCastCoroutine` 协程来生成浮标。

### 状态机不做的事

- 不持有 GameObject/Transform 引用（浮标位置通过参数传入）
- 不调用 NetworkServer.Spawn/Destroy（通过回调让 Controller 做）
- 不读写 FishingSystem 的任何字段（配置在构造时一次性读取到 FishingConfig）
- 不依赖 MonoBehaviour（可以单元测试）

### 状态机的 Tick 方法

```csharp
// Controller 每帧调用，传入当前浮标状态
public void Tick(float deltaTime, FloatContext ctx)

public struct FloatContext
{
    public Vector3 floatPosition;       // 浮标当前位置
    public Vector3 playerPosition;      // 玩家位置
    public SubstrateType substrate;     // 浮标所在地形
    public Transform floatTransform;    // 浮标 Transform（给 Pathfinder 用）
    public bool attractInput;           // 当前收线输入
    public List<FishingLootData> availableLoot; // 水域可用 loot（从 FishingFloat 获取）
}
```

### 状态机持有的内部状态

```csharp
private FishingState _state;
private float _catchCheckTimer;
private float _castingTimer;            // Casting 状态的延迟计时
private bool _caughtLoot;
private FishingLootData _caughtLootData;
private float _lootWeight;
private FishingBaitData _bait;
private float _lineLoad;
private float _overLoad;
private float _attractFloatSpeed;
private float _finalSpeed;
private float _randomSpeedChangerTimer;
private float _randomSpeedChanger;
private FishingFloatPathfinder _pathfinder;
```

## 五、降频同步策略

### lineLoad / overLoad

| 参数 | 值 | 说明 |
|------|-----|------|
| lineLoad 阈值 | 0.5f | 变化超过 0.5 才同步 |
| overLoad 阈值 | 0.1f | overLoad 最大 2s，需要更精细 |
| 最大间隔 | 0.15s | 即使没超阈值，0.15s 也强制同步一次 |
| 实现位置 | NetworkFishingController.ServerMonitorState() | |

```csharp
private float _lastSyncedLineLoad;
private float _lastSyncedOverLoad;
private float _syncTimer;

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
```

## 六、FishingPresenter 设计

### 职责

读取 NetworkFishingController 上的 SyncVar 和状态，单向写入 FishingSystem/FishingRod/FishingLineStatus/HandIK 的字段，驱动视觉表现（动画、线渲染、鱼竿弯曲）。UI 由 FishingUI 独立处理。

### 关键设计

1. **复用临时 SO**：Awake 时创建一个 FishingLootData 实例，后续只更新字段
2. **null 安全**：所有插件字段访问都做 null check
3. **本地 vs 远程**：
   - 本地玩家：Presenter 驱动 + 本地 Charging 状态直接写 _castInput/_currentCastForce
   - 远程玩家：纯 Presenter 驱动（SyncVar hook 触发）
4. **HandIK 兼容**：HandIK 读 `_fishingSystem._advanced._caughtLoot`、`_attractInput`、`_castFloat`、`_fishingRod._fishingFloat`，Presenter 确保这些字段在所有客户端都被正确设置

### Presenter 的 Apply 方法

```csharp
public void Apply(FishingState state, bool attractInput, Transform floatTransform)
{
    // 根据 state 和 attractInput 写入所有插件视觉字段
    // Displaying 状态下清空所有钓鱼动画状态（DisplayFishHold 独立处理手部IK）
    // 这是唯一允许写入插件字段的地方（UI 除外）
}

public void ApplyLootData(string lootName, int lootTier, string lootDescription)
{
    // 设置复用的 FishingLootData SO，供 HandIK 角度计算使用
}

public void ClearLootData()
{
    // 回到 Idle 时清空 loot 数据
}
```

## 七、NetworkFishingController v2 设计

### 职责精简

v1 的 Controller 里混了大量状态监控逻辑（CheckCaughtLootChanged、CheckExternalFloatDestruction 等）。v2 里这些全部由状态机回调驱动，Controller 只做：

1. **生命周期**：OnStartServer 创建状态机，OnStartClient/OnStartAuthority 禁用 FishingSystem
2. **输入**：HandleLocalInput 读 Fire1/Fire2，发 Command
3. **Command**：CmdCast、CmdStartAttract、CmdStopAttract、CmdForceStop、CmdFixLine、CmdToggleRod、CmdPickupFish
4. **状态机回调处理**：收到回调 → 更新 SyncVar / 发 Rpc
5. **Tick**：每帧调用状态机 Tick + Presenter Apply
6. **降频同步**：ThrottledSyncLineStatus

### 不再需要的东西

- ~~ServerMonitorState()~~ — 状态机回调替代
- ~~CheckCaughtLootChanged()~~ — 状态机内部处理
- ~~CheckExternalFloatDestruction()~~ — 状态机管理浮标生命周期
- ~~直接读写 _fishingSystem 字段~~ — 全部通过 Presenter

## 八、保留的原始功能清单

确保 v2 不丢失任何 FishingGameTool 原始功能：

| 原始功能 | v2 实现位置 | 状态 |
|---------|------------|------|
| HandleInput (Fire1/Fire2) | NetworkFishingController.HandleLocalInput | ✅ |
| CastFloat + CastingDelay | FishingStateMachine (Casting 状态) + Controller 协程 | ✅ |
| AttractFloat — InAir 分支 | FishingStateMachine.TickFloating (attractInput + InAir → 销毁) | ✅ |
| AttractFloat — Land 分支 | FishingStateMachine.TickFloating (attractInput + Land → 拉回) | ✅ |
| AttractFloat — Water 分支 | FishingStateMachine.TickFloating (attractInput + Water → Pathfinder 拉回) | ✅ |
| AttractFloat — CaughtLoot 分支 | FishingStateMachine.TickHooked | ✅ |
| CheckingLoot + 概率计算 | FishingStateMachine + FishingLootCalculator.RollCatchCheck | ✅ |
| ChooseFishingLoot + 稀有度 | FishingStateMachine + FishingLootCalculator.ChooseLoot | ✅ |
| AttractWithLoot + 速度计算 | FishingStateMachine + FishingLootCalculator.CalcLootSpeed/CalcFinalAttractSpeed | ✅ |
| CalculateLineLoad + 断线 | FishingStateMachine（自己计算，不调用 FishingRod.CalculateLineLoad） | ✅ |
| LineLengthLimitation | FishingStateMachine.TickFloating | ✅ |
| GrabLoot + SpawnLootItem | FishingStateMachine → OnLootGrabbed 回调 → Controller NetworkServer.Spawn | ✅ |
| ForceStopFishing | CmdForceStop → 状态机 ForceReset → Idle | ✅ |
| FixFishingLine | CmdFixLine → 状态机 FixLine → 清除 LineBroken | ✅ |
| AddBait | CmdAddBait → 状态机 SetBait | ✅ |
| FishingRod 弯曲动画 | FishingRod.Update 自己算（读 _fishingFloat + _lootCaught）+ NetworkFishingRod 同步 | ✅ |
| FishingLine 线渲染 | FishingRod.Update 自己算（读 _fishingFloat）| ✅ |
| HandIK 持竿动画 | HandIK.Update 读插件字段，Presenter 确保字段正确 | ✅ |
| SimpleUIManager 蓄力条 | 读 _castInput + _currentCastForce，本地 Charging 直接写 | ✅ |
| SimpleUIManager 线负载条 | 读 _currentLineLoad + _currentOverLoad，Presenter 写入 | ✅ |
| SimpleUIManager loot 信息 | 读 _caughtLootData，Presenter 用复用 SO 写入 | ✅ |
| FishingFloat 水面动画 | FishingFloat.Update 自己跑（不受 FishingSystem.enabled 影响）| ✅ |
| FishingFloatPathfinder 鱼游泳路径 | FishingStateMachine 持有 Pathfinder 实例 | ✅ |

### CalculateLineLoad 的特殊处理

v1 里服务端调用 `FishingRod.CalculateLineLoad()`，这意味着服务端依赖 FishingRod 组件。v2 里状态机自己计算 lineLoad/overLoad/attractSpeed，完全复刻 FishingRod.CalculateLineLoad 的数学逻辑，不再调用插件方法。这样：
- 状态机不依赖任何 Unity 组件
- FishingRod 在客户端只做弯曲动画和线渲染（它的 CalculateLineLoad 不会被调用）
- 断线检测在状态机内部完成，通过 OnLineBroken 回调通知 Controller

## 九、迁移计划

### 步骤

1. 创建 `FishingState.cs` — 状态枚举
2. 创建 `FishingStateMachine.cs` — 完整状态机，包含所有原始逻辑
3. 创建 `FishingPresenter.cs` — 表现层桥接
4. 重写 `NetworkFishingController.cs` — 精简主控
5. 删除 `ServerFishingLogic.cs` — 被 FishingStateMachine 完全替代
6. `FishingLootCalculator.cs` — 保留不变
7. `NetworkFishingFloat.cs` / `NetworkFishingRod.cs` / `NetworkPlayerSetup.cs` / `ItemInfoBinder.cs` — 保留不变
8. 更新 `PROGRESS.md`

### 风险点

- FishingFloatPathfinder 依赖 Transform 和 FishingFloat 组件 — 状态机需要通过 FloatContext 传入，不能完全脱离 Unity
- FishingRod.CalculateLineLoad 里的断线逻辑 — v2 里状态机自己算，但 FishingRod 的 OnLineBreak 回调仍然需要注册（防止 FishingRod.Update 里的 CalculateBend 触发意外行为）
- SimpleUIManager 直接读 FishingSystem 字段 — 不改插件代码，Presenter 确保字段值正确即可


## 十、鱼展示系统 (Fish Display)

### 流程

1. Hooked 状态下，鱼被拉到 `catchDistance` 内 → `GrabLoot` 触发
2. `OnLootGrabbed` 回调 → Controller 调用 `ServerSpawnHeldFish()`
3. 服务端根据 `syncLootName` 从 `FishDatabase` 查找对应 prefab
4. `Instantiate` prefab → 移除 Rigidbody/Collider → `NetworkServer.Spawn`
5. `RpcAttachFishToHand` → 所有客户端禁用 NetworkTransform，添加 `DisplayFishHold` 组件
6. 进入 Displaying 状态

### DisplayFishHold 组件

- 挂在玩家 GameObject 上（运行时动态添加）
- `OnAnimatorIK`：左手 IK 目标 = 头骨位置 + up*0.1 + forward*0.35
- `LateUpdate`：鱼跟随左手骨骼位置（不做 parent，避免 Mirror NetworkObject 父子限制）
- 鱼旋转：X+ 朝上（`Quaternion.Euler(0,0,90)` 使鱼头朝上、身体垂下）
- 鱼缩放：`originalScale * 0.25`
- 位置偏移：down=0.05, right=0.15

### 收鱼

- 按 E 键 → `CmdPickupFish` → 服务端 `NetworkServer.Destroy` 鱼
- `RpcCleanupFishHold` → 客户端清理 DisplayFishHold 组件
- 状态回到 Idle

### 为什么不做 parent

Mirror 的 NetworkObject 不允许运行时改变父子关系（会导致同步异常）。所以鱼模型在 `LateUpdate` 里每帧跟随手骨位置，同时禁用 NetworkTransform 防止网络同步覆盖位置。

## 十一、水面行走防护 (Water Walking Prevention)

### 方案：Layer Collision Matrix

- Player = Layer 6
- Water = Layer 4
- Float = Layer 7
- Physics Layer Collision Matrix 中取消 Player-Water 碰撞
- CharacterController 不再与水面 MeshCollider 碰撞 → 玩家掉入水中

### 为什么不影响鱼漂

`FishingFloat.CheckSurface` 使用 `Physics.OverlapSphere`，该方法不受 Layer Collision Matrix 影响（Collision Matrix 只影响物理碰撞，不影响 Query/Overlap）。鱼漂在 Layer 7，正常检测水面。

### 鱼漂穿透修复

水面是薄 MeshCollider，鱼漂高速飞行时可能穿透。在 `ServerCastCoroutine` 中设置 `rb.collisionDetectionMode = CollisionDetectionMode.Continuous` 解决。

## 十二、鱼 Prefab 生成 (FishSetupEditor)

### 工具位置

`Assets/Scripts/Editor/FishSetupEditor.cs` → 菜单 `Tools/Fish Setup/Generate All Fish`

### 执行流程

1. 检查当前场景是否为 GameScene
2. 从 FBX 创建 Prefab：
   - 解包 FBX prefab
   - 计算 mesh bounds，偏移 pivot 使鱼头（X+ 方向）在原点
   - 添加 Rigidbody、BoxCollider、NetworkIdentity
3. 创建 FishingLootData ScriptableObject（稀有度、重量范围等）
4. 更新 FishDatabase（lootName → prefab 映射）
5. 将所有 LootData 写入场景水面物体的 FishingLoot 组件
6. 注册 prefab 到 NetworkManager.spawnPrefabs

### 当前鱼种（7条，稀有度均为20）

| 鱼名 | 等级 | 重量范围 |
|------|------|---------|
| Bigeye Tuna | Rare | 15-40 |
| Yellow Croaker | Common | 0.3-2 |
| Moorish Idol | Uncommon | 0.3-1.5 |
| Brown Rabbitfish | Common | 0.2-1 |
| Thickhead Scorpionfish | Uncommon | 0.5-3 |
| Emperor Snapper | Epic | 5-25 |
| Chub Mackerel | Common | 0.3-2.5 |

### Pivot 偏移

FBX 鱼模型头部朝 X+ 方向。FishSetupEditor 在创建 Prefab 时：
- 计算所有 Renderer 的合并 bounds
- 创建 MeshPivot 子物体，将 mesh 偏移使 bounds.max.x 对齐原点
- 这样鱼头在 (0,0,0)，方便 DisplayFishHold 定位

## 十三、鱼竿装备系统 (Rod Equip/Unequip)

### 操作

- F 键切换鱼竿显示/隐藏
- 仅在 Idle 和 Displaying 状态下可用
- `syncRodEquipped` SyncVar + hook `OnRodEquippedChanged` 同步到所有客户端

### 视觉

- `ApplyRodVisuals(equipped)`：控制 FishingRod GameObject 和 HandIK 组件的 active/enabled
- 未装备时不能抛竿（`HandleLocalInput` 中 `if (!syncRodEquipped) return`）

## 十四、待实现功能 (TODO)

### 鱼饵系统 (Bait System)

当前状态：未实现。`FishingStateMachine` 中 `_bait` 始终为 null。

FishingGameTool 插件已有 `FishingBaitData` 数据结构（BaitTier: Uncommon/Rare/Epic/Legendary），`FishingLootCalculator.ChooseLoot` 原本会根据 baitTier 过滤可钓鱼种（无饵只能钓 Common 鱼）。当前已临时移除该过滤，所有鱼按 rarity 权重均等随机。

实现鱼饵系统需要：
- 背包/物品系统（存储鱼饵）
- 装备鱼饵的 UI 和网络逻辑（`CmdSetBait` → `FishingStateMachine.SetBait()`）
- 恢复 `ChooseLoot` 中的 baitTier 过滤逻辑
- 不同鱼饵影响咬钩概率（`RollCatchCheck` 中已有对应逻辑）
