<#
.SYNOPSIS
    Code Quality Metrics Script - Avalia a qualidade do codigo gerado por IA.

.DESCRIPTION
    Analisa todos os arquivos .cs da solution (excluindo obj/bin/tests) e atribui
    notas de 0 a 100 para cada arquivo em 7 categorias:
    
    1. Complexidade Ciclomatica (CC)
    2. Acoplamento de Codigo (AC)
    3. Reusabilidade de Funcoes/Classes (RF)
    4. Linhas de Codigo (LOC)
    5. God Classes (GC)
    6. Duplicacao de Codigo (DC)
    7. Profundidade Logica (PL)

    Nota final = media ponderada das 7 categorias.

.NOTES
    Execucao: powershell -ExecutionPolicy Bypass -File scripts\code-quality-metrics.ps1
    Flags: -Detailed (mostra metricas brutas), -ExportCsv (exporta CSV)
#>

param(
    [string]$SolutionRoot = (Split-Path $PSScriptRoot -Parent),
    [switch]$Detailed,
    [switch]$ExportCsv
)

$ExcludePatterns = @("*\obj\*", "*\bin\*", "*\tests\*", "*Test*\*")

$Weights = @{
    CyclomaticComplexity = 0.20
    Coupling             = 0.15
    Reusability          = 0.15
    LinesOfCode          = 0.10
    GodClass             = 0.15
    Duplication          = 0.10
    NestingDepth         = 0.15
}

function Get-SourceFiles {
    param([string]$Root)
    $allFiles = Get-ChildItem -Path "$Root\src" -Recurse -Filter "*.cs" -File
    foreach ($file in $allFiles) {
        $excluded = $false
        foreach ($pattern in $ExcludePatterns) {
            if ($file.FullName -like $pattern) { $excluded = $true; break }
        }
        if (-not $excluded) { $file }
    }
}

function Measure-CyclomaticComplexity {
    param([string]$Content)
    $patterns = @('\bif\s*\(', '\belse\s+if\s*\(', '\bwhile\s*\(', '\bfor\s*\(', '\bforeach\s*\(', '\bcase\s+', '\bcatch\s*\(', '\?\?', '&&', '\|\|', '\bswitch\s*\(')
    $complexity = 1
    foreach ($pattern in $patterns) {
        $complexity += ([regex]::Matches($Content, $pattern)).Count
    }
    return $complexity
}

function Measure-Coupling {
    param([string]$Content)
    $usingMatches = [regex]::Matches($Content, '^\s*using\s+([\w.]+);', [System.Text.RegularExpressions.RegexOptions]::Multiline)
    $injectionMatches = [regex]::Matches($Content, '\b(I[A-Z]\w+|[A-Z]\w+Service|[A-Z]\w+Manager|[A-Z]\w+Handler)\b')
    $types = @{}
    foreach ($m in $usingMatches) { $types[$m.Groups[1].Value] = $true }
    foreach ($m in $injectionMatches) { $types[$m.Groups[1].Value] = $true }
    return $types.Count
}

function Measure-Reusability {
    param([string]$Content)
    $score = 70
    if ($Content -match '\binterface\s+I') { $score += 10 }
    $staticMethods = ([regex]::Matches($Content, '\bpublic\s+static\b')).Count
    if ($staticMethods -gt 0) { $score += [Math]::Min($staticMethods * 3, 10) }
    if ($Content -match '<T>|<T\w*>') { $score += 5 }
    if ($Content -match '\bthis\s+\w+\s+\w+') { $score += 5 }
    $publicMethods = ([regex]::Matches($Content, '\bpublic\b.*\b(Task|void|string|int|bool|Result|IResult)\b')).Count
    $privateMethods = ([regex]::Matches($Content, '\bprivate\b.*\b(Task|void|string|int|bool|Result)\b')).Count
    if ($publicMethods -eq 0 -and $privateMethods -gt 3) { $score -= 15 }
    return [Math]::Max(0, [Math]::Min(100, $score))
}

function Measure-LinesOfCode {
    param([string]$Content)
    $lines = ($Content -split "`n")
    $nonEmpty = ($lines | Where-Object { $_.Trim().Length -gt 0 -and $_.Trim() -notmatch '^\s*//' -and $_.Trim() -notmatch '^\s*\*' }).Count
    return $nonEmpty
}

function Measure-GodClass {
    param([string]$Content)
    $methods = ([regex]::Matches($Content, '\b(public|private|protected|internal)\s+(static\s+)?(async\s+)?(Task|void|string|int|bool|long|Result|IResult|IReadOnlyList|List|IObservable)\b')).Count
    $properties = ([regex]::Matches($Content, '\b(public|private)\s+\w+\s+\w+\s*\{\s*get')).Count
    $fields = ([regex]::Matches($Content, '\bprivate\s+(readonly\s+)?\w+\s+_\w+')).Count
    return ($methods + $properties + $fields)
}

function Measure-NestingDepth {
    param([string]$Content)
    $maxDepth = 0; $currentDepth = 0; $inMethod = $false
    foreach ($line in ($Content -split "`n")) {
        $trimmed = $line.Trim()
        if ($trimmed -match '\b(public|private|protected|internal)\b.*\(.*\)') { $inMethod = $true; $currentDepth = 0 }
        if ($inMethod) {
            $opens = ($trimmed.ToCharArray() | Where-Object { $_ -eq '{' }).Count
            $closes = ($trimmed.ToCharArray() | Where-Object { $_ -eq '}' }).Count
            $currentDepth += $opens
            $maxDepth = [Math]::Max($maxDepth, $currentDepth)
            $currentDepth -= $closes
            if ($currentDepth -le 0) { $inMethod = $false; $currentDepth = 0 }
        }
    }
    return $maxDepth
}

function Measure-Duplication {
    param([string]$Content)
    $lines = $Content -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 10 }
    $lineCount = @{}
    foreach ($line in $lines) {
        $normalized = $line -replace '"[^"]*"', '""' -replace '\b\d+\b', 'N' -replace '\b_\w+', '_x'
        if ($lineCount.ContainsKey($normalized)) { $lineCount[$normalized]++ }
        else { $lineCount[$normalized] = 1 }
    }
    $duplicatedLines = ($lineCount.Values | Where-Object { $_ -gt 1 } | Measure-Object -Sum).Sum
    if (-not $duplicatedLines) { $duplicatedLines = 0 }
    $totalLines = @($lines).Count
    if ($totalLines -eq 0) { return 0.0 }
    return [double]$duplicatedLines / [double]$totalLines
}

# Scoring
function Get-CyclomaticScore { param([int]$Complexity, [int]$MethodCount)
    if ($MethodCount -eq 0) { return 100 }
    $avg = $Complexity / [Math]::Max(1, $MethodCount)
    if ($avg -le 3) { return 100 } if ($avg -le 5) { return 90 } if ($avg -le 7) { return 80 }
    if ($avg -le 10) { return 70 } if ($avg -le 15) { return 50 } if ($avg -le 20) { return 30 }
    return 10
}
function Get-CouplingScore { param([int]$Count)
    if ($Count -le 3) { return 100 } if ($Count -le 5) { return 90 } if ($Count -le 8) { return 80 }
    if ($Count -le 12) { return 65 } if ($Count -le 16) { return 50 } if ($Count -le 20) { return 35 }
    return 15
}
function Get-LocScore { param([int]$Lines)
    if ($Lines -le 50) { return 100 } if ($Lines -le 100) { return 90 } if ($Lines -le 150) { return 80 }
    if ($Lines -le 200) { return 70 } if ($Lines -le 300) { return 55 } if ($Lines -le 400) { return 40 }
    return 20
}
function Get-GodClassScore { param([int]$Members)
    if ($Members -le 5) { return 100 } if ($Members -le 8) { return 90 } if ($Members -le 12) { return 80 }
    if ($Members -le 15) { return 65 } if ($Members -le 20) { return 50 } if ($Members -le 30) { return 30 }
    return 10
}
function Get-DuplicationScore { param([double]$Ratio)
    if ($Ratio -le 0.03) { return 100 } if ($Ratio -le 0.05) { return 90 } if ($Ratio -le 0.10) { return 75 }
    if ($Ratio -le 0.15) { return 60 } if ($Ratio -le 0.25) { return 40 }
    return 20
}
function Get-NestingScore { param([int]$Depth)
    if ($Depth -le 2) { return 100 } if ($Depth -le 3) { return 90 } if ($Depth -le 4) { return 75 }
    if ($Depth -le 5) { return 60 } if ($Depth -le 6) { return 40 }
    return 20
}
function Get-WeightedScore { param([hashtable]$S)
    $w = $S.Cyclomatic * 0.20 + $S.Coupling * 0.15 + $S.Reusability * 0.15 + $S.Loc * 0.10 + $S.GodClass * 0.15 + $S.Duplication * 0.10 + $S.Nesting * 0.15
    return [Math]::Round($w, 1)
}
function Get-Grade { param([double]$Score)
    if ($Score -ge 90) { return "A+" } if ($Score -ge 85) { return "A" } if ($Score -ge 80) { return "A-" }
    if ($Score -ge 75) { return "B+" } if ($Score -ge 70) { return "B" } if ($Score -ge 65) { return "B-" }
    if ($Score -ge 60) { return "C+" } if ($Score -ge 55) { return "C" } if ($Score -ge 50) { return "C-" }
    if ($Score -ge 40) { return "D" } return "F"
}
function Get-Color { param([double]$Score)
    if ($Score -ge 80) { return "Green" } if ($Score -ge 60) { return "Yellow" } return "Red"
}

# === MAIN ===
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  CODE QUALITY METRICS - Ayn Thor Manager" -ForegroundColor Cyan
Write-Host "  Analise de Boas Praticas para Codigo Gerado por IA" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""

$files = @(Get-SourceFiles -Root $SolutionRoot)
Write-Host "  Arquivos analisados: $($files.Count)" -ForegroundColor Gray
Write-Host ""

$results = @()

foreach ($file in $files) {
    $content = Get-Content -Path $file.FullName -Raw -ErrorAction SilentlyContinue
    if (-not $content) { continue }
    $relativePath = $file.FullName.Replace($SolutionRoot + "\", "")
    $project = ($relativePath -split "\\")[1]

    $cc = Measure-CyclomaticComplexity -Content $content
    $coupling = Measure-Coupling -Content $content
    $reusability = Measure-Reusability -Content $content
    $loc = Measure-LinesOfCode -Content $content
    $godClass = Measure-GodClass -Content $content
    $duplication = Measure-Duplication -Content $content
    $nesting = Measure-NestingDepth -Content $content

    $scores = @{
        Cyclomatic  = Get-CyclomaticScore -Complexity $cc -MethodCount ([Math]::Max(1, $godClass))
        Coupling    = Get-CouplingScore -Count $coupling
        Reusability = $reusability
        Loc         = Get-LocScore -Lines $loc
        GodClass    = Get-GodClassScore -Members $godClass
        Duplication = Get-DuplicationScore -Ratio $duplication
        Nesting     = Get-NestingScore -Depth $nesting
    }
    $finalScore = Get-WeightedScore -S $scores

    $results += [PSCustomObject]@{
        File        = $file.Name
        Path        = $relativePath
        Project     = $project
        CC          = $scores.Cyclomatic
        AC          = $scores.Coupling
        RF          = $scores.Reusability
        LOC         = $scores.Loc
        GC          = $scores.GodClass
        DC          = $scores.Duplication
        PL          = $scores.Nesting
        Final       = $finalScore
        Grade       = Get-Grade -Score $finalScore
        RawCC       = $cc
        RawCoupling = $coupling
        RawLOC      = $loc
        RawMembers  = $godClass
        RawNesting  = $nesting
        RawDupPct   = [Math]::Round($duplication * 100, 1)
    }
}

# Per-file table
Write-Host "----------------------------------------------------------------" -ForegroundColor DarkGray
Write-Host "  NOTAS POR ARQUIVO (0-100: quanto maior, melhor)" -ForegroundColor White
Write-Host "----------------------------------------------------------------" -ForegroundColor DarkGray
Write-Host ("{0,-42} {1,4} {2,4} {3,4} {4,4} {5,4} {6,4} {7,4} {8,6} {9,4}" -f "Arquivo", "CC", "AC", "RF", "LOC", "GC", "DC", "PL", "FINAL", "NOTA") -ForegroundColor DarkGray
Write-Host "----------------------------------------------------------------" -ForegroundColor DarkGray

$sortedResults = $results | Sort-Object Final
foreach ($r in $sortedResults) {
    $color = Get-Color -Score $r.Final
    $name = if ($r.File.Length -gt 42) { $r.File.Substring(0, 39) + "..." } else { $r.File }
    $line = "{0,-42} {1,4} {2,4} {3,4} {4,4} {5,4} {6,4} {7,4} {8,6} {9,4}" -f $name, $r.CC, $r.AC, $r.RF, $r.LOC, $r.GC, $r.DC, $r.PL, $r.Final, $r.Grade
    Write-Host $line -ForegroundColor $color
}
Write-Host "----------------------------------------------------------------" -ForegroundColor DarkGray

# Per-project
Write-Host ""
Write-Host "  RESUMO POR PROJETO" -ForegroundColor White
Write-Host "----------------------------------------------------------------" -ForegroundColor DarkGray
Write-Host ("{0,-35} {1,5} {2,4} {3,4} {4,4} {5,4} {6,4} {7,4} {8,4} {9,6} {10,4}" -f "Projeto", "Arqs", "CC", "AC", "RF", "LOC", "GC", "DC", "PL", "MEDIA", "NOTA") -ForegroundColor DarkGray
Write-Host "----------------------------------------------------------------" -ForegroundColor DarkGray

$projects = $results | Group-Object Project
foreach ($proj in ($projects | Sort-Object { ($_.Group | Measure-Object Final -Average).Average })) {
    $avgFin = [Math]::Round(($proj.Group | Measure-Object Final -Average).Average, 1)
    $avgCC  = [Math]::Round(($proj.Group | Measure-Object CC -Average).Average, 0)
    $avgAC  = [Math]::Round(($proj.Group | Measure-Object AC -Average).Average, 0)
    $avgRF  = [Math]::Round(($proj.Group | Measure-Object RF -Average).Average, 0)
    $avgLOC = [Math]::Round(($proj.Group | Measure-Object LOC -Average).Average, 0)
    $avgGC  = [Math]::Round(($proj.Group | Measure-Object GC -Average).Average, 0)
    $avgDC  = [Math]::Round(($proj.Group | Measure-Object DC -Average).Average, 0)
    $avgPL  = [Math]::Round(($proj.Group | Measure-Object PL -Average).Average, 0)
    $grade  = Get-Grade -Score $avgFin
    $color  = Get-Color -Score $avgFin
    $line = "{0,-35} {1,5} {2,4} {3,4} {4,4} {5,4} {6,4} {7,4} {8,4} {9,6} {10,4}" -f $proj.Name, $proj.Count, $avgCC, $avgAC, $avgRF, $avgLOC, $avgGC, $avgDC, $avgPL, $avgFin, $grade
    Write-Host $line -ForegroundColor $color
}
Write-Host "----------------------------------------------------------------" -ForegroundColor DarkGray

# Global score
$globalAvg = [Math]::Round(($results | Measure-Object Final -Average).Average, 1)
$globalGrade = Get-Grade -Score $globalAvg
$globalColor = Get-Color -Score $globalAvg

Write-Host ""
Write-Host "================================================================" -ForegroundColor $globalColor
Write-Host ("  NOTA GERAL DA SOLUTION:  $globalAvg / 100  (Grade: $globalGrade)") -ForegroundColor $globalColor
Write-Host "================================================================" -ForegroundColor $globalColor

# Top 5 worst
Write-Host ""
Write-Host "  TOP 5 ARQUIVOS QUE PRECISAM DE ATENCAO:" -ForegroundColor Yellow
$worstFiles = $results | Sort-Object Final | Select-Object -First 5
foreach ($w in $worstFiles) {
    $issues = @()
    if ($w.CC -lt 70) { $issues += "CC:$($w.RawCC) branches" }
    if ($w.AC -lt 70) { $issues += "AC:$($w.RawCoupling) deps" }
    if ($w.LOC -lt 70) { $issues += "LOC:$($w.RawLOC) linhas" }
    if ($w.GC -lt 70) { $issues += "GC:$($w.RawMembers) membros" }
    if ($w.PL -lt 70) { $issues += "PL:depth $($w.RawNesting)" }
    if ($w.DC -lt 70) { $issues += "DC:$($w.RawDupPct)% dup" }
    $issueStr = if ($issues.Count -gt 0) { " -> " + ($issues -join ", ") } else { "" }
    Write-Host ("    {0,-40} Score: {1,5} ({2}){3}" -f $w.File, $w.Final, $w.Grade, $issueStr) -ForegroundColor Yellow
}

# Legend
Write-Host ""
Write-Host "  Legenda:" -ForegroundColor Gray
Write-Host "    CC  = Complexidade Ciclomatica    (menos branch/loop por metodo = melhor)" -ForegroundColor Gray
Write-Host "    AC  = Acoplamento de Codigo       (menos dependencias externas = melhor)" -ForegroundColor Gray
Write-Host "    RF  = Reusabilidade               (interfaces, generics, extensions = melhor)" -ForegroundColor Gray
Write-Host "    LOC = Linhas de Codigo            (arquivos menores = melhor)" -ForegroundColor Gray
Write-Host "    GC  = God Classes                 (menos membros por classe = melhor)" -ForegroundColor Gray
Write-Host "    DC  = Duplicacao de Codigo        (menos linhas repetidas = melhor)" -ForegroundColor Gray
Write-Host "    PL  = Profundidade Logica         (menos aninhamento = melhor)" -ForegroundColor Gray
Write-Host ""
Write-Host "  Escala: 90-100=A+ | 85-89=A | 80-84=A- | 75-79=B+ | 70-74=B | 65-69=B- | 60-64=C+ | 55-59=C | 50-54=C- | 40-49=D | <40=F" -ForegroundColor Gray
Write-Host ""

if ($ExportCsv) {
    $csvPath = Join-Path $SolutionRoot "code-quality-report.csv"
    $results | Select-Object Path, Project, CC, AC, RF, LOC, GC, DC, PL, Final, Grade, RawCC, RawCoupling, RawLOC, RawMembers, RawNesting, RawDupPct |
        Export-Csv -Path $csvPath -NoTypeInformation -Encoding UTF8
    Write-Host "  Relatorio exportado para: $csvPath" -ForegroundColor Green
    Write-Host ""
}

if ($globalAvg -ge 70) { exit 0 } else { exit 1 }
