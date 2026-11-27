param(
    [string]$ExecutablePath = (Join-Path $PSScriptRoot '..\src\bin\Debug\net48\TiaOpennessMcp.exe')
)

# Offline checks for tia_delete_plc_type, tia_rename_plc_type, tia_rename_block, the import file_path sources
# and the tia_import_scl_block backup decision. Never attaches to TIA Portal or PLCSIM.
$ErrorActionPreference = 'Stop'
$ExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$binaryDirectory = Split-Path $ExecutablePath
$repositoryRoot = Split-Path $PSScriptRoot
$apiDirectory = Join-Path $repositoryRoot 'src\lib'
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('tia-plc-block-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $testExe = Join-Path $temporary 'PlcBlockToolTests.exe'
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
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /nowarn:1685 /utf8output "/out:$testExe" @references `
        (Join-Path $PSScriptRoot 'Test-PlcBlockTools.cs')
    if ($LASTEXITCODE -ne 0) { throw "PLC block tool test harness compilation failed: $LASTEXITCODE" }
    Copy-Item -LiteralPath ($ExecutablePath + '.config') -Destination ($testExe + '.config')
    $data = Join-Path $temporary 'data'
    New-Item -ItemType Directory -Path $data | Out-Null
    & $testExe $binaryDirectory $apiDirectory $data
    if ($LASTEXITCODE -ne 0) { throw "PLC block tool tests failed: $LASTEXITCODE" }
}
finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
