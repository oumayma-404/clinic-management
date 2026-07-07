# Seed Data - Development Only

## Purpose

The seed data in `ClinicSeedData.cs` is **optional** and intended **only for development and testing**. It provides:

- Example clinics to test multi-clinic functionality
- Test users with different roles for authorization testing
- Sample patients to verify clinic isolation

## Important Notes

1. **Not Used by Default**: The seed data is NOT automatically seeded. It's just a helper class.
2. **Production**: In production, clinics should be created through:
   - Admin API endpoints (to be created)
   - Admin UI interface
   - Manual database setup
3. **Development Only**: If you want to use seed data, it should only be enabled in Development environment.

## How to Use (Optional)

If you want to seed data during development, you can add this to `ApplicationDbContext.OnModelCreating`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    
    // Seed data ONLY in Development
    #if DEBUG
    if (!Clinics.Any())
    {
        modelBuilder.Entity<Clinic>().HasData(ClinicSeedData.GetClinics());
        modelBuilder.Entity<User>().HasData(ClinicSeedData.GetUsers());
        modelBuilder.Entity<Patient>().HasData(ClinicSeedData.GetPatients());
    }
    #endif
}
```

Or use environment-based seeding in `Program.cs`:

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

## Recommendation

**For Production**: Remove or ignore the seed data. Create clinics through proper API endpoints with proper validation and business logic.

**For Development**: Use seed data only if you need quick test data. Otherwise, create clinics through the API.





