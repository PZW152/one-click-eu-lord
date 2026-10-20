<#
.SYNOPSIS
    Shared helpers for Build.ps1 and Test.ps1: finding the in-box C# compiler and
    the assemblies to reference.

.DESCRIPTION
    Dot-source this; it defines functions and does nothing on its own.

    There is no SDK requirement anywhere in this project. .NET Framework 4.8 is part
    of Windows 11, so csc.exe is already on the machine and the assemblies are in the
    GAC. A developer pack, when one happens to be installed, gives nicer reference
    assemblies, so those are preferred when present.
#>

Set-StrictMode -Version Latest

function Resolve-Csc {
    $candidates = @(
        "$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:windir\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    )
    foreach ($path in $candidates) { if (Test-Path -LiteralPath $path) { return $path } }
    throw "The in-box C# compiler was not found. Looked in: $($candidates -join ', ')"
}

function Resolve-Reference {
    param([Parameter(Mandatory)][string]$Name)

    $refDirs = @(
        "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8",
        "$env:ProgramFiles\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8",
        "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2",
        "$env:windir\Microsoft.NET\Framework64\v4.0.30319\WPF",
        "$env:windir\Microsoft.NET\Framework64\v4.0.30319"
    )
    foreach ($dir in $refDirs) {
        $candidate = Join-Path $dir "$Name.dll"
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }

    # The GAC, preferring 64-bit then architecture-neutral.
    $gacRoot = "$env:windir\Microsoft.NET\assembly"
    foreach ($flavour in 'GAC_64', 'GAC_MSIL', 'GAC_32') {
        $dir = Join-Path $gacRoot $flavour
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        $hit = Get-ChildItem -LiteralPath $dir -Recurse -Filter "$Name.dll" -ErrorAction SilentlyContinue |
               Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }

    throw "The reference assembly $Name.dll was not found."
}

<#
    The application icon, drawn here rather than committed as a binary so it stays
    reviewable in a diff: a signal-red square with a white notch cut out of the lower
    right. Geometric, legible at 16 px, and the same red as the accent in the UI.
#>
function New-AppIcon {
    param([Parameter(Mandatory)][string]$Path)

    Add-Type -AssemblyName System.Drawing

    $sizes = 16, 32, 48, 64, 128, 256
    $payloads = New-Object 'System.Collections.Generic.List[byte[]]'

    foreach ($size in $sizes) {
        $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
            $red = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 226, 0, 26))
            $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
            try {
                $graphics.FillRectangle($red, 0, 0, $size, $size)

                # The notch: the lower-right quarter, inset by an eighth, so the mark
                # keeps a red margin on every edge.
                $unit = $size / 8.0
                $graphics.FillRectangle($white, [float](4 * $unit), [float](4 * $unit), [float](3 * $unit), [float](3 * $unit))
            } finally {
                $red.Dispose(); $white.Dispose()
            }
        } finally {
            $graphics.Dispose()
        }

        $stream = New-Object System.IO.MemoryStream
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $payloads.Add($stream.ToArray())
        } finally {
            $stream.Dispose(); $bitmap.Dispose()
        }
    }

    # ICONDIR, then one 16-byte ICONDIRENTRY per image, then the PNG payloads.
    $out = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($out)
    try {
        $writer.Write([UInt16]0)                 # reserved
        $writer.Write([UInt16]1)                 # type: icon
        $writer.Write([UInt16]$sizes.Count)

        $offset = 6 + (16 * $sizes.Count)
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $size = $sizes[$i]
            $dimension = if ($size -ge 256) { 0 } else { $size }   # 0 means 256 in this format
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)               # palette entries
            $writer.Write([byte]0)               # reserved
            $writer.Write([UInt16]1)             # colour planes
            $writer.Write([UInt16]32)            # bits per pixel
            $writer.Write([UInt32]$payloads[$i].Length)
            $writer.Write([UInt32]$offset)
            $offset += $payloads[$i].Length
        }
        foreach ($payload in $payloads) { $writer.Write($payload) }

        $writer.Flush()
        [IO.File]::WriteAllBytes($Path, $out.ToArray())
    } finally {
        $writer.Dispose(); $out.Dispose()
    }
}
