# TODO — 技术债务 & 待优化

## 架构重构（核心玩法稳定后）

- [ ] `NetworkFishingController` 职责过重，需要拆分：
  - `PlayerInputController` — 所有本地输入（F装备、Fire1/Fire2钓鱼、ESC菜单），分发给对应系统
  - `PlayerEquipment` — 装备状态（SyncVar、显隐），独立 NetworkBehaviour，为多工具切换做准备
  - `NetworkFishingController` — 只保留钓鱼状态机 + 网络同步

## 装备系统

- [ ] F 键切换是临时方案，后续改为背包系统驱动装备
- [ ] 装备/收起缺少过渡动画（当前瞬间出现/消失）
- [ ] 如果有多种工具（不同鱼竿、网兜等），需要通用装备槽系统替代单 bool

## UI

- [ ] 所有 UI 文字目前是英文（LiberationSans 不支持中文），正式做 UI 时需导入中文 TMP 字体
- [ ] UI 全部运行时代码创建，后续考虑改为 prefab 方式管理

## 网络 / 同步

- [ ] 渔获（鱼 prefab）没有 NetworkIdentity，客户端看不到别人钓上来的鱼
- [ ] 需要实现渔获展示系统（钓上鱼后举起展示，所有玩家可见）

## 已知问题

- [ ] 关闭窗口进程残留 — 已加 Process.Kill 兜底，但根因是 KCP 后台线程阻塞退出
- [ ] 服务器日志有 Convex Mesh 警告（地形石头模型面数过多），不影响功能
