param(
  [Parameter(Mandatory=$true)][string]$Path,
  [int]$MaxPayloadHex = 48,        # bytes of payload to show in hex
  [int]$SmallMax = 600,            # only dump payloads <= this many bytes (commands, not frames)
  [switch]$Summary,                # print transfer-type/endpoint summary only
  [string]$OnlyEndpoint = $null,   # filter hex like 0x04 / 0x80
  [int]$FromRec = -1,              # show ALL transfers in record range [FromRec..ToRec]
  [int]$ToRec = -1
)

$bytes = [System.IO.File]::ReadAllBytes($Path)
$len = $bytes.Length

function U16($o){ [BitConverter]::ToUInt16($bytes, $o) }
function U32($o){ [BitConverter]::ToUInt32($bytes, $o) }

# Global header = 24 bytes
$pos = 24
$rec = 0
$stats = @{}   # key "transfer|epHex|dir" -> count
$ep0 = $null
if ($OnlyEndpoint) { $ep0 = [Convert]::ToInt32($OnlyEndpoint,16) }

while ($pos + 16 -le $len) {
  $inclLen = U32 ($pos + 8)
  $dataStart = $pos + 16
  if ($dataStart + $inclLen -gt $len) { break }

  # USBPCAP pseudo header
  $h = $dataStart
  $headerLen = U16 $h
  $endpoint  = $bytes[$h + 21]
  $transfer  = $bytes[$h + 22]
  $dataLength= U32 ($h + 23)
  $info      = $bytes[$h + 16]
  # payload begins after the pheader (headerLen)
  $payStart  = $h + $headerLen
  $payLen    = $inclLen - $headerLen
  if ($payLen -lt 0) { $payLen = 0 }

  $dir = if (($endpoint -band 0x80) -ne 0) { "IN " } else { "OUT" }
  $tname = switch ($transfer) { 0 {"ISO"} 1 {"INTR"} 2 {"CTRL"} 3 {"BULK"} default {"T$transfer"} }
  $epHex = "0x{0:x2}" -f $endpoint

  $key = "{0}|{1}|{2}" -f $tname, $epHex, $dir
  if (-not $stats.ContainsKey($key)) { $stats[$key] = [pscustomobject]@{ Count=0; Bytes=0; MinLen=[int]::MaxValue; MaxLen=0 } }
  $stats[$key].Count++
  $stats[$key].Bytes += $dataLength
  if ($dataLength -lt $stats[$key].MinLen) { $stats[$key].MinLen = $dataLength }
  if ($dataLength -gt $stats[$key].MaxLen) { $stats[$key].MaxLen = $dataLength }

  $inRange = ($FromRec -ge 0 -and $rec -ge $FromRec -and $rec -le $ToRec)
  if (-not $Summary) {
    $showThis = ($payLen -gt 0 -and $payLen -le $SmallMax)
    if ($FromRec -ge 0) { $showThis = $inRange }   # range mode: show all transfers (incl big bulk, truncated)
    if ($ep0 -ne $null -and $endpoint -ne $ep0) { $showThis = $false }
    if ($showThis) {
      $take = [Math]::Min($MaxPayloadHex, $payLen)
      $hex = if ($take -gt 0) { ($bytes[$payStart..($payStart+$take-1)] | ForEach-Object { $_.ToString('x2') }) -join ' ' } else { '' }
      # For control, also decode setup (first 8 bytes of payload are the setup packet on CTRL stage)
      $extra = ""
      if ($tname -eq "CTRL" -and $payLen -ge 8) {
        $bmRT = $bytes[$payStart]
        $bReq = $bytes[$payStart+1]
        $wVal = U16 ($payStart+2)
        $wIdx = U16 ($payStart+4)
        $wLen = U16 ($payStart+6)
        $extra = ("  setup[bmRT=0x{0:x2} bReq=0x{1:x2} wVal=0x{2:x4} wIdx=0x{3:x4} wLen={4}]" -f $bmRT,$bReq,$wVal,$wIdx,$wLen)
      }
      Write-Output ("#{0,-6} {1} {2} {3} len={4,-6} pay={5,-5}{6}  {7}" -f $rec,$tname,$epHex,$dir,$dataLength,$payLen,$extra,$hex)
    }
  }

  $pos = $dataStart + $inclLen
  $rec++
  if ($FromRec -ge 0 -and $rec -gt $ToRec) { Write-Output ("== stopped at rec {0} ==" -f $rec); break }
}

Write-Output ""
Write-Output ("== Records: {0} ==" -f $rec)
if ($Summary -or $true) {
  $stats.GetEnumerator() | Sort-Object Name | ForEach-Object {
    Write-Output ("{0,-16} count={1,-6} totBytes={2,-12} min={3} max={4}" -f $_.Key, $_.Value.Count, $_.Value.Bytes, $_.Value.MinLen, $_.Value.MaxLen)
  }
}
