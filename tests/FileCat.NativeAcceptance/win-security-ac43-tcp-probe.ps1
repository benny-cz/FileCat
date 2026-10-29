param([string]$HostAddress = '192.168.58.129', [int]$Port = 445)
$ErrorActionPreference = 'Stop'
$socket = [Net.Sockets.TcpClient]::new()
try {
    $task = $socket.ConnectAsync($HostAddress, $Port)
    if (-not $task.Wait(3000) -or -not $socket.Connected) { throw 'The private endpoint did not accept the TCP probe.' }
    [IO.File]::WriteAllText((Join-Path $env:TEMP 'filecat-security-ac43-tcp-probe.txt'),
        "Target=${HostAddress}:$Port`nConnected=True`nSMBBytesSent=0")
}
finally { $socket.Dispose() }
