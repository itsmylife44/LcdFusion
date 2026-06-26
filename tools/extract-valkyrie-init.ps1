param(
  [Parameter(Mandatory=$true)][string]$Path,
  [int]$StopAfterFrames = 3   # stop a few frames after streaming begins
)

$bytes = [System.IO.File]::ReadAllBytes($Path)
$len = $bytes.Length
function U16($o){ [BitConverter]::ToUInt16($bytes, $o) }
function U32($o){ [BitConverter]::ToUInt32($bytes, $o) }

$pos = 24
$rec = 0
$frameStarts = 0
$pendingCmd = $null       # last SET_REPORT command hex, awaiting possible GET_REPORT
$events = New-Object System.Collections.Generic.List[string]

while ($pos + 16 -le $len) {
  $inclLen = U32 ($pos + 8)
  $dataStart = $pos + 16
  if ($dataStart + $inclLen -gt $len) { break }
  $h = $dataStart
  $headerLen = U16 $h
  $endpoint  = $bytes[$h + 21]
  $transfer  = $bytes[$h + 22]
  $payStart  = $h + $headerLen
  $payLen    = $inclLen - $headerLen
  if ($payLen -lt 0) { $payLen = 0 }

  if ($transfer -eq 2 -and $payLen -ge 8) {
    $bmRT = $bytes[$payStart]
    $bReq = $bytes[$payStart+1]
    $wVal = U16 ($payStart+2)
    $wIdx = U16 ($payStart+4)
    if ($bmRT -eq 0x00 -and $bReq -eq 0x0b) {
      $events.Add(("SETIF  alt=0x{0:x4} iface=0x{1:x4}" -f $wVal,$wIdx))
    }
    elseif ($bmRT -eq 0x21 -and $bReq -eq 0x09 -and $payLen -ge 16) {
      # SET_REPORT (feature) OUT: 8 cmd bytes follow the 8-byte setup
      $cmd = ($bytes[($payStart+8)..($payStart+15)] | ForEach-Object { $_.ToString('x2') }) -join ' '
      $events.Add("CMD    $cmd")
    }
    elseif ($bmRT -eq 0xa1 -and $bReq -eq 0x01) {
      $events.Add("  GET_REPORT(reply expected)")
    }
  }
  elseif ($transfer -eq 3 -and $endpoint -eq 0x04 -and $payLen -ge 8) {
    if ($bytes[$payStart] -eq 0xff -and $bytes[$payStart+1] -eq 0x00 -and $bytes[$payStart+5] -eq 0x14 -and $bytes[$payStart+7] -eq 0xf0) {
      $frameStarts++
      $events.Add("==== FRAME-START #$frameStarts (rec $rec) ====")
      if ($frameStarts -ge $StopAfterFrames) { break }
    }
  }

  $pos = $dataStart + $inclLen
  $rec++
}

$events | ForEach-Object { Write-Output $_ }
Write-Output ("`n== total records scanned: {0}, frame-starts: {1} ==" -f $rec, $frameStarts)
