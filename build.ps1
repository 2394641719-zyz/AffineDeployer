param(
    [string]$GameRoot,
    [string]$MelonLoaderDir,
    [string]$AffineDll,
    [string]$ZigPath,
    [string]$Kernel32Lib,
    [string]$OutputDir,
    [switch]$RunTests
)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
if(!$OutputDir){$OutputDir=Join-Path $root 'artifacts'}
$OutputDir=[IO.Path]::GetFullPath($OutputDir)
if(!$MelonLoaderDir){$MelonLoaderDir=if($GameRoot){Join-Path $GameRoot 'MelonLoader\net35'}else{Join-Path $root 'deps\MelonLoader\net35'}}
$MelonLoaderDir=[IO.Path]::GetFullPath($MelonLoaderDir)
foreach($name in @('MelonLoader.dll','0Harmony.dll')){if(!(Test-Path -LiteralPath (Join-Path $MelonLoaderDir $name))){throw "Missing $name. Use -GameRoot or -MelonLoaderDir; see BUILDING.md."}}
if(!$ZigPath){$command=Get-Command zig -ErrorAction SilentlyContinue;if($command){$ZigPath=$command.Source}else{throw 'Zig compiler missing. Put Zig 0.14.1 on PATH or pass -ZigPath.'}}
$ZigPath=(Resolve-Path -LiteralPath $ZigPath).Path
if(!$Kernel32Lib){
    $kits=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\Lib'
    if(Test-Path -LiteralPath $kits){$Kernel32Lib=Get-ChildItem -LiteralPath $kits -Directory | Where-Object {$_.Name -match '^\d+\.\d+\.\d+\.\d+$'} | Sort-Object {[version]$_.Name} -Descending | ForEach-Object {Join-Path $_.FullName 'um\x64\kernel32.lib'} | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1}
}
if(!$Kernel32Lib -or !(Test-Path -LiteralPath $Kernel32Lib)){throw 'Windows SDK kernel32.lib missing. Install Windows SDK or pass -Kernel32Lib.'}
$build=Join-Path $root 'build'
New-Item -ItemType Directory -Force -Path $build,$OutputDir | Out-Null
$env:ZIG_GLOBAL_CACHE_DIR=Join-Path $root '.cache\zig'
$native=Join-Path $build 'bootstrap.dll'
& $ZigPath cc -target x86_64-windows-msvc -std=c11 -Os -shared -nostdlib -fno-stack-protector (Join-Path $root 'native\bootstrap.c') '-Wl,--entry,affine_entry' $Kernel32Lib -o $native
if($LASTEXITCODE -ne 0){throw 'Native bootstrap compilation failed.'}
function BuildProject([string]$relative){
    dotnet build (Join-Path $root $relative) -c Release -p:NuGetAudit=false "-p:MelonLoaderDir=$MelonLoaderDir"
    if($LASTEXITCODE -ne 0){throw "Build failed: $relative"}
}
BuildProject 'managed\NativeBridge.csproj'
BuildProject 'integrator\AffineIntegrator.csproj'
$integrator=Join-Path $root 'integrator\bin\Release\net472\AffineIntegrator.exe'
$deployer=Join-Path $OutputDir 'AffineDeployer.exe'
Copy-Item -LiteralPath $integrator -Destination $deployer -Force
Copy-Item -LiteralPath (Join-Path $root 'USAGE.txt') -Destination (Join-Path $OutputDir 'USAGE.txt') -Force
$products=@($deployer)
$sample=$null
if($AffineDll){
    $AffineDll=(Resolve-Path -LiteralPath $AffineDll).Path
    $session=Join-Path $build ('validation-'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $session | Out-Null
    $sample=Join-Path $session 'affine_io_single.dll'
    $process=Start-Process -FilePath $integrator -ArgumentList @('--build',('"'+$AffineDll+'"'),('"'+$sample+'"')) -WindowStyle Hidden -Wait -PassThru
    if($process.ExitCode -ne 0){throw (Get-Content -LiteralPath "$sample.error.txt" -Raw)}
    BuildProject 'tests\LoadCheck.csproj'
    $checker=Join-Path $root 'tests\bin\Release\net8.0\LoadCheck.dll'
    $managed=Join-Path $root 'managed\bin\Release\net472\AffineSingle.Managed.dll'
    dotnet $checker --inspect $AffineDll $sample $managed
    if($LASTEXITCODE -ne 0){throw 'PE preservation check failed.'}
    dotnet $checker $sample
    if($LASTEXITCODE -ne 0){throw 'Windows LoadLibrary check failed.'}
    $dll=Join-Path $OutputDir 'affine_io_single.dll'
    Copy-Item -LiteralPath $sample -Destination $dll -Force
    Copy-Item -LiteralPath (Join-Path $root 'MANUAL-INSTALL.txt') -Destination (Join-Path $OutputDir 'MANUAL-INSTALL.txt') -Force
    $products+=$dll
}
if($RunTests){
    BuildProject 'coin_tests\CoinTests.csproj'
    & (Join-Path $root 'coin_tests\bin\Release\net472\CoinTests.exe')
    if($LASTEXITCODE -ne 0){throw 'Coin algorithm tests failed.'}
    if($AffineDll){
        BuildProject 'deploy_tests\DeployTests.csproj'
        & (Join-Path $root 'deploy_tests\bin\Release\net472\DeployTests.exe') $AffineDll (Join-Path $session 'deployment-fixture')
        if($LASTEXITCODE -ne 0){throw 'Deployment and restore tests failed.'}
    }else{Write-Output 'Deployment tests skipped: pass -AffineDll to test PE merge and installation.'}
}
Get-FileHash -LiteralPath $products -Algorithm SHA256 | ForEach-Object {"$($_.Hash)  $([IO.Path]::GetFileName($_.Path))"} | Set-Content -LiteralPath (Join-Path $OutputDir 'SHA256SUMS.txt') -Encoding ascii
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $OutputDir 'LICENSE') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE-NOTICE.md') -Destination (Join-Path $OutputDir 'LICENSE-NOTICE.md') -Force
Write-Output "Ready: $OutputDir"
