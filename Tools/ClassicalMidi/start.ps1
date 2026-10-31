param(
    [string]$OutputDirectory,
    [double]$Hours = 8,
    [string]$PythonExecutable
)
$ErrorActionPreference = 'Stop'
if (-not $PythonExecutable) {
    $pythonCommand = Get-Command python, python3 -ErrorAction SilentlyContinue |
        Where-Object { $_.Source -notlike '*\WindowsApps\*' } | Select-Object -First 1
    if ($pythonCommand) { $PythonExecutable = $pythonCommand.Source }
}
$pythonExe = $PythonExecutable
if (-not $pythonExe) { throw 'Set -PythonExecutable to a Python 3 interpreter; the Microsoft Store alias is not a runtime.' }
if (-not (Test-Path -LiteralPath $pythonExe -PathType Leaf)) {
    throw "Python runtime missing: $pythonExe"
}
$projectDirectory = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if (-not $OutputDirectory) {
    $runName = [DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(8)).ToString('yyyyMMdd-HHmmss')
    $OutputDirectory = Join-Path $projectDirectory "Downloads\ClassicalMidi\$runName"
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$collectorScript = Join-Path $PSScriptRoot 'collector.py'
$processArguments = @('-u', ('"{0}"' -f $collectorScript), '--output', ('"{0}"' -f $OutputDirectory), '--hours', $Hours.ToString([Globalization.CultureInfo]::InvariantCulture))
$collectorProcess = Start-Process -FilePath $pythonExe -ArgumentList $processArguments -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $OutputDirectory 'stdout.log') -RedirectStandardError (Join-Path $OutputDirectory 'stderr.log')
[PSCustomObject]@{
    pid = $collectorProcess.Id
    output = $OutputDirectory
    status = Join-Path $OutputDirectory 'status.json'
} | ConvertTo-Json
