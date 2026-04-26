param(
  [ValidateSet("wa-1", "wa-2", "wa-3")]
  [string]$Channel = "wa-2",
  [ValidateSet("Preview", "FocusChannel", "OpenChatRow")]
  [string]$Action = "Preview",
  [double]$RowRatio = 0.28,
  [switch]$Execute
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms

$mouseApi = @"
using System;
using System.Runtime.InteropServices;
public class VisualMouse {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
"@
Add-Type -TypeDefinition $mouseApi -ErrorAction SilentlyContinue

function Get-ChannelBounds {
  param([string]$ChannelId)

  $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $index = switch ($ChannelId) {
    "wa-1" { 0 }
    "wa-2" { 1 }
    "wa-3" { 2 }
  }
  $width = [Math]::Floor($screen.Width / 3)
  $left = $screen.Left + ($width * $index)
  $right = if ($index -eq 2) { $screen.Right } else { $left + $width }

  return [pscustomobject]@{
    Channel = $ChannelId
    Left = $left
    Top = $screen.Top
    Right = $right
    Bottom = $screen.Bottom
    Width = $right - $left
    Height = $screen.Height
  }
}

function Invoke-Click {
  param([int]$X, [int]$Y)
  [void][VisualMouse]::SetCursorPos($X, $Y)
  Start-Sleep -Milliseconds 80
  [VisualMouse]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
  Start-Sleep -Milliseconds 80
  [VisualMouse]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
}

$bounds = Get-ChannelBounds $Channel
$points = [pscustomobject]@{
  Channel = $Channel
  Action = $Action
  Execute = [bool]$Execute
  ChannelBounds = $bounds
  FocusPoint = [pscustomobject]@{
    X = [Math]::Floor($bounds.Left + ($bounds.Width * 0.55))
    Y = [Math]::Floor($bounds.Top + ($bounds.Height * 0.5))
  }
  ChatListPoint = [pscustomobject]@{
    X = [Math]::Floor($bounds.Left + ($bounds.Width * 0.22))
    Y = [Math]::Floor($bounds.Top + ($bounds.Height * $RowRatio))
  }
}

if ($Action -eq "FocusChannel" -and $Execute) {
  Invoke-Click $points.FocusPoint.X $points.FocusPoint.Y
}

if ($Action -eq "OpenChatRow" -and $Execute) {
  Invoke-Click $points.ChatListPoint.X $points.ChatListPoint.Y
}

$points | ConvertTo-Json -Depth 6
