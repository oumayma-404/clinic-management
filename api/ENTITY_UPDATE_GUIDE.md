# Entity Update Guide - Adding Clinic Support

This guide shows exactly what needs to be changed in existing entities to support multi-clinic functionality.

## Patient Entity Updates

### 1. Add ClinicId Property

In `api/ClinicManagement.Domain/Entities/Patient.cs`:

```csharp
public class Patient : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }  // ADD THIS
    public string FirstName { get; private set; }
    // ... existing properties ...
    
    // Navigation properties
    public Clinic Clinic { get; private set; } = null!;  // ADD THIS
    // ... existing navigation properties ...
}
```

### 2. Update Constructor

```csharp
public Patient(
    Guid id,
    Guid clinicId,  // ADD THIS PARAMETER
    string firstName,
    string lastName,
    DateTime dateOfBirth,
    string gender,
    Email email,
    PhoneNumber phoneNumber,
    Address? address = null,
    InsuranceInfo? insuranceInfo = null,
    string? medicalHistory = null,  // ADD IF NOT EXISTS
    string? allergies = null)  // ADD IF NOT EXISTS
{
    Id = id;
    ClinicId = clinicId;  // ADD THIS
    FirstName = firstName ?? throw new ArgumentNullException(nameof(firstName));
    // ... rest of initialization ...
}
```

## Appointment Entity Updates

### 1. Add ClinicId and DoctorId Properties

In `api/ClinicManagement.Domain/Entities/Appointment.cs`:

```csharp
public class Appointment : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }  // ADD THIS
    public string? DoctorId { get; private set; }  // ADD THIS (Auth0 sub)
    public Guid? PatientId { get; private set; }
    // ... existing properties ...
    
    // Navigation properties
    public Clinic Clinic { get; private set; } = null!;  // ADD THIS
    public Patient? Patient { get; private set; }
    // ... existing navigation properties ...
}
```

### 2. Update Constructor

```csharp
public Appointment(
    Guid id,
    Guid clinicId,  // ADD THIS PARAMETER
    Guid? patientId,
    string? doctorId,  // ADD THIS PARAMETER
    DateTime appointmentDateTime,
    TimeSpan duration,
    string? doctorName = null,
    string? notes = null,
    Guid? recurringAppointmentId = null,
    Guid? procedureTypeId = null,
    int? procedureDurationMinutes = null,
    string? procedureColorHex = null)
{
    Id = id;
    ClinicId = clinicId;  // ADD THIS
    DoctorId = doctorId;  // ADD THIS
    PatientId = patientId;
    AppointmentDateTime = appointmentDateTime;
    // ... rest of initialization ...
}
```

## EF Core Configuration Updates

### PatientConfiguration

In `api/ClinicManagement.Infrastructure/Persistence/Configurations/PatientConfiguration.cs`:

```csharp
public void Configure(EntityTypeBuilder<Patient> builder)
{
    // ... existing configuration ...
    
    builder.Property(p => p.ClinicId)
        .IsRequired();
    
    // ... existing configuration ...
    
    builder.HasOne(p => p.Clinic)
        .WithMany(c => c.Patients)
        .HasForeignKey(p => p.ClinicId)
        .OnDelete(DeleteBehavior.Restrict);
}
```

### AppointmentConfiguration

In `api/ClinicManagement.Infrastructure/Persistence/Configurations/AppointmentConfiguration.cs`:

```csharp
public void Configure(EntityTypeBuilder<Appointment> builder)
{
    // ... existing configuration ...
    
    builder.Property(a => a.ClinicId)
        .IsRequired();
    
    builder.Property(a => a.DoctorId)
        .HasMaxLength(200);  // Auth0 sub can be long
    
    // ... existing configuration ...
    
    builder.HasOne(a => a.Clinic)
        .WithMany(c => c.Appointments)
        .HasForeignKey(a => a.ClinicId)
        .OnDelete(DeleteBehavior.Restrict);
    
    builder.HasIndex(a => a.ClinicId);
    builder.HasIndex(a => new { a.ClinicId, a.AppointmentDateTime });
}
```

## Migration Steps

1. **Update Entities** (as shown above)
2. **Update EF Configurations** (as shown above)
3. **Create Migration:**
   ```bash
   dotnet ef migrations add AddMultiClinicSupport --project ClinicManagement.Infrastructure --startup-project ClinicManagement.API
   ```
4. **Review Migration** - Check the generated migration file
5. **Update Existing Data** - If you have existing data, you'll need to:
   - Assign existing patients to a default clinic
   - Assign existing appointments to a clinic and optionally a doctor
6. **Apply Migration:**
   ```bash
   dotnet ef database update --project ClinicManagement.Infrastructure --startup-project ClinicManagement.API
   ```

## Data Migration Script (if needed)

If you have existing data, add this to your migration's `Up()` method:

```csharp
// Create a default clinic if it doesn't exist
var defaultClinicId = Guid.Parse("00000000-0000-0000-0000-000000000001");
if (!context.Clinics.Any(c => c.Id == defaultClinicId))
{
    context.Clinics.Add(new Clinic(
        defaultClinicId,
        "Default Clinic",
        "Default Address",
        "Default Phone",
        "default@clinic.com"));
}

// Assign all existing patients to default clinic
context.Database.ExecuteSqlRaw(
    "UPDATE \"Patients\" SET \"ClinicId\" = '00000000-0000-0000-0000-000000000001' WHERE \"ClinicId\" IS NULL");

// Assign all existing appointments to default clinic
context.Database.ExecuteSqlRaw(
    "UPDATE \"Appointments\" SET \"ClinicId\" = '00000000-0000-0000-0000-000000000001' WHERE \"ClinicId\" IS NULL");
```

## Testing After Migration

1. Verify all patients have a ClinicId
2. Verify all appointments have a ClinicId
3. Test API endpoints with different user roles
4. Verify clinic isolation works (users can't see other clinic's data)





