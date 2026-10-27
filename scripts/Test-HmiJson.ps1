param(
    [string]$ExecutablePath = (Join-Path $PSScriptRoot '..\src\bin\Debug\net48\TiaOpennessMcp.exe'),
    [string]$ApiDirectory = 'C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48',
    [int]$TiaProcessId,
    [string]$TargetName,
    [switch]$AllowRollbackTests
)

$ErrorActionPreference = 'Stop'
if ($AllowRollbackTests -and (-not $TiaProcessId -or -not $TargetName)) {
    throw 'Rollback tests require an explicit TiaProcessId and TargetName.'
}
if ($TiaProcessId -and -not $AllowRollbackTests) {
    throw 'Live tests require -AllowRollbackTests (temporary objects, never committed or saved).'
}
$ExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$binaryDirectory = Split-Path $ExecutablePath
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('tia-json-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $testExe = Join-Path $temporary 'HmiJsonTests.exe'
    $references = @(
        "/r:$ExecutablePath"
        "/r:$ApiDirectory\Siemens.Engineering.Base.dll"
        "/r:$ApiDirectory\Siemens.Engineering.WinCCUnified.dll"
        "/r:$binaryDirectory\System.Text.Json.dll"
        "/r:$binaryDirectory\System.Memory.dll"
        "/r:$binaryDirectory\Microsoft.Bcl.AsyncInterfaces.dll"
        '/r:System.Drawing.dll'
    )
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo "/out:$testExe" @references (Join-Path $PSScriptRoot 'Test-HmiJson.cs')
    if ($LASTEXITCODE -ne 0) { throw "Test harness compilation failed: $LASTEXITCODE" }
    Copy-Item -LiteralPath ($ExecutablePath + '.config') -Destination ($testExe + '.config')
    $testArgs = @($binaryDirectory, $ApiDirectory)
    if ($AllowRollbackTests) { $testArgs += @("$TiaProcessId", $TargetName) }
    & $testExe @testArgs
    if ($LASTEXITCODE -ne 0) { throw "HMI JSON regression tests failed: $LASTEXITCODE" }
}
finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
