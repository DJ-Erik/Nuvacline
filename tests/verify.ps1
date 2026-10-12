# Verifica end-to-end di CleanSlate su un eseguibile reale.
#
# Controlla:
#   1. l'avvio della GUI, la pressione di "Analizza" e la lettura dello stato finale;
#   2. che il report locale venga generato e che il percorso appaia nella finestra;
#   3. che la finestra si chiuda e il processo non resti residente;
#   4. che in fase di esecuzione non vengano aperte connessioni TCP in uscita;
#   5. che registro di sistema, servizi, attivita pianificate e avvio automatico
#      non subiscano modifiche.
#
# Nota: WinForms su .NET Framework espone i controlli tramite il bridge MSAA, quindi i
# pulsanti non offrono InvokePattern. L'attivazione avviene con BM_CLICK sull'handle del
# controllo, che e' esattamente cio' che l'automazione UIA riporta come AutomationId.
#
# Uso: powershell -ExecutionPolicy Bypass -File tests\verify.ps1 [-Exe <percorso>]
# Codice di uscita: 0 se tutti i controlli sono superati, 1 altrimenti.

param(
    [string]$Exe = ""
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

# Se qualche errore non previsto interrompe la verifica, il processo avviato non deve
# restare in esecuzione: viene terminato prima di uscire.
$script:proc = $null
trap {
    if ($null -ne $script:proc) { try { $script:proc.Kill() } catch { } }
    Write-Host ('ERRORE NON GESTITO NELLA VERIFICA: ' + $_.Exception.Message)
    exit 1
}

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrEmpty($Exe)) { $Exe = Join-Path $root 'bin\Release\CleanSlate.exe' }

$script:failures = New-Object System.Collections.ArrayList
$script:notes = New-Object System.Collections.ArrayList

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

# ---------------------------------------------------------------- snapshot ---

$registryKeys = @(
    'HKCU:\Software\CleanSlate',
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run',
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce',
    'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Windows',
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run',
    'HKCU:\Software\Microsoft\.NETFramework',
    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run'
)

function Get-RegistrySnapshot {
    $sb = New-Object System.Text.StringBuilder
    foreach ($key in $registryKeys) {
        [void]$sb.AppendLine('=== ' + $key)
        if (Test-Path -LiteralPath $key) {
            $item = Get-Item -LiteralPath $key
            foreach ($name in ($item.GetValueNames() | Sort-Object)) {
                [void]$sb.AppendLine('  V ' + $name + '=' + ($item.GetValue($name) | Out-String).Trim())
            }
            foreach ($sub in (Get-ChildItem -LiteralPath $key -ErrorAction SilentlyContinue | Sort-Object PSChildName)) {
                [void]$sb.AppendLine('  K ' + $sub.PSChildName)
            }
        } else {
            [void]$sb.AppendLine('  (assente)')
        }
    }
    return $sb.ToString()
}

function Get-ServiceNames {
    try { return ((Get-Service | ForEach-Object { $_.Name }) | Sort-Object) -join '|' }
    catch { return '(non disponibile)' }
}

function Get-ScheduledTaskNames {
    try {
        $cmd = Get-Command Get-ScheduledTask -ErrorAction SilentlyContinue
        if ($null -eq $cmd) { return '(modulo non disponibile)' }
        return ((Get-ScheduledTask | ForEach-Object { $_.TaskName }) | Sort-Object) -join '|'
    } catch { return '(non disponibile)' }
}

# --------------------------------------------------------------- automazione ---

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$nativeSource = @'
using System;
using System.Runtime.InteropServices;
public static class CleanSlateClick {
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
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

function Find-ById($scope, $id) {
    foreach ($e in (Get-Descendants $scope)) { if ($e.Current.AutomationId -eq $id) { return $e } }
    return $null
}

function Invoke-Button($element) {
    $handle = [IntPtr]([int]$element.Current.AutomationId)
    [void][CleanSlateClick]::SendMessage($handle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
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

function Close-SiblingWindows($processId, $excludeHandle, $log) {
    foreach ($w in Get-SiblingWindows $processId $excludeHandle) {
        if ($w.Current.Name) { [void]$log.Add($w.Current.Name) }
        try {
            $pattern = $w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
            $pattern.Close()
        } catch { }
        Start-Sleep -Milliseconds 300
    }
}

# ------------------------------------------------------------------ esecuzione ---

Write-Host ''
Write-Host 'Verifica end-to-end di CleanSlate'
Write-Host ('Eseguibile: ' + $Exe)
Write-Host ''

Check (Test-Path -LiteralPath $Exe) ('L''eseguibile esiste: ' + $Exe)
if (-not (Test-Path -LiteralPath $Exe)) { exit 1 }

Write-Host '1) Preparazione e snapshot iniziali...'
$registryBefore = Get-RegistrySnapshot
$servicesBefore = Get-ServiceNames
$tasksBefore = Get-ScheduledTaskNames

$reportsDir = Join-Path $env:LOCALAPPDATA 'CleanSlate\reports'
$reportCountBefore = 0
if (Test-Path -LiteralPath $reportsDir) {
    $reportCountBefore = @(Get-ChildItem -LiteralPath $reportsDir -Filter 'cleanslate-*.txt' -ErrorAction SilentlyContinue).Count
}

Write-Host '2) Avvio della finestra...'
$proc = Start-Process -FilePath $Exe -PassThru
$script:proc = $proc
for ($i = 0; $i -lt 100; $i++) {
    Start-Sleep -Milliseconds 200
    $proc.Refresh()
    if ($proc.MainWindowHandle -ne 0) { break }
}
$proc.Refresh()

Check ($proc.MainWindowHandle -ne 0) 'La finestra principale è stata creata'
if ($proc.MainWindowHandle -eq 0) {
    try { $proc.Kill() } catch { }
    exit 1
}

$mainWin = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
Check ($mainWin.Current.Name -like 'CleanSlate*') ('Titolo della finestra: ' + $mainWin.Current.Name)

$statusEl = Find-ByName $mainWin 'Pronto.'
$summaryEl = Find-ByName $mainWin 'Nessuna analisi eseguita. Età minima attuale: 24 ore.'
$reportEl = Find-ByName $mainWin 'Report: nessuno generato. Viene scritto al termine di un''analisi o di una pulizia.'
Check ($null -ne $statusEl) 'Etichetta di stato individuata'
Check ($null -ne $summaryEl) 'Etichetta di riepilogo individuata'
Check ($null -ne $reportEl) 'Etichetta del percorso del report individuata'
if ($null -eq $statusEl -or $null -eq $summaryEl -or $null -eq $reportEl) {
    try { $proc.Kill() } catch { }
    exit 1
}

$statusId = $statusEl.Current.AutomationId
$summaryId = $summaryEl.Current.AutomationId
$reportId = $reportEl.Current.AutomationId

# Stato iniziale: elenco vuoto e pulsante di pulizia disabilitato.
$gridEl = Find-ByName $mainWin 'Nessun elemento in elenco'
Check ($null -ne $gridEl) 'La tabella iniziale dichiara "Nessun elemento in elenco"'
$btnCleanEl = Find-ByName $mainWin 'Pulisci selezionati'
Check ($null -ne $btnCleanEl) 'Il pulsante "Pulisci selezionati" è presente'
if ($null -ne $btnCleanEl) {
    Check (-not $btnCleanEl.Current.IsEnabled) 'Prima dell''analisi la pulizia è disabilitata (nessuna selezione)'
}

$dialogLog = New-Object System.Collections.ArrayList
$mainHandle = $mainWin.Current.NativeWindowHandle

Write-Host '3) Pressione di "Analizza"...'
$btnAnalyze = Find-ByName $mainWin 'Analizza'
Check ($null -ne $btnAnalyze) 'Il pulsante "Analizza" è raggiungibile'
if ($null -eq $btnAnalyze) {
    try { $proc.Kill() } catch { }
    exit 1
}

$netSamples = 0
$netHits = 0

# Campionamento delle connessioni TCP in uscita in un job in parallelo, cosi' da coprire
# anche le operazioni che terminano in meno di un secondo.
$netJob = Start-Job -ScriptBlock {
    param($processId, $durationSeconds, $minSamples)
    $samples = 0
    $hits = 0
    $deadline = (Get-Date).AddSeconds($durationSeconds)
    $hardStop = (Get-Date).AddSeconds($durationSeconds + 60)
    while (((Get-Date) -lt $deadline) -or (($samples -lt $minSamples) -and (Get-Date) -lt $hardStop)) {
        $samples++
        try {
            $conns = @(Get-NetTCPConnection -OwningProcess $processId -ErrorAction SilentlyContinue |
                Where-Object { $_.State -eq 'Established' -and $_.RemoteAddress -notin @('0.0.0.0', '::', '127.0.0.1', '::1') })
            if ($conns.Count -gt 0) { $hits += $conns.Count }
        } catch { }
        Start-Sleep -Milliseconds 50
    }
    New-Object PSObject -Property @{ Samples = $samples; Hits = $hits }
} -ArgumentList $proc.Id, 45, 40

Invoke-Button $btnAnalyze

$statusText = ''
$terminal = 'false'
$deadline = (Get-Date).AddSeconds(180)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 250

    Close-SiblingWindows $proc.Id $mainHandle $dialogLog

    $status = Find-ById $mainWin $statusId
    if ($null -ne $status) { $statusText = $status.Current.Name }
    if ($statusText -match '^Analisi (completata|annullata|non riuscita)\.') { $terminal = 'true'; break }
}

Check ($terminal -eq 'true') ('Lo stato terminale è stato raggiunto: "' + $statusText + '"')

# Eventuale finestra di dialogo mostrata subito dopo lo stato terminale.
for ($i = 0; $i -lt 10; $i++) {
    Start-Sleep -Milliseconds 300
    Close-SiblingWindows $proc.Id $mainHandle $dialogLog
}

$summary = Find-ById $mainWin $summaryId
$summaryText = if ($null -ne $summary) { $summary.Current.Name } else { '' }
Check ($summaryText -like 'File analizzati:*') ('Riepilogo letto: ' + $summaryText)

# La tabella deve dichiarare lo stesso numero di elementi proposti dal riepilogo.
$gridAfter = $null
foreach ($e in (Get-Descendants $mainWin)) {
    if ($e.Current.Name -like '*elementi in elenco') { $gridAfter = $e; break }
}
Check ($null -ne $gridAfter) ('La tabella dichiara il contenuto: ' +
    $(if ($null -ne $gridAfter) { $gridAfter.Current.Name } else { '(assente)' }))

$proposed = -1
if ($summaryText -match 'Proposti:\s*([0-9]+)') { $proposed = [int]$Matches[1] }
$listed = -1
if ($null -ne $gridAfter -and $gridAfter.Current.Name -match '^([0-9]+) elementi in elenco') { $listed = [int]$Matches[1] }
Check ($proposed -ge 0 -and $listed -eq $proposed) `
    ('Gli elementi mostrati coincidono con quelli proposti (' + $listed + ' vs ' + $proposed + ')')

$btnCleanAfter = Find-ByName $mainWin 'Pulisci selezionati'
if ($null -ne $btnCleanAfter) {
    Check ($btnCleanAfter.Current.IsEnabled) 'Dopo l''analisi la pulizia è abilitata (ci sono selezioni)'
}

$reportLabel = Find-ById $mainWin $reportId
$reportText = if ($null -ne $reportLabel) { $reportLabel.Current.Name } else { '' }
Check ($reportText -like 'Report: *') ('Etichetta report: ' + $reportText)

$reportPath = $null
if ($reportText -match '^Report: (.+)$') { $reportPath = $Matches[1].Trim() }
Check ($null -ne $reportPath -and (Test-Path -LiteralPath $reportPath)) 'Il file di report esiste sul disco'

if ($null -ne $reportPath -and (Test-Path -LiteralPath $reportPath)) {
    $reportContent = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8
    Check ($reportContent -like '*report locale*') 'Il report contiene l''intestazione attesa'
    Check ($reportContent -notlike ('*' + $env:USERPROFILE + '*')) 'Il report non contiene il profilo utente'

    $reportCountAfter = @(Get-ChildItem -LiteralPath $reportsDir -Filter 'cleanslate-*.txt' -ErrorAction SilentlyContinue).Count
    Check ($reportCountAfter -eq ($reportCountBefore + 1)) `
        ('Esattamente un nuovo report (prima: ' + $reportCountBefore + ', dopo: ' + $reportCountAfter + ')')
}

Check ($null -ne (Find-ByName $mainWin 'Pulisci selezionati')) 'Il pulsante "Pulisci selezionati" è presente'
Check ($null -ne (Find-ByName $mainWin 'Apri report')) 'Il pulsante "Apri report" è presente'

if ($dialogLog.Count -gt 0) {
    Note ('Finestre di dialogo mostrate durante l''analisi: ' + (($dialogLog | Select-Object -Unique) -join ' | '))
}

Write-Host '4) Chiusura della finestra...'
try { $proc.CloseMainWindow() | Out-Null } catch { }

$exited = $proc.WaitForExit(20000)
Check ($exited) 'Il processo si è chiuso con la finestra'
if (-not $exited) {
    Note 'Il processo non è terminato: viene terminato forzatamente (solo per la verifica).'
    try { $proc.Kill(); $proc.WaitForExit(10000) } catch { }
}
Start-Sleep -Milliseconds 500

$stillRunning = $null -ne (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)
Check (-not $stillRunning) 'Nessun processo CleanSlate residente dopo la chiusura'

$netResult = $null
try {
    Wait-Job -Job $netJob -Timeout 70 | Out-Null
    $netResult = Receive-Job -Job $netJob
} catch { }
try { Remove-Job -Job $netJob -Force } catch { }
if ($null -ne $netResult) {
    $netSamples = $netResult.Samples
    $netHits = $netResult.Hits
}

Write-Host '5) Controlli su registro, servizi e attivita...'
$registryAfter = Get-RegistrySnapshot
$servicesAfter = Get-ServiceNames
$tasksAfter = Get-ScheduledTaskNames

Check ($registryBefore -eq $registryAfter) 'Il registro di sistema è invariato'
Check ($servicesBefore -eq $servicesAfter) 'Elenco dei servizi invariato'
Check ($tasksBefore -eq $tasksAfter) 'Elenco delle attività pianificate invariato'
Check (-not (Test-Path -LiteralPath 'HKCU:\Software\CleanSlate')) 'Nessuna chiave di registro creata da CleanSlate'

$runEntries = @()
try {
    $runKey = Get-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $runEntries = @($runKey.GetValueNames() | Where-Object { $_ -like '*CleanSlate*' })
} catch { }
Check ($runEntries.Count -eq 0) 'Nessuna voce di avvio automatico creata'

Check ($netSamples -ge 40) ('Numero di campionamenti di rete sufficiente (' + $netSamples + ')')
Check ($netHits -eq 0) ('Nessuna connessione TCP in uscita durante l''esecuzione (campionamenti: ' + $netSamples + ')')

# -------------------------------------------------------------------- esito ---

Write-Host ''
foreach ($n in $script:notes) { Write-Host ('NOTA: ' + $n) }

if ($script:failures.Count -eq 0) {
    Write-Host 'VERIFICA: OK (tutti i controlli superati)'
    exit 0
}

Write-Host ('VERIFICA: KO (' + $script:failures.Count + ' controlli falliti)')
foreach ($f in $script:failures) { Write-Host ('  - ' + $f) }
exit 1
