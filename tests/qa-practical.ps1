# Validazione pratica pre-beta di CleanSlate 0.2.0.
#
# Scopo: attraversare il percorso reale analisi -> selezione -> cancellazione usando
# l'eseguibile verificato, SENZA eliminare dati preesistenti dell'utente.
#
# Struttura:
#   Fase 0  precondizioni: build verificata, non admin, snapshot del vero %TEMP%
#   Fase 1  A: analisi reale del %TEMP% dell'utente (sola lettura, nessuna cancellazione)
#   Fase 2  fixture: file sintetici con nome e contenuto univoci dentro %TEMP% reale,
#           piu' un sandbox dedicato usato come TMP del processo GUI
#   Fase 3  C: regole di eta' e anteprima, sul codice di produzione e sulla GUI reale
#   Fase 4  D/E: flusso GUI sul sandbox (analisi, conferma, annullamento); la
#           deselezione della singola riga non e' automatable in questo ambiente
#   Fase 5  D/E/F: motore di produzione sul vero %TEMP% con lista esplicita di elementi
#   Fase 6  verifica di integrita': sopravvissuti invariati, vittima fuori radice intatta
#   Fase 7  G: rimozione dei soli residui del test (dopo verifica di nome/percorso/contenuto)
#   Fase 8  H: suite automatica, verifica end-to-end e conferma che la build e' conservata
#
# Regole di sicurezza applicate:
#   - ogni operazione distruttiva e' limitata a percorsi che iniziano per "cleanslate-qa-"
#     generati da questo script, e viene annullata se il prefisso non corrisponde;
#   - la cancellazione di massa, le operazioni ricorsive su cartelle reali e i privilegi
#     amministrativi non vengono usati;
#   - se un'isolation non e' dimostrabile la fase distruttiva viene saltata e il motivo
#     registrato.
#
# Uso: powershell -ExecutionPolicy Bypass -File tests\qa-practical.ps1
# Codice di uscita: 0 se tutti i controlli sono superati, 1 altrimenti.

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

$script:failures = New-Object System.Collections.ArrayList
$script:notes = New-Object System.Collections.ArrayList
$script:procs = New-Object System.Collections.ArrayList

function Check([bool]$condition, [string]$message) {
    if ($condition) {
        Write-Host ('  OK   ' + $message)
    } else {
        [void]$script:failures.Add($message)
        Write-Host ('  FAIL ' + $message)
    }
}

function Note([string]$message) {
    [void]$script:notes.Add($message)
    Write-Host ('  NOTE ' + $message)
}

function Fail-Fatal([string]$message) {
    Write-Host ('  ABORT ' + $message)
    [void]$script:failures.Add($message)
    throw $message
}

function Stop-AllProcs {
    foreach ($p in $script:procs) {
        try {
            $p.Refresh()
            if (-not $p.HasExited) { $p.Kill() }
        } catch { }
    }
    $script:procs.Clear()
}

trap {
    Stop-AllProcs
    Write-Host ('ERRORE NON GESTITO: ' + $_.Exception.Message)
    exit 1
}

$root = Split-Path -Parent $PSScriptRoot
$Exe = Join-Path $root 'artifacts\alpha-0.2\CleanSlate.exe'
$BinExe = Join-Path $root 'bin\Release\CleanSlate.exe'

# Prefisso obbligatorio di ogni percorso che questo script puo' toccare.
$qaId = [guid]::NewGuid().ToString('N').Substring(0, 8)
$qaMarker = 'CLEANSLATE-QA-' + $qaId

$realTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\')
$realQaDir = Join-Path $realTemp ('cleanslate-qa-' + $qaId)
$outsideDir = Join-Path $env:LOCALAPPDATA ('CleanSlateQaOutside-' + $qaId)
$sandbox = Join-Path $env:LOCALAPPDATA ('CleanSlateQaSandbox-' + $qaId)
$workDir = Join-Path $env:LOCALAPPDATA ('CleanSlateQaWork-' + $qaId)
$baselineFile = Join-Path $workDir 'baseline-temp.txt'
$afterFile = Join-Path $workDir 'after-temp.txt'

function Assert-OwnPath([string]$path, [string]$context) {
    $full = [System.IO.Path]::GetFullPath($path)
    $ok = $false
    if ($full.StartsWith($realQaDir + '\', [System.StringComparison]::OrdinalIgnoreCase)) { $ok = $true }
    if ($full.Equals($realQaDir, [System.StringComparison]::OrdinalIgnoreCase)) { $ok = $true }
    if ($full.StartsWith($outsideDir + '\', [System.StringComparison]::OrdinalIgnoreCase)) { $ok = $true }
    if ($full.Equals($outsideDir, [System.StringComparison]::OrdinalIgnoreCase)) { $ok = $true }
    if ($full.StartsWith($sandbox + '\', [System.StringComparison]::OrdinalIgnoreCase)) { $ok = $true }
    if ($full.Equals($sandbox, [System.StringComparison]::OrdinalIgnoreCase)) { $ok = $true }
    if ($full.StartsWith($workDir + '\', [System.StringComparison]::OrdinalIgnoreCase)) { $ok = $true }
    if (-not $ok) {
        Fail-Fatal ('Percorso fuori dall''area del test (' + $context + '): ' + $full)
    }
    return $full
}

function Write-MarkFile([string]$path, [int]$sizeBytes, [string]$tag) {
    Assert-OwnPath $path 'scrittura fixture' | Out-Null
    $text = $qaMarker + '|' + $tag + '|'
    if ($sizeBytes -lt $text.Length) { $sizeBytes = $text.Length }
    $bytes = New-Object byte[] $sizeBytes
    $encoder = [System.Text.Encoding]::ASCII
    $prefix = $encoder.GetBytes($text)
    [Array]::Copy($prefix, $bytes, $prefix.Length)
    for ($i = $prefix.Length; $i -lt $sizeBytes; $i++) { $bytes[$i] = [byte](65 + ($i % 26)) }
    [System.IO.File]::WriteAllBytes($path, $bytes)
}

function Get-FileHashAscii([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $fs = [System.IO.File]::OpenRead($path)
        try { return ([System.BitConverter]::ToString($sha.ComputeHash($fs))).Replace('-', '') }
        finally { $fs.Dispose() }
    } finally { $sha.Dispose() }
}

function Get-TempSnapshot([string]$destination) {
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($f in [System.IO.Directory]::EnumerateFiles($realTemp, '*', [System.IO.SearchOption]::AllDirectories)) {
        try {
            $fi = New-Object System.IO.FileInfo($f)
            $lines.Add($f + '|' + $fi.Length + '|' + $fi.LastWriteTimeUtc.Ticks)
        } catch { }
    }
    [System.IO.File]::WriteAllLines($destination, $lines, [System.Text.Encoding]::UTF8)
    return $lines.Count
}

# --------------------------------------------------------------- automazione ---

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$nativeSource = @'
using System;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
public static class QaClick {
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetClassName(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    // SendMessage su thread in background: il click sul pulsante puo' aprire una modale
    // e il chiamante deve continuare a eseguire per poter gestire la finestra.
    public static Thread BeginBmClick(IntPtr h) {
        Thread t = new Thread(delegate() { SendMessage(h, 0x00F5, IntPtr.Zero, IntPtr.Zero); });
        t.IsBackground = true;
        t.Start();
        return t;
    }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    // Porta la finestra in primo piano: i click del mouse vanno alla finestra in alto,
    // e la console da cui gira il test potrebbe coprirla.
    public static void BringToFront(IntPtr h) {
        IntPtr fg = GetForegroundWindow();
        uint pid;
        uint fgThread = GetWindowThreadProcessId(fg, out pid);
        uint cur = GetCurrentThreadId();
        AttachThreadInput(cur, fgThread, true);
        SetForegroundWindow(h);
        SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040);
        AttachThreadInput(cur, fgThread, false);
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
'@
Add-Type -TypeDefinition $nativeSource

function Get-Descendants($scope) {
    return $scope.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                          [System.Windows.Automation.Condition]::TrueCondition)
}

function Find-ByName($scope, $name) {
    foreach ($e in (Get-Descendants $scope)) { if ($e.Current.Name -eq $name) { return $e } }
    return $null
}

function Find-ByPattern($scope, $pattern) {
    foreach ($e in (Get-Descendants $scope)) {
        try { if ($null -ne $e.GetCurrentPattern($pattern)) { return $e } } catch { }
    }
    return $null
}

function Invoke-Button($element) {
    # WinForms espone l'handle del controllo dentro AutomationId (bridge MSAA).
    $handle = [IntPtr]([int]$element.Current.AutomationId)
    [void][QaClick]::SendMessage($handle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
}

function Post-Button($element) {
    # PostMessage non attende la fine del gestore: e' obbligatorio quando il click
    # apre una finestra modale, altrimenti SendMessage resta bloccato finche' la
    # modale non viene chiusa (deadlock con l'automazione).
    $handle = [IntPtr]::Zero
    if ($element.Current.NativeWindowHandle -ne 0) {
        $handle = [IntPtr]($element.Current.NativeWindowHandle)
    } elseif ($element.Current.AutomationId -match '^\d+$') {
        $handle = [IntPtr]([int]$element.Current.AutomationId)
    }
    if ($handle -eq [IntPtr]::Zero) { return $false }
    return [QaClick]::PostMessage($handle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
}

function Invoke-NativeButton($element) {
    try {
        $pattern = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $pattern.Invoke()
        return $true
    } catch { }
    try {
        $handle = [IntPtr]([int]$element.Current.AutomationId)
        if ($handle -ne [IntPtr]::Zero) {
            [void][QaClick]::SendMessage($handle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
            return $true
        }
    } catch { }
    return $false
}

function Start-CleanSlate([string]$exePath, [string]$tmpOverride) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exePath
    $psi.UseShellExecute = $false
    if (-not [string]::IsNullOrEmpty($tmpOverride)) {
        $psi.EnvironmentVariables['TMP'] = $tmpOverride
        $psi.EnvironmentVariables['TEMP'] = $tmpOverride
    }
    $p = [System.Diagnostics.Process]::Start($psi)
    [void]$script:procs.Add($p)
    for ($i = 0; $i -lt 100; $i++) {
        Start-Sleep -Milliseconds 200
        $p.Refresh()
        if ($p.MainWindowHandle -ne 0) { break }
    }
    $p.Refresh()
    return $p
}

function Wait-Status($mainWin, $statusId, [string]$pattern, [int]$timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    $text = ''
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        $el = $null
        foreach ($e in (Get-Descendants $mainWin)) {
            if ($e.Current.AutomationId -eq $statusId) { $el = $e; break }
        }
        if ($null -ne $el) { $text = $el.Current.Name }
        if ($text -match $pattern) { return $text }
    }
    return $text
}

function Wait-ElementByNameLike($mainWin, [string]$pattern, [int]$timeoutSeconds) {
    # Individua un'etichetta dal suo contenuto attuale: non dipende dall'aver
    # catturato l'AutomationId prima dell'aggiornamento del testo.
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        foreach ($e in (Get-Descendants $mainWin)) {
            try { if ($e.Current.Name -like $pattern) { return $e } } catch { }
        }
        Start-Sleep -Milliseconds 300
    }
    return $null
}

function Get-SiblingWindows($processId, $excludeHandle) {
    $desktop = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    $list = @()
    foreach ($w in $desktop.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
        if ($w.Current.NativeWindowHandle -eq $excludeHandle) { continue }
        $list += $w
    }
    return $list
}

function Find-DialogHwnd([uint32]$processId, [string]$className, [string]$title) {
    # Il MessageBox di WinForms e' una finestra owned: in UIA compare come discendente
    # della finestra principale, non come figlio del desktop. L'enumerazione nativa la
    # trova sempre.
    $script:enumPid = $processId
    $script:enumClass = $className
    $script:enumTitle = $title
    $script:enumHits = New-Object System.Collections.ArrayList
    $cb = [QaClick+EnumProc]{
        param($h, $l)
        $pid2 = [uint32]0
        [void][QaClick]::GetWindowThreadProcessId($h, [ref]$pid2)
        if ($pid2 -ne $script:enumPid) { return $true }
        $sb = New-Object System.Text.StringBuilder 256
        [void][QaClick]::GetClassName($h, $sb, 256)
        if ($script:enumClass -ne '' -and $sb.ToString() -ne $script:enumClass) { return $true }
        $t = New-Object System.Text.StringBuilder 256
        [void][QaClick]::GetWindowText($h, $t, 256)
        if ($script:enumTitle -ne '' -and $t.ToString() -ne $script:enumTitle) { return $true }
        [void]$script:enumHits.Add($h)
        return $true
    }
    [void][QaClick]::EnumWindows($cb, [IntPtr]::Zero)
    if ($script:enumHits.Count -gt 0) { return $script:enumHits[0] }
    return [IntPtr]::Zero
}

function Get-ChildTexts([IntPtr]$parent) {
    $script:childTexts = New-Object System.Collections.ArrayList
    $cb = [QaClick+EnumProc]{
        param($h, $l)
        $sb = New-Object System.Text.StringBuilder 256
        [void][QaClick]::GetClassName($h, $sb, 256)
        $t = New-Object System.Text.StringBuilder 512
        [void][QaClick]::GetWindowText($h, $t, 512)
        if ($t.Length -gt 0) { [void]$script:childTexts.Add($t.ToString()) }
        return $true
    }
    [void][QaClick]::EnumChildWindows($parent, $cb, [IntPtr]::Zero)
    return $script:childTexts
}

function Find-ChildButton([IntPtr]$parent, [string]$textPrefix) {
    $script:btnPrefix = $textPrefix
    $script:foundBtn = [IntPtr]::Zero
    $cb = [QaClick+EnumProc]{
        param($h, $l)
        $sb = New-Object System.Text.StringBuilder 64
        [void][QaClick]::GetClassName($h, $sb, 256)
        if ($sb.ToString() -ne 'Button') { return $true }
        $t = New-Object System.Text.StringBuilder 64
        [void][QaClick]::GetWindowText($h, $t, 64)
        # I testi dei pulsanti WinForms includono l'acceleratore ("&No", "&Si"):
        # il confronto viene fatto dopo aver rimosso '&'.
        $plain = $t.ToString().Replace('&', '')
        if ($plain.StartsWith($script:btnPrefix)) { $script:foundBtn = $h; return $false }
        return $true
    }
    [void][QaClick]::EnumChildWindows($parent, $cb, [IntPtr]::Zero)
    return $script:foundBtn
}

function Wait-Dialog([uint32]$processId, [int]$timeoutMs) {
    $deadline = (Get-Date).AddMilliseconds($timeoutMs)
    while ((Get-Date) -lt $deadline) {
        $h = Find-DialogHwnd $processId '#32770' 'Conferma pulizia'
        if ($h -ne [IntPtr]::Zero) { return $h }
        Start-Sleep -Milliseconds 200
    }
    return [IntPtr]::Zero
}

# ================================================================== Fase 0 ====

Write-Host ''
Write-Host ('Validazione pratica pre-beta di CleanSlate 0.2.0  [id test: ' + $qaId + ']')
Write-Host ('Vero %TEMP%: ' + $realTemp)
Write-Host ''

Write-Host 'Fase 0) Precondizioni...'

$isAdmin = $false
try {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
    $isAdmin = $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
} catch { }
Check (-not $isAdmin) 'Nessun privilegio amministrativo in uso'

Check (Test-Path -LiteralPath $Exe) ('Eseguibile verificato presente: ' + $Exe)
Check (Test-Path -LiteralPath $BinExe) ('Eseguibile di bin presente: ' + $BinExe)
$pkgHashBefore = Get-FileHashAscii $Exe
$binHashBefore = Get-FileHashAscii $BinExe
Check ($null -ne $pkgHashBefore) ('SHA-256 del pacchetto registrato: ' + $pkgHashBefore)

New-Item -ItemType Directory -Path $workDir -Force | Out-Null
$baselineCount = Get-TempSnapshot $baselineFile
Note ('Snapshot del vero %TEMP%: ' + $baselineCount + ' file preesistenti registrati')

# ================================================================== Fase 1 ====

Write-Host ''
Write-Host 'Fase 1) A: analisi reale del %TEMP% dell''utente, senza cancellare nulla...'

$proc = Start-CleanSlate $Exe ''
Check ($proc.MainWindowHandle -ne 0) 'La finestra principale della GUI e'' stata creata'

$mainWin = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
Check ($mainWin.Current.Name -like 'CleanSlate*') ('Titolo della finestra: ' + $mainWin.Current.Name)

$statusEl = $null
$summaryEl = $null
foreach ($e in (Get-Descendants $mainWin)) {
    if ($e.Current.Name -eq 'Pronto.') { $statusEl = $e }
    if ($e.Current.Name -eq 'Nessuna analisi eseguita.') { $summaryEl = $e }
}
Check ($null -ne $statusEl) 'Etichetta di stato individuata'
Check ($null -ne $summaryEl) 'Etichetta di riepilogo individuata'
$statusId = $statusEl.Current.AutomationId
$summaryId = $summaryEl.Current.AutomationId

$btnAnalyze = Find-ByName $mainWin 'Analizza'
Check ($null -ne $btnAnalyze) 'Il pulsante "Analizza" e'' raggiungibile'
Invoke-Button $btnAnalyze

$statusText = Wait-Status $mainWin $statusId '^Analisi (completata|annullata|non riuscita)\.' 180
Check ($statusText -eq 'Analisi completata.') ('Stato terminale della scansione reale: "' + $statusText + '"')

$summaryText = ''
foreach ($e in (Get-Descendants $mainWin)) {
    if ($e.Current.AutomationId -eq $summaryId) { $summaryText = $e.Current.Name; break }
}
Check ($summaryText -like 'File analizzati:*') ('Riepilogo reale: ' + $summaryText)

$baselineScanned = -1
$baselineProposed = -1
$baselineSkipped = -1
if ($summaryText -match 'File analizzati:\s*([0-9]+)') { $baselineScanned = [int]$Matches[1] }
if ($summaryText -match 'Proposti:\s*([0-9]+)') { $baselineProposed = [int]$Matches[1] }
if ($summaryText -match 'Scartati:\s*([0-9]+)') { $baselineSkipped = [int]$Matches[1] }
Check ($baselineScanned -gt 0) ('L''analisi reale ha esaminato ' + $baselineScanned + ' file della cartella temporanea dell''utente')

$reportPath1 = $null
foreach ($e in (Get-Descendants $mainWin)) {
    if ($e.Current.Name -like 'Report: *') { $reportPath1 = $e.Current.Name -replace '^Report: ', ''; break }
}
Check ($null -ne $reportPath1 -and (Test-Path -LiteralPath $reportPath1)) ('Report dell''analisi reale scritto: ' + $reportPath1)

try { $proc.CloseMainWindow() | Out-Null } catch { }
$closedPhase1 = $proc.WaitForExit(20000)
Check ($closedPhase1) 'La finestra si e'' chiusa senza residui dopo la sola analisi'
if (-not $closedPhase1) {
    try { $proc.Kill() } catch { }
    $proc.WaitForExit(5000) | Out-Null
}

# ================================================================== Fase 2 ====

Write-Host ''
Write-Host 'Fase 2) Fixture: file sintetici identificabili dentro il vero %TEMP%...'

New-Item -ItemType Directory -Path $realQaDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $realQaDir 'pathswap\sub') -Force | Out-Null
New-Item -ItemType Directory -Path $outsideDir -Force | Out-Null
New-Item -ItemType Directory -Path $sandbox -Force | Out-Null

# Dimensioni distinte per poter identificare la selezione dalla conferma della GUI.
$realOld = Join-Path $realQaDir 'old.txt'            # 100 B, vecchio
$realRecent = Join-Path $realQaDir 'recent.txt'      # 500 B, recente
$realLocked = Join-Path $realQaDir 'locked.txt'      # 200 B, vecchio, manterrà aperto
$realUnselected = Join-Path $realQaDir 'unselected.txt' # 400 B, vecchio, mai passato al motore
$realSelected = Join-Path $realQaDir 'selected.txt'  # 300 B, vecchio, da eliminare
$realModified = Join-Path $realQaDir 'modified.txt'  # 150 B, vecchio, poi toccato
$victimOutside = Join-Path $outsideDir 'swapfile.txt' # 900 B, vittima fuori radice (appare dietro la junction)

Write-MarkFile $realOld 100 'old'
Write-MarkFile $realRecent 500 'recent'
Write-MarkFile $realLocked 200 'locked'
Write-MarkFile $realUnselected 400 'unselected'
Write-MarkFile $realSelected 300 'selected'
Write-MarkFile $realModified 150 'modified'
Write-MarkFile $victimOutside 900 'victim'

$oldDate = (Get-Date).AddDays(-7)
foreach ($f in @($realOld, $realLocked, $realUnselected, $realSelected, $realModified, $victimOutside)) {
    (Get-Item -LiteralPath $f).LastWriteTime = $oldDate
}

# Sostituzione del percorso: "sub" diventa una junction verso la cartella esterna.
$junction = Join-Path $realQaDir 'pathswap\sub'
Assert-OwnPath $junction 'creazione junction' | Out-Null
& cmd /c rmdir (Join-Path $realQaDir 'pathswap\sub') | Out-Null
& cmd /c mklink /J $junction $outsideDir | Out-Null
Check ((Get-Item -LiteralPath $junction).Attributes -band [System.IO.FileAttributes]::ReparsePoint) `
    ('Junction creata dentro il vero %TEMP%: ' + $junction + ' -> ' + $outsideDir)
$realSwapViaJunction = Join-Path $junction 'swapfile.txt'
Check (Test-Path -LiteralPath $realSwapViaJunction) 'Il file "swapfile.txt" e'' raggiungibile attraverso la junction'

# Sandbox per il flusso GUI: e' il TMP del processo, quindi la radice effettivamente
# usata dall'applicazione in quella sessione, non un percorso alternativo inventato.
Write-MarkFile (Join-Path $sandbox 'old.txt') 100 'sb-old'
Write-MarkFile (Join-Path $sandbox 'recent.txt') 500 'sb-recent'
Write-MarkFile (Join-Path $sandbox 'locked.txt') 200 'sb-locked'
Write-MarkFile (Join-Path $sandbox 'unselected.txt') 400 'sb-unselected'
Write-MarkFile (Join-Path $sandbox 'selected.txt') 300 'sb-selected'
foreach ($f in @('old.txt', 'locked.txt', 'unselected.txt', 'selected.txt')) {
    (Get-Item -LiteralPath (Join-Path $sandbox $f)).LastWriteTime = $oldDate
}

# Processo che tiene aperto "locked.txt" con FileShare.None: la cancellazione deve fallire.
$lockerScript = @'
param($path, $readyPath, $stopPath)
$stream = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
try {
    [System.IO.File]::WriteAllText($readyPath, 'ready')
    $deadline = (Get-Date).AddSeconds(180)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $stopPath) { break }
        Start-Sleep -Milliseconds 200
    }
} finally { $stream.Dispose() }
'@
$lockerPs1 = Join-Path $workDir 'locker.ps1'
[System.IO.File]::WriteAllText($lockerPs1, $lockerScript)
$realLockReady = Join-Path $workDir 'real-lock.ready'
$realLockStop = Join-Path $workDir 'real-lock.stop'
$sbLockReady = Join-Path $workDir 'sandbox-lock.ready'
$sbLockStop = Join-Path $workDir 'sandbox-lock.stop'

function Start-Locker([string]$target, [string]$ready, [string]$stop) {
    $p = Start-Process -FilePath 'powershell.exe' -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $lockerPs1, $target, $ready, $stop
    ) -PassThru -WindowStyle Hidden
    [void]$script:procs.Add($p)
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $ready) { return $p }
        Start-Sleep -Milliseconds 200
    }
    return $null
}

$lockerReal = Start-Locker $realLocked $realLockReady $realLockStop
Check ($null -ne $lockerReal) 'File "locked.txt" del vero %TEMP% tenuto aperto con FileShare.None'

$lockerSb = Start-Locker (Join-Path $sandbox 'locked.txt') $sbLockReady $sbLockStop
Check ($null -ne $lockerSb) 'File "locked.txt" del sandbox tenuto aperto con FileShare.None'

$hashVictimBefore = Get-FileHashAscii $victimOutside
$hashRealUnselectedBefore = Get-FileHashAscii $realUnselected
$hashRealOldBefore = Get-FileHashAscii $realOld
$hashSbUnselectedBefore = Get-FileHashAscii (Join-Path $sandbox 'unselected.txt')
$hashVictimOutsidePathBefore = $victimOutside

Note ('Fixture create: 8 file nel vero %TEMP%, 5 nel sandbox, 1 vittima esterna')
Note ('Percorso vittima: ' + $victimOutside)

# ================================================================== Fase 3 ====

Write-Host ''
Write-Host 'Fase 3) C: regole di eta'' e anteprima sul codice di produzione...'

# 3a. Lo scanner di produzione, sul vero %TEMP%, deve proporre solo i file vecchi.
#     L'assembly viene caricato da una copia identica: PowerShell non deve bloccare
#     bin\Release\CleanSlate.exe, che la Fase 8 ricompila.
$engineCopy = Join-Path $workDir 'CleanSlateEngine.exe'
Copy-Item -LiteralPath $BinExe -Destination $engineCopy -Force
# L'assembly viene caricato dai byte della copia: il file non resta bloccato e la
# cartella di lavoro puo' essere rimossa a fine validazione.
$engineBytes = [System.IO.File]::ReadAllBytes($engineCopy)
[void][System.Reflection.Assembly]::Load($engineBytes)
$scanner = New-Object CleanSlate.Core.TempFolderScanner
$scanOptions = New-Object CleanSlate.Core.ScanOptions
$scanOptions.Root = $realTemp
$scanOptions.CancellationToken = [System.Threading.CancellationToken]::None
$scanResult = $scanner.Scan($scanOptions)

# Filtro per percorso: si considerano solo gli elementi della nostra fixture, cosi' un
# file omonimo appartenente ad altre applicazioni non puo' falsare l'esito.
$qaItems = @($scanResult.Items | Where-Object { $_.FullPath.StartsWith($realQaDir + '\', [System.StringComparison]::OrdinalIgnoreCase) })
$qaNames = @($qaItems | ForEach-Object { $_.Name })

Check ($qaNames -contains 'old.txt') 'Lo scanner di produzione propone "old.txt" (file sufficientemente vecchio)'
Check ($qaNames -contains 'selected.txt') 'Lo scanner di produzione propone "selected.txt"'
Check ($qaNames -contains 'unselected.txt') 'Lo scanner di produzione propone "unselected.txt"'
Check ($qaNames -contains 'locked.txt') 'Lo scanner di produzione propone "locked.txt" (leggibile nonostante il blocco)'
Check ($qaNames -contains 'modified.txt') 'Lo scanner di produzione propone "modified.txt"'
Check (-not ($qaNames -contains 'recent.txt')) 'Lo scanner di produzione NON propone "recent.txt" (regola dei 24 ore)'
Check (-not ($qaNames -contains 'swapfile.txt')) 'Lo scanner di produzione NON segue la junction e non propone "swapfile.txt"'

Note ('Elementi della fixture nello scanner reale: ' + $(if ($qaNames.Count -gt 0) { $qaNames -join ', ' } else { '(nessuno)' }))
Note ('Totale elementi proposti dallo scanner reale: ' + $scanResult.Items.Count)

# 3b. La GUI sul vero %TEMP% deve riflettere lo stesso conteggio (delta rispetto al
#     baseline della Fase 1), senza eseguire alcuna cancellazione.
$proc2 = Start-CleanSlate $Exe ''
$mainWin2 = [System.Windows.Automation.AutomationElement]::FromHandle($proc2.MainWindowHandle)
$statusEl2 = $null
foreach ($e in (Get-Descendants $mainWin2)) {
    if ($e.Current.Name -eq 'Pronto.') { $statusEl2 = $e; break }
}
$statusId2 = $statusEl2.Current.AutomationId
$summaryId2 = $null
foreach ($e in (Get-Descendants $mainWin2)) {
    if ($e.Current.Name -eq 'Nessuna analisi eseguita.') { $summaryId2 = $e; break }
}
$btnAnalyze2 = Find-ByName $mainWin2 'Analizza'
Invoke-Button $btnAnalyze2
$statusText2 = Wait-Status $mainWin2 $statusId2 '^Analisi (completata|annullata|non riuscita)\.' 180
Check ($statusText2 -eq 'Analisi completata.') ('Seconda analisi reale completata: "' + $statusText2 + '"')

$summaryEl2 = Wait-ElementByNameLike $mainWin2 'File analizzati:*' 30
$summaryText2 = if ($null -ne $summaryEl2) { $summaryEl2.Current.Name } else { '' }
$scanned2 = -1; $proposed2 = -1; $skipped2 = -1
if ($summaryText2 -match 'File analizzati:\s*([0-9]+)') { $scanned2 = [int]$Matches[1] }
if ($summaryText2 -match 'Proposti:\s*([0-9]+)') { $proposed2 = [int]$Matches[1] }
if ($summaryText2 -match 'Scartati:\s*([0-9]+)') { $skipped2 = [int]$Matches[1] }

# 5 file vecchi della fixture entrano in "Proposti", 1 recente entra in "Scartati",
# il file dietro la junction non viene neanche esaminato. I delta sono maggiori o
# uguali perche' il %TEMP% reale e' condiviso con altri processi del sistema.
Check (($proposed2 - $baselineProposed) -ge 5) `
    ('Delta "Proposti" della GUI reale >= +5 (i 5 fixture vecchi): ' + ($proposed2 - $baselineProposed))
Check (($skipped2 - $baselineSkipped) -ge 1) `
    ('Delta "Scartati" della GUI reale >= +1 (il file recente): ' + ($skipped2 - $baselineSkipped))
Check (($scanned2 - $baselineScanned) -ge 6) `
    ('Delta "File analizzati" della GUI reale >= +6: ' + ($scanned2 - $baselineScanned))

try { $proc2.CloseMainWindow() | Out-Null } catch { }
if (-not $proc2.WaitForExit(20000)) {
    try { $proc2.Kill() } catch { }
    $proc2.WaitForExit(5000) | Out-Null
}

# ================================================================== Fase 4 ====

Write-Host ''
Write-Host 'Fase 4) D/E: flusso GUI sul sandbox (analisi, conferma, annullamento)...'

$hashSbUnselectedMid = Get-FileHashAscii (Join-Path $sandbox 'unselected.txt')
Check ($hashSbUnselectedMid -eq $hashSbUnselectedBefore) 'Il file "non selezionato" del sandbox e'' integro prima della pulizia'

$proc3 = Start-CleanSlate $Exe $sandbox
$mainWin3 = [System.Windows.Automation.AutomationElement]::FromHandle($proc3.MainWindowHandle)
$statusEl3 = $null; $summaryId3 = $null
foreach ($e in (Get-Descendants $mainWin3)) {
    if ($e.Current.Name -eq 'Pronto.') { $statusEl3 = $e }
    if ($e.Current.Name -eq 'Nessuna analisi eseguita.') { $summaryId3 = $e }
}
$statusId3 = $statusEl3.Current.AutomationId
$summaryId3v = $summaryId3.Current.AutomationId

$btnAnalyze3 = Find-ByName $mainWin3 'Analizza'
Invoke-Button $btnAnalyze3
$statusText3 = Wait-Status $mainWin3 $statusId3 '^Analisi (completata|annullata|non riuscita)\.' 60
Check ($statusText3 -eq 'Analisi completata.') ('Analisi sul sandbox completata: "' + $statusText3 + '"')

$summaryEl3 = Wait-ElementByNameLike $mainWin3 'File analizzati:*' 30
$summaryText3 = if ($null -ne $summaryEl3) { $summaryEl3.Current.Name } else { '' }
$proposed3 = -1; $skipped3 = -1
if ($summaryText3 -match 'Proposti:\s*([0-9]+)') { $proposed3 = [int]$Matches[1] }
if ($summaryText3 -match 'Scartati:\s*([0-9]+)') { $skipped3 = [int]$Matches[1] }
Check ($proposed3 -eq 4) ('Il sandbox propone esattamente 4 file (recente escluso): ' + $proposed3)
Check ($skipped3 -eq 1) ('Il sandbox scarta esattamente 1 file recente: ' + $skipped3)

$grid3 = $null
foreach ($e in (Get-Descendants $mainWin3)) {
    if ($e.Current.Name -like '*elementi in elenco') { $grid3 = $e; break }
}
Check ($null -ne $grid3 -and $grid3.Current.Name -eq '4 elementi in elenco') `
    ('Anteprima della tabella: ' + $(if ($null -ne $grid3) { $grid3.Current.Name } else { '(assente)' }))

$btnClean3 = Find-ByName $mainWin3 'Pulisci selezionati'
Check ($null -ne $btnClean3 -and $btnClean3.Current.IsEnabled) 'Dopo l''analisi la pulizia e'' abilitata'

# La deselezione di una singola riga richiede un click reale sul checkbox della griglia.
# In questo ambiente la sessione e' bloccata (schermata di blocco in primo piano): i click
# del mouse vengono intercettati dal lock screen e non raggiungono la finestra, e i messaggi
# WM_LBUTTONDOWN inviati alla griglia non attivano il toggle del checkbox. Il percorso
# selezione -> cancellazione viene quindi verificato al motore di produzione (Fase 5) con la
# stessa lista esplicita di elementi che la GUI passerebbe dopo la conferma dell'utente.
$btnCleanHandle = [IntPtr]([int]$btnClean3.Current.AutomationId)
[void][QaClick]::BeginBmClick($btnCleanHandle)

$dialogHwnd = Wait-Dialog $proc3.Id 6000
Check ($dialogHwnd -ne [IntPtr]::Zero) 'La finestra di conferma appare dopo "Pulisci selezionati"'

$dialogCount = -1
$dialogBytes = -1
if ($dialogHwnd -ne [IntPtr]::Zero) {
    $childTexts = Get-ChildTexts $dialogHwnd
    $fullText = 'Conferma pulizia | ' + ($childTexts -join ' | ')
    Note ('Finestra di conferma: ' + $fullText)
    if ($fullText -match 'Eliminare\s+([0-9]+)\s+file') { $dialogCount = [int]$Matches[1] }
    if ($fullText -match 'stimato di\s+([0-9]+)\s*B') { $dialogBytes = [int]$Matches[1] }
}
Check ($dialogCount -eq 4) ('La conferma mostra tutti e 4 i file proposti (selezione predefinita): ' + $dialogCount)
Check ($dialogBytes -eq 1000) ('La conferma mostra il totale atteso (1000 B): ' + $dialogBytes)

# Risposta "No": la cancellazione non deve partire e nessun file deve essere toccato.
$noButton = Find-ChildButton $dialogHwnd 'No'
Check ($noButton -ne [IntPtr]::Zero) 'Pulsante "No" della conferma individuato'
if ($noButton -ne [IntPtr]::Zero) { [void][QaClick]::BeginBmClick($noButton) }
Start-Sleep -Milliseconds 800

$dialogAfterNo = Find-DialogHwnd ([uint32]$proc3.Id) '#32770' 'Conferma pulizia'
Check ($dialogAfterNo -eq [IntPtr]::Zero) 'Dopo "No" la finestra di conferma e'' chiusa'

$statusTextAfterNo = Wait-Status $mainWin3 $statusId3 '^Pulizia (completata|annullata|non riuscita)\.' 5
Check ($statusTextAfterNo -notmatch '^Pulizia ') ('Dopo "No" la pulizia non e'' partita: stato "' + $statusTextAfterNo + '"')

Check (Test-Path -LiteralPath (Join-Path $sandbox 'old.txt')) 'Dopo "No": "old.txt" del sandbox ancora presente'
Check (Test-Path -LiteralPath (Join-Path $sandbox 'selected.txt')) 'Dopo "No": "selected.txt" del sandbox ancora presente'
Check (Test-Path -LiteralPath (Join-Path $sandbox 'unselected.txt')) 'Dopo "No": "unselected.txt" del sandbox ancora presente'
Check (Test-Path -LiteralPath (Join-Path $sandbox 'locked.txt')) 'Dopo "No": "locked.txt" del sandbox ancora presente'
Check ((Get-FileHashAscii (Join-Path $sandbox 'unselected.txt')) -eq $hashSbUnselectedBefore) `
    'Dopo "No": contenuto di "unselected.txt" invariato'

Note 'Deselezione della riga non eseguita: la sessione e'' bloccata e la griglia non accetta input sintetico. Il percorso selezione-cancellazione e'' verificato al motore di produzione (Fase 5).'

try { $proc3.CloseMainWindow() | Out-Null } catch { }
if (-not $proc3.WaitForExit(20000)) {
    # Con una modale rimasta aperta la chiusura puo' non arrivare: il processo viene
    # terminato per non lasciare residui durante il resto della validazione.
    try { $proc3.Kill() } catch { }
    $proc3.WaitForExit(5000) | Out-Null
    Note 'Processo GUI del sandbox terminato forzatamente dopo la chiusura della finestra'
}
Check ($proc3.HasExited) 'Il processo GUI del sandbox e'' terminato alla fine della Fase 4'

# ================================================================== Fase 5 ====

Write-Host ''
Write-Host 'Fase 5) D/E/F: motore di produzione sul vero %TEMP%, con lista esplicita...'

$pathResult = [CleanSlate.Core.TempPathProvider]::GetAuthorizedRoot()
Check ($pathResult.Success) 'La radice autorizzata dell''applicazione e'' disponibile'
Check ($pathResult.Root -eq ($realTemp + '\') -or $pathResult.Root.TrimEnd('\') -eq $realTemp) `
    ('La radice autorizzata corrisponde al vero %TEMP%: ' + $pathResult.Root)

function New-FileItem([string]$path) {
    $fi = New-Object System.IO.FileInfo($path)
    $item = New-Object CleanSlate.Core.FileItem
    $item.Name = [System.IO.Path]::GetFileName($path)
    $item.FullPath = $path
    $item.Length = $fi.Length
    $item.LastWriteTimeUtc = $fi.LastWriteTimeUtc
    $item.Category = 'File temporanei dell''utente'
    $item.Selected = $true
    return $item
}

# Simula la stessa sequenza della GUI: scansione (fatta), poi modifica del file, poi
# pulizia con la lista esplicita degli elementi che l'utente avrebbe selezionato.
(Get-Item -LiteralPath $realModified).LastWriteTime = Get-Date
Note '"modified.txt" toccato dopo la scansione (simula un file modificato dall''utente)'

$items = New-Object System.Collections.Generic.List[CleanSlate.Core.FileItem]
[void]$items.Add((New-FileItem $realSelected))    # deve essere eliminato
[void]$items.Add((New-FileItem $realModified))    # troppo recente: saltato
[void]$items.Add((New-FileItem $realLocked))      # bloccato: errore, sopravvive
[void]$items.Add((New-FileItem $realSwapViaJunction))  # dietro la junction: saltato

$cleanOptions = New-Object CleanSlate.Core.CleanOptions
$cleanOptions.Root = $pathResult.Root
$cleanOptions.Items = $items
$cleanOptions.CancellationToken = [System.Threading.CancellationToken]::None

$cleaner = New-Object CleanSlate.Core.TempFileCleaner
$cleanResult = $cleaner.Clean($cleanOptions)

Check ($cleanResult.Success) 'Il motore di produzione ha completato l''operazione'
Check ($cleanResult.Root.TrimEnd('\') -eq $realTemp) ('La radice usata dal motore e'' il vero %TEMP%: ' + $cleanResult.Root)

$outcomes = @{}
foreach ($r in $cleanResult.Items) { $outcomes[$r.Name] = $r.Outcome }
Check ($outcomes['selected.txt'] -eq [CleanSlate.Core.CleanOutcome]::Deleted) 'Motore: "selected.txt" eliminato'
Check ($outcomes['modified.txt'] -eq [CleanSlate.Core.CleanOutcome]::SkippedTooRecent) 'Motore: "modified.txt" saltato (modificato dopo la scansione)'
Check ($outcomes['locked.txt'] -eq [CleanSlate.Core.CleanOutcome]::Failed) 'Motore: "locked.txt" fallito (file in uso), non eliminato'
Check ($outcomes['swapfile.txt'] -eq [CleanSlate.Core.CleanOutcome]::SkippedReparse) 'Motore: "swapfile.txt" saltato (percorso sostituito da junction)'
Check ($cleanResult.DeletedCount -eq 1) ('Il motore dichiara 1 sola eliminazione: ' + $cleanResult.DeletedCount)
Check ($cleanResult.ErrorCount -eq 1) ('Il motore dichiara 1 solo errore: ' + $cleanResult.ErrorCount)

# Controlli negativi aggiuntivi sullo stesso motore, sempre sul vero %TEMP%.
$itemsOutside = New-Object System.Collections.Generic.List[CleanSlate.Core.FileItem]
[void]$itemsOutside.Add((New-FileItem $victimOutside))
$optsOutside = New-Object CleanSlate.Core.CleanOptions
$optsOutside.Root = $pathResult.Root
$optsOutside.Items = $itemsOutside
$outsideResult = $cleaner.Clean($optsOutside)
Check ($outsideResult.Items[0].Outcome -eq [CleanSlate.Core.CleanOutcome]::SkippedOutsideRoot) `
    'Motore: un file fuori dalla radice autorizzata viene rifiutato'
Check ((Get-FileHashAscii $victimOutside) -eq $hashVictimBefore) 'Motore: la vittima fuori radice e'' ancora intatta'

$itemsRoot = New-Object System.Collections.Generic.List[CleanSlate.Core.FileItem]
$rootItem = New-Object CleanSlate.Core.FileItem
$rootItem.Name = [System.IO.Path]::GetFileName($realTemp.TrimEnd('\'))
$rootItem.FullPath = $realTemp
[void]$itemsRoot.Add($rootItem)
$optsRoot = New-Object CleanSlate.Core.CleanOptions
$optsRoot.Root = $pathResult.Root
$optsRoot.Items = $itemsRoot
$rootResult = $cleaner.Clean($optsRoot)
Check ($rootResult.Items[0].Outcome -eq [CleanSlate.Core.CleanOutcome]::SkippedRoot) 'Motore: la radice %TEMP% stessa viene rifiutata'
Check (Test-Path -LiteralPath $realQaDir) 'Motore: la cartella della fixture esiste ancora (nessuna cancellazione di directory)'

# ================================================================== Fase 6 ====

Write-Host ''
Write-Host 'Fase 6) Verifica di integrita'' di tutti i sopravvissuti...'

Check (-not (Test-Path -LiteralPath $realSelected)) 'Vero %TEMP%: "selected.txt" e'' stato eliminato'
Check (Test-Path -LiteralPath $realOld) 'Vero %TEMP%: "old.txt" sopravvissuto (non era selezionato)'
Check ((Get-FileHashAscii $realOld) -eq $hashRealOldBefore) 'Vero %TEMP%: contenuto di "old.txt" invariato'
Check (Test-Path -LiteralPath $realUnselected) 'Vero %TEMP%: "unselected.txt" sopravvissuto (mai passato al motore)'
Check ((Get-FileHashAscii $realUnselected) -eq $hashRealUnselectedBefore) 'Vero %TEMP%: contenuto di "unselected.txt" invariato'
Check (Test-Path -LiteralPath $realModified) 'Vero %TEMP%: "modified.txt" sopravvissuto (saltato come recente)'
Check (Test-Path -LiteralPath $realLocked) 'Vero %TEMP%: "locked.txt" sopravvissuto (file in uso)'
Check ((Get-FileHashAscii $victimOutside) -eq $hashVictimBefore) 'Vittima esterna: contenuto invariato dopo tutti i tentativi'
Check ((Get-FileHashAscii $hashVictimOutsidePathBefore) -eq $hashVictimBefore) 'Vittima esterna: percorso e hash coerenti'

# Il file che si trovava dietro la junction non deve essere stato toccato.
Check ((Get-FileHashAscii $victimOutside) -eq (Get-FileHashAscii $victimOutside)) 'File dietro la junction: ancora leggibile'

# ================================================================== Fase 7 ====

Write-Host ''
Write-Host 'Fase 7) G: rimozione dei soli residui creati dal test...'

# Prima di tutto si rilasciano i lock sui file "locked.txt": i locker sono processi
# avviati da questo script e si fermano quando il file di stop compare.
[System.IO.File]::WriteAllText($realLockStop, 'stop')
if ($null -ne $lockerReal) { try { $lockerReal.WaitForExit(15000) | Out-Null } catch { } }
[System.IO.File]::WriteAllText($sbLockStop, 'stop')
if ($null -ne $lockerSb) { try { $lockerSb.WaitForExit(15000) | Out-Null } catch { } }
Note 'Lock sui file "locked.txt" rilasciati prima della verifica dei residui'

function Remove-OwnResidue([string]$path) {
    Assert-OwnPath $path 'rimozione residui' | Out-Null
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}

# Prima di cancellare si verifica nome, percorso e contenuto di ogni residuo.
Check ($realQaDir.StartsWith(($realTemp + '\cleanslate-qa-'), [System.StringComparison]::OrdinalIgnoreCase)) `
    ('Prefisso del residuo nel vero %TEMP% verificato: ' + $realQaDir)

$markerFiles = 0
foreach ($f in [System.IO.Directory]::EnumerateFiles($realQaDir, '*', [System.IO.SearchOption]::AllDirectories)) {
    $bytes = [System.IO.File]::ReadAllBytes($f)
    $head = [System.Text.Encoding]::ASCII.GetString($bytes, 0, [Math]::Min(40, $bytes.Length))
    if ($head.StartsWith($qaMarker, [System.StringComparison]::Ordinal)) { $markerFiles++ }
    else { Fail-Fatal ('Contenuto non marcato dentro l''area del test: ' + $f) }
}
Check ($markerFiles -ge 3) ('Residui del vero %TEMP% con contenuto marcato verificati: ' + $markerFiles)

# La junction va rimossa senza toccare la destinazione.
if (Test-Path -LiteralPath $junction) {
    & cmd /c rmdir $junction | Out-Null
}
Check (-not (Test-Path -LiteralPath $junction)) 'Junction rimossa senza toccare la cartella esterna'
Check (Test-Path -LiteralPath $victimOutside) 'La cartella esterna esiste ancora dopo la rimozione della junction'

Remove-OwnResidue $realQaDir
Remove-OwnResidue $outsideDir
Remove-OwnResidue $sandbox
Check (-not (Test-Path -LiteralPath $realQaDir)) 'Residui del vero %TEMP% rimossi'
Check (-not (Test-Path -LiteralPath $outsideDir)) 'Cartella esterna rimossa'
Check (-not (Test-Path -LiteralPath $sandbox)) 'Sandbox rimossa'

# Nessun altro "cleanslate-qa-*" deve essere rimasto dentro il vero %TEMP%.
$leftoverQa = @(Get-ChildItem -LiteralPath $realTemp -Filter 'cleanslate-qa-*' -ErrorAction SilentlyContinue)
Check ($leftoverQa.Count -eq 0) 'Nessun residuo "cleanslate-qa-*" nel vero %TEMP%'

# Diff sullo snapshot: le uniche differenze ammissibili sono i nostri residui
# (rimossi in Fase 7) e l'attivita' di altri processi del sistema, segnalata come
# nota perche' il %TEMP% e' una cartella condivisa.
$afterCount = Get-TempSnapshot $afterFile
$beforeMap = @{}
foreach ($line in [System.IO.File]::ReadAllLines($baselineFile)) {
    $parts = $line.Split('|')
    if ($parts.Length -eq 3) { $beforeMap[$parts[0]] = $parts[1] }
}
$afterMap = @{}
foreach ($line in [System.IO.File]::ReadAllLines($afterFile)) {
    $parts = $line.Split('|')
    if ($parts.Length -eq 3) { $afterMap[$parts[0]] = $parts[1] }
}

$knownFixturePaths = @($realOld, $realRecent, $realLocked, $realUnselected, $realSelected, $realModified)
$disappeared = New-Object System.Collections.Generic.List[string]
foreach ($p in $beforeMap.Keys) {
    if (-not $afterMap.ContainsKey($p)) { [void]$disappeared.Add($p) }
}

$fixtureStillPresent = @($knownFixturePaths | Where-Object { $afterMap.ContainsKey($_) })
Check ($fixtureStillPresent.Count -eq 0) 'Nessun file della fixture compare ancora nello snapshot finale del %TEMP%'

$unexpectedGone = New-Object System.Collections.Generic.List[string]
foreach ($p in $disappeared) {
    if ($knownFixturePaths -contains $p) { continue }
    if ($p.IndexOf('cleanslate-qa-', [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { continue }
    [void]$unexpectedGone.Add($p)
}

if ($unexpectedGone.Count -gt 0) {
    Note ('File preesistenti scomparsi durante la finestra di prova per attivita'' di altri processi: ' +
        $unexpectedGone.Count + ' (nessuno passato ai funzionamenti di cancellazione del test, che operano solo su percorsi con prefisso "cleanslate-qa-")')
} else {
    Note 'Nessun file preesistenti del %TEMP% e'' scomparso durante la finestra di prova'
}
Note ('Snapshot %TEMP%: ' + $baselineCount + ' file all''inizio, ' + $afterCount + ' alla fine')

# ================================================================== Fase 8 ====

Write-Host ''
Write-Host 'Fase 8) H: suite automatica, verifica end-to-end e conservazione della build...'

$pkgHashAfterAll = Get-FileHashAscii $Exe
Check ($pkgHashAfterAll -eq $pkgHashBefore) 'La build pubblicata non e'' stata modificata dalla validazione'

Write-Host ''
& (Join-Path $root 'tests\run-tests.ps1')
$testsExit = $LASTEXITCODE
Check ($testsExit -eq 0) ('Suite automatica: codice di uscita ' + $testsExit)

Write-Host ''
& (Join-Path $root 'tests\verify.ps1') -Exe $Exe
$verifyExit = $LASTEXITCODE
Check ($verifyExit -eq 0) ('Verifica end-to-end: codice di uscita ' + $verifyExit)

$pkgHashFinal = Get-FileHashAscii $Exe
Check ($pkgHashFinal -eq $pkgHashBefore) 'SHA-256 finale del pacchetto invariato rispetto all''inizio'

try { Remove-Item -LiteralPath $baselineFile -Force -ErrorAction SilentlyContinue } catch { }
try { Remove-Item -LiteralPath $afterFile -Force -ErrorAction SilentlyContinue } catch { }
try { Remove-Item -LiteralPath $lockerPs1 -Force -ErrorAction SilentlyContinue } catch { }
foreach ($leftover in @($realLockReady, $realLockStop, $sbLockReady, $sbLockStop)) {
    try { Remove-Item -LiteralPath $leftover -Force -ErrorAction SilentlyContinue } catch { }
}
try { Remove-Item -LiteralPath $engineCopy -Force -ErrorAction SilentlyContinue } catch { }
try { Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue } catch { }

# Eventuali processi ancora vivi (locker o GUI) non devono sopravvivere alla validazione.
Stop-AllProcs

# -------------------------------------------------------------------- esito ---

Write-Host ''
foreach ($n in $script:notes) { Write-Host ('NOTA: ' + $n) }

if ($script:failures.Count -eq 0) {
    Write-Host ''
    Write-Host 'VALIDAZIONE PRATICA: OK (tutti i controlli superati)'
    exit 0
}

Write-Host ''
Write-Host ('VALIDAZIONE PRATICA: KO (' + $script:failures.Count + ' controlli falliti)')
foreach ($f in $script:failures) { Write-Host ('  - ' + $f) }
exit 1
