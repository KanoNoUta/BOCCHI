# Local Test Conventions

- The local BOCCHI test DLL must be built to `BOCCHI/bin/Release_CN/BOCCHI.dll`.
- Use `dotnet build BOCCHI/BOCCHI.csproj -c Release_CN --no-restore`; do not add `-p:Platform=x64` for the local test copy, because that writes to `bin/x64/Release_CN` instead.
- After CE crowdsource changes, run `dotnet run --project tests/BOCCHI.DataSmoke/BOCCHI.DataSmoke.csproj -c Debug --no-build -- --ce-crowdsource`.
- CE crowdsource history uses the server aggregate query `zone=0`; ended records remain visible and stale `Battle` observations render as ended.

# BossModReborn Local Test Conventions

- Build the test artifact from `BossmodRebornWork/BossMod/bin/Release/BossModReborn.dll`.
- Do not deploy the Debug artifact for gameplay testing.
- The local dev/test DLL is `BossmodRebornWork\BossMod\bin\Release\BossModReborn.dll`; Dalamud dev plugin settings point directly to this file.
- Do not treat `C:\Users\Administrator\AppData\Roaming\XIVLauncherCN\installedPlugins\BossModReborn\7.5.5.8\BossModReborn.dll` as the active test copy unless explicitly requested.
- Before replacing the active test DLL, preserve the existing file as `BossModReborn.dll.bak-<timestamp>` when practical.
- After building, verify the active test DLL SHA-256 matches the Release artifact.
- The active dev plugin may need a BossModReborn reload before the new DLL is used.

# BOCCHI 发布流程

- 源码仓库：`F:\project\卫月插件`，发布远端为 `maintainer`，国服开发分支为 `cn`，公开主分支为 `main`。
- 插件库仓库：`F:\project\卫月插件\DalamudPlugins-KanoNoUta`，远端为 `origin`，分支为 `main`；插件库地址为 `https://raw.githubusercontent.com/KanoNoUta/DalamudPlugins/main/pluginmaster.json`。
- 发布前同时更新 `BOCCHI/BOCCHI.csproj` 的 `<Version>`、`BOCCHI/BOCCHI.json` 的 `AssemblyVersion` 与 `Changelog`、根目录 `CHANGELOG.md`；manifest 版本使用四段格式，例如项目版本 `3.3.26` 对应 `3.3.26.0`。
- 先执行 `git fetch maintainer` 并用 `git rev-list --left-right --count cn...maintainer/main` 确认远端未领先；不得 reset 或混入分析目录、回放、备份、根目录旧压缩包及其他插件工程。
- 本地测试构建固定执行 `dotnet build BOCCHI/BOCCHI.csproj -c Release_CN --no-restore`，产物必须是 `BOCCHI/bin/Release_CN/BOCCHI.dll`，不要加 `-p:Platform=x64`。
- 构建 smoke 固定执行：`dotnet build tests/BOCCHI.DataSmoke/BOCCHI.DataSmoke.csproj -c Debug --no-restore`、`dotnet run --project tests/BOCCHI.DataSmoke/BOCCHI.DataSmoke.csproj -c Debug --no-build`；CE 相关改动还要执行带 `-- --ce-crowdsource` 的专项 smoke。
- 源码提交时精确暂存本次发布文件，提交信息使用 `发布 BOCCHI <version>：<摘要>`；推送命令为 `git push maintainer cn:main`，推送后按需创建同版本 tag。
- 发布包从 `BOCCHI/bin/Release_CN` 完整输出制作，写入 `DalamudPlugins-KanoNoUta/plugins/BOCCHI/latest.zip`；zip 根目录必须直接包含 `BOCCHI.dll`、`BOCCHI.json`、依赖 DLL、`icon.png`、`assets/`、`Data/`、`Translations/`，不能额外套一层目录。
- 同步 `DalamudPlugins-KanoNoUta/plugins/BOCCHI/BOCCHI.json` 和 `plugins/BOCCHI/icon.png`，然后在插件库根目录运行 `python generate_pluginmaster.py`；生成器以 zip 根目录内的 `BOCCHI.json` 为版本真源。
- 打包后检查 zip 内 manifest 版本、文件清单和 DLL SHA-256；zip 内 `BOCCHI.dll` 哈希必须等于 `BOCCHI/bin/Release_CN/BOCCHI.dll`。
- 插件库只暂存 `plugins/BOCCHI/BOCCHI.json`、`plugins/BOCCHI/latest.zip`、`plugins/BOCCHI/icon.png`、`pluginmaster.json`，提交信息使用 `Publish BOCCHI <version>`，然后执行 `git push origin main`。
- 发布后从 raw 地址重新下载 `pluginmaster.json` 和 `plugins/BOCCHI/latest.zip`：catalog 中 `InternalName = BOCCHI` 的 `AssemblyVersion`、changelog 和下载链接必须正确，包必须 HTTP 200，下载包及包内 DLL 哈希必须匹配本地发布件。

# BossModReborn 发布流程

- 源码仓库：`F:\project\卫月插件\BossmodRebornWork`。
- 国服插件目录仓库：`F:\project\卫月插件\DalamudPlugins-KanoNoUta`。
- 本地测试 DLL 唯一路径：`F:\project\卫月插件\BossmodRebornWork\BossMod\bin\Release\BossModReborn.dll`。
- 编译命令：`dotnet build BossMod/BossModReborn.csproj -c Release --no-restore -v:minimal -p:DalamudLibPath='C:\Users\Administrator\AppData\Roaming\XIVLauncherCN\addon\Hooks\dev\'`。
- 编译后用 `Get-FileHash BossMod/bin/Release/BossModReborn.dll -Algorithm SHA256` 记录 Release DLL 哈希。
- 源码推送目标为 `kano` 远端的当前开发分支；只提交确认过的源码和 `manifest.json`，不要把 `analysis/`、`build/`、`tests/`、回放或临时目录混进提交。
- 发布包固定放在 `DalamudPlugins-KanoNoUta/plugins/BossModReborn/latest.zip`，压缩包根目录必须包含：`BossModReborn.dll`、`BossModReborn.pdb`、`BossModReborn.json`、`DefaultRotationPresets.json`。
- 发布包使用 Release 输出，不得使用 Debug DLL；替换 `latest.zip` 前保留一个带时间戳的 `.bak` 备份。
- 更新 `DalamudPlugins-KanoNoUta/plugins/BossModReborn/BossModReborn.json` 的 `Changelog`，并同步 `pluginmaster.json` 中 `InternalName = BossModReborn` 的 `Changelog`、`AssemblyVersion` 和 `LastUpdate`。
- 发布仓库推送命令：`git add plugins/BossModReborn/BossModReborn.json plugins/BossModReborn/latest.zip pluginmaster.json; git commit -m \"Publish BossModReborn <version>\"; git push origin main`。若远端领先，先 `git pull --rebase origin main`，解决仅限 BossModReborn 条目的冲突后再推送。
- 发布后验证两个链接：
  - catalog：`https://raw.githubusercontent.com/KanoNoUta/DalamudPlugins/main/pluginmaster.json`
  - package：`https://raw.githubusercontent.com/KanoNoUta/DalamudPlugins/main/plugins/BossModReborn/latest.zip`
- catalog 中应显示最新版本和 changelog；package 返回 HTTP 200，且其内容用 `tar -tf` 检查为上述四个文件。Dalamud 可能缓存 catalog，必要时刷新插件列表或重启后再测试。
