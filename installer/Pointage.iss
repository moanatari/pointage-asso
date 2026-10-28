; Installateur de Pointage (Inno Setup 6).
; Compilation : iscc /DMyAppVersion=1.2.3 /DSourceDir=C:\chemin\vers\publish installer\Pointage.iss
; Le résultat est installer\Output\Pointage-Setup.exe. Ce nom exact est attendu par la mise à jour automatique
; (src/Pointage/Updates/UpdateService.cs) : ne pas le changer.

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
; Dossier produit par « dotnet publish ». Un chemin relatif se lit à partir de ce fichier .iss.
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

[Setup]
; Identifiant fixe : il permet à une nouvelle version de remplacer la précédente au lieu de s'installer à côté.
AppId={{6B0C1E5A-3F7D-4C2B-9A8E-5D1F0B7C4E21}
AppName=Pointage
AppVersion={#MyAppVersion}
AppPublisher=Pointage
DefaultDirName={autopf}\Pointage
DisableProgramGroupPage=yes
OutputBaseFilename=Pointage-Setup
; Créer un service Windows et une règle de pare-feu demande les droits administrateur.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
; Le service est arrêté « à la main » dans PrepareToInstall : on ne veut pas de fenêtre de fermeture d'applications.
CloseApplications=no

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Files]
; Les symboles de débogage (.pdb) ne servent à rien sur le poste de l'association.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: recursesubdirs ignoreversion

[Dirs]
; Dossier de la base de données (membres, présences). uninsneveruninstall : désinstaller ne supprime jamais les données.
Name: "{commonappdata}\Pointage"; Flags: uninsneveruninstall

[Run]
; Verrouillage du dossier de données, à chaque installation et mise à jour : héritage supprimé, accès complet réservé
; à SYSTEM (le service) et aux Administrateurs. Ainsi les autres comptes du poste ne peuvent ni lire la base des membres,
; ni remplacer le dossier des mises à jour par un lien (attaque par « jonction »). On utilise les identifiants SID
; (S-1-5-18 = SYSTEM, S-1-5-32-544 = Administrateurs) pour que cela marche aussi sur un Windows en français.
Filename: "{sys}\icacls.exe"; Parameters: """{commonappdata}\Pointage"" /inheritance:r /grant:r *S-1-5-18:(OI)(CI)F *S-1-5-32-544:(OI)(CI)F"; Flags: runhidden; StatusMsg: "Protection du dossier de données..."

; Les commandes sc.exe et netsh.exe ci-dessous peuvent échouer sans gravité (service ou règle déjà présents lors d'une
; mise à jour) : Inno Setup ne s'arrête pas sur un code de retour non nul d'une ligne [Run].
; « create » échoue si le service existe déjà ; « config » remet alors le bon chemin et le démarrage automatique.
Filename: "{sys}\sc.exe"; Parameters: "create Pointage binPath= ""\""{app}\Pointage.exe\"""" start= auto"; Flags: runhidden; StatusMsg: "Création du service Pointage..."
Filename: "{sys}\sc.exe"; Parameters: "config Pointage binPath= ""\""{app}\Pointage.exe\"""" start= auto"; Flags: runhidden
; Redémarrage automatique du service s'il plante (au bout de 60 s ; compteur d'échecs remis à zéro après 24 h).
Filename: "{sys}\sc.exe"; Parameters: "failure Pointage reset= 86400 actions= restart/60000"; Flags: runhidden

; Règle de pare-feu : la borne (une tablette ou un autre PC) doit joindre le port 5080 sur le réseau local.
; Pas de profil « public » (Wi-Fi inconnu) pour ne rien exposer par erreur. On supprime d'abord l'ancienne règle
; pour ne jamais en cumuler plusieurs.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Pointage"""; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""Pointage"" dir=in action=allow protocol=TCP localport=5080 profile=private,domain"; Flags: runhidden; StatusMsg: "Ouverture du port 5080..."

Filename: "{sys}\sc.exe"; Parameters: "start Pointage"; Flags: runhidden; StatusMsg: "Démarrage du service Pointage..."
; Proposé seulement en installation normale (pas en mise à jour silencieuse).
Filename: "http://localhost:5080/"; Description: "Ouvrir Pointage"; Flags: shellexec postinstall skipifsilent

[UninstallRun]
; L'arrêt réel (avec attente) est fait dans InitializeUninstall ; ici on le redemande par sécurité, puis on supprime.
; La base de données dans C:\ProgramData\Pointage est volontairement conservée.
Filename: "{sys}\sc.exe"; Parameters: "stop Pointage"; Flags: runhidden; RunOnceId: "StopService"
Filename: "{sys}\sc.exe"; Parameters: "delete Pointage"; Flags: runhidden; RunOnceId: "DeleteService"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Pointage"""; Flags: runhidden; RunOnceId: "DeleteFirewallRule"

[Code]
// Demande l'arrêt du service Pointage et attend qu'il soit réellement arrêté (30 s au plus).
// Sans cela, Pointage.exe est encore verrouillé et ne peut pas être remplacé pendant une mise à jour.
// Renvoie True si le service est arrêté ou n'existe pas.
function ArreterService(): Boolean;
var
  Code, I: Integer;
begin
  Result := True;
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop Pointage', '', SW_HIDE, ewWaitUntilTerminated, Code);
  // 1060 = le service n'existe pas (première installation) ; 1062 = il n'est pas démarré : rien à attendre.
  if (Code = 1060) or (Code = 1062) then Exit;
  Result := False;
  for I := 1 to 30 do
  begin
    // « find » renvoie 0 quand « sc query » affiche STOPPED.
    if Exec(ExpandConstant('{cmd}'), '/C sc query Pointage | find "STOPPED" > nul', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0) then
    begin
      Result := True;
      Exit;
    end;
    Sleep(1000);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not ArreterService() then
    Result := 'Le service Pointage ne s''arrête pas. Arrêtez-le depuis la console Services de Windows, puis relancez l''installation.';
end;

// Même chose à la désinstallation (les fonctions du désinstalleur portent le préfixe « un. »).
function un.ArreterService(): Boolean;
var
  Code, I: Integer;
begin
  Result := True;
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop Pointage', '', SW_HIDE, ewWaitUntilTerminated, Code);
  if (Code = 1060) or (Code = 1062) then Exit;
  Result := False;
  for I := 1 to 30 do
  begin
    if Exec(ExpandConstant('{cmd}'), '/C sc query Pointage | find "STOPPED" > nul', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0) then
    begin
      Result := True;
      Exit;
    end;
    Sleep(1000);
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  un.ArreterService();
end;
