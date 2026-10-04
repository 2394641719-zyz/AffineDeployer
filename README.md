# AffineDeployer

把 `affine_io.dll` 和实体 Coin 加点补丁整合成一个 DLL，并自动安装到游戏目录。主要配合 [Curva G431 主控](https://github.com/QHPaeek/Mai_stm32/tree/G431)使用。

## 怎么用

先退出游戏和 AMDaemon，再打开 `AffineDeployer.exe`：

1. 选择原版、未经整合的 `affine_io.dll`。
2. 选择游戏目录，也就是 `Sinmai.exe` 所在的文件夹。
3. 点击“一键整合并部署”，完成后用原来的 `start.bat` 启动。

程序会生成 `affine_io_single.dll`，修改启动脚本和 Segatools 的 DLL 路径，并配置 AquaMai 的投币功能。旧的 `AffineHostProbe.dll`、`AffineGameLights.dll` 会移入备份，Mods 里不用再放这两个插件。

每次部署都会保存原文件。需要回退时，选择同一个游戏目录，点击“恢复上次部署”。如果只想拿到整合后的 DLL，可以用“仅生成单 DLL”。详细步骤见 [使用说明](USAGE.txt)。

## Hook 注入和启动脚本

一键部署会修改游戏目录里的 `start.bat`：用 `inject.exe` 将原有的 `mai2hook.dll` 分别注入 AMDaemon 和 Sinmai，并在两者启动之间等待 3 秒。`mai2hook.dll` 本身不会被替换。

手动安装时，先备份启动脚本，确认 `inject.exe` 和 `mai2hook.dll` 都在游戏根目录。启动命令可以参考：

```bat
@echo off
set OPENSSL_ia32cap=:~0x20000000
pushd "%~dp0"

start /min "" inject.exe -d -k mai2hook.dll amdaemon.exe -f -c config_common.json config_server.json config_client.json
timeout /t 3 /nobreak >nul
inject.exe -d -k mai2hook.dll Sinmai.exe -screen-fullscreen 1 -screen-width 4320 -screen-height 3840 -silent-crashes -monitor 2
taskkill /f /im amdaemon.exe
popd
```

示例中的分辨率和 `-monitor` 参数来自开发时的机器，使用时保留你原脚本里适合自己显示器的参数。`taskkill` 用于游戏退出后结束 AMDaemon。

整合 DLL 由 `segatools.ini` 加载，在 `[mai2io]` 下设置：

```ini
[mai2io]
path=affine_io_single.dll
```

不用再用 `inject.exe` 注入 `affine_io_single.dll`。投币桥接还需要启用 AquaMai 的 `[GameSystem.VirtualCoin]`，具体配置见 [手动安装说明](MANUAL-INSTALL.txt)。本版不附加灯光转发；现有 hook 配置下的灯光效果请在自己的设备上确认。

## 环境和已知限制

部署器是单个 EXE，需要 Windows x64 和 .NET Framework 4.7.2 或更高版本。游戏目录需已有 Segatools/mai2hook、inject、MelonLoader net35 和 AquaMai。

目前在 SDGB1.56、MelonLoader 0.6.4、AquaMai 1.9.18 上测试过，实体投币加点和开始游戏扣点正常。投币通过主控输入处理，不模拟键盘；点数只在这次游戏运行期间保留，重启后清零，最高24点。

本版不初始化或转发灯光，灯光由现有 hook 处理。新版成品仍需实机验证投币和稳定性。

更新 `affine_io.dll` 后可以重新整合。如果新版改了接口或协议，也可能需要更新部署器。

## 编译

需要 .NET SDK、Zig 和 Windows SDK，以及 MelonLoader 的两个引用 DLL。完整步骤和测试命令见 [BUILDING.md](BUILDING.md)。

## 致谢与许可

感谢 [QHPaeek/Mai_stm32](https://github.com/QHPaeek/Mai_stm32) 提供 Curva 主控项目。

本工具源码采用 [MIT](LICENSE) 许可证，版权署名为 `2394641719-zyz`。原版 Affine DLL、主控固件和其他第三方组件保留各自的授权，见 [LICENSE-NOTICE.md](LICENSE-NOTICE.md)。
