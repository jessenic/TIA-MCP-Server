param(
    [string]$ExecutablePath = (Join-Path $PSScriptRoot '..\src\bin\Debug\net48\TiaOpennessMcp.exe'),
    # Also run read-only live tests against this PLC (S7-1200/S7-1500, TIA V17+ project), e.g. 192.168.0.10.
    [string]$Address
)

$ErrorActionPreference = 'Stop'
$ExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$binaryDirectory = Split-Path $ExecutablePath
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('tia-plc-online-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    # Compile with the Roslyn compiler the .NET SDK ships (the test uses C# 9 features).
    $sdkRoot = Join-Path (Split-Path (Get-Command dotnet).Source) 'sdk'
    $sdkCsc = Get-ChildItem -LiteralPath $sdkRoot -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'Roslyn\bincore\csc.dll' } |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $sdkCsc) { throw 'Roslyn csc.dll not found in the .NET SDK.' }
    $framework = "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
    if (-not (Test-Path -LiteralPath $framework)) { throw ".NET Framework 4.8 reference assemblies not found at $framework." }
    $testExe = Join-Path $temporary 'PlcOnlineTests.exe'
    $references = @(
        "/r:$ExecutablePath"
        "/r:$binaryDirectory\S7CommPlusDriver.dll"
        "/r:$binaryDirectory\System.Text.Json.dll"
        "/r:$binaryDirectory\System.Memory.dll"
        "/r:$framework\mscorlib.dll"
        "/r:$framework\System.dll"
        "/r:$framework\System.Core.dll"
        "/r:$framework\System.Net.Http.dll"
        "/r:$framework\Facades\netstandard.dll"
        "/r:$framework\Facades\System.Runtime.dll"
    )
    & dotnet $sdkCsc /nologo /noconfig /nostdlib /langversion:9.0 /nullable:enable /platform:x64 "/out:$testExe" @references (Join-Path $PSScriptRoot 'Test-PlcOnline.cs')
    if ($LASTEXITCODE -ne 0) { throw "Test harness compilation failed: $LASTEXITCODE" }
    Get-ChildItem -LiteralPath $binaryDirectory -Filter '*.dll' | Copy-Item -Destination $temporary
    Copy-Item -LiteralPath $ExecutablePath -Destination $temporary
    Copy-Item -LiteralPath ($ExecutablePath + '.config') -Destination ($testExe + '.config')
    $testArgs = @()
    if ($Address) { $testArgs += $Address }
    & $testExe @testArgs
    if ($LASTEXITCODE -ne 0) { throw "PLC online tests failed: $LASTEXITCODE" }
}
finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
