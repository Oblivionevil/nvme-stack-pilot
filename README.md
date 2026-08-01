# NVMe Stack Pilot

NVMe Stack Pilot ist eine kleine Windows-Desktopanwendung, mit der sich der experimentelle
native NVMe-Treiberpfad von Windows 11 prüfen, aktivieren und wieder auf den klassischen
`stornvme.sys`-/`disk.sys`-Stack zurückstellen lässt.

Die Anwendung zeigt vor jedem Eingriff die exakten Registry-Operationen, prüft typische
Risikofaktoren und legt die benötigte SafeBoot-Absicherung an. Unbekannte Windows-Feature-
Overrides werden ausschließlich angezeigt und niemals verändert.

> [!WARNING]
> Das Umschalten des Storage-Treiberpfads ist ein systemkritischer Eingriff. Sichere wichtige
> Daten und den BitLocker-Wiederherstellungsschlüssel, bevor du Änderungen vornimmst. Die
> Nutzung erfolgt auf eigenes Risiko.

Microsofts öffentliche Ankündigung zur allgemeinen Verfügbarkeit des nativen NVMe-Stacks
bezieht sich auf [Windows Server 2025](https://techcommunity.microsoft.com/blog/windowsservernewsandbestpractices/announcing-native-nvme-in-windows-server-2025-ushering-in-a-new-era-of-storage-p/4477353/).
Die von diesem Projekt angebotene Client-Aktivierung ist deshalb ausdrücklich experimentell.

## Funktionen

- erkennt den laufenden klassischen oder nativen NVMe-Stack
- zeigt NVMe-Controller, Datenträger, Treiberdienste und Boot-Datenträger
- prüft BitLocker, Intel RST/VMD, VeraCrypt und Storage Spaces
- validiert Registry-Werte einschließlich ihres exakten Datentyps
- erstellt und verifiziert SafeBoot-Einträge für `nvmedisk`
- führt mehrstufige Registry-Änderungen mit Verifikation und Rollback aus
- unterscheidet ausstehende, nicht zugeordnete und von Windows ignorierte Änderungen
- entfernt ausschließlich bekannte alte NVMe-Override-IDs

## Voraussetzungen

- Windows 11 x64, vorgesehen für 24H2
- .NET Framework 4.8.1
- Administratorrechte

## Bauen und testen

```powershell
dotnet build NvmeDriverSwitch.sln --configuration Release
dotnet test NvmeDriverSwitch.sln --configuration Release
```

Das Programm wird unter
`src\NvmeDriverSwitch\bin\x64\Release\net481\NvmeStackPilot.exe` erzeugt.

## Sicherheitshinweise

- Die Anwendung startet immer erhöht, da sie unter `HKLM\SYSTEM` schreibt.
- Vorhandene unbekannte Feature-Overrides bleiben schreibgeschützt.
- SafeBoot wird vor dem Treiber-Override geschrieben und verifiziert.
- Bei Teilfehlern versucht die Anwendung, den vorherigen Zustand wiederherzustellen.
- Ein fehlgeschlagener Preflight-Check wird als unbekannter Zustand angezeigt, nicht als Entwarnung.

## Status

Experimentelles Werkzeug für erfahrene Windows-Anwender. Vor einem produktiven Einsatz sollte
die Funktion auf der konkreten Windows-Build- und Hardwarekombination getestet werden.
