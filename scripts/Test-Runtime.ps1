param(
    [string]$ExecutablePath = (Join-Path $PSScriptRoot '..\src\bin\Debug\net48\TiaOpennessMcp.exe')
)

# Offline tests for the UTF-8 transport, the SCL source encoding and the S7-PLCSIM Advanced diagnosis.
# Never attaches to TIA Portal and never calls PLCSIM (the PLCSIM Advanced API is not resolvable in the test).
$ErrorActionPreference = 'Stop'
$ExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$binaryDirectory = Split-Path $ExecutablePath
$apiDirectory = Join-Path (Split-Path $PSScriptRoot) 'src\lib'
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('tia-runtime-tests-' + [guid]::NewGuid().ToString('N'))
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
    $testExe = Join-Path $temporary 'RuntimeTests.exe'
    $references = @(
        "/r:$ExecutablePath"
        "/r:$binaryDirectory\System.Text.Json.dll"
        "/r:$binaryDirectory\System.Text.Encodings.Web.dll"
        "/r:$binaryDirectory\System.Memory.dll"
        "/r:$binaryDirectory\System.Threading.Tasks.Extensions.dll"
        "/r:$binaryDirectory\Microsoft.Bcl.AsyncInterfaces.dll"
        "/r:$apiDirectory\Siemens.Engineering.Base.dll"
        "/r:$apiDirectory\Siemens.Engineering.Step7.dll"
        "/r:$apiDirectory\Siemens.Engineering.WinCC.dll"
        "/r:$apiDirectory\Siemens.Engineering.WinCCUnified.dll"
        "/r:$framework\mscorlib.dll"
        "/r:$framework\System.dll"
        "/r:$framework\System.Core.dll"
        "/r:$framework\Facades\netstandard.dll"
        "/r:$framework\Facades\System.Runtime.dll"
    )
    & dotnet $sdkCsc /nologo /noconfig /nostdlib /langversion:9.0 /nullable:enable /platform:x64 "/out:$testExe" @references (Join-Path $PSScriptRoot 'Test-Runtime.cs')
    if ($LASTEXITCODE -ne 0) { throw "Test harness compilation failed: $LASTEXITCODE" }
    # The build output never contains the PLCSIM Advanced API (Private=False); do not copy it from anywhere.
    Get-ChildItem -LiteralPath $binaryDirectory -Filter '*.dll' |
        Where-Object { $_.Name -notlike 'Siemens.Simatic.Simulation*' } |
        Copy-Item -Destination $temporary
    Copy-Item -LiteralPath $ExecutablePath -Destination $temporary
    Copy-Item -LiteralPath ($ExecutablePath + '.config') -Destination ($testExe + '.config')
    # The stdio test runs the copied server exe (no config.json beside it, so defaults apply).
    & $testExe (Join-Path $temporary (Split-Path $ExecutablePath -Leaf)) $apiDirectory
    if ($LASTEXITCODE -ne 0) { throw "Runtime tests failed: $LASTEXITCODE" }
}
finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
