---
name: godot-csharp
description: Écrire ou relire du C# pour le client Eden sous Godot 4.8 .NET 10 (scripts, nœuds, code pur testable, threads, allocations). À utiliser pour toute modification de godot_project/scripts ou src/.
---

# C# Godot 4.8 / .NET 10

- **Pur ou adaptateur.** Le code qui n'a pas besoin de `Godot.*` reste
  pur et testé dans `NetTests` ou `Eden.Core.Tests`. Pour ajouter un
  fichier pur à NetTests, l'inclure dans `NetTests.csproj` (`Compile
  Include`). Les morceaux Godot vont dans un `partial` sous `#if GODOT`
  (ex. `EdenVfs.Godot.cs`).
- **Thread principal** pour toute mutation de `Node`. Les handlers réseau
  parsent, puis mettent en file vers la boucle principale. Ne pas faire de
  `Node.Duplicate` en parallèle (essayé dans le portage, non sûr).
- **Boucles chaudes** (`_Process`, HUD, entités) : pas d'allocation par
  frame, pas de LINQ, pas de boxing, pas de `GetNode` répété (mettre la
  référence en cache), peu de conversions Variant ou `StringName`.
- **Statique** : un initialiseur de propriété statique s'exécute avant le
  constructeur statique. Une dépendance d'enregistrement
  (`Encoding.RegisterProvider`) s'initialise donc dans le constructeur.
- **Nullable activé.** Un warning nouveau se corrige ou se justifie. La
  cible est 0 warning, ce qui est l'état au 2026-11-02.
- Build : `tools/build.sh`. Tests : `tools/test.sh --no-server`.
- Doc : `tutorials/scripting/c_sharp/` (basics, differences, signals,
  collections, diagnostics), en version 4.8. Vérifier sur le binaire
  toute API vue dans `/latest/`.
