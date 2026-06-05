# WolfNET Radio — Linux Client

Cross-platform port of the WolfNET Radio client using Avalonia UI and PortAudio.
Shares all networking, model, and ViewModel code with the Windows WPF client.

## Requirements

| Dependency | Purpose | Install |
|---|---|---|
| `libportaudio2` | Audio I/O (ALSA/PipeWire/PulseAudio) | `apt install libportaudio2` |
| `input` group | Global PTT on Wayland | `sudo usermod -aG input $USER` |

## Setup (Ubuntu / Debian)

```bash
sudo apt install libportaudio2
sudo usermod -aG input $USER   # global PTT (requires logout/reboot)
# Download WolfNET-Radio-linux-x64.tar.gz from releases
tar xzf WolfNET-Radio-linux-x64.tar.gz
chmod +x WolfNETRadio
./WolfNETRadio
```

## Setup (Fedora)

```bash
sudo dnf install portaudio
sudo usermod -aG input $USER
./WolfNETRadio
```

## Global PTT on Wayland

WolfNET Radio uses `/dev/input/eventX` (libevdev) for global PTT — keys trigger
the radio even when the app window isn't focused. This works on both X11 and
pure Wayland sessions (GNOME, KDE, Sway, Hyprland, etc.).

**Requires the `input` group:**
```bash
sudo usermod -aG input $USER
# Log out and back in for the group to take effect
```

Without `input` group access, PTT still works — but only when the WolfNET Radio
window is focused.

## Building from source

```bash
cd WolfNETRadio.Linux
dotnet restore
dotnet run   # development
dotnet publish -r linux-x64 --self-contained   # production binary
```

## Platform differences from Windows client

| Feature | Windows | Linux |
|---|---|---|
| UI framework | WPF | Avalonia 11 |
| Audio | NAudio (WaveIn/WasapiOut) | PortAudio (ALSA/PipeWire/PulseAudio) |
| Global keyboard/mouse hooks | Win32 WH_KEYBOARD_LL | libevdev /dev/input |
| Joystick/HOTAS | WinMM joyGetPosEx | libevdev (JS events) |
| Wayland support | N/A | ✅ native |
| SRS protocol | ✅ | ✅ identical |
| Radio key auth | ✅ | ✅ identical |
| Channel A/B, VOX, pan | ✅ | ✅ identical |
