# 多人钓鱼游戏 - 部署指南

## 服务器信息

- IP: `47.95.178.225`
- 系统: Ubuntu 24.04
- 用户: `root`（SSH 密钥认证，无需密码）
- 端口: `7777 UDP`（Mirror KCP 传输层）
- 部署路径: `/root/server/`

## Unity Build 步骤

### 前置：修复 Prefab（如有代码改动）

Unity 菜单 → `Tools > Multiplayer Fishing Setup > 4. Fix Existing Prefabs`

### Build Server

1. `File > Build Settings`
2. Target Platform: `Linux`
3. Server Build: ✅ 勾选 `Server Build` / `Dedicated Server`
4. 输出路径: `D:\Unity\Project\server`
5. 可执行文件名: `server.x86_64`

### Build Client

1. `File > Build Settings`
2. Target Platform: `Windows`
3. Server Build: ❌ 不勾选
4. 输出到你想要的路径

## 服务器部署

### 1. 停止旧进程

```bash
ssh root@47.95.178.225 "pkill -f server.x86_64 || true"
```

### 2. 上传新 build

```bash
scp -r "D:\Unity\Project\server" root@47.95.178.225:/root/
```

### 3. 启动服务器

```bash
ssh root@47.95.178.225 "chmod +x /root/server/server.x86_64; cd /root/server; nohup ./server.x86_64 -batchmode -nographics > /root/server.log 2>&1 &"
```

### 4. 验证运行

```bash
ssh root@47.95.178.225 "ps aux | grep server.x86_64 | grep -v grep"
```

### 5. 查看日志

```bash
ssh root@47.95.178.225 "tail -50 /root/server.log"
```

## 一键部署（合并命令）

```bash
ssh root@47.95.178.225 "pkill -f server.x86_64 || true" && scp -r "D:\Unity\Project\server" root@47.95.178.225:/root/ && ssh root@47.95.178.225 "chmod +x /root/server/server.x86_64; cd /root/server; nohup ./server.x86_64 -batchmode -nographics > /root/server.log 2>&1 &"
```

## 常用运维命令

| 操作 | 命令 |
|------|------|
| 查看进程 | `ssh root@47.95.178.225 "ps aux \| grep server"` |
| 实时日志 | `ssh root@47.95.178.225 "tail -f /root/server.log"` |
| 停止服务器 | `ssh root@47.95.178.225 "pkill -f server.x86_64"` |
| 查看端口 | `ssh root@47.95.178.225 "ss -ulnp \| grep 7777"` |
| 查看内存 | `ssh root@47.95.178.225 "free -h"` |

## 客户端连接

客户端通过 `HeadlessAutoStart.cs` 自动连接到 `47.95.178.225:7777`，无需手动输入。
