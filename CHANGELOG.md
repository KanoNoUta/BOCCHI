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
