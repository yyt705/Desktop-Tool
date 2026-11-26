param([string]$OutputPath)
$ErrorActionPreference = "Stop"

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    throw "未找到 .NET Framework 4.x 编译器: $csc"
}

$root = $PSScriptRoot
$outExe = if ($OutputPath) { [IO.Path]::GetFullPath($OutputPath) } else { Join-Path $root "DeskFences.exe" }
$manifest = Join-Path $root "app.manifest"
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root "src") -Filter "*.cs" -File | Sort-Object Name | ForEach-Object FullName)

$argsList = @(
    "/nologo",
    "/target:winexe",
    "/optimize+",
    "/platform:x64",
    "/win32manifest:$manifest",
    "/out:$outExe",
    "/reference:System.dll",
    "/reference:System.Core.dll",
    "/reference:System.Drawing.dll",
    "/reference:System.Windows.Forms.dll",
    "/reference:System.Web.Extensions.dll",
    "/lib:$(Split-Path $csc)\WPF",
    "/reference:UIAutomationClient.dll",
    "/reference:UIAutomationTypes.dll",
    "/reference:WindowsBase.dll"
) + $sources

Write-Host "正在编译 64 位原生 DeskFences.exe ..."
& $csc @argsList
if ($LASTEXITCODE -ne 0) {
    throw "编译失败，退出码: $LASTEXITCODE"
}

$info = Get-Item $outExe
$ver = $info.VersionInfo.ProductVersion
if (-not $ver) { $ver = $info.VersionInfo.FileVersion }
Write-Host ("编译成功: {0} v{1} ({2:N1} KB)" -f $info.FullName, $ver, ($info.Length / 1KB))
