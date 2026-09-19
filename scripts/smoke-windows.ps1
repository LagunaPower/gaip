param([string]$Binary = "$PSScriptRoot/../artifacts/Release/win-x64/GAIP.exe")
$ErrorActionPreference = 'Stop'
$binaryPath = (Resolve-Path -LiteralPath $Binary).Path
$runRoot = Join-Path $PSScriptRoot "../artifacts/smoke/windows-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$runRoot = (Resolve-Path -LiteralPath $runRoot).Path
# Run only the executable, with no adjacent runtime or Avalonia DLLs.
$standalone = Join-Path $runRoot 'GAIP.exe'
Copy-Item -LiteralPath $binaryPath -Destination $standalone
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class GaipNativeSmoke {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr value);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr value);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder value, int max);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    public static IntPtr FindWindow(int processId) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, unused) => {
            uint owner; GetWindowThreadProcessId(hwnd, out owner);
            var title = new StringBuilder(512); GetWindowText(hwnd, title, title.Capacity);
            if (owner == processId && title.ToString().StartsWith("G@IP")) { found = hwnd; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@
$process = Start-Process -FilePath $standalone -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runRoot 'stdout.log') -RedirectStandardError (Join-Path $runRoot 'stderr.log')
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    $window = [IntPtr]::Zero
    while (!$process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        $window = [GaipNativeSmoke]::FindWindow($process.Id)
        if ($window -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 200
    }
    if ($window -eq [IntPtr]::Zero) { throw "La fenêtre native G@IP ne s’est pas ouverte." }
    Start-Sleep -Seconds 2
    if ($process.HasExited) { throw "G@IP s’est arrêté juste après ouverture." }
    $title = [Text.StringBuilder]::new(512)
    [void][GaipNativeSmoke]::GetWindowText($window, $title, $title.Capacity)
    [void][GaipNativeSmoke]::PostMessage($window, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
    if (!$process.WaitForExit(10000)) { throw 'La fermeture normale de G@IP a expiré.' }
    if ($process.ExitCode -ne 0) { throw "Code de sortie inattendu : $($process.ExitCode)" }
    $result = [ordered]@{ platform='Windows'; binary=$binaryPath; isolatedExecutable=$standalone; windowTitle=$title.ToString(); exitCode=$process.ExitCode; stderrBytes=(Get-Item -LiteralPath (Join-Path $runRoot 'stderr.log')).Length }
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'result.json') -Encoding utf8
    $result | ConvertTo-Json
} finally {
    # Only the process started by this smoke test may be terminated on failure.
    if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
}
