param(
    [string]$ExecutablePath = (Join-Path $PSScriptRoot '..\src\bin\Debug\net48\TiaOpennessMcp.exe')
)

$ErrorActionPreference = 'Stop'
$ExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$binaryDirectory = Split-Path $ExecutablePath
$repositoryRoot = Split-Path $PSScriptRoot
$apiDirectory = Join-Path $repositoryRoot 'src\lib'
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('tia-hmi-tool-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $testExe = Join-Path $temporary 'HmiToolBehaviorTests.exe'
    $references = @(
        "/r:$ExecutablePath"
        "/r:$binaryDirectory\System.Text.Json.dll"
        "/r:$binaryDirectory\System.Memory.dll"
        "/r:$binaryDirectory\Microsoft.Bcl.AsyncInterfaces.dll"
        "/r:$apiDirectory\Siemens.Engineering.Base.dll"
        "/r:$apiDirectory\Siemens.Engineering.Step7.dll"
        "/r:$apiDirectory\Siemens.Engineering.WinCC.dll"
        "/r:$apiDirectory\Siemens.Engineering.WinCCUnified.dll"
        "/r:$apiDirectory\Siemens.Engineering.TestSuite.dll"
        "/r:$apiDirectory\Siemens.Simatic.Simulation.Runtime.Api.x64.dll"
        '/r:System.Drawing.dll'
        '/r:Microsoft.CSharp.dll'
    )
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /nowarn:1685 "/out:$testExe" @references `
        (Join-Path $PSScriptRoot 'Test-HmiToolBehaviors.cs')
    if ($LASTEXITCODE -ne 0) { throw "Behavior test harness compilation failed: $LASTEXITCODE" }
    Copy-Item -LiteralPath ($ExecutablePath + '.config') -Destination ($testExe + '.config')
    & $testExe $binaryDirectory $apiDirectory
    if ($LASTEXITCODE -ne 0) { throw "HMI tool behavior tests failed: $LASTEXITCODE" }
}
finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
