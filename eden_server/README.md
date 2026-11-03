# eden_server
Dépôt de serveur Eden au cas où le jeu fermerait

On installe docker en premier lieu

On rajoute les clés SSH pour mettre en place la liaison SSH facilement

Placez-vous dans le dossier parent souhaité puis clonez le dépôt :

```bash
git clone git@github.com:Noah-Lhommedieu/eden_server.git
```


Ensuite va s'enchainer une suite de commande pour :
- Mettre a jour les paquets d'ubuntu
- Installer des utilitaires et l'encodage nécessaire pour Eden
- Ouvrir les ports nécessaires
- Rajouter les droits d'exécutions des différents scripts
- Créer le dossier de montage de la base de donnée et lui donner des permissions de modifications
- Lancer la stack de postgresql
- Et enfin copier et exécuter le dump SQL sur la base de données

```bash
sudo apt update
sudo apt install -y net-tools vim psmisc locales git
sudo locale-gen en_US.UTF-8

sudo ufw allow 7643
sudo ufw allow 6546
sudo ufw allow 5050
sudo ufw allow 10010
sudo ufw allow 2067
sudo ufw allow 5560
sudo ufw allow 7777
sudo ufw allow 40001
sudo ufw allow 8888
sudo ufw allow 6543
sudo ufw allow 6544
sudo ufw allow 6545
sudo ufw allow 6551
sudo ufw allow 10022
sudo ufw allow 5710
sudo ufw allow 7878
sudo ufw allow 22402
sudo ufw allow 22409
sudo ufw allow 5650
sudo ufw allow 7654
sudo ufw allow 5924
sudo ufw allow 6666
sudo ufw allow 28092
sudo ufw allow 5712
sudo ufw allow 10166
sudo ufw allow 15306
sudo ufw allow 6424
sudo ufw allow 8104
sudo ufw allow 10465
sudo ufw allow 20205

sudo chmod +x ./start
sudo chmod +x ./stop
sudo chmod +x ./server_us/TicketServer/TicketServer
sudo chmod +x ./server_us/GatewayServer/GatewayServer
sudo chmod +x ./server_us/LoginServer/LoginServer
sudo chmod +x ./server_us/MissionServer/MissionServer
sudo chmod +x ./server_us/WorldServer/WorldServer
sudo chmod +x ./server_us/ZoneServer/ZoneServer

mkdir -p dbdata
sudo chmod 777 -R dbdata

docker compose up -d

docker cp ./database.out eden_server-postgres-1:/database.out
docker exec -it eden_server-postgres-1 psql -U postgres -d postgres -f database.out
```

Dorénavant une suite d'action manuel vont être faites
Premièrement, on lance Visual studio code (ou nano dans ubuntu) et on met dans GAMEDBIP et ACCOUNTDBIP l'IP localhost si elle n'y est pas

On met l'ip qu'on trouve quand on fait ifconfig ou ip addr (sur Ubuntu) dans le connect.ini et connects.ini du client

Ensuite on va sur navicat et on créer une connexion localhost, on se dirige vers la database "edenfr_db" puis dans la table "worlds" et on change l'ip ext et l'ip int en 127.0.0.1 des worlds aux id 1010 et 1011 (worldserver 101 et zoneserver 101)
Puis dans la database "edenfr_account" et dans la table "world" on met aussi l'ip 127.0.0.1 au channel 1

Dès que c'est fait, on peut lancer le serveur via la commande ci-dessous, le easyfun.bat pour lancer le client du jeu et joueur avec le compte test123 test123

```bash
./start
```

