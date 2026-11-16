# 构建脚本：生成界面 → 编译 exe
#
#   .\scripts\build.ps1                          # 自动找游戏目录
#   .\scripts\build.ps1 -Game "D:\Games\Roadrash"
#   .\scripts\build.ps1 -NoAssets                # 不含游戏素材（界面用占位背景）
#   .\scripts\build.ps1 -Zip                     # 顺便打包 release zip
param(
    [string]$Game = "",
    [switch]$NoAssets,
    [switch]$Zip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# ---- 找 Python ----
$python = $null
foreach ($c in @("python", "py")) {
    $cmd = Get-Command $c -ErrorAction SilentlyContinue
    if ($cmd) { $python = $cmd.Source; break }
}
if (-not $python) { throw "找不到 Python，请先安装 Python 3.9+ 并加入 PATH" }

# ---- 找 csc（.NET Framework 自带）----
$csc = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw "找不到 csc.exe（需要 Windows 自带的 .NET Framework 4.x）" }

New-Item -ItemType Directory -Force build, dist | Out-Null
$ui = Join-Path $root "build\rrs_editor.html"

# ---- 1. 生成界面 ----
if ($NoAssets) {
    & $python "src\build_assets.py" --no-assets --out $ui
} elseif ($Game) {
    & $python "src\build_assets.py" --game $Game --out $ui
} else {
    & $python "src\build_assets.py" --out $ui
}
if ($LASTEXITCODE -ne 0) { throw "界面生成失败" }

# ---- 2. 编译 ----
$exe = Join-Path $root "dist\暴力摩托存档修改器.exe"
& $csc /nologo /target:winexe /optimize+ /out:$exe `
    /win32icon:"src\ui\icon.ico" `
    /resource:"$ui",rrs_editor.html `
    /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    "src\Launcher.cs"
if ($LASTEXITCODE -ne 0) { throw "编译失败" }

Write-Host "OK -> $exe" -ForegroundColor Green

# ---- 3. 打包（可选）----
if ($Zip) {
    $stage = Join-Path $root "build\package\暴力摩托存档修改器"
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force (Join-Path $stage "源码") | Out-Null
    Copy-Item $exe $stage
    Copy-Item "docs\usage.md" (Join-Path $stage "存档修改说明.md")
    Copy-Item "src\Launcher.cs", "src\build_assets.py", "src\ui\template.html", "src\ui\icon.ico" (Join-Path $stage "源码")
    $zipPath = Join-Path $root "dist\暴力摩托存档修改器_v1.0.0.zip"
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path $stage -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "ZIP  -> $zipPath" -ForegroundColor Green
}
