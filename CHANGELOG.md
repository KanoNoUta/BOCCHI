# 3.3.20

- 修复紧急停止后角色仍持续移动：FATE/CE 寻路监视与嵌套回城链在停止时立即感知并停止 vnavmesh，不再每 1-2 秒重新提交移动
- 修复 CE 出发延迟未可靠执行，改为在导航链创建时直接加入固定等待，不再依赖运行中临时插入延迟
- 延迟范围保留小数秒精度，并对反向、非有限值和越界配置进行安全归一化
- 日志记录每次 CE 出发实际等待时长，便于现场确认配置是否生效

# 3.3.19

- 重构整体 UI 与精简模式，自动运行改为单一启停按钮，启动、停止和依赖准备状态可见
- 北岛寻宝接入 68 点编号路线与高清路线图，支持就近起点、指定起点、水晶传送成本规划和实时宝箱定位
- 开箱后保持原编号顺序，实时宝箱插队时终止旧 vnavmesh 路线，避免跳号、走回头路或到箱子前不交互
- 修复每小时金币将进入时现有余额误计为收入，并修复暂停后 vnavmesh、Lifestream 或自动战斗状态残留
- 寻宝运行时独占导航，清理已排队的 FATE/CE 回城，阻止事件结束回调抢占寻宝路线

# 3.3.18

- 新增主页精简模式，在保留关键状态和控制入口的同时减少主窗口占用
- 普通怪危险区按玩家等级过滤，避免不会主动攻击当前等级玩家的怪物造成无意义绕行
- 新增 `/ochth`、`/bocchith` 与 `/bocchi th` 宝箱猎人快捷指令，可直接启动或停止寻宝
- 补充主页模式、等级避让策略、命令路由和宝箱猎人生命周期 smoke 测试
- 作者信息新增 岚玉棠，并同步项目与插件 manifest 元数据

# 3.3.14

- 重构主界面与设置界面，南岛、北岛的事件状态、FATE/CE 开关和刷怪目标分别显示与保存
- 旧版混合怪物配置自动迁移为南北岛独立列表，不丢失已有选择
- FATE 完成或失效后强制返回初始营地，回营地完成前不再选择下一活动
- PromeRotation 到达 FATE 后先选择有效怪物，再启动 ACR 与主动攻击；循环意外停止时自动恢复并在目标死亡后重新选怪
- 挖宝取消逐箱强制下坐骑，骑乘状态可直接开箱并继续前往下一节点

# 3.3.13

- 合并 PR #3，完善北岛传送落点、导航稳定停止与 vnavmesh 0.7.6.0 精确版本校验
- 挖宝接入完整 LGB 数据源，改用国服安全的 SimpleMove，避免 PathfindCancelable IPC 序列化异常
- FATE 结束后优先返回营地，Buff 按缺失状态分别补充，不再重复整套上 Buff
- 紧急停止统一终止挖宝、胡萝卜、刷怪、Chain、vnavmesh、Lifestream、AE 与 PromeRotation 状态

# 3.3.12

- 北岛水晶传送改为动态解析当前场景内水晶对象位置，不再只依赖静态维护坐标
- 交互判定统一使用 3.5 米实际距离，避免角色停在看似接近但客户端无法交互的位置
- 接入 Lifestream 传送状态、失败原因和序列号 IPC，准确区分排队、派发、完成与失败，避免自动导航误推进

# 3.3.11

- 合并 PR #2，收紧活动状态变化后的自动导航继续执行条件，结束或失效后不再提交移动、传送与路径请求
- CE 只在报名仍开放且尚未进入事件时接近，最终随机落点仅提交一次，不满足参与条件时安全结束
- 以太之光接近和传送落点统一使用维护坐标与真实交互范围，避免停在 Lifestream 无法交互的位置
- 补充导航策略、CE 最终落点、传送范围与相关回归 smoke 测试

# 3.3.10

- 完善北岛自动寻宝：启动时自动施放魔寻宝，按实时 Layout 节点规划路线，靠近实际宝箱、停止寻路、下坐骑并确认开启后才推进
- 北岛缺少预计算数据时动态比较直走、返回与水晶传送；拒绝 Partial 路线，并在跨河、断层或源水晶不可达时强制返回后改走目标水晶
- Return/Teleport 改为有超时、可取消、可验证的托管子链，停止或换图时不会留下孤儿任务，也不会在失败后无限重复
- 宝箱按节点 BaseId 精准匹配，扩大有限位置容差，避免相邻宝箱串箱；交互统一为三次有限重试，不再叠加成九次
- 胡萝卜兔箱加入新对象识别、靠近、节流交互、生成/交互超时和重复路线状态清理，失败不再永久卡住队列
- 智能路线会校验 vnavmesh 终点，未真正抵达终点的 Partial 路线不再参与 FATE、CE 或寻宝路径选择
- FATE/CE 会等待 vnavmesh 真正启动并校验最终落点，确认下坐骑后才开启自动循环；慢加载 Return/Teleport 不再被默认五秒超时误杀
- 自动器只把仍在运行或仍有待办项的 Ocelot 队列视为繁忙，已完成但等待清理的空队列不再让 FATE/CE 永久停在原地
- 自动切换低等级辅助职改为比较所有已解锁未满级职业的真实等级，自由人只有在实际最低时才会被选择
- 保留并整合 3.3.9 的连续移动、上/下坐骑、Lifestream IPC 兼容和旧 FATE 清理修复

# 3.3.9

- 兼容 Lifestream 部分 IPC 尚未注册或重启后延迟注册的情况，自动器会等待可用并在失败后安全恢复
- 修复移动中反复重算路线、先寻路后上坐骑、重复上坐骑及活动结束后重选旧 FATE 导致的走走停停
- 修复传送后只跑几步就下坐骑、原地反复下坐骑以及水晶传送步骤被提前推进的问题

# 3.3.8

- 修复 PromeRotation 未认证或 IPC 返回失败时被 Ocelot 当成未完成谓词，导致 FATE/CE 自动器链每帧重试且完全不移动的问题
- PromeRotation 启停改为非阻塞可选集成，失败日志限制为每项操作最多 30 秒一次
- 到达 FATE/CE 后由 50% 随机下坐骑改为必定下坐骑
- 修复尚无可选敌人的 FATE 到场后被当成寻路失败、反复重建活动的问题
- 修复 BossMod AI 在暂停非法模式、事件结束/失效或换图后可能残留开启的问题；非战斗状态启动非法模式时会先关闭 AI，进入 FATE/CE 后再开启

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
