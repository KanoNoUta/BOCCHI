<a href='https://ko-fi.com/I2I01E6IBC' target='_blank'><img height='36' style='border:0px;height:36px;' src='https://storage.ko-fi.com/cdn/kofi5.png?v=6' border='0' alt='Buy Me a Coffee at ko-fi.com' /></a>

# BOCCHI

BOCCHI 国服维护版，当前适配国服 7.55 新月岛南部与北部。

- 本维护仓库：[KanoNoUta/BOCCHI](https://github.com/KanoNoUta/BOCCHI)
- 国服插件库：[KanoNoUta/DalamudPlugins](https://github.com/KanoNoUta/DalamudPlugins)
- 原始项目：[OhKannaDuh/BOCCHI](https://github.com/OhKannaDuh/BOCCHI)
- 前序国服维护：[NiGuangOwO/OccultCrescentHelper](https://github.com/NiGuangOwO/OccultCrescentHelper)

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
- Auto buffs (Bard/Knight/Monk)

## Plans

- Carrot hunter

## CN build and smoke test

```powershell
dotnet restore BOCCHI\BOCCHI.csproj --locked-mode
dotnet build BOCCHI\BOCCHI.csproj -c Release_CN -p:Platform=x64 --no-restore
dotnet run --project tests\BOCCHI.DataSmoke\BOCCHI.DataSmoke.csproj -c Release_CN -p:Platform=x64
```

## Known issues

- North Horn real-time treasure/carrot radar is supported, but automated hunting remains disabled until North Horn
  node levels and precomputed vnavmesh routes are collected in game.
- North Horn Forked Tower trap layouts are not available yet; South Horn trap coordinates are never reused there.
