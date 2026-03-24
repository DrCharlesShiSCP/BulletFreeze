$ErrorActionPreference = "Stop"

$outputPath = Join-Path $PSScriptRoot "..\Assets\HDRI\EasternEuropeWilderness_Day.hdr"
$outputPath = [System.IO.Path]::GetFullPath($outputPath)
$outputDir = [System.IO.Path]::GetDirectoryName($outputPath)

if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

$width = 3072
$height = 1536

function Clamp01([double]$v) {
    if ($v -lt 0.0) { return 0.0 }
    if ($v -gt 1.0) { return 1.0 }
    return $v
}

function Lerp([double]$a, [double]$b, [double]$t) {
    return $a + (($b - $a) * $t)
}

function SmoothStep([double]$edge0, [double]$edge1, [double]$x) {
    if ($edge0 -eq $edge1) { return 0.0 }
    $t = Clamp01 (($x - $edge0) / ($edge1 - $edge0))
    return $t * $t * (3.0 - (2.0 * $t))
}

function Frac([double]$v) {
    return $v - [Math]::Floor($v)
}

function Noise2([double]$x, [double]$y) {
    $n = [Math]::Sin(($x * 12.9898) + ($y * 78.233)) * 43758.5453
    return Frac $n
}

function Fbm([double]$x, [double]$y) {
    $sum = 0.0
    $amp = 0.55
    $freq = 1.0

    for ($octave = 0; $octave -lt 4; $octave++) {
        $sum += ((Noise2 ($x * $freq) ($y * $freq)) * $amp)
        $freq *= 2.03
        $amp *= 0.5
    }

    return $sum
}

function ToRgbE([double]$r, [double]$g, [double]$b) {
    $v = [Math]::Max($r, [Math]::Max($g, $b))
    if ($v -lt 1.0e-32) {
        return [byte[]](0, 0, 0, 0)
    }

    $exp = [Math]::Floor([Math]::Log($v, 2.0)) + 1.0
    $scale = 256.0 / [Math]::Pow(2.0, $exp)

    $rb = [byte][Math]::Min(255.0, [Math]::Max(0.0, [Math]::Floor($r * $scale)))
    $gb = [byte][Math]::Min(255.0, [Math]::Max(0.0, [Math]::Floor($g * $scale)))
    $bb = [byte][Math]::Min(255.0, [Math]::Max(0.0, [Math]::Floor($b * $scale)))
    $eb = [byte]([int]$exp + 128)

    return [byte[]]($rb, $gb, $bb, $eb)
}

function Write-ChannelRle([System.IO.BinaryWriter]$writer, [byte[]]$channel) {
    $index = 0
    $length = $channel.Length

    while ($index -lt $length) {
        $runLength = 1
        while (($index + $runLength) -lt $length -and
               $runLength -lt 127 -and
               $channel[$index + $runLength] -eq $channel[$index]) {
            $runLength++
        }

        if ($runLength -ge 4) {
            $writer.Write([byte](128 + $runLength))
            $writer.Write([byte]$channel[$index])
            $index += $runLength
            continue
        }

        $literalStart = $index
        $literalLength = 0

        while ($index -lt $length -and $literalLength -lt 128) {
            $lookAheadRun = 1
            while (($index + $lookAheadRun) -lt $length -and
                   $lookAheadRun -lt 127 -and
                   $channel[$index + $lookAheadRun] -eq $channel[$index]) {
                $lookAheadRun++
            }

            if ($lookAheadRun -ge 4) {
                break
            }

            $index++
            $literalLength++
        }

        $writer.Write([byte]$literalLength)
        for ($i = 0; $i -lt $literalLength; $i++) {
            $writer.Write([byte]$channel[$literalStart + $i])
        }
    }
}

function Get-TerrainHeight([double]$u) {
    $ridge =
        ([Math]::Sin(($u * [Math]::PI * 2.0 * 1.3) + 0.55) * 0.010) +
        ([Math]::Sin(($u * [Math]::PI * 2.0 * 3.1) - 1.2) * 0.006) +
        ([Math]::Sin(($u * [Math]::PI * 2.0 * 5.7) + 2.1) * 0.003)

    return -0.028 + $ridge
}

$sunAzimuth = 2.15
$sunElevation = 0.74
$sunDirX = ([Math]::Cos($sunElevation) * [Math]::Cos($sunAzimuth))`n$sunDirY = [Math]::Sin($sunElevation)`n$sunDirZ = ([Math]::Cos($sunElevation) * [Math]::Sin($sunAzimuth))

$utf8 = New-Object System.Text.UTF8Encoding($false)
$fileStream = [System.IO.File]::Open($outputPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
$writer = New-Object System.IO.BinaryWriter($fileStream, $utf8)

try {
    $header = "#?RADIANCE`nFORMAT=32-bit_rle_rgbe`nEXPOSURE=1.0000000000000`n`n-Y $height +X $width`n"
    $writer.Write($utf8.GetBytes($header))

    for ($y = 0; $y -lt $height; $y++) {
        $scanlineR = New-Object byte[] $width
        $scanlineG = New-Object byte[] $width
        $scanlineB = New-Object byte[] $width
        $scanlineE = New-Object byte[] $width

        $v = $y / ($height - 1.0)
        $latitude = ((1.0 - $v) - 0.5) * [Math]::PI
        $sinLat = [Math]::Sin($latitude)
        $cosLat = [Math]::Cos($latitude)

        for ($x = 0; $x -lt $width; $x++) {
            $u = $x / ($width - 1.0)
            $longitude = ($u * [Math]::PI * 2.0) - [Math]::PI
            $dirX = $cosLat * [Math]::Cos($longitude)
            $dirY = $sinLat
            $dirZ = $cosLat * [Math]::Sin($longitude)

            $horizonT = SmoothStep -0.15 0.65 ($dirY + 0.12)

            $skyR = Lerp 0.72 0.13 $horizonT
            $skyG = Lerp 0.86 0.34 $horizonT
            $skyB = Lerp 1.04 0.92 $horizonT

            $warmHaze = (SmoothStep -0.03 0.25 $dirY) * 0.08
            $skyR += $warmHaze
            $skyG += $warmHaze * 0.55

            $sunDot = ($dirX * $sunDirX) + ($dirY * $sunDirY) + ($dirZ * $sunDirZ)
            if ($sunDot -gt 0.0) {
                $sunGlow = [Math]::Pow($sunDot, 28.0) * 3.5
                $sunCore = [Math]::Pow($sunDot, 1200.0) * 55.0
                $skyR += $sunGlow * 1.2 + $sunCore * 1.0
                $skyG += $sunGlow * 1.05 + $sunCore * 0.95
                $skyB += $sunGlow * 0.72 + $sunCore * 0.72
            }

            $cloudUvX = ($u * 5.2) + ($dirY * 0.35)
            $cloudUvY = (($dirY + 0.28) * 4.6) + ([Math]::Sin($longitude * 1.8) * 0.08)
            $cloudNoise = Fbm $cloudUvX $cloudUvY
            $cloudShape = SmoothStep 0.47 0.78 $cloudNoise
            $cloudBand = (SmoothStep -0.02 0.52 $dirY) * (1.0 - (SmoothStep 0.55 0.92 $dirY))
            $cloudMask = $cloudShape * $cloudBand
            $cloudSun = [Math]::Pow([Math]::Max(0.0, $sunDot), 9.0)
            $cloudBrightness = 0.35 + ($cloudSun * 0.9)
            $skyR = Lerp $skyR ($skyR + (1.05 * $cloudBrightness)) ($cloudMask * 0.82)
            $skyG = Lerp $skyG ($skyG + (1.03 * $cloudBrightness)) ($cloudMask * 0.80)
            $skyB = Lerp $skyB ($skyB + (1.00 * $cloudBrightness)) ($cloudMask * 0.78)

            $terrainHeight = Get-TerrainHeight $u
            $forestBandTop = $terrainHeight + ((Noise2 ($u * 38.0) 0.35) - 0.5) * 0.018
            $isTerrain = $dirY -lt $terrainHeight
            $isForest = ($dirY -lt $forestBandTop) -and ($dirY -gt ($terrainHeight - 0.05))

            if ($isTerrain) {
                $groundDepth = Clamp01 (($terrainHeight - $dirY) / 0.85)
                $fieldNoise = Fbm ($u * 8.0) (($dirY + 0.8) * 3.0)
                $fieldMix = SmoothStep 0.25 0.78 $fieldNoise

                $groundR = Lerp 0.11 0.24 $groundDepth
                $groundG = Lerp 0.24 0.43 $groundDepth
                $groundB = Lerp 0.09 0.18 $groundDepth

                $groundR += $fieldMix * 0.04
                $groundG += $fieldMix * 0.07
                $groundB += $fieldMix * 0.02

                if ($isForest) {
                    $treeNoise = Noise2 ($u * 90.0) (($dirY + 0.4) * 120.0)
                    $treeHeight = SmoothStep 0.34 0.82 $treeNoise
                    $forestMask = SmoothStep ($terrainHeight - 0.005) ($forestBandTop + 0.008) $dirY
                    $forestMask = 1.0 - $forestMask

                    $groundR = Lerp $groundR 0.08 ($forestMask * $treeHeight)
                    $groundG = Lerp $groundG 0.19 ($forestMask * $treeHeight)
                    $groundB = Lerp $groundB 0.08 ($forestMask * $treeHeight)
                }

                $haze = (SmoothStep -0.12 0.08 $dirY) * 0.55
                $skyR = Lerp $groundR 0.72 $haze
                $skyG = Lerp $groundG 0.84 $haze
                $skyB = Lerp $groundB 0.98 $haze
            }

            $rgbe = ToRgbE $skyR $skyG $skyB
            $scanlineR[$x] = $rgbe[0]
            $scanlineG[$x] = $rgbe[1]
            $scanlineB[$x] = $rgbe[2]
            $scanlineE[$x] = $rgbe[3]
        }

        $writer.Write([byte]2)
        $writer.Write([byte]2)
        $writer.Write([byte]($width -shr 8))
        $writer.Write([byte]($width -band 255))
        Write-ChannelRle $writer $scanlineR
        Write-ChannelRle $writer $scanlineG
        Write-ChannelRle $writer $scanlineB
        Write-ChannelRle $writer $scanlineE
    }
}
finally {
    $writer.Dispose()
    $fileStream.Dispose()
}

Write-Host "Generated HDRI at $outputPath"

