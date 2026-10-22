<p align="center">
  <img src="assets/icon.png" width="128" alt="BOCCHI">
</p>

<h1 align="center">BOCCHI · 新月岛综合辅助</h1>

<p align="center">
  国服维护版，适配国服 7.55 新月岛南部与北部
</p>

<p align="center">
  <a href="CHANGELOG.md"><img alt="版本" src="https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fraw.githubusercontent.com%2FKanoNoUta%2FDalamudPlugins%2Fmain%2Fpluginmaster.json&query=%24%5B%3F(%40.InternalName%3D%3D'BOCCHI')%5D.AssemblyVersion&label=%E7%89%88%E6%9C%AC&color=ff7aa8"></a>
  <img alt="国服 7.55" src="https://img.shields.io/badge/%E5%9B%BD%E6%9C%8D-7.55-4c8bf5">
  <img alt="Dalamud" src="https://img.shields.io/badge/Dalamud-CN-7a5cff">
  <a href="LICENSE.md"><img alt="License" src="https://img.shields.io/github/license/KanoNoUta/BOCCHI?color=999"></a>
</p>

---

## 安装

1. 在游戏里打开卫月设置（`/xlsettings`）→ **测试版** → **自定义插件仓库**。
2. 添加下面这个地址并保存：

   ```text
   https://raw.githubusercontent.com/KanoNoUta/DalamudPlugins/main/pluginmaster.json
   ```

3. 打开插件安装器（`/xlplugins`），搜索 **BOCCHI** 安装。
4. 进入新月岛后用 `/bocchi` 打开主界面，`/bocchicfg` 打开设置。

> [!NOTE]
> 寻路和传送需要 **vnavmesh** 与 **Lifestream**；自动战斗可配合 **PromeRotation** 以及 **BossMod** / **BossMod Reborn** 的 AI。

## 功能一览

<table>
<tr>
<td width="50%" valign="top">

### 🗺️ 信息面板
- FATE / CE 面板：奖励（魂晶、图鉴、碎晶）、进度和预计完成时间
- 一键传送、上坐骑并寻路到 FATE / CE
- 两歧塔事件计时、进度和陷阱记录
- 金币、银币、经验每小时统计，可选 5 分钟趋势图
- 魔法罐刷新计时与倒计时

</td>
<td width="50%" valign="top">

### 💰 寻宝
- 宝箱、胡萝卜雷达与连线
- 宝箱猎人：自动规划路线、开箱，可暂停 / 继续 / 重新规划
- 南岛预设分区路线，可选起始区域；北岛动态安全路线
- 只开银箱、危险天气与夜间暂停、定时重放寻宝术
- 危险路段可选忍者隐遁

</td>
</tr>
<tr>
<td width="50%" valign="top">

### 🤖 自动运行
- 按距离和进度挑选 FATE / CE，途中可改去报名中的 CE
- 魔法罐蹲点、罐后宝箱、“罐子与寻宝”模式
- 活动间隙自动寻宝、营地空闲自动修理
- 两歧塔报名时自动去报名点
- 分诊复活模式、图鉴补全模式
- 副本轮换，即刻退本可选 Omni / XSZToolbox / DailyRoutines

</td>
<td width="50%" valign="top">

### ⚔️ 辅助职业与刷怪
- 南北岛所有辅助职业的自动增益与技能
- 刷怪：多个命名刷怪点、被打还手、为魔法罐和寻宝让位
- 回城、以太之光传送、冲刺赶路
- 手动放技能时停止寻路、回城传送后停下（只传送不走路）
- 更新日志窗口（`/bocchi changelog`）与日志查看器（`/bocchi logs`）

</td>
</tr>
</table>

## 常用指令

| 指令 | 作用 |
| --- | --- |
| `/bocchi` | 打开主界面 |
| `/bocchicfg` | 打开设置 |
| `/bocchi th` | 开关宝箱猎人 |
| `/bocchi changelog` | 查看本版本更新内容 |
| `/bocchi logs` | 打开日志窗口，反馈问题时可一键复制 |

## 更新日志

每个版本的改动见 [CHANGELOG.md](CHANGELOG.md)，更新后第一次加载插件也会自动弹出。

## 已知问题

- 北岛寻宝直接读取实时布局节点，没有预计算文件也能通过强制回城或以太之光恢复路线。胡萝卜坐标在发现后才会缓存，冷启动时需要先在游戏里靠近收集一次。
- 北岛两歧塔支持事件追踪和实时陷阱记录，但平台中心、半径和完整陷阱布局仍需游戏内采样；南岛的陷阱坐标不会套用到北岛。
- 魂晶物品 `51967`–`51974` 已在界面中支持，具体掉落来源仍待国服 7.55 实测确认。
- 新移植的功能大多未经游戏内实测，遇到问题请附上 `/bocchi logs` 里复制的日志反馈。

## 自行构建

```powershell
dotnet restore BOCCHI\BOCCHI.csproj --locked-mode
dotnet build BOCCHI\BOCCHI.csproj -c Release_CN -p:Platform=x64 --no-restore
dotnet run --project tests\BOCCHI.DataSmoke\BOCCHI.DataSmoke.csproj -c Release_CN -p:Platform=x64
```

## 致谢

- 原作者 [OhKannaDuh](https://github.com/OhKannaDuh/BOCCHI)，大部分新功能移植自上游 BOCCHI 4.2；喜欢的话可以去[支持原作者](https://ko-fi.com/I2I01E6IBC)
- 早期国服移植 [NiGuangOwO](https://github.com/NiGuangOwO/BOCCHI)
- 国服 7.55 适配与维护：KanoNoUta、岚玉棠
