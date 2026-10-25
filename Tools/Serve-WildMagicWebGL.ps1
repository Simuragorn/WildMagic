param(
    [string]$Root = (Join-Path $PSScriptRoot 'WebGL'),
    [int]$Port = 8080
)

$ErrorActionPreference = 'Stop'
$resolvedRoot = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Root).Path)
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Any, $Port)
$listener.Start()

$radminAddress = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.InterfaceAlias -like '*Radmin*' } |
    Select-Object -First 1 -ExpandProperty IPAddress
$displayAddress = if ($radminAddress) { $radminAddress } else { 'RADMIN-HOST-IP' }

Write-Host ''
Write-Host 'WildMagic WebGL server is running.' -ForegroundColor Green
Write-Host "Send this URL to Player 2: http://${displayAddress}:$Port" -ForegroundColor Cyan
Write-Host 'Keep this window open while playing. Press Ctrl+C to stop.' -ForegroundColor DarkGray
Write-Host ''

function Get-ContentType([string]$path) {
    switch ([IO.Path]::GetExtension($path).ToLowerInvariant()) {
        '.html' { 'text/html; charset=utf-8' }
        '.js'   { 'text/javascript; charset=utf-8' }
        '.wasm' { 'application/wasm' }
        '.json' { 'application/json; charset=utf-8' }
        '.css'  { 'text/css; charset=utf-8' }
        '.png'  { 'image/png' }
        '.ico'  { 'image/x-icon' }
        default { 'application/octet-stream' }
    }
}

try {
    while ($true) {
        $client = $listener.AcceptTcpClient()
        try {
            $client.ReceiveTimeout = 5000
            $stream = $client.GetStream()
            $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::ASCII, $false, 1024, $true)
            $requestLine = $reader.ReadLine()
            if ([string]::IsNullOrWhiteSpace($requestLine)) { continue }
            while ($true) {
                $header = $reader.ReadLine()
                if ([string]::IsNullOrEmpty($header)) { break }
            }

            $parts = $requestLine.Split(' ')
            $method = $parts[0]
            $urlPath = if ($parts.Length -gt 1) { $parts[1].Split('?')[0] } else { '/' }
            $relative = [Uri]::UnescapeDataString($urlPath.TrimStart('/')).Replace('/', [IO.Path]::DirectorySeparatorChar)
            if ([string]::IsNullOrWhiteSpace($relative)) { $relative = 'index.html' }
            $filePath = [IO.Path]::GetFullPath((Join-Path $resolvedRoot $relative))

            $status = '200 OK'
            $body = [byte[]]::new(0)
            if (!$filePath.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase) -or
                !(Test-Path -LiteralPath $filePath -PathType Leaf)) {
                $status = '404 Not Found'
                $body = [Text.Encoding]::UTF8.GetBytes('Not found')
                $contentType = 'text/plain; charset=utf-8'
            } else {
                $body = [IO.File]::ReadAllBytes($filePath)
                $contentType = Get-ContentType $filePath
            }

            $headers = "HTTP/1.1 $status`r`nContent-Type: $contentType`r`nContent-Length: $($body.Length)`r`nCache-Control: no-cache`r`nConnection: close`r`n`r`n"
            $headerBytes = [Text.Encoding]::ASCII.GetBytes($headers)
            $stream.Write($headerBytes, 0, $headerBytes.Length)
            if ($method -ne 'HEAD' -and $body.Length -gt 0) {
                $stream.Write($body, 0, $body.Length)
            }
            $stream.Flush()
        } catch {
            Write-Warning $_.Exception.Message
        } finally {
            $client.Dispose()
        }
    }
} finally {
    $listener.Stop()
}
