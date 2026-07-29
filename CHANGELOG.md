# 3.3.7

- 紧急修复 3.3.6 自动模式会停在 `PlanningRoute` 智能路线预计算阶段、角色完全不移动的问题
- 实时 FATE/CE 自动赶路恢复为立即按距离成本选择路线并出发
- 多候选 vnavmesh 路线预计算继续保留为独立能力，但不再位于自动模式的必经路径上

# 3.3.6

- 接入 PromeRotation 官方 IPC：赶路、暂停、换图、退出活动和 MobFarmer 采集阶段自动停止，进入 FATE/CE 或刷怪战斗时自动启动
- 合并 PR #1 的智能路线规划、FATE 目标丢失恢复和可选自动换岛功能
- 智能路线候选改为严格串行调用 vnavmesh，避免并发预计算重新触发 `Pathfinding task is in progress`
- 自动模式只有实际观察到 FATE/CE 参与状态后才允许以 `Idle` 判定结束，修复到场前反复重选同一事件的三秒走停循环
- FATE 目标死亡或消失时等待当前算路完成并节流恢复事件主路线，不再拆掉整条活动链
- 暂停自动模式时安全检查 vnavmesh IPC，未加载或尚未注册时不再抛出异常

# 3.3.5

- 修复自动模式只检查 `Path.IsRunning`，在 vnavmesh 仍计算路线时误判停止并重复提交的问题
- 修复 `Pathfinding task is in progress...` 高频刷屏及由重复重寻路造成的走走停停
- FATE 追踪只接受当前活动的敌人，并为移动目标重寻路加入计算状态门闩与 1 秒节流
- CE 最后一段随机落点会等待已有算路结束，并仅在 vnavmesh 接受请求后切换状态

# 3.3.4

- 修复北岛 FATE 2075“诅咒宝珠——邪瞳”的魔路点映射，固定使用右上方“妖火湖北岸”
- 自动器与事件面板不再先传送到中间“妖火渔村”；已验证的南侧绕行路线继续作为人在中间区域时的兜底

# 3.3.3

- 修复北岛 CE 63“拟态使魔——变形法师”的魔路点映射
- 自动器、事件面板手动传送和传送后寻路统一使用右上方“妖火湖北岸”，不再落到中间“妖火渔村”

# 3.3.2

- 修复 92 点北岛 FATE 2075 绕行路线会受到默认 30 秒寻路等待限制而被提前打断的问题
- 为该路线加入专用长时等待与提前停止保护；未抵达东岸终点时不会回退到可能穿河的普通寻路
- 抵达已验证的 FATE 范围内终点后直接进入参与阶段，避免不必要的二次路径计算

# 3.3.1

- 修复北岛 FATE 2075 从魔路点出发时会被 vnavmesh 引向河面的跨河失败问题
- 新增经逐段地面验证的 92 点南侧绕行路线，并使用 `FollowPath` 保持该路线而不重新规划水面捷径
- 自动寻路、手动寻路与传送后寻路都会在需要时先上坐骑；补充路线完整性烟测

# 3.3.0

- 完整适配国服 7.55 新月岛北部
- 新增北岛 FATE、CE、调查记录、8 个辅助职业与双塔事件支持
- 宝箱与兔子胡萝卜加入运行时路线、混合路径、超时重试、不可达跳过和采集缓存
- 修复 CE 原生对象生命周期问题，避免事件消失后访问失效指针导致崩溃
- 修复双塔轮次重置、延迟回调污染和取消事件误计完成等问题
- 调查记录、事件奖励与提醒支持逐项配置；北岛新魂晶来源在实机确认前明确标记为待确认

# 3.2.1

- Fixed an `AccessViolationException` when logging a despawned FATE
- Snapshot FATE names, positions, radii, and progress while Dalamud's `IFate` is still valid
- Preserve tracked FATE instances across framework ticks instead of retaining invalid game-memory handles

# 3.2.0

- Added Chinese 7.55 support for Occult Crescent: North Horn (territory 1346)
- Added North Horn aethernet, FATE, critical encounter, tower, currency, and support-job data
- Added North Horn field-monster IDs and raised mob/pathfinder level limits to 40
- Made zone, base-camp, aethernet, currency, and event handling territory-aware
- Added Simplified Chinese support-experience parsing and fixed treasure-count macro parsing
- Disabled precomputed treasure/carrot hunting safely when current-zone route data is unavailable
- Allowed Release_CN builds to initialize when loaded as a local development plugin

# 0.11.0

- Updated UI to include both a teleport and move to button
- Can no longer click teleport if you are already next to the destination aetheryte
- Updated aethenet shard for Brain Drain
- Added some custom paths for certain fates, so that the path taken to walk to them is more natural

# 0.12.0

- Removed Crowdsourcing module
- Added WindowManager Module
    - This module allow you to configure if the main and config windows open and close on plugin load, enter zone and
      exit zone

# 0.12.1

- Changed labels in WindowManager config slightly
