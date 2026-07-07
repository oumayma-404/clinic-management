# Clinic Management Flow - Implementation Guide

This document describes the Auth0 authentication and clinic management flow implementation.

## Overview

The system follows this recommended flow:

1. **User signs up/logs in via Auth0** - Auth0 authenticates the user
2. **App checks if user has a clinic** - Query backend database for user
3. **Clinic creation (first user/admin)** - User creates clinic and becomes admin
4. **Subsequent users join existing clinic** - Users join using clinic code

## API Endpoints

### 1. Check User Status
**GET** `/api/clinics/user-status`

Checks if the current authenticated user has a clinic associated.

**Response:**
```json
{
  "isSuccess": true,
  "value": {
    "hasClinic": true,
    "clinicId": "guid",
    "clinicName": "Clinic Name",
    "role": "admin",
    "user": {
      "id": "auth0-sub",
      "clinicId": "guid",
      "role": "admin",
      "email": "user@example.com",
      "fullName": "User Name",
      "createdAt": "2024-01-01T00:00:00Z"
    }
  }
}
```

If user doesn't exist:
```json
{
  "isSuccess": true,
  "value": {
    "hasClinic": false,
    "user": null
  }
}
```

### 2. Create Clinic
**POST** `/api/clinics`

Creates a new clinic and associates the current user as admin.

**Request Body:**
```json
{
  "name": "Downtown Medical Center",
  "address": "123 Main Street",
  "phone": "+1-555-0101",
  "email": "clinic@example.com",
  "generateCode": true
}
```

**Response:**
```json
{
  "isSuccess": true,
  "value": {
    "id": "guid",
    "name": "Downtown Medical Center",
    "address": "123 Main Street",
    "phone": "+1-555-0101",
    "email": "clinic@example.com",
    "code": "ABC123",
    "createdAt": "2024-01-01T00:00:00Z"
  }
}
```

### 3. Join Clinic
**POST** `/api/clinics/join`

Joins an existing clinic using a clinic code.

**Request Body:**
```json
{
  "code": "ABC123",
  "role": "secretary"
}
```

**Valid roles:** `doctor`, `secretary`, `admin`

**Response:**
```json
{
  "isSuccess": true,
  "value": {
    "id": "guid",
    "name": "Downtown Medical Center",
    "code": "ABC123",
    ...
  }
}
```

## Implementation Details

### Domain Layer

**Clinic Entity:**
- Added `Code` property (nullable string, max 20 chars)
- Unique index on `Code` (where Code IS NOT NULL)
- `SetCode()` method to update clinic code

**Clinic Repository:**
- `GetByCodeAsync()` - Find clinic by code
- `CodeExistsAsync()` - Check if code exists

### Application Layer

**Commands:**
- `CreateClinicCommand` - Creates clinic and user as admin
- `JoinClinicCommand` - Joins user to existing clinic

**Queries:**
- `GetUserStatusQuery` - Checks if user exists and has clinic

**DTOs:**
- `ClinicDto` - Clinic information
- `UserStatusDto` - User and clinic status
- `CreateClinicRequest` - Request for creating clinic
- `JoinClinicRequest` - Request for joining clinic

### Clinic Code Generation

- 6-character alphanumeric code (A-Z, 0-9)
- Automatically generated when `generateCode: true`
- Ensures uniqueness by checking database
- Example: `ABC123`, `XYZ789`

## Frontend Integration Flow

### Step 1: User Authentication
```typescript
// User logs in via Auth0
// Receives JWT token with 'sub' claim
```

### Step 2: Check User Status
```typescript
// After login, call:
GET /api/clinics/user-status
// With JWT token in Authorization header

if (response.hasClinic) {
  // User has clinic, proceed to app
  redirectToDashboard();
} else {
  // User needs to create or join clinic
  showClinicSetupScreen();
}
```

### Step 3: Create or Join Clinic

**Option A: Create Clinic (First User)**
```typescript
POST /api/clinics
{
  name: "My Clinic",
  generateCode: true
}
```

**Option B: Join Clinic (Subsequent Users)**
```typescript
POST /api/clinics/join
{
  code: "ABC123",
  role: "secretary"
}
```

### Step 4: Update Auth0 Metadata (Optional)

After creating/joining clinic, you can update Auth0 `app_metadata`:

```javascript
// Auth0 Management API
await auth0Management.users.update(
  { id: userId },
  {
    app_metadata: {
      clinic_id: clinicId,
      role: "admin"
    }
  }
);
```

Then configure Auth0 Rule/Action to inject these into JWT:

```javascript
// Auth0 PostLogin Action
exports.onExecutePostLogin = async (event, api) => {
  const namespace = 'https://clinic-management.com/';
  
  if (event.user.app_metadata?.clinic_id) {
    api.idToken.setCustomClaim(
      namespace + 'clinic_id',
      event.user.app_metadata.clinic_id
    );
    api.accessToken.setCustomClaim(
      namespace + 'clinic_id',
      event.user.app_metadata.clinic_id
    );
  }
  
  if (event.user.app_metadata?.role) {
    api.idToken.setCustomClaim(
      namespace + 'role',
      event.user.app_metadata.role
    );
    api.accessToken.setCustomClaim(
      namespace + 'role',
      event.user.app_metadata.role
    );
  }
};
```

## Security Considerations

1. **Authentication Required:** All endpoints require JWT authentication
2. **User Validation:** Commands check if user already has a clinic
3. **Role Validation:** Join command validates role is one of: doctor, secretary, admin
4. **Code Validation:** Join command validates clinic code exists
5. **Unique Codes:** Clinic codes are unique and indexed

## Database Migration

After implementing, create a migration:

```bash
dotnet ef migrations add AddClinicCode --project api/ClinicManagement.Infrastructure --startup-project api/ClinicManagement.API
dotnet ef database update --project api/ClinicManagement.Infrastructure --startup-project api/ClinicManagement.API
```

## Next Steps

1. **Update Auth0 PostLogin Action** to inject clinic_id and role into JWT
2. **Frontend Implementation** - Create clinic setup/join screens
3. **Email Extraction** - Update commands to extract email from JWT claims
4. **FullName Extraction** - Update commands to extract name from JWT claims




