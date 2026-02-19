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
Idle → Charging → Casting → Floating → Hooked → Idle
                                         ↓
                                     LineBroken → Idle
```

| 状态 | 描述 | 谁拥有 | 服务端行为 | 客户端表现 |
|------|------|--------|-----------|-----------|
| Idle | 待机 | 服务端 | 无 | 无浮标、无 UI |
| Charging | 蓄力中 | **纯客户端** | 无（不同步） | 蓄力条 UI |
| Casting | 抛竿延迟中 | 服务端 | _spawnFloatDelay 等待后生成浮标 | 抛竿动画 |
| Floating | 浮标在水面 | 服务端 | 定时 CheckLoot、LineLengthLimitation、无鱼收线 | 浮标可见、线渲染 |
| Hooked | 鱼上钩 | 服务端 | 选 loot、AttractWithLoot、CalculateLineLoad、距离够近 GrabLoot→Idle | 鱼竿弯曲、线负载 UI、loot 信息 |
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
| Hooked | Idle | attractInput + 浮标回到 catchDistance 内（GrabLoot）|
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
├── FishingState.cs              — 状态枚举
├── FishingStateMachine.cs       — 服务端状态机（纯 C# 类，不是 MonoBehaviour）
├── FishingLootCalculator.cs     — 纯静态计算（概率、稀有度、速度）[已有，保留]
├── FishingPresenter.cs          — 表现层桥接：读网络状态，单向驱动 FishingSystem/FishingRod/HandIK
├── NetworkFishingController.cs  — 主控 NetworkBehaviour：输入、Command/Rpc、SyncVar、持有状态机和 Presenter
├── NetworkFishingFloat.cs       — 网络化浮标 [已有，保留]
├── NetworkFishingRod.cs         — 鱼竿弯曲同步 [已有，保留]
├── NetworkPlayerSetup.cs        — 玩家组件配置 [已有，保留]
├── ItemInfoBinder.cs            — 场景物品绑定 [已有，保留]
```

### 各文件职责边界

| 文件 | 知道什么 | 不知道什么 |
|------|---------|-----------|
| FishingState | 状态枚举定义 | 任何逻辑 |
| FishingStateMachine | 状态转换、钓鱼核心逻辑、FishingLootCalculator | Mirror、MonoBehaviour、FishingSystem 插件 |
| FishingLootCalculator | 概率数学、速度计算 | 状态、网络、Unity 组件（除了 Vector3/Random） |
| FishingPresenter | FishingSystem/FishingRod/HandIK 的字段 | 网络、状态机逻辑 |
| NetworkFishingController | Mirror 网络、输入、状态机、Presenter | 钓鱼核心逻辑细节 |

## 三、数据流

```
[客户端输入] → Command → [服务端 FishingStateMachine] → SyncVar/Rpc → [客户端 Presenter] → [FishingGameTool 组件]
```

### 同步数据 (SyncVars on NetworkFishingController)

```csharp
// 核心状态（服务端写，所有客户端读）
[SyncVar] FishingState state;           // 当前状态枚举

// Hooked 状态数据
[SyncVar] string lootName;              // 战利品名称
[SyncVar] int lootTier;                 // 战利品等级
[SyncVar] string lootDescription;       // 战利品描述
[SyncVar] float syncLineLoad;           // 线负载（降频同步）
[SyncVar] float syncOverLoad;           // 过载值（降频同步）

// 输入状态（服务端写，远程客户端读）
[SyncVar] bool syncAttractInput;        // 收线输入
```

### Presenter 驱动映射

Presenter 是单向的：只从网络状态写入插件字段，永远不从插件读取。

| 网络状态 | 驱动的插件字段 | 说明 |
|---------|---------------|------|
| state == Casting | FishingSystem._castFloat = true | 触发抛竿动画 |
| state >= Floating | FishingRod._fishingFloat = ActiveFloatTransform | 线渲染需要浮标引用 |
| state == Hooked | FishingRod._lootCaught = true | 鱼竿弯曲 |
| state == Hooked | FishingSystem._advanced._caughtLoot = true | HandIK/UI 读取 |
| syncAttractInput | FishingSystem._attractInput | HandIK 动画 |
| syncLineLoad | FishingLineStatus._currentLineLoad | UI 线负载条 |
| syncOverLoad | FishingLineStatus._currentOverLoad | UI 过载条 |
| lootName/Tier/Desc | FishingSystem._advanced._caughtLootData (临时 SO) | UI loot 信息 |
| state == LineBroken | FishingLineStatus._isLineBroken = true | 断线表现 |
| 本地 Charging | FishingSystem._castInput = true | 蓄力条 UI（仅本地） |
| 本地 Charging | FishingSystem._currentCastForce | 蓄力条进度（仅本地） |

### 临时 ScriptableObject 的处理

v1 的问题：每次 RpcOnLootSelected 都 CreateInstance 一个 FishingLootData，没有销毁，内存泄漏。

v2 方案：
- Presenter 持有一个复用的 FishingLootData 实例（Awake 时创建一次）
- 每次收到新 loot 数据时更新这个实例的字段，不重新创建
- 状态回到 Idle 时清空字段（不销毁实例）

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
public Action<float, Vector3> OnRequestSpawnFloat;    // 请求生成浮标（force, direction）
public Action OnRequestDestroyFloat;                  // 请求销毁浮标
public Action<FishingLootData, float> OnLootSelected; // loot 选定（数据, 重量）
public Action<GameObject> OnLootGrabbed;              // loot 被抓取（loot prefab 实例）
public Action OnLineBroken;                           // 断线
```

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

读取 NetworkFishingController 上的 SyncVar 和状态，单向写入 FishingSystem/FishingRod/FishingLineStatus/HandIK 的字段，驱动表现。

### 关键设计

1. **复用临时 SO**：Awake 时创建一个 FishingLootData 实例，后续只更新字段
2. **null 安全**：所有插件字段访问都做 null check
3. **本地 vs 远程**：
   - 本地玩家：Presenter 驱动 + 本地 Charging 状态直接写 _castInput/_currentCastForce
   - 远程玩家：纯 Presenter 驱动（SyncVar hook 触发）
4. **HandIK 兼容**：HandIK 读 `_fishingSystem._advanced._caughtLoot`、`_attractInput`、`_castFloat`、`_fishingRod._fishingFloat`，Presenter 确保这些字段在所有客户端都被正确设置

### Presenter 的 Apply 方法

```csharp
public void Apply(FishingState state, SyncData data, Transform floatTransform)
{
    // 根据 state 和 data 写入所有插件字段
    // 这是唯一允许写入插件字段的地方
}
```

## 七、NetworkFishingController v2 设计

### 职责精简

v1 的 Controller 里混了大量状态监控逻辑（CheckCaughtLootChanged、CheckExternalFloatDestruction 等）。v2 里这些全部由状态机回调驱动，Controller 只做：

1. **生命周期**：OnStartServer 创建状态机，OnStartClient/OnStartAuthority 禁用 FishingSystem
2. **输入**：HandleLocalInput 读 Fire1/Fire2，发 Command
3. **Command**：CmdCast、CmdStartAttract、CmdStopAttract、CmdForceStop、CmdFixLine、CmdAddBait
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


## 十、待实现功能 (TODO)

### 鱼饵系统 (Bait System)

当前状态：未实现。`FishingStateMachine` 中 `_bait` 始终为 null。

FishingGameTool 插件已有 `FishingBaitData` 数据结构（BaitTier: Uncommon/Rare/Epic/Legendary），`FishingLootCalculator.ChooseLoot` 原本会根据 baitTier 过滤可钓鱼种（无饵只能钓 Common 鱼）。当前已临时移除该过滤，所有鱼按 rarity 权重均等随机。

实现鱼饵系统需要：
- 背包/物品系统（存储鱼饵）
- 装备鱼饵的 UI 和网络逻辑（`CmdSetBait` → `FishingStateMachine.SetBait()`）
- 恢复 `ChooseLoot` 中的 baitTier 过滤逻辑
- 不同鱼饵影响咬钩概率（`RollCatchCheck` 中已有对应逻辑）
