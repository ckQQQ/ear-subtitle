param([string]$Python)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
if (-not $Python) {
    $saved = Join-Path $PSScriptRoot 'python-path.txt'
    if (Test-Path -LiteralPath $saved) { $Python = (Get-Content -LiteralPath $saved -Raw).Trim() }
}
if (-not $Python) {
    $candidate = Join-Path $env:USERPROFILE 'anaconda3\envs\ChatTTS-Enhanced\python.exe'
    if (Test-Path -LiteralPath $candidate) { $Python = $candidate }
    else { $Python = (Get-Command python -ErrorAction Stop).Source }
}
$env:PYTHONUTF8 = '1'
& $Python -u (Join-Path $PSScriptRoot 'setup_models.py')
exit $LASTEXITCODE
