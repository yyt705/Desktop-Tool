$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$csc = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testDir = Join-Path ([IO.Path]::GetTempPath()) ("DeskFencesRunner-{0}" -f [guid]::NewGuid())
[IO.Directory]::CreateDirectory($testDir) | Out-Null
$exe = Join-Path $testDir 'DeskFencesTests.exe'
$manifest = Join-Path $root 'app.manifest'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src'), $PSScriptRoot -Filter '*.cs' -File | ForEach-Object FullName)
try {
    & $csc /nologo /target:exe /platform:x64 "/win32manifest:$manifest" /main:DeskFences.RegressionTests "/out:$exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/lib:$(Split-Path $csc)\WPF" /r:UIAutomationClient.dll /r:UIAutomationTypes.dll /r:WindowsBase.dll @sources
    if ($LASTEXITCODE) { throw "Test compilation failed ($LASTEXITCODE)." }
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $runner = [Diagnostics.Process]::Start($start)
    try {
        $stdoutTask = $runner.StandardOutput.ReadToEndAsync()
        $stderrTask = $runner.StandardError.ReadToEndAsync()
        if (-not $runner.WaitForExit(60000)) {
            $runner.Kill()
            $runner.WaitForExit()
            throw 'Regression tests timed out after 60 seconds.'
        }
        $out = $stdoutTask.GetAwaiter().GetResult()
        $err = $stderrTask.GetAwaiter().GetResult()
        if ($out) { Write-Host $out.TrimEnd() }
        if ($err) { Write-Error $err.TrimEnd() -ErrorAction Continue }
        if ($runner.ExitCode) { throw "Regression tests failed ($($runner.ExitCode))." }
    } finally {
        $runner.Dispose()
    }
} finally {
    foreach ($name in @('DeskFencesTests.exe', 'fences_config.json')) {
        $path = Join-Path $testDir $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
    [IO.Directory]::Delete($testDir)
}
