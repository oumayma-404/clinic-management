# Architecture de Synchronisation Google Calendar

## Vue d'ensemble

Le système utilise une synchronisation bidirectionnelle entre l'application de gestion de clinique et Google Calendar :

- **App → Google Calendar** : Synchronisation immédiate (temps réel)
- **Google Calendar → App** : Synchronisation périodique via un job en arrière-plan

## Pourquoi cette architecture ?

### Synchronisation immédiate (App → Google Calendar)

Lorsqu'un rendez-vous est créé, mis à jour ou supprimé dans l'application, il est synchronisé **immédiatement** avec Google Calendar. Cela garantit :

1. **Temps réel** : Les changements apparaissent instantanément dans Google Calendar
2. **Fiabilité** : Les utilisateurs voient immédiatement leurs rendez-vous dans leur calendrier Google
3. **Cohérence** : Pas de délai entre la création dans l'app et l'apparition dans Google Calendar

**Implémentation :**
- `CreateAppointmentCommand` → synchronise immédiatement après création
- `UpdateAppointmentCommand` → synchronise immédiatement après mise à jour
- Lorsqu'un rendez-vous est annulé ou complété, l'événement est supprimé de Google Calendar

### Synchronisation périodique (Google Calendar → App)

Un job en arrière-plan (Hangfire) s'exécute périodiquement pour synchroniser les changements **depuis** Google Calendar **vers** l'application. Cela permet :

1. **Capter les changements externes** : Si quelqu'un modifie un rendez-vous directement dans Google Calendar
2. **Créer de nouveaux rendez-vous** : Si un rendez-vous est créé directement dans Google Calendar
3. **Synchronisation bidirectionnelle complète** : Assure que les deux systèmes restent synchronisés

**Implémentation :**
- `GoogleCalendarSyncJob` → s'exécute périodiquement (configuré dans `Program.cs`)
- `SyncGoogleCalendarToAppointmentsAsync` → récupère les événements de Google Calendar et les synchronise avec l'app

## Flux de synchronisation

### Création d'un rendez-vous

```
1. Utilisateur crée un rendez-vous dans l'app
2. CreateAppointmentCommand est exécuté
3. Rendez-vous est sauvegardé dans la base de données
4. Sync immédiate vers Google Calendar (fire and forget)
5. Événement créé dans Google Calendar
```

### Mise à jour d'un rendez-vous

```
1. Utilisateur met à jour un rendez-vous dans l'app
2. UpdateAppointmentCommand est exécuté
3. Rendez-vous est mis à jour dans la base de données
4. Sync immédiate vers Google Calendar (fire and forget)
5. Événement mis à jour dans Google Calendar
```

### Annulation/Suppression d'un rendez-vous

```
1. Utilisateur annule un rendez-vous (status = Cancelled)
2. UpdateAppointmentCommand est exécuté
3. Rendez-vous est marqué comme annulé dans la base de données
4. Sync immédiate vers Google Calendar (fire and forget)
5. SyncService détecte le status "Cancelled" et supprime l'événement de Google Calendar
```

### Changement dans Google Calendar

```
1. Utilisateur modifie un rendez-vous directement dans Google Calendar
2. Job en arrière-plan s'exécute (périodiquement)
3. SyncGoogleCalendarToAppointmentsAsync récupère les événements
4. Compare avec les rendez-vous existants dans l'app
5. Met à jour ou crée les rendez-vous correspondants
```

## Pourquoi pas seulement un job en arrière-plan ?

Si nous utilisions **uniquement** un job en arrière-plan pour synchroniser dans les deux sens :

- ❌ **Délai** : Les changements n'apparaîtraient dans Google Calendar qu'au prochain exécution du job (peut être plusieurs minutes/heures)
- ❌ **Expérience utilisateur** : L'utilisateur devrait attendre pour voir son rendez-vous dans Google Calendar
- ❌ **Fiabilité** : Si le job échoue, les changements ne seraient pas synchronisés

Avec la synchronisation immédiate :

- ✅ **Temps réel** : Les changements apparaissent instantanément
- ✅ **Meilleure expérience** : L'utilisateur voit immédiatement son rendez-vous dans Google Calendar
- ✅ **Fiabilité** : Même si le job échoue, les changements sont déjà synchronisés

## Gestion des erreurs

### Synchronisation immédiate (fire and forget)

La synchronisation immédiate utilise `Task.Run` en mode "fire and forget" :

- ✅ **Non bloquant** : Ne ralentit pas la réponse à l'utilisateur
- ✅ **Résilient** : Les erreurs sont loggées mais n'affectent pas la création/mise à jour du rendez-vous
- ✅ **Configurable** : Si Google Calendar n'est pas configuré, la synchronisation est ignorée silencieusement

### Synchronisation périodique

Le job en arrière-plan utilise Hangfire avec retry automatique :

- ✅ **Retry** : 3 tentatives automatiques en cas d'échec
- ✅ **Logging** : Toutes les erreurs sont loggées
- ✅ **Monitoring** : Visible dans le dashboard Hangfire (`/hangfire`)

## Configuration

### Job en arrière-plan

Le job est configuré dans `Program.cs` :

```csharp
RecurringJob.AddOrUpdate<GoogleCalendarSyncJob>(
    "google-calendar-sync",
    job => job.SyncFromGoogleCalendar(),
    Cron.Hourly); // S'exécute toutes les heures
```

Vous pouvez modifier la fréquence en changeant `Cron.Hourly` :
- `Cron.Minutely` : Toutes les minutes
- `Cron.Hourly` : Toutes les heures
- `Cron.Daily` : Tous les jours
- `Cron.Weekly` : Toutes les semaines

## Résumé

| Direction | Méthode | Fréquence | Raison |
|-----------|---------|----------|--------|
| **App → Google Calendar** | Immédiate | Temps réel | Expérience utilisateur, cohérence |
| **Google Calendar → App** | Job en arrière-plan | Périodique (par défaut: horaire) | Capturer les changements externes |

Cette architecture garantit une synchronisation bidirectionnelle fiable et en temps réel entre l'application et Google Calendar.










