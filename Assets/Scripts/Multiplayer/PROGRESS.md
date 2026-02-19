# 多人钓鱼系统改造进度

## 项目概述

将 FishingGameTool 插件的单机钓鱼系统改造为基于 Mirror 的多人网络钓鱼系统。

## 当前架构 (v2)

### 核心原则
- 自己的状态机是唯一真相，FishingGameTool 只做表现
- FishingSystem.enabled = false，永远不跑它的逻辑
- 所有游戏逻辑跑在服务端，客户端只做输入 + 表现
- 通过 Presenter 层单向驱动插件组件，不直接读写插件字段

### 文件结构

```
Assets/Scripts/Multiplayer/
├── FishingState.cs              — 状态枚举 (Idle/Casting/Floating/Hooked/LineBroken)
├── FishingStateMachine.cs       — 服务端状态机（纯 C# 类，不依赖 MonoBehaviour）
├── FishingLootCalculator.cs     — 纯静态计算（概率、稀有度、速度）
├── FishingPresenter.cs          — 表现层桥接：读网络状态，单向驱动插件组件
├── NetworkFishingController.cs  — 主控 NetworkBehaviour：输入、Command/Rpc/SyncVar
├── NetworkFishingFloat.cs       — 网络化浮标（服务端物理、客户端运动学）
├── NetworkFishingRod.cs         — 鱼竿弯曲同步
├── NetworkPlayerSetup.cs        — 本地/远程玩家组件配置
├── ItemInfoBinder.cs            — 场景物品绑定
└── ScreenLogger.cs              — 屏幕日志 + 文件日志（F1 切换）
```

### 数据流
```
[客户端输入] → Command → [服务端 FishingStateMachine] → SyncVar/Rpc → [客户端 Presenter] → [FishingGameTool 组件]
```

## 已完成

### v2 架构重写
- [x] FishingState.cs — 状态枚举
- [x] FishingStateMachine.cs — 完整服务端状态机
- [x] FishingPresenter.cs — 单向驱动插件字段，复用 SO 避免内存泄漏
- [x] NetworkFishingController.cs — 精简主控，降频同步 lineLoad/overLoad
- [x] 删除 ServerFishingLogic.cs（被 FishingStateMachine 替代）

### Bug 修复记录

#### Session 1 (v2 初始调试)
1. **浮标飞行中销毁** — 抛竿后按右键收线，浮标在空中就被销毁。原因：TickFloating InAir 分支没有守卫。修复：添加 `_hasLandedOnWater` 标志，只有浮标接触过水面后才允许 InAir 销毁。
2. **TickLineLoad 角度计算方向错误** — 用了浮标的 forward 而不是鱼竿的 forward。修复：FloatContext 传入 rodForward/rodPosition。
3. **overLoad 累积逻辑不匹配** — 原版在 CalculateLineLoad 内部用 `==` 判断 maxLineLoad 时累积 overLoad。我们的代码拆分到了 TickHooked 和 TickLineLoad，且用了 `>=`。修复：overLoad 累积移入 TickLineLoad，用 `==` 匹配原版。
4. **Presenter `_rod.LootCaught(hooked)` 缺少守卫** — 在 loot 数据到达前就设置 `_lootCaught = true`，导致鱼竿提前弯曲。修复：改用 `safeHooked = hooked && hasLootData && floatTransform != null`。
5. **HandIK NullRef 风险** — `_caughtLoot = true` 但浮标已销毁时，HandIK 读 `_fishingFloat.position` 崩溃。修复：`safeHooked` 条件包含 `floatTransform != null`。
6. **重复抛竿** — `canCast` 检查 `syncState == Idle` 但 SyncVar 有网络延迟，松开 Fire1 后再按会发送重复 CmdCast。修复：添加 `_castSent` 标志，阻止进一步抛竿直到 syncState 离开 Idle。

#### Session 2 (全面对比检查)
7. **LineLengthLimitation 距离计算错误** — 用了 playerPosition 到 floatPosition 的距离，但原版用的是 lineAttachment（鱼竿尖端）到浮标的距离。playerPosition 比 lineAttachment 远 1-2 米，导致提前触发线长限制，可能干扰浮标飞行轨迹。修复：FloatContext 添加 `lineAttachmentPosition`，TickLineLengthLimitation 改用正确距离。
8. **TickLineLoad 角度计算位置错误** — 用了 playerPosition 作为角度计算的起点，但原版用的是 FishingRod 的 transform.position。修复：FloatContext 添加 `rodPosition` 和 `rodForward`，TickLineLoad 改用 rod 的位置和朝向。
9. **浮标 Rigidbody 力施加时序** — `AddForce` 在 `NetworkServer.Spawn` 之前调用，但如果预制体默认 isKinematic=true，力不会生效。修复：在 AddForce 前显式设置 `rb.isKinematic = false`。
10. **NetworkTransform 类型和方向错误** — 
    - 浮标预制体用了 `NetworkTransformUnreliable` + `syncDirection=ClientToServer`，但浮标是服务端权威（服务端跑物理），应该是 `ServerToClient`。客户端看不到服务端的位置更新，浮标在客户端停在生成位置。
    - 玩家预制体用了 `NetworkTransformUnreliable`，CharacterController 移动需要可靠同步。
    - 修复：Editor 脚本改为使用 `NetworkTransformReliable`，浮标设置 `syncDirection=ServerToClient`。添加 Step 4 菜单项修复已有预制体。

### 调试工具
- [x] 全部三个核心文件添加 Debug.Log（`[NFC][Server]`、`[NFC][Client]`、`[NFC][Rpc]`、`[StateMachine]`、`[Presenter]` 前缀）
- [x] ScreenLogger.cs — 屏幕日志叠加层（F1 切换）+ 文件日志到 Application.persistentDataPath
- [x] 浮标诊断日志 — 每 0.5 秒记录浮标位置、基底类型、速度、状态

### Editor 设置脚本
- [x] Step 1: 创建网络浮标预制体（NetworkTransformReliable + ServerToClient）
- [x] Step 2: 创建玩家预制体（NetworkTransformReliable + ClientToServer）
- [x] Step 3: 创建 Lobby + Game 场景
- [x] Step 4: 修复已有预制体（NetworkTransform 升级）

## 测试步骤

### 修复已有预制体（必须先做）
1. Unity Editor 中运行 `Tools > Multiplayer Fishing Setup > 4. Fix Existing Prefabs`
2. 重新 Build 客户端（Development Build）

### 测试流程
1. IDE 启动 Server Only
2. Build 客户端连接
3. 验证走路同步（应该平滑，不再瞬移）
4. 验证抛竿 → 浮标落水（查看 ScreenLogger 的 FloatDiag 日志）
5. 验证等鱼 → 上钩 → 搏斗 → 收线 → 抓鱼
6. 验证蓄力条、线负载条、loot 信息 UI
7. 验证断线 + 修线
8. 第二个客户端连接，验证远程玩家动画

## 待办

### 需要联调验证
- [ ] Server Only + 客户端完整钓鱼流程
- [ ] 多玩家同时钓鱼
- [ ] 蓄力条 UI 正常显示
- [ ] 线负载条 UI 正常显示
- [ ] Loot 信息文字正常显示
- [ ] 远程玩家 HandIK 持竿动画正常
- [ ] 远程玩家鱼竿弯曲 + 线渲染正常
- [ ] 断线 + 修线流程

### 后续功能（记录，暂不实现）
- [ ] AddBait 完整网络化（需要背包/物品系统）
- [ ] Tab 菜单功能网络化（鱼饵/视角/退出按钮 — 示例 UI）
- [ ] AddCustomCatchProbabilityData 网络化
- [ ] LootCatchType.InvokeEvent 模式网络化
- [ ] Loot prefab 注册到 NetworkManager Registered Spawnable Prefabs
- [ ] 断线重连时钓鱼状态恢复
- [ ] InteractionSystem UnityEvent 目标改为 NetworkFishingController
