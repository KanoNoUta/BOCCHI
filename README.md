<a href='https://ko-fi.com/I2I01E6IBC' target='_blank'><img height='36' style='border:0px;height:36px;' src='https://storage.ko-fi.com/cdn/kofi5.png?v=6' border='0' alt='Buy Me a Coffee at ko-fi.com' /></a>

# BOCCHI

BOCCHI 国服维护版，当前适配国服 7.55 新月岛南部与北部。

- 维护与国服 7.55 适配：KanoNoUta
- 本维护仓库：[KanoNoUta/BOCCHI](https://github.com/KanoNoUta/BOCCHI)
- 国服插件库：[KanoNoUta/DalamudPlugins](https://github.com/KanoNoUta/DalamudPlugins)

自定义插件仓库地址：

```text
https://raw.githubusercontent.com/KanoNoUta/DalamudPlugins/main/pluginmaster.json
```

## Features

- Occult Crescent: South Horn and North Horn (CN 7.55)

- Treasure radar & hunter
    - Lists nearby treasure and draws a line to them
    - Automatically make your way around the map looting chests
- Carrot radar
    - Lists nearby carrots and draws a line to them
- Silver/Gold per hour tracker
- Exp per hour tracker
- Active Fate/CE tracker
    - Displays demiatma, notes & soul shards dropped by Fate/CE
    - Displays Fate/CE progress
    - Displays estimated completion time
    - Button to teleport, mount and pathfind to Fate/CE
    - Automatic return after Fate/CE
- Auto buffs and actions for all supported South Horn and North Horn auxiliary jobs
- Forked Tower event timer, progress tracking, and territory-safe trap capture
- Per-event FATE/CE rewards, investigation records, alerts, and Automator controls

## CN build and smoke test

```powershell
dotnet restore BOCCHI\BOCCHI.csproj --locked-mode
dotnet build BOCCHI\BOCCHI.csproj -c Release_CN -p:Platform=x64 --no-restore
dotnet run --project tests\BOCCHI.DataSmoke\BOCCHI.DataSmoke.csproj -c Release_CN -p:Platform=x64
```

## Known issues

- North Horn treasure and carrot automation can run with direct-distance fallback routes. Runtime coordinates are cached
  after discovery, and the debug panels can precompute persistent vnavmesh routes. A cold start with no trustworthy
  coordinates will ask you to collect nearby nodes in game first.
- North Horn Forked Tower supports independent event tracking and safe live capture. Exact platform centers, radii,
  and complete trap layouts still require in-game samples; South Horn trap coordinates are never reused there.
- Soul-crystal items `51967`–`51974` are supported by the UI, but their exact source events remain marked as pending
  verification until confirmed from live CN 7.55 data.
