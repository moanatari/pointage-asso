// Demande l'arrêt du service Pointage et attend qu'il soit réellement arrêté (30 s au plus).
// Sans cela, Pointage.exe est encore verrouillé et ne peut pas être remplacé pendant une mise à jour.
// Renvoie True si le service est arrêté ou n'existe pas.
// Inclus deux fois par Pointage.iss : {#Prefixe} vaut « » (installateur) puis « un. » (désinstalleur).
function {#Prefixe}ArreterService(): Boolean;
var
  Code, I: Integer;
begin
  // Si sc.exe ne peut même pas être lancé, le code de retour n'a aucun sens : on échoue franchement.
  if not Exec(ExpandConstant('{sys}\sc.exe'), 'stop Pointage', '', SW_HIDE, ewWaitUntilTerminated, Code) then
  begin
    Result := False;
    Exit;
  end;
  Result := True;
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
