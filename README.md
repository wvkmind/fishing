# Fishing Online — 多人在线钓鱼游戏

## 环境要求

| 项目 | 版本 |
|------|------|
| Unity | 6000.3.8f1 (Unity 6) |
| Mirror | 96.0.1 |
| FishingGameTool | v1.2 (Unity Asset Store) |

## 从零恢复项目

### 1. 克隆仓库

```bash
git clone git@github.com:wvkmind/fishing.git
```

### 2. 用正确版本的 Unity 打开项目

使用 Unity Hub 安装 `6000.3.8f1`，打开克隆下来的项目文件夹。
首次打开会有大量报错（缺少第三方包的引用），这是正常的。

### 3. 导入 Mirror

- Unity 菜单 → `Window > Package Manager`
- 搜索 Mirror 或从 Asset Store 导入 Mirror v96.0.1
- 也可以从 [Mirror GitHub Releases](https://github.com/MirrorNetworking/Mirror/releases) 下载对应版本的 unitypackage 导入

### 4. 导入 FishingGameTool

- 从 Unity Asset Store 导入 FishingGameTool v1.2
- 导入路径应为 `Assets/FishingGameTool/`

### 5. 等待编译完成

两个包导入后 Unity 会重新编译，等待控制台无报错。

### 6. 修复 Prefab 和场景（如需要）

如果 prefab 引用丢失（Inspector 里出现 Missing Script），运行：

- `Tools > Multiplayer Fishing Setup > 4. Fix Existing Prefabs`
- `Tools > Multiplayer Fishing Setup > 5. Fix Lobby Scene (LobbyUI)`

正常情况下 prefab 和场景已经在 git 里保存了正确的引用，只要包的 GUID 没变就不需要这步。

### 7. 验证

- 打开 `Assets/Scenes/LobbyScene.unity`
- 点 Play，应该看到 Lobby UI（CONNECT / QUIT 按钮）

## 项目结构

```
Assets/Scripts/
├── Editor/
│   └── MultiplayerSetupEditor.cs    # Unity 菜单工具（创建 prefab/场景）
├── Multiplayer/
│   ├── NetworkFishingController.cs   # 核心网络控制器
│   ├── FishingStateMachine.cs        # 服务端钓鱼状态机
│   ├── FishingPresenter.cs           # 客户端视觉驱动
│   ├── FishingUI.cs                  # 游戏内 UI（进度条、ESC 菜单）
│   ├── LobbyUI.cs                   # 大厅 UI
│   ├── NetworkPlayerSetup.cs         # 本地/远程玩家组件配置
│   ├── NetworkFishingFloat.cs        # 网络浮漂
│   ├── NetworkFishingRod.cs          # 网络鱼竿弯曲同步
│   ├── HeadlessAutoStart.cs          # 服务器自动启动 / 客户端连接
│   ├── ItemInfoBinder.cs             # 场景物品信息绑定
│   ├── ScreenLogger.cs               # 屏幕日志
│   ├── ServerFrameLimiter.cs         # 服务器帧率限制
│   ├── FishingLootCalculator.cs      # 渔获计算
│   └── FishingState.cs               # 状态枚举
└── Tests/                            # 编辑器测试
```

## 构建与部署

详见 [DEPLOYMENT.md](DEPLOYMENT.md)

## 技术债务

详见 [TODO.md](TODO.md)
