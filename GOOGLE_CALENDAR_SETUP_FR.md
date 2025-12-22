# Guide de Configuration Google Calendar (Français)

Ce guide vous aidera à configurer la synchronisation bidirectionnelle entre le système de gestion de clinique et Google Calendar.

## Étape 1: Créer un projet Google Cloud et activer l'API Calendar

1. Allez sur [Google Cloud Console](https://console.cloud.google.com/)
2. Créez un nouveau projet ou sélectionnez un projet existant
3. Activez l'API Google Calendar :
   - Naviguez vers "APIs & Services" > "Library"
   - Recherchez "Google Calendar API"
   - Cliquez sur "Enable"

## Étape 2: Configurer l'écran de consentement OAuth

**IMPORTANT:** Avant de créer les identifiants OAuth, vous devez configurer l'écran de consentement.

1. Allez dans "APIs & Services" > "OAuth consent screen"
2. Choisissez "External" (pour le développement) ou "Internal" (si vous avez Google Workspace)
3. Remplissez les informations requises:
   - **App name**: "Clinic Management" (ou le nom de votre choix)
   - **User support email**: Votre email
   - **Developer contact information**: Votre email
4. Cliquez sur "Save and Continue"
5. Sur la page "Scopes", cliquez sur "Add or Remove Scopes"
6. Ajoutez ces scopes:
   - `https://www.googleapis.com/auth/calendar`
   - `https://www.googleapis.com/auth/calendar.events`
7. Cliquez sur "Update" puis "Save and Continue"
8. Sur la page "Test users", cliquez sur "ADD USERS"
9. **Ajoutez votre email** (benkhalifa.oumayma98@gmail.com) et tous les emails qui devront utiliser l'application
10. Cliquez sur "Save and Continue"
11. Sur la page "Summary", cliquez sur "Back to Dashboard"

**Note:** En mode test, seuls les utilisateurs ajoutés dans "Test users" peuvent utiliser l'application.

## Étape 3: Créer les identifiants OAuth 2.0

1. Allez dans "APIs & Services" > "Credentials"
2. Cliquez sur "Create Credentials" > "OAuth client ID"
3. Choisissez "Web application" comme type d'application
4. Ajoutez les URI de redirection autorisés (voir ci-dessous)
5. Cliquez sur "Create"
6. Enregistrez le **Client ID** et le **Client Secret**

## Étape 4: Configurer les URI de redirection OAuth 2.0

**IMPORTANT:** Cette étape est obligatoire avant d'utiliser OAuth 2.0 Playground.

1. Allez sur [Google Cloud Console](https://console.cloud.google.com/)
2. Sélectionnez votre projet
3. Naviguez vers **APIs & Services** > **Credentials**
4. Cliquez sur votre **OAuth 2.0 Client ID** (celui créé à l'étape 2)
5. Sous **Authorized redirect URIs**, cliquez sur **ADD URI**
6. Ajoutez cette URI: `https://developers.google.com/oauthplayground`
7. Cliquez sur **SAVE**

**Note:** Si vous ne faites pas cette étape, vous obtiendrez l'erreur:
> "Vous ne pouvez pas vous connecter à cette appli, car elle ne respecte pas le règlement OAuth 2.0 de Google."

## Étape 5: Obtenir un Refresh Token

Vous devez obtenir un refresh token pour permettre à l'application d'accéder à Google Calendar.

### Option A: Utiliser OAuth 2.0 Playground (Recommandé pour les tests)

1. Allez sur [OAuth 2.0 Playground](https://developers.google.com/oauthplayground/)
2. Cliquez sur l'icône d'engrenage (⚙️) en haut à droite
3. Cochez "Use your own OAuth credentials"
4. Entrez votre Client ID et Client Secret
5. Dans le panneau de gauche, trouvez "Calendar API v3"
6. Sélectionnez les scopes suivants:
   - `https://www.googleapis.com/auth/calendar`
   - `https://www.googleapis.com/auth/calendar.events`
7. Cliquez sur "Authorize APIs"
8. Connectez-vous avec le compte Google qui a accès au calendrier que vous voulez synchroniser
9. Cliquez sur "Exchange authorization code for tokens"
10. Copiez le **Refresh token**

### Option B: Utiliser un script

Vous pouvez utiliser un script simple pour obtenir le refresh token programmatiquement.

## Étape 6: Configurer l'Application

Ajoutez la configuration suivante dans `appsettings.json`:

```json
{
  "GoogleCalendar": {
    "ClientId": "VOTRE_CLIENT_ID",
    "ClientSecret": "VOTRE_CLIENT_SECRET",
    "RefreshToken": "VOTRE_REFRESH_TOKEN",
    "CalendarId": "primary"
  }
}
```

**Note:** 
- `CalendarId` peut être:
  - `"primary"` - pour le calendrier principal
  - Un ID de calendrier spécifique (trouvé dans les paramètres Google Calendar)
  - Une adresse email pour un calendrier partagé

## Étape 7: Créer la migration de base de données

Exécutez la commande suivante pour créer une migration pour le nouveau champ `GoogleCalendarEventId`:

```bash
dotnet ef migrations add AddGoogleCalendarEventId --project api/ClinicManagement.Infrastructure --startup-project api/ClinicManagement.API
```

Puis appliquez la migration:

```bash
dotnet ef database update --project api/ClinicManagement.Infrastructure --startup-project api/ClinicManagement.API
```

## Comment ça fonctionne

### Synchronisation de la Clinique vers Google Calendar

- Lorsque vous créez ou mettez à jour un rendez-vous dans le système de gestion de clinique, il se synchronise automatiquement avec Google Calendar
- Le rendez-vous est créé/mis à jour dans Google Calendar avec:
  - Résumé: "Appointment: [Nom du Patient]"
  - Description: Inclut le nom du médecin, les notes, le statut et l'ID du patient
  - Lieu: Nom du médecin (si disponible)
  - Heures de début/fin: Basées sur la date/heure du rendez-vous et la durée

### Synchronisation de Google Calendar vers la Clinique

- Un travail en arrière-plan s'exécute toutes les heures pour vérifier les changements dans Google Calendar
- Si un nouvel événement est trouvé dans Google Calendar qui correspond aux modèles de rendez-vous de la clinique, il crée un rendez-vous dans le système
- Si un événement existant est mis à jour dans Google Calendar, le rendez-vous correspondant est mis à jour

## Dépannage

### "Google Calendar credentials are not configured"

- Assurez-vous que tous les champs requis sont remplis dans `appsettings.json`
- Vérifiez que la section de configuration s'appelle exactement `GoogleCalendar`

### "Invalid refresh token"

- Le refresh token peut avoir expiré ou être révoqué
- Générez un nouveau refresh token en utilisant OAuth 2.0 Playground
- Assurez-vous d'utiliser le bon compte Google

### Erreur OAuth 2.0 Playground

Si vous obtenez l'erreur:
> "Vous ne pouvez pas vous connecter à cette appli, car elle ne respecte pas le règlement OAuth 2.0 de Google."

**Solution:**
1. Allez dans Google Cloud Console > APIs & Services > Credentials
2. Cliquez sur votre OAuth 2.0 Client ID
3. Ajoutez `https://developers.google.com/oauthplayground` dans "Authorized redirect URIs"
4. Sauvegardez
5. Réessayez dans OAuth 2.0 Playground

### Erreur 403: access_denied - "L'appli n'a pas terminé la procédure de validation"

Si vous obtenez l'erreur:
> "Accès bloqué : clinic management n'a pas terminé la procédure de validation de Google"

**Solution:**
1. Allez dans Google Cloud Console > APIs & Services > OAuth consent screen
2. Cliquez sur l'onglet "Test users" (ou "Utilisateurs de test")
3. Cliquez sur "ADD USERS" (ou "AJOUTER DES UTILISATEURS")
4. Ajoutez votre email (benkhalifa.oumayma98@gmail.com) et tous les emails qui devront utiliser l'application
5. Cliquez sur "ADD" puis "SAVE"
6. Attendez quelques minutes pour que les changements prennent effet
7. Réessayez dans OAuth 2.0 Playground

**Note:** En mode test, seuls les utilisateurs ajoutés dans "Test users" peuvent autoriser l'application. Pour la production, vous devrez soumettre l'application à la vérification de Google.

### Les événements ne se synchronisent pas

- Vérifiez les logs de l'application pour les erreurs
- Vérifiez que l'API Google Calendar est activée dans votre projet Google Cloud
- Assurez-vous que le refresh token a les bons scopes
- Vérifiez le tableau de bord Hangfire (`/hangfire`) pour voir si le travail en arrière-plan s'exécute

## Notes de sécurité

- **Ne commitez jamais** `appsettings.json` avec de vrais identifiants dans le contrôle de version
- Utilisez des variables d'environnement ou Azure Key Vault pour la production
- Le refresh token fournit un accès à long terme - gardez-le sécurisé
- Envisagez d'utiliser des comptes de service pour les déploiements en production

