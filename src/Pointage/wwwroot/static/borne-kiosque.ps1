<#
.SYNOPSIS
    Configure un PC pour afficher la borne de pointage en plein écran dès l'ouverture de session.

.DESCRIPTION
    Crée un raccourci dans le dossier « Démarrage » de l'utilisateur courant. Au prochain démarrage
    de Windows, Microsoft Edge s'ouvre en plein écran (mode kiosque) sur la page de la borne.

    Le mode « navigation privée » (InPrivate) n'est volontairement PAS utilisé : il effacerait à chaque
    fermeture le cookie qui lie cette borne au poste du responsable, et il faudrait la relier à chaque fois.

.PARAMETER Url
    Adresse de la borne, par exemple http://PC-ACCUEIL:5080/borne (remplacer PC-ACCUEIL par le nom
    ou l'adresse IP du poste où Pointage est installé).

.EXAMPLE
    .\borne-kiosque.ps1 -Url http://PC-ACCUEIL:5080/borne

.NOTES
    À lancer une seule fois, depuis la session Windows qui servira de borne.
    Pour quitter le mode kiosque : Alt+F4. Pour désactiver : supprimer le raccourci « Borne Pointage »
    du dossier Démarrage (Win+R, taper shell:startup).
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Url
)

$ErrorActionPreference = 'Stop'

# On vérifie l'adresse tout de suite : une faute de frappe ici donnerait une borne vide au prochain démarrage.
$uri = $null
if (-not [Uri]::TryCreate($Url, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -notin 'http', 'https') {
    throw "Adresse invalide : '$Url'. Exemple attendu : http://PC-ACCUEIL:5080/borne"
}

# Emplacement d'Edge : d'abord le registre, sinon le dossier habituel.
$edge = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe' -ErrorAction SilentlyContinue).'(default)'
if (-not $edge -or -not (Test-Path $edge)) {
    $edge = Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'
}
if (-not (Test-Path $edge)) {
    throw "Microsoft Edge est introuvable sur ce PC."
}

$raccourci = Join-Path ([Environment]::GetFolderPath('Startup')) 'Borne Pointage.lnk'
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($raccourci)
$lnk.TargetPath = $edge
$lnk.Arguments = "--kiosk `"$($uri.AbsoluteUri)`" --edge-kiosk-type=fullscreen --no-first-run"
$lnk.Save()

Write-Host "Raccourci créé : $raccourci"
Write-Host "La borne s'ouvrira en plein écran à la prochaine ouverture de session."
