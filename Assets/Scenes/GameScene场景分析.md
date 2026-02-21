# GameScene 场景分析 — 新建海岛场景参考

## 一、GameScene 完整物体清单

### 1. 基础设施（每个游戏场景都需要）

| 物体名 | 类型 | 作用 | 新场景处理 |
|--------|------|------|-----------|
| **SpawnPoint** | GameObject + `NetworkStartPosition` | 玩家出生点，位置 (98.1, 4.215, 66.77) | ✅ 我可以创建，改坐标即可 |
| **Directional Light** | Light (Directional) + URP AdditionalLightData | 主光源，暖色 (1, 0.84, 0.66)，强度 4，开启阴影 | ✅ 我可以创建 |
| **EventSystem** | EventSystem + StandaloneInputModule | UI 事件系统（会被 LobbyUI 禁用） | ✅ 我可以创建 |
| **Global Volume** | URP Volume (IsGlobal=true) | 后处理，引用 Volume Profile (guid: fa32c16936b002549b6ad445c4df1aeb) | ✅ 可复用同一个 Profile，我创建 Volume 物体 |
| **DemoManager** | MonoBehaviour (guid: 3f3581a46ef65e24299e80122e1fbb09) | FishingGameTool 的 Demo 管理器 | ✅ 我可以创建 |
| **ItemInfoBinder** | MonoBehaviour (`MultiplayerFishing.ItemInfoBinder`) | 绑定场景内 ItemInfo 到本地玩家 | ✅ 我可以创建 |
| **`****`分隔符** | 空 GameObject | 仅用于 Hierarchy 分组，无功能 | 可忽略 |

### 2. 地形（需要你在 Unity 编辑器中制作）

| 物体名 | 类型 | 详情 | 新场景处理 |
|--------|------|------|-----------|
| **Terrain** | Terrain + TerrainCollider | TerrainData: `7ec5a5ff271c44a459770e96872d8d48`，位置 (0,0,0)，Layer 3，Static，树距 300，细节距 100 | ⚠️ **你需要在 Unity 中新建 Terrain 并手动雕刻海岛地形** |
| **Collider / Collider (1) / (2) / (3)** | BoxCollider (Layer 3) | Terrain 的子物体，额外碰撞区域 | ⚠️ 根据新地形需要手动调整 |

### 3. 水面（需要你在 Unity 编辑器中调整）

| 物体名 | 类型 | 详情 | 新场景处理 |
|--------|------|------|-----------|
| **RiverPart1** | Plane Mesh + MeshCollider + MeshRenderer + `FishingLoot` 脚本 | Layer 4，位置 (144.19, 4.99, 70.15)，缩放 (2.46, 1, 1.23)，使用 River.mat 材质，挂载 `FishingLoot` 组件配置了 7 种鱼的 LootData | ⚠️ **需要你调整** |
| **WaterZoneTrigger** | BoxCollider (IsTrigger) + `WaterZone` 脚本 | RiverPart1 的子物体，Size (10, 3, 10)，检测玩家落水 | ⚠️ 跟随水面调整 |
| **RiverPart2** | Plane Mesh + MeshCollider + MeshRenderer | Layer 0，位置 (168.51, 14.85, 55.36)，缩放 (3.62, 1, 1.23)，同样使用 River.mat，**没有 FishingLoot 组件**（纯装饰水面） | ⚠️ 海岛场景可能不需要 |

**水面关键点：**
- 水面材质路径：`Assets/FishingGameTool/Example/Materials/River.mat`（使用 `RiverShader.shadergraph`）
- 另有 `WaterShader.shadergraph` 和 `WaterBlock_50m.prefab` 可用于海面
- 钓鱼功能的水面必须挂 `FishingLoot` 组件（`FishingGameTool.Fishing.Loot.FishingLoot`），配置可钓鱼种
- 落水检测需要子物体挂 `WaterZone` + BoxCollider (IsTrigger)
- **海岛场景建议**：用 `WaterBlock_50m.prefab` 做大面积海面，或者用 Plane + `Water_mat_01.mat` / `River.mat`

### 4. 岩石装饰（可以直接复用 Prefab）

| Prefab | 数量 | 来源路径 |
|--------|------|---------|
| Rock_Overgrown_A | 2 个 | `TerrainSampleAssets/Prefabs/Rocks/Rock_Overgrown_A.prefab` |
| Rock_Overgrown_B | ~8 个 | `TerrainSampleAssets/Prefabs/Rocks/Rock_Overgrown_B.prefab` |
| Rock_Overgrown_C | ~15 个 | `TerrainSampleAssets/Prefabs/Rocks/Rock_Overgrown_C.prefab` |
| Rock_Overgrown_D | ~35 个 | `TerrainSampleAssets/Prefabs/Rocks/Rock_Overgrown_D.prefab` |

✅ 这些 Prefab 可以直接拖到新场景，我无法帮你摆放位置（需要在 Unity 编辑器中可视化操作）。

### 5. 交互道具（可以复用）

| 物体名 | 类型 | 作用 |
|--------|------|------|
| **Item_FishingBoost** | BoxCollider + MeshRenderer + ItemInfo + InteractionObject | 钓鱼增益道具，Layer 8 |
| **Item_EpicBait** | 同上 | 史诗鱼饵 |
| **Item_RareBait** | 同上 | 稀有鱼饵 |
| **Item_UncommonBait** | 同上 | 普通鱼饵 |
| **Item_LegendaryBait** | 同上 | 传说鱼饵 |

每个 Item 都有对应的 UI 子物体（UncommonBaitUI / FishingBoostUI 等，WorldSpace Canvas + Text）。

✅ 这些可以直接从 GameScene 复制到新场景，改位置即可。

### 6. 渲染设置（RenderSettings）

| 设置 | 值 |
|------|-----|
| Fog | 开启，Exponential Squared，密度 0.003，颜色暖白 |
| Skybox | Material guid: `73f7c508467b9df4ca1b2ba2001b0e83`（即 `Sky.mat`） |
| Ambient | Skybox 模式，强度 1.5 |
| Lightmap | 已烘焙 |

✅ 新场景可以用相同的 Skybox 和 Fog 设置。海岛场景可能需要调整 Fog 颜色/密度来匹配海洋氛围。

---

## 二、分类总结

### ✅ 我能帮你创建的（代码/配置层面）

1. **新场景的 .unity 文件骨架**（但 Unity 场景文件是二进制序列化的 YAML，实际操作建议在 Unity 编辑器中 Duplicate GameScene 然后修改）
2. **LobbyUI 注册新地图** — 在 `AvailableMaps` 数组中添加新条目
3. **SpawnPoint** (NetworkStartPosition) — 改坐标
4. **EventSystem、DemoManager、ItemInfoBinder、Global Volume** — 纯配置物体
5. **FishingLoot 配置** — 可以复用现有 7 种鱼，也可以为海岛配不同的鱼种
6. **Build Settings** — 确保新场景加入 Build Settings

### ⚠️ 你需要在 Unity 编辑器中制作的

1. **Terrain 地形** — 海岛地形需要手动雕刻（高度图、纹理绘制、树木/草地放置）
2. **水面位置和大小** — 海岛需要大面积海水，用 Plane 或 `WaterBlock_50m` 铺设，调整位置/缩放
3. **WaterZoneTrigger 范围** — 跟随水面调整 BoxCollider 大小
4. **岩石/装饰物摆放** — 拖 Prefab 到场景中调位置
5. **交互道具位置** — 从 GameScene 复制 Item_* 物体，改位置
6. **光照烘焙** — 新场景需要重新 Bake Lightmap
7. **Terrain Collider 子物体** — 根据地形调整额外碰撞体

### 💡 水面方案建议

海岛场景的水面和内陆湖不同：
- **内陆湖 (GameScene)**：用 2 个旋转的 Plane（RiverPart1/2）+ River.mat
- **海岛场景建议**：
  - 用一个大的 Plane（或多个 `WaterBlock_50m`）铺满海面
  - 材质可以用 `Water_mat_01.mat`（路径：`FishingGameTool/Example/Prefabs/Water/`）或复用 `River.mat`
  - 钓鱼区域的水面挂 `FishingLoot` 组件
  - 非钓鱼区域的水面不挂（纯视觉）
  - 每个钓鱼水面需要子物体 `WaterZoneTrigger`（BoxCollider IsTrigger + WaterZone 脚本）

### 📋 其他注意事项

- 新场景名（如 `IslandScene`）需要加入 Unity Build Settings
- `AdditiveSceneManager` 会自动按需加载，不需要改服务器代码
- 可以为海岛配置不同的鱼种（新建 FishingLootData assets）
- Skybox 材质 `Sky.mat` 在 `FishingGameTool/Example/Materials/` 下，海岛可以复用或换一个

---

## 三、推荐操作步骤

1. 在 Unity 中 **Duplicate GameScene** → 重命名为 `IslandScene`
2. 删除所有 Rock_Overgrown_* 和旧的 RiverPart1/2
3. 新建/替换 Terrain，雕刻海岛地形
4. 铺设海面水面（大 Plane + 水材质 + FishingLoot + WaterZone）
5. 重新摆放岩石装饰和交互道具
6. 调整 SpawnPoint 位置
7. 调整 Directional Light 角度（海岛可能需要不同的光照方向）
8. Bake Lightmap
9. 加入 Build Settings
10. 告诉我场景名，我来更新 `LobbyUI.AvailableMaps` 注册新地图
