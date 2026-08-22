# Security

UE4SS Installer is a desktop file helper. It lists local Steam Unreal games and, only when you choose Install, downloads a zip from GitHub and extracts it into the game folder you picked. It does not inject into a running process, change Windows security settings, or send telemetry.

## Report a problem

- Vulnerability in this installer: use [GitHub security advisories](https://github.com/mattdavida/ue4ss-Installer/security/advisories/new) if you can, or [open an issue](https://github.com/mattdavida/ue4ss-Installer/issues).
- SmartScreen or antivirus false positive on `UE4SSInstaller.exe`: open an issue and include the scanner name and the file SHA-256.
- Antivirus flag on `dwmapi.dll` next to a game: that file comes from UE4SS, not from this installer. See [UE4SS-RE/RE-UE4SS](https://github.com/UE4SS-RE/RE-UE4SS).

Do not attach tokens or private game files to a public issue.
