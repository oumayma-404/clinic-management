# Role Assignment and Personal Info Implementation

## Overview

This document describes the implementation of role assignment and personal information handling for the multi-clinic management application.

## Changes Made

### 1. Domain Layer

#### Doctor Entity (`ClinicManagement.Domain/Entities/Doctor.cs`)
- **Changed**: `Name` property split into `FirstName` and `LastName`
- **Added**: `FullName` computed property for backward compatibility
- **Updated**: Constructor and `Update` method to accept `firstName` and `lastName` separately

#### User Entity
- No changes needed - already supports role and email

### 2. Application Layer

#### New DTOs
- **DoctorPersonalInfoDto**: Contains `FirstName`, `LastName`, `Specialty`, and `Phone` for doctor registration
- **Updated DoctorDto**: Added `FirstName` and `LastName` properties while keeping `Name` for backward compatibility

#### Updated Commands

**CreateClinicCommand**:
- Added `Role` property (required: "doctor" or "secretary")
- Added `DoctorInfo` property (required if Role is "doctor")
- Validates role and doctor info
- Creates doctor record if role is "doctor"
- Updates Auth0 app_metadata with clinic_id and role

**JoinClinicCommand**:
- Added `DoctorInfo` property (required if Role is "doctor")
- Validates role and doctor info
- Creates doctor record if role is "doctor"
- Updates Auth0 app_metadata with clinic_id and role

#### Updated Queries
- **GetUserStatusQuery**: Updated to map `FirstName` and `LastName` to `DoctorDto`
- **UpdateDoctorsCommand**: Updated to handle `FirstName`/`LastName` with backward compatibility for `Name`

#### New Services
- **IAuth0ManagementService**: Interface for updating Auth0 user metadata
- **Auth0ManagementService**: Implementation that:
  - Gets Management API access token using client credentials
  - Updates user's `app_metadata` with `clinic_id` and `role`

### 3. Infrastructure Layer

#### Auth0ManagementService (`ClinicManagement.Infrastructure/Services/Auth0ManagementService.cs`)
- Implements `IAuth0ManagementService`
- Uses Auth0 Management API to update user metadata
- Requires Management API credentials in configuration

#### Service Registration
- Added `HttpClient` registration for Auth0 API calls
- Registered `IAuth0ManagementService` as scoped service

### 4. Common Services

#### ClinicContext
- Added `GetUserEmail()` method to extract email from JWT claims

## Configuration

### appsettings.json

Add Auth0 Management API credentials:

```json
{
  "Auth0": {
    "Domain": "your-domain.auth0.com",
    "Audience": "https://clinic-management-api",
    "ManagementApi": {
      "ClientId": "YOUR_MANAGEMENT_API_CLIENT_ID",
      "ClientSecret": "YOUR_MANAGEMENT_API_CLIENT_SECRET"
    }
  }
}
```

### Auth0 Setup

1. **Create Management API Application**:
   - Go to Auth0 Dashboard → Applications → APIs → Auth0 Management API
   - Create a Machine to Machine application
   - Grant permissions: `read:users`, `update:users`
   - Copy Client ID and Client Secret to appsettings.json

2. **Configure PostLogin Action** (if not already done):
   - Create a PostLogin Action in Auth0
   - Add code to inject `clinic_id` and `role` from `app_metadata` into JWT:

```javascript
exports.onExecutePostLogin = async (event, api) => {
  const namespace = 'https://clinic-management.com/';
  
  if (event.user.app_metadata?.clinic_id) {
    api.idToken.setCustomClaim(namespace + 'clinic_id', event.user.app_metadata.clinic_id);
    api.accessToken.setCustomClaim(namespace + 'clinic_id', event.user.app_metadata.clinic_id);
  }
  
  if (event.user.app_metadata?.role) {
    api.idToken.setCustomClaim(namespace + 'role', event.user.app_metadata.role);
    api.accessToken.setCustomClaim(namespace + 'role', event.user.app_metadata.role);
  }
};
```

## API Endpoints

### Create Clinic

**POST** `/api/clinics`

```json
{
  "name": "Clinic Name",
  "address": "123 Main St",
  "phone": "+216 12 345 678",
  "email": "clinic@example.com",
  "generateCode": true,
  "role": "doctor",
  "doctorInfo": {
    "firstName": "John",
    "lastName": "Doe",
    "specialty": "Dentist",
    "phone": "+216 12 345 679"
  }
}
```

**Response**: `ClinicDto` with clinic information

### Join Clinic

**POST** `/api/clinics/join`

```json
{
  "code": "ABC123",
  "role": "doctor",
  "doctorInfo": {
    "firstName": "Jane",
    "lastName": "Smith",
    "specialty": "Orthodontist",
    "phone": "+216 12 345 680"
  }
}
```

**Response**: `ClinicDto` with clinic information

## Database Migration

**IMPORTANT**: You need to create a migration to update the `Doctors` table:

```bash
dotnet ef migrations add UpdateDoctorEntityFirstNameLastName --project api/ClinicManagement.Infrastructure --startup-project api/ClinicManagement.API
dotnet ef database update --project api/ClinicManagement.Infrastructure --startup-project api/ClinicManagement.API
```

The migration should:
1. Add `FirstName` and `LastName` columns
2. Migrate data from `Name` to `FirstName` and `LastName` (split on space)
3. Optionally drop `Name` column (or keep it for backward compatibility)

## Validation

### Create Clinic
- ✅ Role must be "doctor" or "secretary"
- ✅ If role is "doctor", `DoctorInfo` is required
- ✅ If role is "doctor", `FirstName`, `LastName`, and `Specialty` are required
- ✅ User must not already belong to a clinic

### Join Clinic
- ✅ Role must be "doctor" or "secretary"
- ✅ If role is "doctor", `DoctorInfo` is required
- ✅ If role is "doctor", `FirstName`, `LastName`, and `Specialty` are required
- ✅ Clinic code must exist
- ✅ User must not already belong to a clinic

## Flow

1. **User logs in** via Auth0
2. **Frontend checks** if user has clinic (via `/api/clinics/user-status`)
3. **If no clinic**:
   - User chooses to create or join clinic
   - User selects role (doctor or secretary)
   - If doctor: User provides personal info (firstName, lastName, specialty, phone)
   - Frontend calls create/join endpoint
4. **Backend**:
   - Validates role and doctor info (if applicable)
   - Creates clinic (if creating)
   - Creates User record with role
   - Creates Doctor record (if role is doctor)
   - Updates Auth0 app_metadata with clinic_id and role
5. **User redirected** to main app
6. **Next login**: JWT contains clinic_id and role from app_metadata

## Error Handling

- Auth0 Management API failures are logged but don't fail the operation
- User is still created in database even if Auth0 update fails
- Validation errors return appropriate error messages
- All operations are transactional (Unit of Work pattern)

## Testing

1. Test creating clinic as doctor with personal info
2. Test creating clinic as secretary (no personal info)
3. Test joining clinic as doctor with personal info
4. Test joining clinic as secretary (no personal info)
5. Test validation errors (missing fields, invalid role)
6. Test Auth0 metadata update
7. Test JWT claims after update

## Notes

- Email is extracted from JWT claims (Auth0 provides this)
- FullName for User is set from doctor's firstName + lastName if role is doctor
- Doctor entity has `UserId` to link to User when they register
- Backward compatibility: `DoctorDto.Name` still works, maps to `FullName`



