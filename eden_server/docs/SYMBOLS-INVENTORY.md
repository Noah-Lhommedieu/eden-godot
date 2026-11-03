# Inventaire des Symboles lapis:: par Catégorie

> **Extraction depuis:** ZoneServer (5,760 classes), WorldServer (905 classes), MissionServer (2,458 classes)  
> **Total:** 9,123 classes  
> **Date:** 15 Octobre 2026

---

## 1. Système de Classes

### Classes principales
- `CClassDBO` - Base de données des classes
- `CCharacter::ChangeClassAA` - Changement de classe avec AA
- `CCharacter::GainClassPoint` - Gain de points de classe
- `CCharacter::SetClassLevel` - Définition niveau de classe
- `CCharacter::GetClassGroup` - Groupe de classe
- `CCharacter::GetClassExpRate` - Taux d'XP de classe
- `CCharacter::GetClassExpDice` - Dice d'XP de classe
- `CCharacter::SetClassExp` - Définition XP de classe

### Factory et Management
- `CPlayerFactory::AddClass` - Ajout d'une classe
- `CPlayerFactory::ChangeClass` - Changement de classe
- `CPlayerFactory::RemoveClass` - Suppression d'une classe
- `CPlayerFactory::SwitchClassEquip` - Switch équipement de classe
- `CPlayerFactory::CheckWeaponBeforeChangeClass` - Vérification arme avant changement
- `CPlayerFactory::UpdateClassSpellCard` - Mise à jour spells de classe

### Spells de Classe
- `CSpellMasterFactory::GetClassSpell` - Récupération spell de classe
- `CSpellMasterFactory::RemoveClassSpell` - Suppression spell de classe
- `CSpellMasterTemplate::CheckClass` - Vérification classe
- `CClassDBO::InitClassSpellSets` - Initialisation sets de spells de classe
- `CClassDBO::InitAAEffectSets` - Initialisation sets d'effets AA

### Network
- `CNE_CZ_ZoneServerChangeClass` - Changement de classe (réseau)
- `CNE_CZ_ZoneServerQueryClassList` - Query liste de classes
- `CNE_CZ_ZoneServerPlayerLicense::SetCurrentClassLicense` - License classe actuelle
- `CNE_CZ_ZoneServerPlayerLicense::SetOtherClassLicense` - License autre classe

### Effects
- `CEC_ClassExpRate` - Effet taux d'XP de classe
- `CLevelDBO::LoadClassLevelExp` - Chargement XP niveau de classe
- `CLevelDBO::LoadClassWakenLevelExp` - Chargement XP classe éveillée

---

## 2. Système d'Inventaire

### Containers
- `CInventory` - Inventaire principal
- `CBagContainer` - Conteneur de sac
- `CBagContainer::CBagContainer` - Constructeur
- `CBagContainer::GetItemNumber` - Nombre d'items
- `CBankShop` - Boutique de banque
- `CBankShop::OpenShop` - Ouverture banque

### Item Management
- `CCharacter::RegAllItems` - Enregistrement tous items
- `CCharacter::UnRegAllItems` - Désenregistrement tous items
- `CCharacter::ClearAllItems` - Suppression tous items
- `CCharacter::AddCoolDownItem` - Ajout item cooldown
- `CCharacter::RemoveCoolDownItem` - Suppression item cooldown
- `CCharacter::ClearCoolDownItem` - Clear cooldown item
- `CCharacter::GetWeaponItem` - Récupération arme
- `CCharacter::GetWeaponItemForAction` - Arme pour action
- `CCharacter::FindEquipTrainItemLocation` - Localisation item équipement training
- `CCharacter::FindEquipTrainCloneItem` - Item clone équipement training
- `CCharacter::SetEquipTrainItemID` - Set ID item training
- `CCharacter::InformItemSwitch` - Notification switch item
- `CCharacter::UseDestinyRoadItem` - Utilisation item Destiny Road

### Item Operations
- `CAddItemControl` - Contrôle ajout item
- `CAddItemControl::OnInventoryFull` - Quand inventaire plein
- `CAddItemControl::OnOK` - Confirmation
- `CCloneItem` - Item cloné
- `CCloneItem::GetMaxDurability` - Durabilité max
- `CCloneItem::GetRepairPrice` - Prix réparation
- `CCloneItem::GetSysPrice` - Prix système
- `CCloneItem::InitAddAttr` - Initialisation attributs ajoutés
- `CCloneItem::PreAllocateID` - Pré-allocation ID
- `CCloneItem::SetAllocateIDRange` - Set range ID
- `CCloneItem::DeallocateID` - Désallocation ID
- `CCloneItem::Destroy` - Destruction

### Operation Handlers
- `CCombineOp::GetItemData` - Get données item (combine)
- `CCombineOp::SetItemData` - Set données item (combine)
- `CCompoundOp::GetItemData` - Get données item (compound)
- `CCompoundOp::SetItemData` - Set données item (compound)
- `CCubeOp::GetItemData` - Get données item (cube)
- `CCubeOp::SetItemData` - Set données item (cube)
- `CChangeItemMaker` - Maker changement item
- `CChangeItemMaker::CheckExecute` - Vérification exécution
- `CChangeItemMaker::Execute` - Exécution
- `CDrillItem` - Perçage item
- `CDrillItem::CheckExecute` - Vérification exécution
- `CDrillItem::Execute` - Exécution

### Network
- `CNE_CZ_ZoneServerAskVendorContainer` - Ask container vendeur
- `CNE_CZ_ZoneServerAskVendor` - Ask vendeur
- `CNE_CZ_ZoneServerLeaveShop` - Leave shop

---

## 3. Système d'Enchantement (250+ fonctions)

### Enchant Manager
- `CEnchantMgr` - Manager d'enchantement
- `CEnchantMgr::~CEnchantMgr` - Destructeur
- `CEnchantMgr::GetEnchantsForTeam` - Enchants pour team

### Character Enchants
- `CCharacter::BackupAllEnchants` - Backup tous enchants
- `CCharacter::RecoverAllEnchants` - Récupération tous enchants
- `CCharacter::RecoverAllElfEquipEnchants` - Récupération enchants équipement elf
- `CCharacter::RecoverAppellationEnchants` - Récupération enchants appellation
- `CCharacter::ClearAllEnchantSaves` - Clear sauvegardes enchants
- `CCharacter::ClearOnceEnchant` - Clear enchant unique
- `CCharacter::RemoveAllEquipEnchants` - Suppression tous enchants équipement
- `CCharacter::RemoveAllNoTransNodeEnchants` - Suppression enchants non-transférables
- `CCharacter::RemoveEnchant` - Suppression enchant
- `CCharacter::InvokeAAEnchants` - Invocation enchants AA
- `CCharacter::CheckLastBuildingEnchant` - Vérification dernier enchant building

### Enchant Effects (CEC_Enchant*)
- `CEC_EnchantCheck` - Vérification enchant
- `CEC_EnchantCheckEx` - Vérification enchant étendue
- `CEC_EnchantCheck::CanExecute` - Peut exécuter
- `CEC_EnchantCheck::Execute` - Exécution
- `CEC_EnchantCheck::Init` - Initialisation
- `CEC_EnchantCheckEx::Init` - Initialisation étendue
- `CEC_EnchantCheckByType` - Vérification par type
- `CEC_EnchantCheckByType::CanExecute` - Peut exécuter
- `CEC_EnchantCheckByType::Execute` - Exécution
- `CEC_EnchantCheckByType::Init` - Initialisation
- `CEC_EnchantCheckByType::RollBack` - Rollback
- `CEC_EnchantCheckBuffStack` - Vérification stack buff
- `CEC_EnchantCheckBuffStack::CanExecute` - Peut exécuter
- `CEC_EnchantCheckBuffStack::Execute` - Exécution
- `CEC_EnchantCheckBuffStack::Init` - Initialisation
- `CEC_EnchantIncrease` - Augmentation enchant
- `CEC_EnchantIncrease::CanExecute` - Peut exécuter
- `CEC_EnchantIncrease::Execute` - Exécution
- `CEC_EnchantIncrease::Init` - Initialisation
- `CEC_EnchantKill` - Kill enchant
- `CEC_EnchantKill::CanExecute` - Peut exécuter
- `CEC_EnchantKill::Execute` - Exécution
- `CEC_EnchantKill::Init` - Initialisation
- `CEC_EnchantKillRandom` - Kill random enchant
- `CEC_EnchantKillRandom::CanExecute` - Peut exécuter
- `CEC_EnchantKillRandom::Execute` - Exécution
- `CEC_EnchantCancel` - Annulation enchant
- `CEC_EnchantCancel::CanExecute` - Peut exécuter
- `CEC_EnchantCancel::Execute` - Exécution
- `CEC_EnchantCancel::Init` - Initialisation

### Enchant for Team
- `CEnchant::IsForTeam` - Est pour team

### Buildings
- `CCharacter::CheckLastBuildingEnchant` - Vérification dernier building enchant

---

## 4. Système de Party/Team/Guild

### Team Management
- `CTeamManager` - Manager de team
- `CTeamManager::Ins` - Instance singleton
- `CTeamMember` - Membre de team
- `CTeamMember::CTeamMember` - Constructeur

### Character Team Operations
- `CCharacter::OnCreateTeam` - Sur création team
- `CCharacter::OnQuitFromTeam` - Sur quit team
- `CCharacter::OnBeforeJoinTeam` - Avant join team
- `CCharacter::IsMyTeam` - Est ma team
- `CCharacter::ChangeRecruitTeamData` - Changement données recruit team
- `CCharacter::ClearRecruitTeamData` - Clear données recruit team
- `CCharacter::ClearAllTeamSpell` - Clear tous spells team
- `CCharacter::RecordEffectIdForTeam` - Enregistrement effect ID pour team
- `CCharacter::TeamGainGold` - Gain gold team

### Colosseum Team
- `CColosseum::CreateTeam` - Création team
- `CColosseum::DropTeam` - Drop team
- `CColosseum::GetTeamSide` - Get côté team
- `CColosseum::GetTeamEventAreaID` - Get ID area event team
- `CColosseum::GetTeamAliveScore` - Get score team vivante
- `CColosseum::IsTeamAllDie` - Est-ce que team toute morte
- `CColosseum::IsTeamAllOutNode` - Est-ce que team toute sortie
- `CColosseum::AllGotoTeamPreparePos` - Tous goto position préparation team
- `CColosseum::AllGotoTeamStartPos` - Tous goto position départ team
- `CColosseum::TeamInfoQuery` - Query info team
- `CColosseum::TeamWin` - Team gagne
- `CColosseum::TotalTeamDamage` - Dégâts totaux team
- `CColosseumFactory::ColosseumTeamQuery` - Query team colosseum

### Battlefield Team
- `CBattlefield::SetTeamScore` - Set score team
- `CBattlefield::SetAllTeamData` - Set toutes données team

### Family/Guild
- `CFamily` - Famille/guilde
- `CFamily::CFamily` - Constructeur
- `CFamilyDBO` - DBO famille
- `CFamilyDBO::CFamilyDBO` - Constructeur
- `CCharacterDBO::GainFamilyHonor` - Gain honneur famille
- `CCharacterDBO::GetAllFamilyName` - Get tous noms famille
- `CCharacter::FamilyLevyTaxes` - Taxes famille
- `CCharacter::FamilyLevyBusinessTaxes` - Taxes business famille
- `CCharacter::IsInFamilyInstance` - Est dans instance famille
- `CCharacter::IsInRaidInstance` - Est dans instance raid
- `CCharacter::IsInRaid25Instance` - Est dans instance raid 25

### Battle Crusade Family
- `CFamilyBattle` - Combat famille
- `CFamilyBattle::FamilyCatch` - Capture famille

### Party Effects
- `CEC_PartyExp` - Effet XP party
- `CEC_PartyExp::CanExecute` - Peut exécuter
- `CEC_PartyExp::Execute` - Exécution
- `CEC_PartyExp::Init` - Initialisation

### Party Formulas
- `CCharFormula::ExpForTeam` - XP pour team
- `CCharFormula::ExpForTeamNoShare` - XP pour team sans partage
- `CCharFormula::JpForTeamNoShare` - JP pour team sans partage

### Events
- `CEventFamilyBattleTimer` - Timer combat famille
- `CEventFamilyBattleTimer::CEventFamilyBattleTimer` - Constructeur
- `CEventFamilyBattleTimer::~CEventFamilyBattleTimer` - Destructeur
- `CEventFamilyBattleTimer::Execute` - Exécution
- `CEventFamilyBattleTimer::GetAllChID` - Get tous IDs characters
- `CEventFamilyTaxationTimer` - Timer taxation famille
- `CEventFamilyTaxationTimer::CEventFamilyTaxationTimer` - Constructeur
- `CEventFamilyTaxationTimer::Execute` - Exécution

---

## 5. Système de Combat et Effects

### Effect Commands (CEC_*)
- `CEC_Effect` - Effet de base
- `CEC_Effect::CanExecute` - Peut exécuter
- `CEC_Effect::Execute` - Exécution
- `CEC_Effect::Init` - Initialisation
- `CEC_Effect::Invoke` - Invocation
- `CEC_EffectChange` - Changement d'effet
- `CEC_EffectChange::CanExecute` - Peut exécuter
- `CEC_EffectChange::Execute` - Exécution
- `CEC_EffectChange::Init` - Initialisation
- `CEC_EffectChange::RollBack` - Rollback
- `CEC_Effect2Target` - Effet vers cible
- `CEC_Effect2Target::CanExecute` - Peut exécuter
- `CEC_Effect2Target::Execute` - Exécution
- `CEC_Effect2Target::Init` - Initialisation
- `CEC_Effect2Other` - Effet vers autre
- `CEC_Effect2Other::CanExecute` - Peut exécuter
- `CEC_Effect2Other::Execute` - Exécution
- `CEC_EffectNextID` - Effet ID suivant
- `CEC_EffectNextID::CanExecute` - Peut exécuter
- `CEC_EffectNextID::Execute` - Exécution
- `CEC_EffectNextID::Init` - Initialisation
- `CEC_EffectNextID::Invoke` - Invocation
- `CEC_EffectNextID2Other` - Effet ID suivant vers autre
- `CEC_EffectNextID2Other::CanExecute` - Peut exécuter
- `CEC_EffectNextID2Other::Execute` - Exécution

### Buff on Attack (CEC_OnAttackBuff*)
- `CEC_OnAttackBuffBlock` - Buff block sur attaque
- `CEC_OnAttackBuffBlock::CanExecute` - Peut exécuter
- `CEC_OnAttackBuffBlock::Execute` - Exécution
- `CEC_OnAttackBuffBlock::Init` - Initialisation
- `CEC_OnAttackBuffDodge` - Buff esquive sur attaque
- `CEC_OnAttackBuffDodge::CanExecute` - Peut exécuter
- `CEC_OnAttackBuffDodge::Execute` - Exécution
- `CEC_OnAttackBuffDodge::Init` - Initialisation
- `CEC_OnAttackBuffPerry` - Buff parade sur attaque
- `CEC_OnAttackBuffPerry::CanExecute` - Peut exécuter
- `CEC_OnAttackBuffPerry::Execute` - Exécution
- `CEC_OnAttackBuffPerry::Init` - Initialisation
- `CEC_OnAttackBuffPhyCritical` - Buff critique physique sur attaque
- `CEC_OnAttackBuffPhyCritical::CanExecute` - Peut exécuter
- `CEC_OnAttackBuffPhyCritical::Execute` - Exécution
- `CEC_OnAttackBuffPhyCritical::Init` - Initialisation
- `CEC_OnAttackBuffMagicCritical` - Buff critique magique sur attaque
- `CEC_OnAttackBuffMagicCritical::CanExecute` - Peut exécuter
- `CEC_OnAttackBuffMagicCritical::Execute` - Exécution
- `CEC_OnAttackBuffMagicCritical::Init` - Initialisation

### Character Effects
- `CCharacter::AddAAEffectPoint` - Ajout point effet AA
- `CCharacter::RecordEffectIdForTeam` - Enregistrement effect ID pour team
- `CCharacter::RefreshEQEffect` - Refresh effet équipement

### Formula
- `CCharFormula::SpellEffectData` - Données effet spell

---

## 6. Système de Shops et Vente

### Shop Classes
- `CItemShop` - Boutique items
- `CItemShop::CItemShop` - Constructeur
- `CItemShop::OpenShop` - Ouverture boutique
- `CEquipmentShop` - Boutique équipement
- `CEquipmentShop::CEquipmentShop` - Constructeur
- `CEquipmentShop::OpenShop` - Ouverture boutique
- `CEnchantingShop` - Boutique enchantement
- `CEnchantingShop::CEnchantingShop` - Constructeur
- `CEnchantingShop::OpenShop` - Ouverture boutique
- `CAuctionShop` - Boutique enchère
- `CAuctionShop::CAuctionShop` - Constructeur
- `CAuctionShop::OpenShop` - Ouverture boutique
- `CAlchemyShop` - Boutique alchimie
- `CAlchemyShop::CAlchemyShop` - Constructeur
- `CAlchemyShop::OpenShop` - Ouverture boutique
- `CCastingShop` - Boutique casting
- `CCastingShop::CCastingShop` - Constructeur
- `CCastingShop::OpenShop` - Ouverture boutique
- `CMiningShop` - Boutique minage
- `CMiningShop::CMiningShop` - Constructeur
- `CMiningShop::OpenShop` - Ouverture boutique
- `CMissionShop` - Boutique missions
- `CMissionShop::CMissionShop` - Constructeur
- `CMissionShop::OpenShop` - Ouverture boutique
- `CMailShop` - Boutique mail
- `CMailShop::CMailShop` - Constructeur
- `CMailShop::OpenShop` - Ouverture boutique
- `CFamilyBuildingShop` - Boutique building famille
- `CFamilyBuildingShop::CFamilyBuildingShop` - Constructeur
- `CFamilyBuildingShop::OpenShop` - Ouverture boutique
- `CFamilyStorageShop` - Boutique stockage famille
- `CFamilyStorageShop::CFamilyStorageShop` - Constructeur
- `CFamilyStorageShop::OpenShop` - Ouverture boutique
- `CFamilyTownListShop` - Boutique liste villes famille
- `CFamilyTownListShop::CFamilyTownListShop` - Constructeur
- `CFamilyTownListShop::OpenShop` - Ouverture boutique

### Shop Operations
- `CCharacter::FinishOperation` - Fin d'opération
- `CCharacter::TargetInVendorRange` - Cible dans range vendeur
- `CEC_OpenShop` - Ouverture boutique
- `CEC_OpenShop::CanExecute` - Peut exécuter
- `CEC_OpenShop::Execute` - Exécution
- `CEC_OpenShop::Init` - Initialisation
- `CEC_StateNoTrade` - État pas de trade
- `CEC_StateNoTrade::CanExecute` - Peut exécuter
- `CEC_StateNoTrade::Execute` - Exécution
- `CEC_StateNoTrade::Init` - Initialisation
- `CEC_StateNoTrade::RollBack` - Rollback

### Network - Auction
- `CNE_CZ_ZoneServerAuctionBuy` - Achat enchère
- `CNE_CZ_ZoneServerAuctionBuyQuery` - Query achat enchère
- `CNE_CZ_ZoneServerAuctionCancel` - Annulation enchère
- `CNE_CZ_ZoneServerAuctionSell` - Vente enchère
- `CNE_CZ_ZoneServerAuctionSellQuery` - Query vente enchère

### Network - Item Mall
- `CNE_CZ_ZoneServerItemMallReceivableEnter` - Entrée recevable item mall
- `CNE_CZ_ZoneServerItemMallUseItem` - Utilisation item mall

### Logging
- `CLogFactory::ItemMallEnchantLog` - Log enchant item mall
- `CGI_TC_ReloadItemMallDB` - Reload DB item mall

---

## 7. Système de Mariage et Relations

### Lover/Couple
- `CLover` - Amour/couple
- `CLover::CLover` - Constructeur
- `CLover::~CLover` - Destructeur
- `CLover::Init` - Initialisation
- `CLover::IsMember` - Est membre
- `CLover::Propose` - Proposition
- `CLover::SetAnswerType` - Set type réponse
- `CLover::SetDivorce` - Set divorce
- `CLover::CheckDivorce` - Vérification divorce
- `CLover::Divorce` - Divorce
- `CLover::DivorceOK` - Divorce OK
- `CLover::Abandon` - Abandon
- `CLover::Disband` - Dissolution
- `CLover::GetMarry` - Get mariage
- `CLover::GetPartnerData` - Get données partenaire
- `CLover::UpdatePosition` - Mise à jour position
- `CLover::UpdatePartnerPosition` - Mise à jour position partenaire
- `CLover::UpdateClientData` - Mise à jour données client
- `CLover::MemberEnterNode` - Membre entre node
- `CLover::MemberLeaveNode` - Membre quitte node

### Wedding
- `CWedding` - Mariage
- `CWedding::CWedding` - Constructeur
- `CWedding::~CWedding` - Destructeur
- `CWedding::Close` - Fermeture

### Character Lover
- `CCharacter::GetLover` - Get amoureux
- `CCharacter::IsInWedding` - Est dans mariage
- `CCharacter::ClearAllLoverSpell` - Clear tous spells amoureux

### Friend
- `CFriend` - Ami
- `CFriend::~CFriend` - Destructeur
- `CCharacter::IsMyFriend` - Est mon ami
- `CNPC::IsMyFriend` - Est mon ami (NPC)
- `CMonster::IsMyFriend` - Est mon ami (monstre)
- `CDoor::IsMyFriend` - Est mon ami (porte)
- `CPetController::IsMyFriend` - Est mon ami (pet)

### Lover Operations
- `CFriendScroll` - Scroll ami
- `CFriendScroll::CheckExecute` - Vérification exécution
- `CFriendScroll::Execute` - Exécution
- `CLoverInviteeOp::Cancel` - Annulation invitation
- `CLoverInviterOp::Cancel` - Annulation invitation
- `CPlayerFactory::LoverInvite` - Invitation amoureux
- `CPlayerFactory::LoverInviteAccept` - Acceptation invitation
- `CPlayerFactory::LoverInviteReject` - Rejet invitation

### Network - Lover
- `CNE_CZ_ZoneServerLoverBreak` - Rupture amoureux
- `CNE_CZ_ZoneServerLoverInvite` - Invitation amoureux
- `CNE_CZ_ZoneServerLoverMessage` - Message amoureux
- `CNE_CZ_ZoneServerQueryLoverInfo` - Query info amoureux
- `CNE_CZ_ZoneServerWeddingInfoQuery` - Query info mariage
- `CNE_DBA_ClientAllLover` - Tous amoureux
- `CNE_ZMis_ZoneServerLoverUpdate` - Update amoureux
- `CNE_ZMis_ZoneServerWeddingsInfo` - Info mariages

### Effects
- `CEC_GotoLover` - Goto amoureux
- `CEC_GotoLover::CanExecute` - Peut exécuter
- `CEC_GotoLover::Execute` - Exécution
- `CEC_GotoLover::Init` - Initialisation

### Events
- `CEventWeddingClearingTime` - Clearing temps mariage
- `CEventWeddingClearingTime::CEventWeddingClearingTime` - Constructeur
- `CEventWeddingClearingTime::Execute` - Exécution

---

## 8. Système de Dungeons et Instances

### Battlefield
- `CBattlefield` - Champ de bataille
- `CBattlefield::CBattlefield` - Constructeur
- `CBattlefield::~CBattlefield` - Destructeur
- `CBattlefield::ClearAll` - Clear tout
- `CBattlefield::Close` - Fermeture
- `CBattlefield::SetTeamScore` - Set score team
- `CBattlefield::SetAllTeamData` - Set toutes données team

### Battlefield Factory
- `CBattlefieldFactory` - Factory champ de bataille
- `CBattlefieldFactory::CBattlefieldFactory` - Constructeur
- `CBattlefieldFactory::~CBattlefieldFactory` - Destructeur
- `CBattlefieldFactory::Close` - Fermeture
- `CBattlefieldFactory::CreateBattlefield` - Création champ de bataille
- `CBattlefieldFactory::EnterBattlefield` - Entrée champ de bataille
- `CBattlefieldFactory::EnterBattlefieldAnswer` - Réponse entrée
- `CBattlefieldFactory::EnterBattlefieldAnswerResult` - Résultat réponse entrée
- `CBattlefieldFactory::EraseMember` - Effacement membre
- `CBattlefieldFactory::EraseMemberForBFZone` - Effacement membre pour zone BF
- `CBattlefieldFactory::FindMVP` - Find MVP
- `CBattlefieldFactory::FindWinCamp` - Find camp gagnant
- `CBattlefieldFactory::Finish` - Fin
- `CBattlefieldFactory::FinishProcess` - Processus fin
- `CBattlefieldFactory::BroadCastToBattlefieldNode` - Broadcast vers node BF
- `CBattlefieldFactory::BroadCastToCampMember` - Broadcast vers membre camp

### Battle Crusade
- `CBattleCrusade` - Croisade bataille
- `CBattleCrusade::CBattleCrusade` - Constructeur
- `CBattleCrusade::~CBattleCrusade` - Destructeur
- `CBattleCrusade::Start` - Démarrage
- `CBattleCrusade::End` - Fin
- `CBattleCrusade::Close` - Fermeture
- `CBattleCrusade::GetNode` - Get node
- `CBattleCrusade::GetNodeGroup` - Get groupe node
- `CBattleCrusade::SendTimeInfoToAll` - Send info temps à tous

### Battle Crusade Factory
- `CBattleCrusadeFactory` - Factory croisade
- `CBattleCrusadeFactory::CBattleCrusadeFactory` - Constructeur
- `CBattleCrusadeFactory::~CBattleCrusadeFactory` - Destructeur
- `CBattleCrusadeFactory::Init` - Initialisation
- `CBattleCrusadeFactory::Close` - Fermeture
- `CBattleCrusadeFactory::CheckBattleCrusade` - Vérification croisade
- `CBattleCrusadeFactory::CheckAllZoneServerEnchant` - Vérification tous enchants serveur zone
- `CBattleCrusadeFactory::GetBattleCrusade` - Get croisade
- `CBattleCrusadeFactory::GetBattleCrusadeIDByNodeID` - Get ID croisade par node ID
- `CBattleCrusadeFactory::NewInstance` - Nouvelle instance
- `CBattleCrusadeFactory::CharacterEnter` - Entrée character
- `CBattleCrusadeFactory::CharacterLeave` - Sortie character
- `CBattleCrusadeFactory::LeaveBattleCrusade` - Quitter croisade
- `CBattleCrusadeFactory::MonsterDie` - Mort monstre
- `CBattleCrusadeFactory::OnStatusTimeUp` - Sur temps status up
- `CBattleCrusadeFactory::ServerShutDown` - Arrêt serveur
- `CBattleCrusadeFactory::SyncAllBattleCrusade` - Sync toutes croisades
- `CBattleCrusadeFactory::TellMissionCloseBattleCrusade` - Dire mission fermer croisade
- `CBattleCrusadeFactory::TransportToStartPos` - Transport vers position départ
- `CBattleCrusadeFactory::BattleCrusadeStart` - Démarrage croisade
- `CBattleCrusadeFactory::BattleCrusadeClose` - Fermeture croisade
- `CBattleCrusadeFactory::BattleCrusadeStatus` - Status croisade
- `CBattleCrusadeFactory::BroadCastToAllCharacter` - Broadcast à tous characters
- `CBattleCrusadeFactory::AllLeaveBattleCrusade` - Tous quitter croisade
- `CBattleCrusadeFactory::GmForceChangeStatus` - GM force changement status

### Colosseum
- `CColosseum` - Colisée
- `CColosseum::CColosseum` - Constructeur (version simplifiée)
- `CColosseum::CColosseum` - Constructeur (version complète)
- `CColosseum::CreateTeam` - Création team
- `CColosseum::DropTeam` - Drop team
- `CColosseum::GetTeamSide` - Get côté team
- `CColosseum::GetTeamEventAreaID` - Get ID area event team
- `CColosseum::GetTeamAliveScore` - Get score team vivante
- `CColosseum::IsTeamAllDie` - Est-ce que team toute morte
- `CColosseum::IsTeamAllOutNode` - Est-ce que team toute sortie
- `CColosseum::AllGotoTeamPreparePos` - Tous goto position préparation team
- `CColosseum::AllGotoTeamStartPos` - Tous goto position départ team
- `CColosseum::TeamInfoQuery` - Query info team
- `CColosseum::TeamWin` - Team gagne
- `CColosseum::TotalTeamDamage` - Dégâts totaux team

### Battle Royale
- `CBattleRoyale` - Battle royale
- `CBattleRoyale::CBattleRoyale` - Constructeur
- `CBattleRoyaleFactory` - Factory battle royale
- `CBattleRoyaleFactory::CBattleRoyaleFactory` - Constructeur
- `CEventBattleRoyaleForceOpen` - Force ouverture battle royale

### Door (Instance Entry)
- `CDoor` - Porte (téléporteur)
- `CDoor::IsMyTeam` - Est ma team
- `CDoor::IsMyFriend` - Est mon ami
- `CDoor::IsPetFriend` - Est ami pet

### Versus Instance
- `CVersusInstance` - Instance versus
- `CVersusInstance::CVersusInstance` - Constructeur
- `CEventVersusInstanceForceOpen` - Force ouverture instance versus

---

*Note: Cet inventaire est partiel et sera complété au fur et à mesure de l'analyse détaillée.*
