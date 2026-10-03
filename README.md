# AffineDeployer

把 `affine_io.dll` 和游戏灯光转发整合成一个 DLL，并自动安装到游戏目录。主要配合 [Curva G431 主控](https://github.com/QHPaeek/Mai_stm32/tree/G431)使用。

## 怎么用

先退出游戏和 AMDaemon，再打开 `AffineDeployer.exe`：

1. 选择原版、未经整合的 `affine_io.dll`。
2. 选择游戏目录，也就是 `Sinmai.exe` 所在的文件夹。
3. 点击“一键整合并部署”，完成后用原来的 `start.bat` 启动。

程序会生成 `affine_io_single.dll`，修改启动脚本和 Segatools 的 DLL 路径，并配置 AquaMai 的投币功能。旧的 `AffineHostProbe.dll`、`AffineGameLights.dll` 会移入备份，Mods 里不用再放这两个插件。

每次部署都会保存原文件。需要回退时，选择同一个游戏目录，点击“恢复上次部署”。如果只想拿到整合后的 DLL，可以用“仅生成单 DLL”。详细步骤见 [使用说明](USAGE.txt)。

## 环境和已知限制

部署器是单个 EXE，需要 Windows x64 和 .NET Framework 4.7.2 或更高版本。游戏目录需已有 Segatools/mai2hook、inject、MelonLoader net35 和 AquaMai。

目前在 SDGB1.56、MelonLoader 0.6.4、AquaMai 1.9.18 上测试过，实体投币加点和开始游戏扣点正常。投币通过主控输入处理，不模拟键盘；点数只在这次游戏运行期间保留，重启后清零，最高24点。

灯光转发覆盖两侧八键和机身三路，广告牌灯沿用原来的输出方式。灯光接口调用已检查，实际接灯效果还需要验证。

更新 `affine_io.dll` 后可以重新整合。如果新版改了接口或协议，也可能需要更新部署器。

## 编译

需要 .NET SDK、Zig 和 Windows SDK，以及 MelonLoader 的两个引用 DLL。完整步骤和测试命令见 [BUILDING.md](BUILDING.md)。

## 致谢与许可

感谢 [QHPaeek/Mai_stm32](https://github.com/QHPaeek/Mai_stm32) 提供 Curva 主控项目。

本工具源码采用 [MIT](LICENSE) 许可证，版权署名为 `2394641719-zyz`。原版 Affine DLL、主控固件和其他第三方组件保留各自的授权，见 [LICENSE-NOTICE.md](LICENSE-NOTICE.md)。
