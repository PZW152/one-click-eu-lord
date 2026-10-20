<#
.SYNOPSIS
    Builds dist\EU-Overlord.cmd: the whole tool in one double-clickable file.

.DESCRIPTION
    The output is a short batch header followed by src\EUOverlord.ps1. When run,
    the header copies the engine part (everything after the marker line) into a
    temporary .ps1, runs it with the given arguments, then deletes it. A
    double-click therefore opens the interactive menu; arguments such as
    "-Action Status" still work from a command prompt.

    File format rules (all checked by tests\Test-Engine.ps1):
      * UTF-8 WITHOUT a BOM: a BOM in front of "@echo off" breaks cmd.exe.
      * CRLF line endings: cmd.exe mis-parses LF-only batch files.
      * The header is pure ASCII, so it reads the same on every code page.
      * The temp .ps1 is written WITH a BOM, so Windows PowerShell 5.1 decodes
        the Chinese text correctly on non-UTF-8 code pages.

.EXAMPLE
    powershell -NoProfile -File tools\Build-SingleFile.ps1
#>
param(
    [string]$OutFile = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\EU-Overlord.cmd')
)
$ErrorActionPreference = 'Stop'

$engine = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\EUOverlord.ps1'
$marker = '#==EUO-ENGINE-BELOW=='

# The engine's own path goes through environment variables, never through the
# command string, so paths with spaces, quotes or & cannot break the quoting.
$header = @'
@echo off
rem One-Click EU Overlord - single-file edition. Source: https://github.com/PZW152/one-click-eu-overlord
rem Everything below the marker line is the PowerShell engine (src\EUOverlord.ps1).
setlocal
set "EUO_SELF=%~f0"
set "EUO_PS1=%TEMP%\EUOverlord-%RANDOM%%RANDOM%%RANDOM%.ps1"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$t=[IO.File]::ReadAllText($env:EUO_SELF,[Text.Encoding]::UTF8); $m=[regex]::Match($t,'(?m)^#==EUO-ENGINE-BELOW==\r?\n'); if(-not $m.Success){exit 9}; [IO.File]::WriteAllText($env:EUO_PS1,$t.Substring($m.Index+$m.Length),(New-Object Text.UTF8Encoding $true))"
if errorlevel 1 (
    echo Could not unpack the tool. Please download EU-Overlord.cmd again.
    pause
    exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%EUO_PS1%" %*
set "EUO_RC=%errorlevel%"
del /q "%EUO_PS1%" >nul 2>&1
exit /b %EUO_RC%
'@

if ($header -match '[^\x00-\x7F]') { throw 'The batch header must stay pure ASCII.' }

$body = [IO.File]::ReadAllText($engine, [Text.Encoding]::UTF8).TrimStart([char]0xFEFF)
if ($body -match "(?m)^$([regex]::Escape($marker))") { throw "The engine must not contain the marker line '$marker'." }

$text = $header.TrimEnd() + "`n" + $marker + "`n" + $body
$text = ($text -replace "`r`n", "`n") -replace "`n", "`r`n"

$dir = Split-Path -Parent $OutFile
if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
[IO.File]::WriteAllText($OutFile, $text, (New-Object Text.UTF8Encoding($false)))
Write-Output $OutFile
