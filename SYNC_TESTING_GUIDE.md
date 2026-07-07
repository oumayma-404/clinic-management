# Guide de Test de Synchronisation Google Calendar

## Problèmes Courants et Solutions

### 1. Vérifier que la Migration a été Appliquée

La migration `AddGoogleCalendarEventId` doit être appliquée pour ajouter le champ `GoogleCalendarEventId` à la table `Appointments`.

**Vérifier les migrations appliquées :**
```bash
cd api/ClinicManagement.API
dotnet ef migrations list --project ../ClinicManagement.Infrastructure
```

**Appliquer la migration si nécessaire :**
```bash
dotnet ef database update --project ../ClinicManagement.Infrastructure
```

### 2. Vérifier les Logs de l'Application

Quand vous créez un rendez-vous, vérifiez les logs de l'application. Vous devriez voir :
- `"Created Google Calendar event {EventId} for appointment {AppointmentId}"` si la synchronisation réussit
- `"Google Calendar credentials are not configured"` si les credentials manquent
- Des erreurs spécifiques si quelque chose ne va pas

### 3. Tester la Synchronisation Manuellement

J'ai créé un contrôleur de test avec deux endpoints :

#### A. Synchroniser depuis Google Calendar vers la Clinique

```bash
POST http://localhost:5000/api/googlecalendar/sync-from-google
```

Cela va :
- Récupérer tous les événements de Google Calendar
- Créer/mettre à jour les rendez-vous dans votre système

#### B. Synchroniser un Rendez-vous vers Google Calendar

```bash
POST http://localhost:5000/api/googlecalendar/sync-appointment/{appointmentId}
```

Remplacez `{appointmentId}` par l'ID d'un rendez-vous existant.

### 4. Vérifier le Dashboard Hangfire

1. Allez sur `http://localhost:5000/hangfire`
2. Vérifiez que le job "sync-google-calendar" est planifié
3. Regardez l'historique d'exécution pour voir s'il y a des erreurs

### 5. Vérifier dans Google Calendar

1. Ouvrez Google Calendar
2. Vérifiez que les événements sont créés avec le format :
   - **Titre** : "Appointment: [Nom du Patient]"
   - **Description** : Contient les détails du rendez-vous

### 6. Tester la Synchronisation Bidirectionnelle

#### Test 1: Clinic → Google Calendar
1. Créez un nouveau rendez-vous dans votre application
2. Vérifiez dans Google Calendar que l'événement apparaît (peut prendre quelques secondes)

#### Test 2: Google Calendar → Clinic
1. Créez un événement dans Google Calendar avec le titre "Appointment: [Nom d'un patient existant]"
2. Attendez 1 minute (le job s'exécute toutes les minutes en mode test)
3. Vérifiez dans votre application que le rendez-vous apparaît

### 7. Vérifier les Credentials

Assurez-vous que dans `appsettings.json` :
```json
"GoogleCalendar": {
  "ClientId": "...",
  "ClientSecret": "...",
  "RefreshToken": "...",
  "CalendarId": "primary"
}
```

**Note:** Le refresh token peut expirer. Si vous obtenez des erreurs d'authentification, vous devrez peut-être générer un nouveau refresh token.

### 8. Fréquence de Synchronisation

- **Mode Test** : Le job s'exécute toutes les **minutes** (configuré dans `Program.cs`)
- **Mode Production** : Changez `Cron.Minutely` en `Cron.Hourly` dans `Program.cs`

## Dépannage

### Le rendez-vous n'apparaît pas dans Google Calendar

1. Vérifiez les logs de l'application pour des erreurs
2. Testez manuellement avec l'endpoint `/api/googlecalendar/sync-appointment/{id}`
3. Vérifiez que les credentials sont corrects
4. Vérifiez que le refresh token n'a pas expiré

### Les événements Google Calendar n'apparaissent pas dans la clinique

1. Vérifiez le dashboard Hangfire pour voir si le job s'exécute
2. Testez manuellement avec l'endpoint `/api/googlecalendar/sync-from-google`
3. Vérifiez que les événements dans Google Calendar ont le bon format (commencent par "Appointment:")
4. Vérifiez les logs pour des erreurs

### Erreur "Google Calendar credentials are not configured"

1. Vérifiez que la section `GoogleCalendar` existe dans `appsettings.json`
2. Vérifiez que tous les champs sont remplis (ClientId, ClientSecret, RefreshToken)
3. Redémarrez l'application après avoir modifié `appsettings.json`











