# 构建与验证

需要 Windows x64、.NET SDK 8 或更高、.NET Framework 4.7.2 开发目标包、Zig 0.14.1 和 Windows SDK（x64 kernel32.lib）。本机使用 Windows SDK 10.0.26100.0 验证。

还需与目标游戏匹配的 MelonLoader net35 `MelonLoader.dll` 和 `0Harmony.dll`。引用不随本仓库分发，可通过游戏目录提供：

```powershell
.\build.ps1 -GameRoot 'C:\Games\Sinmai' -ZigPath 'C:\Tools\zig\zig.exe'
```

也可只指定引用目录，不需要游戏本体参与构建部署器：

```powershell
.\build.ps1 -MelonLoaderDir 'C:\References\MelonLoader\net35' -ZigPath 'C:\Tools\zig\zig.exe'
```

Zig 已在 PATH 时可省略 `-ZigPath`。Windows SDK 库默认自动寻找，可用 `-Kernel32Lib` 指定。输出默认在 `artifacts/`，可用 `-OutputDir` 改写。

## 完整验证

选择本地未经整合的原版 Affine DLL，构建成品并执行测试：

```powershell
.\build.ps1 -GameRoot 'C:\Games\Sinmai' -ZigPath 'C:\Tools\zig\zig.exe' -AffineDll 'C:\Inputs\affine_io.dll' -RunTests
```

脚本依次构建原生模板、托管桥接和部署器；输入原版 DLL 时，检查原生导出、原始分区字节、内嵌载荷和 Windows LoadLibrary。`-RunTests` 还执行投币算法与隔离部署/恢复测试。部署测试要求 Sinmai 和 AMDaemon 都已退出。

只构建部署器不需要原版 Affine DLL；不提供 `-AffineDll` 时部署测试跳过。隔离测试不会修改真实游戏目录。

投币算法测试使用模拟 AMDaemon/AquaMai 数据，不能替代游戏中 Mono 加载、硬件输入与实际扣点验证。灯光实体效果必须接灯实测。

## 源码顺序

`integrator` 嵌入 `build/bootstrap.dll` 和 `managed/bin/Release/net472/AffineSingle.Managed.dll`。它们由脚本先生成，不是待提交文件；请先运行根目录 `build.ps1`，不要直接从空目录构建 integrator 项目。

`Directory.Build.props` 为引用 DLL 提供统一路径，也支持 MSBuild `-p:MelonLoaderDir=...` 或 `-p:GameRoot=...`。

## Release 内容

- `AffineDeployer.exe`：发布给用户的单文件部署器。
- `USAGE.txt`、`SHA256SUMS.txt`：说明与校验。
- 输入原版 DLL 后还会输出 `affine_io_single.dll` 和 `MANUAL-INSTALL.txt`；是否分发整合 DLL，应由原版 DLL 的授权决定。
