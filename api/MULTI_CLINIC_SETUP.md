# Multi-Clinic Management System - Setup Guide

This guide explains how to set up and use the multi-clinic management system with role-based authorization.

## Overview

The system supports multiple clinics with complete data isolation. Each user belongs to one clinic and can only access data from their clinic. Authorization is role-based with three roles:
- **Doctor**: Can create/edit patients, view and manage appointments
- **Secretary**: Can view patients, schedule appointments
- **Admin**: Can manage users and clinic settings

## JWT Claims Configuration

Auth0 must be configured to include the following claims in the JWT token:

1. **`clinic_id`**: The GUID of the clinic the user belongs to
2. **`role`**: The user's role (`doctor`, `secretary`, or `admin`)
3. **`sub`**: The Auth0 user identifier (used as User.Id)

### Auth0 Rule Example

```javascript
function (user, context, callback) {
  const namespace = 'https://clinic-management.com/';
  
  // Set clinic_id based on user metadata or app_metadata
  context.idToken[namespace + 'clinic_id'] = user.app_metadata?.clinic_id || user.user_metadata?.clinic_id;
  context.idToken[namespace + 'role'] = user.app_metadata?.role || user.user_metadata?.role;
  
  // Also add to access token
  context.accessToken[namespace + 'clinic_id'] = user.app_metadata?.clinic_id || user.user_metadata?.clinic_id;
  context.accessToken[namespace + 'role'] = user.app_metadata?.role || user.user_metadata?.role;
  
  callback(null, user, context);
}
```

## Database Schema Updates

### Required Entity Changes

1. **Patient Entity**: Add `ClinicId` property
2. **Appointment Entity**: Add `ClinicId` and `DoctorId` properties
3. **New Entities**: `Clinic` and `User`

### Migration Steps

1. Add `ClinicId` to Patient entity:
```csharp
public Guid ClinicId { get; private set; }
public Clinic Clinic { get; private set; } = null!;
```

2. Update Patient constructor to include ClinicId:
```csharp
public Patient(
    Guid id,
    Guid clinicId,  // Add this
    string firstName,
    // ... rest of parameters
)
{
    Id = id;
    ClinicId = clinicId;  // Add this
    // ... rest of initialization
}
```

3. Add `ClinicId` and `DoctorId` to Appointment entity:
```csharp
public Guid ClinicId { get; private set; }
public string? DoctorId { get; private set; }  // Auth0 sub of the doctor
public Clinic Clinic { get; private set; } = null!;
```

4. Update Appointment constructor accordingly.

## Repository Implementation Updates

### PatientRepository

Add the `GetByClinicIdAsync` method:

```csharp
public async Task<IEnumerable<Patient>> GetByClinicIdAsync(Guid clinicId, CancellationToken cancellationToken = default)
{
    return await _context.Patients
        .Where(p => p.ClinicId == clinicId)
        .ToListAsync(cancellationToken);
}
```

### AppointmentRepository

Add the `GetByClinicIdAsync` method:

```csharp
public async Task<IEnumerable<Appointment>> GetByClinicIdAsync(
    Guid clinicId, 
    DateTime? startDate = null, 
    DateTime? endDate = null, 
    CancellationToken cancellationToken = default)
{
    var query = _context.Appointments
        .Where(a => a.ClinicId == clinicId);

    if (startDate.HasValue)
        query = query.Where(a => a.AppointmentDateTime >= startDate.Value);

    if (endDate.HasValue)
        query = query.Where(a => a.AppointmentDateTime <= endDate.Value);

    return await query.ToListAsync(cancellationToken);
}
```

## Program.cs Configuration

Update `Program.cs` to register the new services:

```csharp
// Add after existing service registrations
services.AddHttpContextAccessor();
services.AddScoped<IClinicContext, ClinicContext>();

// Configure authorization policies
services.AddAuthorization(options =>
{
    AuthorizationPolicies.ConfigurePolicies(options);
});
services.AddScoped<IAuthorizationHandler, RoleAuthorizationHandler>();
```

## Seed Data (Optional - Development Only)

The seed data in `ClinicSeedData.cs` is **optional** and provided for development/testing purposes only. It includes:
- 2 Clinics
- 2 Doctors per clinic
- 2 Secretaries per clinic
- 1 Admin per clinic
- 3 Patients per clinic

**Important**: The seed data is NOT automatically used. In production, clinics should be created through API endpoints or admin interface.

If you want to use seed data for development, you can add it conditionally in `Program.cs`:

```csharp
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        
        // Only seed if database is empty
        if (!context.Clinics.Any())
        {
            context.Clinics.AddRange(ClinicSeedData.GetClinics());
            context.Users.AddRange(ClinicSeedData.GetUsers());
            context.Patients.AddRange(ClinicSeedData.GetPatients());
            context.SaveChanges();
        }
    }
}
```

**For production**: Create clinics through proper API endpoints with validation.

## API Endpoints

### Patients

- `GET /api/patients` - Get all patients for user's clinic (all roles)
- `POST /api/patients` - Create patient (doctor/secretary only)

### Appointments

- `GET /api/appointments` - Get appointments for user's clinic (all roles)
- `POST /api/appointments` - Create appointment (doctor/secretary only)

### Users

- `GET /api/users` - Get all users for user's clinic (admin only)

## Security Features

1. **Automatic Clinic Filtering**: All queries automatically filter by the user's clinic ID from JWT
2. **Role-Based Authorization**: Endpoints protected with `[Authorize(Policy = "...")]`
3. **Cross-Clinic Protection**: Attempting to access another clinic's data returns 403 Forbidden
4. **JWT Validation**: All endpoints require valid Auth0 JWT token

## Testing

### Test JWT Tokens

For testing, you can create JWT tokens with these claims:

**Doctor from Clinic 1:**
```json
{
  "sub": "auth0|doctor1-clinic1",
  "clinic_id": "11111111-1111-1111-1111-111111111111",
  "role": "doctor"
}
```

**Secretary from Clinic 1:**
```json
{
  "sub": "auth0|secretary1-clinic1",
  "clinic_id": "11111111-1111-1111-1111-111111111111",
  "role": "secretary"
}
```

**Admin from Clinic 1:**
```json
{
  "sub": "auth0|admin1-clinic1",
  "clinic_id": "11111111-1111-1111-1111-111111111111",
  "role": "admin"
}
```

## Error Handling

The system throws `ForbiddenAccessException` when:
- User tries to access data from another clinic
- User doesn't have the required role for an operation
- Clinic ID is missing from JWT token

This exception is automatically converted to HTTP 403 Forbidden response.

## Next Steps

1. Update Patient and Appointment entities to include ClinicId
2. Create and run database migration
3. Update existing repository implementations
4. Test with different user roles and clinics
5. Configure Auth0 rules to include clinic_id and role claims

