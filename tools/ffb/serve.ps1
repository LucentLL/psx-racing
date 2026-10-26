# Serve the wheel force-feedback test page on http://localhost:8765/
#
#   powershell -ExecutionPolicy Bypass -File tools\ffb\serve.ps1
#
# then open http://localhost:8765/ in Chrome or Edge. WebHID only runs on a
# secure origin, and localhost counts as one; a file:// page does not.
# Ctrl+C stops the server.
param([int]$Port = 8765)
$here = $PSScriptRoot
Write-Host "Serving $here"
Write-Host "Open  http://localhost:$Port/webhid-ffb-test.html  in Chrome or Edge.  Ctrl+C stops."
Start-Process "http://localhost:$Port/webhid-ffb-test.html" | Out-Null
# Python's own server: no admin rights, no URL reservation, the right MIME types.
& py -m http.server $Port --bind 127.0.0.1 --directory $here
if ($LASTEXITCODE -ne 0) {
    Write-Host "py failed - trying python"
    & python -m http.server $Port --bind 127.0.0.1 --directory $here
}
