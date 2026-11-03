# Eden.Admin — créer des comptes et des personnages sans le client

Le client Reforged a **retiré l'interface de création de personnage**.
`Eden.Admin` est le CLI qui s'en charge : il utilise **exactement les
mêmes requêtes** que la création in-game (mêmes repositories, même
transaction, mêmes tables) — un personnage créé par l'outil est
indiscernable d'un personnage créé via l'ancien client.

## Lancement

```bash
cd server
export DB_CONN="Host=localhost;Port=5432;Database=fndb1;Username=postgres;Password=…"
export ACCOUNT_DB_CONN="Host=localhost;Port=5432;Database=fnaccount;Username=postgres;Password=…"
ADMIN="src/Eden.Admin/bin/Release/net10.0/Eden.Admin"   # dotnet build -c Release au préalable
```

## Créer un compte

```bash
$ADMIN account create --user moncompte --pass mdp123
$ADMIN account list
```

## Créer un personnage

```bash
# vérifier que le nom est libre (codes natifs : 0 ok, 3 pris, 8/9 longueur, 10 illégal)
$ADMIN char name-check --name MonPerso

# création — race et classe obligatoires, le reste cosmétique
$ADMIN char create --account moncompte --name MonPerso --race 1 --class 1 \
      --gender 0 --hair 5 --face 3 --hair-color 7 --skin-color 2 --eyes 9 --eyes2 4

$ADMIN char list --account moncompte
```

**Races natives** : 1=Humain, 2=Zumi, 3=Anuran (grenouille), 4=Ursun
(ours), 5=Torturran (tortue).
**Genre** : 0=masculin, 1=féminin.
**Classe** : la classe de départ choisie à la création (les 6 classes de
base + classes raciales sont débloquées automatiquement par le serveur
comme sur le natif).

## Limites natives respectées

- Nom : 2-14 caractères, lettres et chiffres uniquement.
- **3 personnages maximum par compte** (limite du serveur officiel).
- Le blob d'apparence 32 octets est re-encodé avec les mêmes bitfields
  que le client produisait (le personnage s'affiche correctement à la
  sélection et en jeu).

## Dépannage

- `compte 'x' introuvable` → crée le compte d'abord (`account create`).
- `nom 'x' refusé (code=3)` → nom déjà pris, choisis-en un autre.
- `déjà 3 personnages` → supprime un perso en jeu ou utilise un autre compte.
