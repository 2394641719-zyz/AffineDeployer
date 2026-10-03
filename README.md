# AffineDeployer

面向 **Curva 开源主控** 的整合部署配套工具。上游项目：[QHPaeek/Mai_stm32](https://github.com/QHPaeek/Mai_stm32)，当前适配参考 G431 分支。感谢 Curva 项目及其维护者。

Windows 单文件整合部署器：将用户提供的原生 `affine_io.dll` 与灯光转发、实体 Coin 游戏端加点桥接整合成一个 DLL，并部署到已有游戏环境。

## 使用

运行构建生成的 `AffineDeployer.exe`，选择未经整合的原版 `affine_io.dll` 和游戏根目录，点击“一键整合并部署”。部署前退出游戏和 AMDaemon。

部署器会生成 `affine_io_single.dll`，修改 `start.bat`、`segatools.ini` 和 AquaMai VirtualCoin 配置，备份文件并禁用旧 `AffineHostProbe.dll` / `AffineGameLights.dll`。可恢复上一次部署，也可仅生成 DLL。

部署器无需旁置模板 DLL。运行需要 Windows x64 和 .NET Framework 4.7.2 或更高版本。游戏需已有 Segatools/mai2hook、inject、MelonLoader net35 和 AquaMai。详见 [USAGE.txt](USAGE.txt)。

## 当前验证范围

在 SDGB1.56、Unity 2018.4.7f1、MelonLoader 0.6.4 net35、AquaMai 1.9.18 环境验证：原生 I/O、实体 Coin 加点和开始游玩扣点正常。

投币桥接读取 AMDaemon USB 计数，通过 AquaMai 游戏点数缓冲加点，不模拟键盘。点数重启清零；当前最高24点。原生后台账本开始计入投币时停止桥接，避免重复加点。

灯光转发已有接口发送记录；测试板未接灯，实体灯光效果尚未验证。灯光包括两侧八键和机身三路，广告牌灯仍走原链路。

PE 合并器检查结构和必要接口，不能保证任意新版本的接口参数或硬件协议兼容。每次更新需复测。

## 开发

见 [BUILDING.md](BUILDING.md)。源码自包含，第三方编译引用 DLL 由使用者自行提供，不随仓库分发。

- `native/`：原生启动器、Mono 接入。
- `managed/`：实体投币桥接和内嵌灯光代码。
- `integrator/`：GUI、PE 合并与部署/恢复。
- `tests/`：PE 保留与 Windows 加载验证。
- `coin_tests/`：投币计数算法测试。
- `deploy_tests/`：配置修改、重复部署与恢复测试。

## 发布准备

仓库不包含游戏本体、第三方 DLL、固件、个人配置或历史诊断日志。构建产物可作为 GitHub Release 附件上传，不提交到源码目录。

本项目采用 [MIT 许可证](LICENSE)。第三方文件的授权范围见 [LICENSE-NOTICE.md](LICENSE-NOTICE.md)。
