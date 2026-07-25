# App Manager API Usage Guide

**Version:** 1.4
**Last Updated:** 2026-04-29
**Base URL:** `https://api.appmanager.com` (or `https://localhost:32769/` for local development)

> **Upgrading from v1.3?** See [`api-migration-notes-v1.4.md`](api-migration-notes-v1.4.md) for the full request-bound parameter rename map. v1.4 is a breaking change — every query/form/route parameter name now carries the `a` prefix.

This guide provides comprehensive documentation for integrating child applications with App Manager's API. Whether you're building a web app, mobile app, desktop application, or AI agent, this guide covers everything you need to know.

### What's new in 1.4

- **Request-bound parameter rename (breaking).** All `[FromQuery]`, `[FromForm]`, and `[FromRoute]` parameter names — and the route-template tokens that bind to them — now carry an `a` prefix per the project's coding-standards convention. For example, `?applicationId=1` is now `?aApplicationId=1`, and `/IssueSvc/{issueId}` is now `/IssueSvc/{aIssueId}`. JSON request/response bodies (DTO field names) are **unchanged** — only URL-bound parameter names changed.
- **C# reference-implementation rename.** The `AppManagerClient` reference code in §4 has been re-emitted under the same convention: instance fields use the `obj` prefix, parameters use `a`, locals use `v`, and booleans pick up `Is`/`Has` form where they did not already. This is documentation-only — the wire contract is defined entirely by the URL/JSON shapes above.
- **No new endpoints, no new error codes, no JSON shape changes.** Strictly a parameter-naming sweep.

---

## Table of Contents

1. [Quick Start](#1-quick-start)
2. [Authentication](#2-authentication) (includes [Password Encryption](#24-password-encryption-recommended))
3. [API Reference](#3-api-reference)
   - [Auth Service (AuthSvc)](#31-auth-service-authsvc)
   - [License Service (LicenseSvc)](#32-license-service-licensesvc)
   - [User Service (UserSvc)](#33-user-service-usersvc)
   - [Feature Service (FeatureSvc)](#34-feature-service-featuresvc)
   - [Payment Service (PaymentSvc)](#35-payment-service-paymentsvc)
   - [Issue Service (IssueSvc)](#36-issue-service-issuesvc)
4. [Code Examples](#4-code-examples)
5. [AI Agent Integration Guide](#5-ai-agent-integration-guide)
6. [Error Handling](#6-error-handling)

---

## 1. Quick Start

Get up and running with the App Manager API in 5 minutes.

### Step 1: Identify Your Application

Every API call should identify which child application is making the request. There are two ways:

**Option A: API Key Headers (Recommended)**

Include your application's API key and secret in request headers. The system automatically resolves the ApplicationId:

```http
X-Api-Key: ak_live_your_api_key_here
X-Api-Secret: your_api_secret_here
```

**Option B: Explicit ApplicationId Parameter**

Pass it as a query parameter (`aApplicationId`, v1.4 naming) or in the request body (`applicationId` JSON field — DTO names are unchanged). Which one varies by endpoint.

**Option C: Both (Recommended for extra safety)**

When both are provided, the system validates they match.

### Step 2: Get the Server's Public Key (for password encryption)

```bash
curl -X GET "https://api.appmanager.com/AuthSvc/public-key"
```

Cache the returned public key. Use it to RSA-encrypt passwords before sending (see [Section 2.4](#24-password-encryption-recommended) for details).

### Step 3: Register or Login a User

```bash
curl -X POST "https://api.appmanager.com/AuthSvc/login" \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: ak_live_your_api_key_here" \
  -H "X-Api-Secret: your_api_secret_here" \
  -d '{
    "email": "user@example.com",
    "encryptedPassword": "base64_rsa_encrypted_password..."
  }'
```

> **Important:** Plain text passwords are **not accepted**. All password fields must be RSA-encrypted. See [Section 2.4](#24-password-encryption-recommended) for implementation details.

**Response:**
```json
{
  "success": true,
  "data": {
    "userId": 1,
    "email": "user@example.com",
    "firstName": "John",
    "lastName": "Doe",
    "applicationRole": "User",
    "appManagerRole": "ApplicationUser",
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "refreshToken": "rt_abc123xyz789...",
    "tokenExpiresAt": "2026-01-26T14:00:00Z",
    "activeLicense": {
      "licenseId": 1,
      "licenseName": "Professional",
      "status": "Active",
      "applicationId": 1,
      "applicationName": "My App"
    }
  },
  "message": "Login successful"
}
```

### Step 4: Use the Access Token

Include the access token (and optionally API key headers) in all subsequent requests:

```bash
curl -X GET "https://api.appmanager.com/UserSvc/profile" \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..." \
  -H "X-Api-Key: ak_live_your_api_key_here" \
  -H "X-Api-Secret: your_api_secret_here"
```

### Authentication Flow Overview

```
+------------------------------------------------------------------+
|                    Authentication Flow                            |
+------------------------------------------------------------------+
|                                                                   |
|  1. Your app sends API Key headers to identify itself             |
|           |                                                       |
|  2. Call GET /AuthSvc/public-key to get RSA public key            |
|           |                                                       |
|  3. User enters credentials — encrypt password with RSA key       |
|           |                                                       |
|  4. Call POST /AuthSvc/login with encryptedPassword               |
|           |                                                       |
|  5. Receive accessToken, refreshToken, and app-scoped license     |
|           |                                                       |
|  6. Use accessToken in Authorization header for all requests      |
|           |                                                       |
|  7. When accessToken expires, call POST /AuthSvc/refresh          |
|           |                                                       |
|  8. Receive new tokens, continue making requests                  |
|                                                                   |
+------------------------------------------------------------------+
```

---

## 2. Authentication

The API uses a dual authentication mechanism:
1. **API Key Authentication (Optional):** Identifies the calling application via `X-Api-Key` and `X-Api-Secret` headers
2. **JWT Bearer Token (Required for protected endpoints):** Identifies the user via `Authorization: Bearer {token}` header
3. **Password Encryption (Recommended):** Passwords should be RSA-encrypted before sending to protect against MITM attacks

### 2.1 API Key Authentication

API keys are created in the AppManager admin UI under each application's settings. When provided, the API key automatically resolves the `applicationId` for the request.

**Headers:**
```http
X-Api-Key: ak_live_your_api_key_here
X-Api-Secret: your_api_secret_here
X-App-Id: 1  (optional, validated against API key if provided)
```

If API key headers are not provided, you must pass the application ID explicitly — as `aApplicationId` (query parameter, v1.4 naming) or as `applicationId` (JSON body field, unchanged).

### 2.2 Obtaining JWT Tokens

Tokens are obtained through:
- **Registration:** `POST /AuthSvc/register` - for new users
- **Login:** `POST /AuthSvc/login` - for existing users

### 2.3 Using Tokens

Include the access token in the Authorization header:

```http
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
Content-Type: application/json
X-Api-Key: ak_live_your_api_key_here     (optional but recommended)
X-Api-Secret: your_api_secret_here        (optional but recommended)
```

### 2.3 Token Refresh

Access tokens expire after a configured duration (default: 1 hour). Use the refresh token to obtain new tokens:

**Request:**
```bash
curl -X POST "https://api.appmanager.com/AuthSvc/refresh" \
  -H "Content-Type: application/json" \
  -d '{
    "refreshToken": "rt_abc123xyz789..."
  }'
```

**Response:**
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGciOiJIUzI1NiIs...",
    "refreshToken": "rt_new_token_xyz...",
    "expiresAt": "2026-01-26T15:00:00Z"
  },
  "message": "Token refreshed successfully"
}
```

### 2.4 Password Encryption (Required)

All passwords **must** be RSA-encrypted before sending to the API. Plain text passwords are rejected. This protects against MITM attacks even when TLS is compromised (e.g., intercepting proxies with custom CA certificates like Fiddler or Charles Proxy).

**Step 1: Fetch the server's public key**

```bash
curl -X GET "https://api.appmanager.com/AuthSvc/public-key" \
  -H "Content-Type: application/json"
```

**Response:**
```json
{
  "success": true,
  "data": {
    "publicKey": "-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkqhki...\n-----END PUBLIC KEY-----",
    "algorithm": "RSA-OAEP-256",
    "encoding": "base64"
  },
  "message": "Use this public key to encrypt passwords before sending"
}
```

**Step 2: Encrypt the password client-side**

Use RSA-OAEP with SHA-256 padding to encrypt the password, then base64-encode the result.

**.NET/C# Example:**
```csharp
using System.Security.Cryptography;
using System.Text;

string EncryptPassword(string aPassword, string aPublicKeyPem)
{
    using var vRsa = RSA.Create();
    vRsa.ImportFromPem(aPublicKeyPem);
    var vEncryptedBytes = vRsa.Encrypt(
        Encoding.UTF8.GetBytes(aPassword),
        RSAEncryptionPadding.OaepSHA256);
    return Convert.ToBase64String(vEncryptedBytes);
}
```

**JavaScript/Node.js Example:**
```javascript
const crypto = require('crypto');

function encryptPassword(password, publicKeyPem) {
  const encrypted = crypto.publicEncrypt(
    { key: publicKeyPem, padding: crypto.constants.RSA_PKCS1_OAEP_PADDING, oaepHash: 'sha256' },
    Buffer.from(password, 'utf8')
  );
  return encrypted.toString('base64');
}
```

**Python Example:**
```python
from cryptography.hazmat.primitives.asymmetric import padding
from cryptography.hazmat.primitives import hashes, serialization
import base64

def encrypt_password(password: str, public_key_pem: str) -> str:
    public_key = serialization.load_pem_public_key(public_key_pem.encode())
    encrypted = public_key.encrypt(
        password.encode('utf-8'),
        padding.OAEP(mgf=padding.MGF1(algorithm=hashes.SHA256()), algorithm=hashes.SHA256(), label=None)
    )
    return base64.b64encode(encrypted).decode('utf-8')
```

**Step 3: Send the encrypted password**

Use the `encryptedPassword` field instead of `password`:

```bash
curl -X POST "https://api.appmanager.com/AuthSvc/login" \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: ak_live_your_api_key_here" \
  -H "X-Api-Secret: your_api_secret_here" \
  -d '{
    "email": "user@example.com",
    "encryptedPassword": "base64_encoded_rsa_encrypted_password..."
  }'
```

> **Breaking Change (v1.2):** Plain text `password` fields are no longer accepted. All password-accepting endpoints (`register`, `login`, `reset-password`, `change-password`) require RSA-encrypted passwords. Requests with plain text passwords will receive a `400 VALIDATION_ERROR`.

### 2.5 Authentication Error Responses

| Error Code | HTTP Status | Description |
|------------|-------------|-------------|
| `UNAUTHORIZED` | 401 | Missing or invalid access token |
| `INVALID_CREDENTIALS` | 401 | Invalid email or password |
| `ACCOUNT_LOCKED` | 423 | Account locked due to too many failed attempts |
| `ACCOUNT_DISABLED` | 403 | Account has been deactivated |
| `EXPIRED_REFRESH_TOKEN` | 401 | Refresh token has expired |
| `REVOKED_REFRESH_TOKEN` | 401 | Refresh token has been revoked |
| `INVALID_REFRESH_TOKEN` | 401 | Refresh token is malformed or unknown |
| `INVALID_RESET_TOKEN` | 400 | Password-reset token is invalid or expired |
| `INVALID_PASSWORD` | 400 | New password does not meet complexity rules |
| `DECRYPTION_FAILED` | 400 | Server could not RSA-decrypt the submitted `encrypted*Password` field (wrong public key, padding, or corrupted base64) |
| `APPLICATION_ID_REQUIRED` | 400 | Endpoint needs an ApplicationId and none was provided (no `X-Api-Key`, no body `applicationId` / query `aApplicationId`) |
| `APP_ID_MISMATCH` | 400 / 401 / 403 | Caller's resolved ApplicationId does not match the resource's / token's ApplicationId. 400 when body and API-key disagree (register, reset-password); 401 on `/AuthSvc/refresh` when the refresh token was issued for a different app; 403 on per-resource lookups (issue, license, etc.) |
| `NO_APP_ACCESS` | 403 | JWT-authenticated user has no `UserApplicationRole` row for the calling application (returned by `GET /UserSvc/profile` when an app context is resolved) |

---

## 3. API Reference

### 3.1 Auth Service (AuthSvc)

Base path: `/AuthSvc`

#### GET /AuthSvc/public-key

Returns the server's RSA public key for client-side password encryption. No authentication required.

**Response:**
```json
{
  "success": true,
  "data": {
    "publicKey": "-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkqhki...\n-----END PUBLIC KEY-----",
    "algorithm": "RSA-OAEP-256",
    "encoding": "base64"
  },
  "message": "Use this public key to encrypt passwords before sending"
}
```

> Cache this key in your application. It only changes if the server's encryption keys are rotated.

#### POST /AuthSvc/register

Registers a new user and associates them with an application. ApplicationId is required (via API key header or request body).

**Request Body:**
```json
{
  "email": "user@example.com",
  "encryptedPassword": "base64_rsa_encrypted_password...",
  "firstName": "John",
  "lastName": "Doe",
  "mobileNumber": "+919876543210",
  "applicationId": 1,
  "applicationRoleCode": "User"
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `email` | Yes | User's email address |
| `encryptedPassword` | Yes | RSA-encrypted password (base64). Encrypt with server's public key using RSA-OAEP-SHA256 |
| `firstName` | Yes | User's first name |
| `lastName` | Yes | User's last name |
| `mobileNumber` | No | User's mobile number |
| `applicationId` | Yes* | Application to register under (*can be provided via X-Api-Key header instead) |
| `applicationRoleCode` | No | Application role to assign (defaults to application's default role) |

**Password Requirements:**
- Minimum 8 characters
- At least 1 uppercase letter
- At least 1 number
- At least 1 special character

**Response:**
```json
{
  "success": true,
  "data": {
    "userId": 123,
    "email": "user@example.com",
    "firstName": "John",
    "lastName": "Doe",
    "applicationRole": "User",
    "appManagerRole": "ApplicationUser",
    "isEmailVerified": false,
    "accessToken": "eyJhbGciOiJIUzI1NiIs...",
    "refreshToken": "rt_abc123xyz789...",
    "tokenExpiresAt": "2026-01-26T14:00:00Z"
  },
  "message": "Registration successful"
}
```

#### POST /AuthSvc/login

Authenticates a user and returns JWT tokens. When applicationId is provided (via API key or request body), the active license and application role are scoped to that specific application.

**Request Body:**
```json
{
  "email": "user@example.com",
  "encryptedPassword": "base64_rsa_encrypted_password...",
  "applicationId": 1
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `email` | Yes | User's email address |
| `encryptedPassword` | Yes | RSA-encrypted password (base64). Encrypt with server's public key using RSA-OAEP-SHA256 |
| `applicationId` | No* | Scopes license and role to this application (*can be provided via X-Api-Key header) |

**Response:**
```json
{
  "success": true,
  "data": {
    "userId": 123,
    "email": "user@example.com",
    "firstName": "John",
    "lastName": "Doe",
    "applicationRole": "User",
    "appManagerRole": "ApplicationUser",
    "isEmailVerified": true,
    "accessToken": "eyJhbGciOiJIUzI1NiIs...",
    "refreshToken": "rt_abc123xyz789...",
    "tokenExpiresAt": "2026-01-26T14:00:00Z",
    "activeLicense": {
      "licenseId": 1,
      "licenseName": "Professional",
      "status": "Active",
      "applicationId": 1,
      "applicationName": "My App",
      "expiryDate": "2027-01-26T00:00:00Z",
      "daysRemaining": 365
    }
  },
  "message": "Login successful"
}
```

#### POST /AuthSvc/refresh

Refreshes an access token using a refresh token. Anonymous (no Authorization header needed).

**Request Body:**
```json
{
  "refreshToken": "rt_abc123xyz789..."
}
```

**Response:**
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGciOiJIUzI1NiIs...",
    "refreshToken": "rt_new_token_xyz...",
    "expiresAt": "2026-01-26T15:00:00Z"
  },
  "message": "Token refreshed successfully"
}
```

> **ApplicationId scoping:** the refresh token stores the ApplicationId it was issued for (`RefreshTokens.ApplicationId`). If the caller supplies an ApplicationId (via `X-Api-Key` header) and it does not match the token's ApplicationId, the request is rejected with `401 APP_ID_MISMATCH`. Legacy tokens with a NULL `ApplicationId` (pre-migration-015) are accepted for backwards compatibility. Callers without any resolvable ApplicationId also pass this check (loose mode).

#### POST /AuthSvc/validate

Validates an access token and returns user information. Anonymous.

**Request Body:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs..."
}
```

**Response:**
```json
{
  "success": true,
  "data": {
    "isValid": true,
    "userId": 123,
    "email": "user@example.com",
    "appManagerRole": "ApplicationUser",
    "expiresAt": "2026-01-26T14:00:00Z"
  }
}
```

> **ApplicationId scoping:** if the JWT carries an `applicationId` claim and the caller is resolvable to a different application (via `X-Api-Key`), the response reports `isValid: false` with `invalidReason: "APP_ID_MISMATCH: token issued for a different application"`. The HTTP status stays 200 (the endpoint never throws on validation failures). If either side has no ApplicationId context, the check is skipped.

#### POST /AuthSvc/logout

Logs out the user. Requires JWT (Authorization: Bearer).

**Request Body:**
```json
{
  "refreshToken": "rt_abc123xyz789...",
  "logoutAllDevices": false
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `refreshToken` | No | Specific token to revoke when no ApplicationId and no `logoutAllDevices` |
| `logoutAllDevices` | No | When `true`, revokes every refresh token for the user across all apps |

> **ApplicationId scoping:** behaviour depends on `logoutAllDevices` and whether an ApplicationId is resolvable:
> - `logoutAllDevices: true` -> revokes every refresh token for the user (user-wide, no app scoping).
> - Otherwise, if `X-Api-Key` / explicit ApplicationId resolves -> revokes only tokens for that user + that ApplicationId (per-app session logout).
> - Otherwise, if a `refreshToken` is in the body -> that specific token is revoked.
> - Otherwise falls back to user-wide revocation.

#### POST /AuthSvc/forgot-password

Initiates a password reset request. Anonymous. Always responds success to prevent email enumeration.

**Request Body:**
```json
{
  "email": "user@example.com"
}
```

> **ApplicationId scoping:** an ApplicationId is **required** (via `X-Api-Key` header) — `APPLICATION_ID_REQUIRED` (400) is returned if missing. The reset token is stamped with this ApplicationId (`PasswordResetTokens.ApplicationId`) so the corresponding `/AuthSvc/reset-password` call can enforce tenant isolation. The reset-email template is also selected per-app.

#### POST /AuthSvc/reset-password

Resets a user's password using a reset token. Anonymous.

**Request Body:**
```json
{
  "token": "reset_token_from_email",
  "encryptedNewPassword": "base64_rsa_encrypted_password..."
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `token` | Yes | Reset token from the password-reset email |
| `encryptedNewPassword` | Yes | RSA-encrypted new password (base64, RSA-OAEP-SHA256) |

> **ApplicationId scoping:** an ApplicationId is **required** (via `X-Api-Key` header) — `APPLICATION_ID_REQUIRED` (400) is returned if missing. The server compares the token's stored `ApplicationId` to the caller's resolved ApplicationId. A mismatch is rejected with `400 APP_ID_MISMATCH` (separate from `INVALID_RESET_TOKEN` so the client can distinguish a wrong-tenant mistake from a stale token). Legacy tokens with NULL `ApplicationId` are accepted.

---

### 3.2 License Service (LicenseSvc)

Base path: `/LicenseSvc`

#### GET /LicenseSvc/types

Gets available license types for purchase. No authentication required. ApplicationId is required.

**Query Parameters:**
- `aApplicationId` (required*): The application ID (*can be provided via X-Api-Key header instead)
- `aCurrency` (optional): Filter pricing by currency code (e.g., "USD", "INR")

**Response:**
```json
{
  "success": true,
  "data": [
    {
      "licenseTypeId": 1,
      "typeName": "Professional",
      "typeCode": "PRO",
      "description": "Full featured license for professionals",
      "licenseModel": "Subscription",
      "maxDevices": 3,
      "durationDays": 365,
      "quantity": null,
      "pricing": [
        {
          "currencyCode": "USD",
          "amount": 99.99,
          "formattedPrice": "$99.99"
        },
        {
          "currencyCode": "INR",
          "amount": 7999.00,
          "formattedPrice": "Rs.7999.00"
        }
      ]
    }
  ],
  "message": "Retrieved 3 license types"
}
```

#### GET /LicenseSvc

Gets the current user's licenses. Requires authentication. Optionally scoped to an application.

**Query Parameters:**
- `aApplicationId` (optional*): Filter licenses by application (*can be provided via X-Api-Key header)

**Response:**
```json
{
  "success": true,
  "data": [
    {
      "licenseId": 1,
      "licenseKey": "LIC-ABC123-XYZ789",
      "licenseName": "Professional",
      "licenseModel": "Subscription",
      "status": "Active",
      "applicationId": 1,
      "applicationName": "My App",
      "purchaseDate": "2026-01-01T00:00:00Z",
      "activationDate": "2026-01-01T00:00:00Z",
      "expiryDate": "2027-01-01T00:00:00Z",
      "daysRemaining": 365,
      "remainingQuantity": null
    }
  ],
  "message": "Retrieved 1 licenses"
}
```

#### POST /LicenseSvc/validate

Validates the current user's license for a specific application. Requires authentication. ApplicationId is required.

**Query Parameters:**
- `aApplicationId` (required*): The application to validate license for (*can be provided via X-Api-Key header)

**Response:**
```json
{
  "success": true,
  "data": {
    "isValid": true,
    "license": {
      "licenseId": 1,
      "licenseName": "Professional",
      "status": "Active",
      "expiryDate": "2027-01-01T00:00:00Z",
      "daysRemaining": 365
    }
  }
}
```

#### POST /LicenseSvc/{aLicenseId}/consume

Consumes quantity from a quantity-based license. Requires authentication.

**Request Body:**
```json
{
  "quantity": 1,
  "reference": "export_report_123"
}
```

**Response:**
```json
{
  "success": true,
  "data": {
    "consumedQuantity": 1,
    "remainingQuantity": 99
  },
  "message": "Quantity consumed successfully"
}
```

| Error Code | HTTP | When |
|------------|------|------|
| `LICENSE_NOT_FOUND` | 404 | License ID does not exist |
| `CROSS_APP_LICENSE` | 403 | Caller's app context (from `X-Api-Key`) does not match the license's `ApplicationId` |
| `LICENSE_INACTIVE` | 400 | License status is not `Active` |
| `INVALID_LICENSE_MODEL` | 400 | License is not a Quantity-model license |
| `INSUFFICIENT_QUANTITY` | 400 | Remaining quantity < requested quantity |

> **ApplicationId scoping:** when the caller has a resolvable ApplicationId (via `X-Api-Key`), the license must belong to that application or the request is rejected with `403 CROSS_APP_LICENSE`. User-ownership (`license.UserId == caller`) is also enforced and returns plain `403 Forbidden` if it fails.

#### DELETE /LicenseSvc/{aLicenseId}/devices/{aDeviceId}

Deactivates a device from a license. Requires authentication.

| Error Code | HTTP | When |
|------------|------|------|
| `LICENSE_NOT_FOUND` | 404 | License ID does not exist |
| `CROSS_APP_LICENSE` | 403 | Caller's app context does not match the license's `ApplicationId` |
| `DEVICE_NOT_FOUND` | 404 | Device not registered against this license |

> **ApplicationId scoping:** same as `/consume` — if the caller has a resolvable ApplicationId, the license's `ApplicationId` must match or `403 CROSS_APP_LICENSE` is returned.

---

### 3.3 User Service (UserSvc)

Base path: `/UserSvc`

All endpoints require authentication.

#### GET /UserSvc/profile

Gets the current user's profile.

**Response:**
```json
{
  "success": true,
  "data": {
    "userId": 123,
    "email": "user@example.com",
    "firstName": "John",
    "lastName": "Doe",
    "mobileNumber": "+919876543210",
    "profileImageUrl": "https://...",
    "applicationRole": "User",
    "isEmailVerified": true,
    "isMobileVerified": false,
    "createdDate": "2025-01-01T00:00:00Z"
  }
}
```

> **ApplicationId scoping:** when the caller has a resolvable ApplicationId (via `X-Api-Key`), the returned `applicationRole` is scoped to that application only — the user's roles in other applications are never leaked. If the user has no `UserApplicationRole` row for the calling app, the endpoint returns `403 NO_APP_ACCESS`. When no app context is resolved (internal admin/management paths), the profile is returned without `applicationRole` scoping, preserving legacy user-global behaviour.

#### PUT /UserSvc/profile

Updates the current user's profile.

**Request Body:**
```json
{
  "firstName": "John",
  "lastName": "Smith",
  "mobileNumber": "+919876543211",
  "profileImageUrl": "https://..."
}
```

#### GET /UserSvc/addresses

Gets the current user's addresses.

#### POST /UserSvc/addresses

Creates or updates an address.

**Request Body:**
```json
{
  "addressType": "Primary",
  "addressLine1": "123 Main St",
  "addressLine2": "Apt 4B",
  "city": "Mumbai",
  "state": "Maharashtra",
  "country": "India",
  "postalCode": "400001"
}
```

#### POST /UserSvc/change-password

Changes the current user's password. **Both passwords must be RSA-encrypted** with the server's public key (RSA-OAEP-SHA256) — plaintext fields are rejected.

**Request Body:**
```json
{
  "encryptedCurrentPassword": "base64_rsa_encrypted_current_password...",
  "encryptedNewPassword": "base64_rsa_encrypted_new_password..."
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `encryptedCurrentPassword` | Yes | RSA-encrypted current password. Use `GET /AuthSvc/public-key` to fetch the key |
| `encryptedNewPassword` | Yes | RSA-encrypted new password. Must satisfy the standard password complexity rules (8+ chars, uppercase, digit, special char) |

| Error Code | HTTP | When |
|------------|------|------|
| `VALIDATION_ERROR` | 400 | A required encrypted field is missing |
| `DECRYPTION_FAILED` | 400 | Either encrypted field failed to decrypt (wrong public key / padding / base64) |
| `INVALID_PASSWORD` | 400 | Decrypted new password does not meet complexity rules |
| `INVALID_CURRENT_PASSWORD` | 400 | Decrypted current password does not match the stored hash |

> **Breaking change (v1.3):** the prior `currentPassword` / `newPassword` plaintext fields have been removed. Use the encrypted variants above.

#### POST /UserSvc/data-export

Submits a GDPR data export request.

**Response:**
```json
{
  "success": true,
  "data": {
    "requestId": "abc123",
    "message": "Your data export request has been submitted. You will receive an email when ready.",
    "estimatedCompletionDate": "2026-01-27T00:00:00Z"
  }
}
```

#### POST /UserSvc/delete-request

Submits a GDPR account deletion request. Returns the request ID and estimated completion date (default 7 days).

**Request Body:**
```json
{
  "reason": "No longer using the service",
  "confirmEmail": "user@example.com"
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `confirmEmail` | Yes | Must exactly match the authenticated user's email (case-insensitive) or `EMAIL_MISMATCH` (400) is returned |
| `reason` | No | Free-text reason recorded in the compliance audit log |

---

### 3.4 Feature Service (FeatureSvc)

Base path: `/FeatureSvc`

All endpoints require authentication.

#### GET /FeatureSvc

Gets all features with access status for the current user. ApplicationId is required.

**Query Parameters:**
- `aApplicationId` (required*): The application ID (*can be provided via X-Api-Key header instead)

**Response:**
```json
{
  "success": true,
  "data": [
    {
      "featureCode": "EXPORT_PDF",
      "featureName": "PDF Export",
      "featureType": "Binary",
      "hasAccess": true,
      "source": "license",
      "reason": null
    },
    {
      "featureCode": "API_REQUESTS",
      "featureName": "API Requests",
      "featureType": "Level",
      "hasAccess": true,
      "source": "license",
      "level": 1000,
      "levelDescription": "1000 API requests per month"
    },
    {
      "featureCode": "PREMIUM_SUPPORT",
      "featureName": "Premium Support",
      "featureType": "Binary",
      "hasAccess": false,
      "source": "license",
      "reason": "Feature not included in your license",
      "requiredLicense": "Enterprise"
    }
  ],
  "message": "Retrieved 10 features"
}
```

#### GET /FeatureSvc/{aFeatureCode}

Checks access to a specific feature by code.

**Response:**
```json
{
  "success": true,
  "data": {
    "featureCode": "EXPORT_PDF",
    "featureName": "PDF Export",
    "featureType": "Binary",
    "hasAccess": true,
    "source": "license"
  },
  "message": "Feature access granted"
}
```

#### GET /FeatureSvc/flags/{aFlagCode}

Checks the status of a feature flag.

**Response:**
```json
{
  "success": true,
  "data": {
    "featureCode": "NEW_DASHBOARD",
    "featureName": "New Dashboard",
    "featureType": "Binary",
    "hasAccess": true,
    "source": "featureFlag",
    "flagInfo": {
      "flagName": "New Dashboard Beta",
      "rolloutPercentage": 50
    }
  },
  "message": "Feature flag is enabled"
}
```

---

### 3.5 Payment Service (PaymentSvc)

Base path: `/PaymentSvc`

All endpoints require authentication.

#### GET /PaymentSvc/transactions

Gets the user's transaction history, optionally scoped to an application.

**Query Parameters:**
- `aApplicationId` (optional*): Filter by application (*can be provided via X-Api-Key header)
- `aPage` (optional): Page number (default: 1)
- `aPageSize` (optional): Items per page (default: 20)

**Response:**
```json
{
  "success": true,
  "data": {
    "items": [
      {
        "transactionId": 1,
        "transactionNumber": "TXN-2026-0001",
        "transactionType": "Purchase",
        "amount": 99.99,
        "currencyCode": "USD",
        "status": "Completed",
        "paymentMethod": "Credit Card",
        "transactionDate": "2026-01-01T10:00:00Z",
        "description": "Professional License Purchase"
      }
    ],
    "totalCount": 5,
    "page": 1,
    "pageSize": 20,
    "totalPages": 1
  }
}
```

#### GET /PaymentSvc/transactions/{aTransactionId}

Gets a specific transaction by ID.

| Error Code | HTTP | When |
|------------|------|------|
| `TRANSACTION_NOT_FOUND` | 404 | No transaction with that ID |
| `403 Forbidden` | 403 | Transaction does not belong to the authenticated user |
| `CROSS_APP_RESOURCE` | 403 | Caller's app context (from `X-Api-Key`) does not match the transaction's `ApplicationId` |

> **ApplicationId scoping:** when the caller has a resolvable ApplicationId, the transaction's `ApplicationId` must match or the request is rejected with `403 CROSS_APP_RESOURCE`.

#### GET /PaymentSvc/invoices

Gets the user's invoices, optionally scoped to an application.

**Query Parameters:**
- `aApplicationId` (optional*): Filter by application (*can be provided via X-Api-Key header)
- `aPage` (optional): Page number (default: 1)
- `aPageSize` (optional): Items per page (default: 20)

#### GET /PaymentSvc/invoices/{aInvoiceId}

Gets a specific invoice by ID.

| Error Code | HTTP | When |
|------------|------|------|
| `INVOICE_NOT_FOUND` | 404 | No invoice with that ID |
| `403 Forbidden` | 403 | Invoice does not belong to the authenticated user |
| `CROSS_APP_RESOURCE` | 403 | Caller's app context (from `X-Api-Key`) does not match the invoice's `ApplicationId` |

> **ApplicationId scoping:** when the caller has a resolvable ApplicationId, the invoice's `ApplicationId` must match or the request is rejected with `403 CROSS_APP_RESOURCE`.

#### GET /PaymentSvc/invoices/{aInvoiceId}/download

Downloads an invoice as PDF. Returns `application/pdf` bytes on success, or a JSON error on failure (`INVOICE_NOT_FOUND`, `PDF_GENERATION_FAILED`, or `403 Forbidden` if the invoice does not belong to the caller).

#### GET /PaymentSvc/subscriptions

Gets the user's active subscriptions, optionally scoped to an application.

**Query Parameters:**
- `aApplicationId` (optional*): Filter by application (*can be provided via X-Api-Key header)

**Response:**
```json
{
  "success": true,
  "data": [
    {
      "subscriptionId": 1,
      "planName": "Professional Monthly",
      "status": "Active",
      "billingCycle": "Monthly",
      "amount": 9.99,
      "currencyCode": "USD",
      "startDate": "2026-01-01T00:00:00Z",
      "currentPeriodEnd": "2026-02-01T00:00:00Z",
      "cancelAtPeriodEnd": false,
      "nextBillingDate": "2026-02-01T00:00:00Z"
    }
  ]
}
```

#### POST /PaymentSvc/subscriptions/{aSubscriptionId}/cancel

Cancels a subscription. Requires authentication.

**Request Body (all fields optional):**
```json
{
  "cancelImmediately": false,
  "reason": "No longer needed"
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `cancelImmediately` | No | `false` (default) = cancel at the end of the current billing period; `true` = cancel right now |
| `reason` | No | Free-text cancellation reason stored on the subscription |

| Error Code | HTTP | When |
|------------|------|------|
| `SUBSCRIPTION_NOT_FOUND` | 404 | No subscription with that ID |
| `403 Forbidden` | 403 | Subscription does not belong to the authenticated user |
| `CROSS_APP_RESOURCE` | 403 | Caller's app context does not match the subscription's `ApplicationId` |
| `ALREADY_CANCELLED` | 400 | Subscription is already in `Cancelled` status |

> **ApplicationId scoping:** when the caller has a resolvable ApplicationId, the subscription's `ApplicationId` must match or the request is rejected with `403 CROSS_APP_RESOURCE`.

#### POST /PaymentSvc/promo-codes/validate

Validates a promo code. Anonymous (no JWT required) but an ApplicationId is mandatory because promo codes are application-scoped.

**Request Body:**
```json
{
  "code": "SAVE20"
}
```

**Response:**
```json
{
  "success": true,
  "data": {
    "code": "SAVE20",
    "discountType": "Percentage",
    "discountValue": 20,
    "description": "20% off your first purchase",
    "expiryDate": "2026-12-31T23:59:59Z"
  },
  "message": "Promo code is valid"
}
```

| Error Code | HTTP | When |
|------------|------|------|
| `VALIDATION_ERROR` | 400 | `code` missing |
| `APPLICATION_ID_REQUIRED` | 400 | Caller did not provide an ApplicationId via `X-Api-Key` or otherwise |
| `PROMO_CODE_NOT_FOUND` | 404 | Code does not exist |
| `PROMO_CODE_NOT_VALID_FOR_APPLICATION` | 400 | Code is app-scoped to a different application than the caller's |
| `PROMO_CODE_INACTIVE` | 400 | Code is disabled |
| `PROMO_CODE_EXPIRED` | 400 | `ValidTo` has passed |
| `PROMO_CODE_EXHAUSTED` | 400 | `CurrentUses >= MaxUses` |

> **ApplicationId scoping:** the endpoint requires a resolvable ApplicationId. Globally-scoped promo codes (stored `ApplicationId == null`) are valid for every app; app-scoped codes must match the caller's ApplicationId, otherwise `PROMO_CODE_NOT_VALID_FOR_APPLICATION` (400, business-logic error — not 403) is returned.

---

### 3.6 Issue Service (IssueSvc)

Base path: `/IssueSvc`

All endpoints require authentication.

#### GET /IssueSvc

Gets all issues for the current user, optionally scoped to an application.

**Query Parameters:**
- `aApplicationId` (optional*): Filter by application (*can be provided via X-Api-Key header)
- `aStatus` (optional): Filter by status (Open, InProgress, Resolved, Closed)

**Response:**
```json
{
  "success": true,
  "data": [
    {
      "issueId": 1,
      "issueNumber": "ISS-2026-0001",
      "title": "Cannot export to PDF",
      "description": "When I try to export...",
      "issueType": "Bug",
      "priority": "High",
      "status": "Open",
      "applicationId": 1,
      "applicationName": "My App",
      "createdDate": "2026-01-25T10:00:00Z",
      "updatedDate": "2026-01-25T10:00:00Z",
      "resolvedDate": null
    }
  ],
  "message": "Retrieved 3 issues"
}
```

#### GET /IssueSvc/{aIssueId}

Gets a specific issue with comments.

**Response:**
```json
{
  "success": true,
  "data": {
    "issueId": 1,
    "issueNumber": "ISS-2026-0001",
    "title": "Cannot export to PDF",
    "description": "When I try to export...",
    "issueType": "Bug",
    "priority": "High",
    "status": "Open",
    "applicationId": 1,
    "applicationName": "My App",
    "createdDate": "2026-01-25T10:00:00Z",
    "updatedDate": "2026-01-25T10:00:00Z",
    "comments": [
      {
        "commentId": 1,
        "comment": "We are investigating this issue.",
        "isInternal": false,
        "createdByName": "Support Team",
        "createdDate": "2026-01-25T11:00:00Z"
      }
    ]
  }
}
```

| Error Code | HTTP | When |
|------------|------|------|
| `ISSUE_NOT_FOUND` | 404 | No issue with that ID |
| `APP_ID_MISMATCH` | 403 | Caller's app context does not match the issue's `ApplicationId` |
| `403 Forbidden` | 403 | Issue was not reported by the authenticated user |

> **ApplicationId scoping:** when the caller has a resolvable ApplicationId, the issue's `ApplicationId` must match or `403 APP_ID_MISMATCH` is returned. User-ownership (`issue.ReportedByUserId == caller`) is also enforced.

#### POST /IssueSvc

Creates a new support issue. ApplicationId is required.

**Request Body:**
```json
{
  "applicationId": 1,
  "title": "Cannot export to PDF",
  "description": "When I try to export my report to PDF, I get an error message saying 'Export failed'.",
  "type": "Bug",
  "priority": "High"
}
```

**Response:**
```json
{
  "success": true,
  "data": {
    "issueId": 5,
    "issueNumber": "ISS-2026-0005",
    "title": "Cannot export to PDF",
    "status": "Open",
    "createdDate": "2026-01-26T12:00:00Z"
  },
  "message": "Issue created successfully"
}
```

#### POST /IssueSvc/{aIssueId}/comments

Adds a comment to an issue. Public (non-internal) comments only; internal-team comments are not exposed through the external API.

**Request Body:**
```json
{
  "comment": "I have attached a screenshot of the error."
}
```

| Error Code | HTTP | When |
|------------|------|------|
| `VALIDATION_ERROR` | 400 | `comment` missing |
| `ISSUE_NOT_FOUND` | 404 | No issue with that ID |
| `APP_ID_MISMATCH` | 403 | Caller's app context does not match the issue's `ApplicationId` |
| `403 Forbidden` | 403 | Issue was not reported by the authenticated user |

> **ApplicationId scoping:** same as `GET /IssueSvc/{aIssueId}` — the issue's `ApplicationId` must match the caller's resolved ApplicationId.

#### POST /IssueSvc/{aIssueId}/close

Closes an issue. No request body.

| Error Code | HTTP | When |
|------------|------|------|
| `ISSUE_NOT_FOUND` | 404 | No issue with that ID |
| `APP_ID_MISMATCH` | 403 | Caller's app context does not match the issue's `ApplicationId` |
| `403 Forbidden` | 403 | Issue was not reported by the authenticated user |
| `STATUS_NOT_FOUND` | 400 | No `Closed` (or `IsFinal`) status is configured for issues |
| `ALREADY_CLOSED` | 400 | Issue is already in the closed status |

> **ApplicationId scoping:** same as above — the issue's `ApplicationId` must match the caller's resolved ApplicationId.

---

## 4. Code Examples (.NET/C#)

This section provides comprehensive .NET/C# code examples for integrating with the App Manager API.

### 4.1 API Client Implementation

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppManager.Client;

/// <summary>
/// HTTP client for interacting with the App Manager API.
/// Handles authentication, token refresh, and all API operations.
/// </summary>
public class AppManagerClient : IDisposable
{
    private readonly HttpClient objHttpClient;
    private readonly JsonSerializerOptions objJsonOptions;
    private string? objAccessToken;
    private string? objRefreshToken;
    private DateTime? objTokenExpiresAt;
    private string? objRsaPublicKey;

    public AppManagerClient(string aBaseUrl = "https://api.appmanager.com")
    {
        objHttpClient = new HttpClient { BaseAddress = new Uri(aBaseUrl) };
        objHttpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        objJsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public bool IsAuthenticated => !string.IsNullOrEmpty(objAccessToken);

    #region Password Encryption

    /// <summary>
    /// Fetches and caches the server's RSA public key for password encryption.
    /// </summary>
    private async Task EnsurePublicKeyAsync()
    {
        if (!string.IsNullOrEmpty(objRsaPublicKey)) return;

        var vResponse = await objHttpClient.GetAsync("/AuthSvc/public-key");
        var vResult = await vResponse.Content.ReadFromJsonAsync<ApiResponse<PublicKeyData>>(objJsonOptions);
        objRsaPublicKey = vResult?.Data?.PublicKey
            ?? throw new AppManagerException("KEY_FETCH_FAILED", "Failed to fetch server public key");
    }

    /// <summary>
    /// Encrypts a password using the server's RSA public key (RSA-OAEP-SHA256).
    /// </summary>
    private string EncryptPassword(string aPassword)
    {
        using var vRsa = System.Security.Cryptography.RSA.Create();
        vRsa.ImportFromPem(objRsaPublicKey);
        var vEncryptedBytes = vRsa.Encrypt(
            System.Text.Encoding.UTF8.GetBytes(aPassword),
            System.Security.Cryptography.RSAEncryptionPadding.OaepSHA256);
        return Convert.ToBase64String(vEncryptedBytes);
    }

    #endregion

    #region Authentication

    /// <summary>
    /// Authenticates a user and stores the access and refresh tokens.
    /// Passwords are RSA-encrypted before transmission.
    /// </summary>
    public async Task<AuthResponseData> LoginAsync(string aEmail, string aPassword)
    {
        await EnsurePublicKeyAsync();
        var vEncryptedPassword = EncryptPassword(aPassword);

        var vResponse = await objHttpClient.PostAsJsonAsync("/AuthSvc/login",
            new { email = aEmail, encryptedPassword = vEncryptedPassword }, objJsonOptions);

        var vResult = await vResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponseData>>(objJsonOptions);

        if (vResult?.Success == true && vResult.Data != null)
        {
            SetTokens(vResult.Data.AccessToken, vResult.Data.RefreshToken, vResult.Data.TokenExpiresAt);
            return vResult.Data;
        }

        throw new AppManagerException(vResult?.Error ?? "LOGIN_FAILED", vResult?.Message ?? "Login failed");
    }

    /// <summary>
    /// Registers a new user account.
    /// </summary>
    public async Task<AuthResponseData> RegisterAsync(RegisterRequest aRequest)
    {
        var vResponse = await objHttpClient.PostAsJsonAsync("/AuthSvc/register", aRequest, objJsonOptions);
        var vResult = await vResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponseData>>(objJsonOptions);

        if (vResult?.Success == true && vResult.Data != null)
        {
            SetTokens(vResult.Data.AccessToken, vResult.Data.RefreshToken, vResult.Data.TokenExpiresAt);
            return vResult.Data;
        }

        throw new AppManagerException(vResult?.Error ?? "REGISTER_FAILED", vResult?.Message ?? "Registration failed");
    }

    /// <summary>
    /// Validates the current access token.
    /// </summary>
    public async Task<ValidateTokenResponse> ValidateTokenAsync()
    {
        if (string.IsNullOrEmpty(objAccessToken))
            throw new AppManagerException("NOT_AUTHENTICATED", "Not authenticated");

        var vResponse = await objHttpClient.PostAsJsonAsync("/AuthSvc/validate",
            new { accessToken = objAccessToken }, objJsonOptions);

        var vResult = await vResponse.Content.ReadFromJsonAsync<ApiResponse<ValidateTokenResponse>>(objJsonOptions);
        return vResult?.Data ?? new ValidateTokenResponse { IsValid = false };
    }

    /// <summary>
    /// Logs out the current user.
    /// </summary>
    public async Task LogoutAsync(bool aLogoutAllDevices = false)
    {
        if (!string.IsNullOrEmpty(objRefreshToken))
        {
            await AuthenticatedPostAsync<object>("/AuthSvc/logout",
                new { refreshToken = objRefreshToken, logoutAllDevices = aLogoutAllDevices });
        }

        ClearTokens();
    }

    /// <summary>
    /// Initiates a password reset request.
    /// </summary>
    public async Task ForgotPasswordAsync(string aEmail)
    {
        var vResponse = await objHttpClient.PostAsJsonAsync("/AuthSvc/forgot-password",
            new { email = aEmail }, objJsonOptions);

        var vResult = await vResponse.Content.ReadFromJsonAsync<ApiResponse<object>>(objJsonOptions);

        if (vResult?.Success != true)
            throw new AppManagerException(vResult?.Error ?? "FORGOT_PASSWORD_FAILED", vResult?.Message ?? "Request failed");
    }

    /// <summary>
    /// Resets a user's password using a reset token.
    /// Password is RSA-encrypted before transmission.
    /// </summary>
    public async Task ResetPasswordAsync(string aToken, string aNewPassword)
    {
        await EnsurePublicKeyAsync();
        var vEncryptedNewPassword = EncryptPassword(aNewPassword);

        var vResponse = await objHttpClient.PostAsJsonAsync("/AuthSvc/reset-password",
            new { token = aToken, encryptedNewPassword = vEncryptedNewPassword }, objJsonOptions);

        var vResult = await vResponse.Content.ReadFromJsonAsync<ApiResponse<object>>(objJsonOptions);

        if (vResult?.Success != true)
            throw new AppManagerException(vResult?.Error ?? "RESET_PASSWORD_FAILED", vResult?.Message ?? "Reset failed");
    }

    #endregion

    #region License Operations

    /// <summary>
    /// Gets available license types (no authentication required).
    /// </summary>
    public async Task<List<LicenseTypeDto>> GetLicenseTypesAsync(string? aCurrency = null)
    {
        var vUrl = "/LicenseSvc/types";
        if (!string.IsNullOrEmpty(aCurrency))
            vUrl += $"?aCurrency={aCurrency}";

        var vResponse = await objHttpClient.GetAsync(vUrl);
        var vResult = await vResponse.Content.ReadFromJsonAsync<ApiResponse<List<LicenseTypeDto>>>(objJsonOptions);
        return vResult?.Data ?? new List<LicenseTypeDto>();
    }

    /// <summary>
    /// Gets the current user's licenses.
    /// </summary>
    public async Task<List<LicenseResponse>> GetLicensesAsync()
    {
        return await AuthenticatedGetAsync<List<LicenseResponse>>("/LicenseSvc") ?? new List<LicenseResponse>();
    }

    /// <summary>
    /// Validates the current user's license.
    /// </summary>
    public async Task<LicenseValidationResponse> ValidateLicenseAsync()
    {
        return await AuthenticatedPostAsync<LicenseValidationResponse>("/LicenseSvc/validate")
            ?? new LicenseValidationResponse { IsValid = false };
    }

    /// <summary>
    /// Consumes quantity from a quantity-based license.
    /// </summary>
    public async Task<ConsumeQuantityResponse> ConsumeQuantityAsync(int aLicenseId, int aQuantity, string? aReference = null)
    {
        return await AuthenticatedPostAsync<ConsumeQuantityResponse>(
            $"/LicenseSvc/{aLicenseId}/consume",
            new { quantity = aQuantity, reference = aReference })
            ?? throw new AppManagerException("CONSUME_FAILED", "Failed to consume quantity");
    }

    /// <summary>
    /// Deactivates a device from a license.
    /// </summary>
    public async Task DeactivateDeviceAsync(int aLicenseId, int aDeviceId)
    {
        await AuthenticatedDeleteAsync($"/LicenseSvc/{aLicenseId}/devices/{aDeviceId}");
    }

    #endregion

    #region User Operations

    /// <summary>
    /// Gets the current user's profile. When the HttpClient is configured to send the
    /// X-Api-Key / X-Api-Secret headers (recommended), the server scopes the returned
    /// <see cref="UserProfileResponse.ApplicationRole"/> to the calling app and returns
    /// <c>NO_APP_ACCESS</c> (403) if the user has no role for that app. Without API-key
    /// headers, the server returns the user's global profile (no applicationRole scoping).
    /// </summary>
    public async Task<UserProfileResponse> GetProfileAsync()
    {
        return await AuthenticatedGetAsync<UserProfileResponse>("/UserSvc/profile")
            ?? throw new AppManagerException("PROFILE_NOT_FOUND", "Profile not found");
    }

    /// <summary>
    /// Updates the current user's profile.
    /// </summary>
    public async Task UpdateProfileAsync(UpdateProfileRequest aRequest)
    {
        await AuthenticatedPutAsync("/UserSvc/profile", aRequest);
    }

    /// <summary>
    /// Gets the current user's addresses.
    /// </summary>
    public async Task<List<AddressResponse>> GetAddressesAsync()
    {
        return await AuthenticatedGetAsync<List<AddressResponse>>("/UserSvc/addresses")
            ?? new List<AddressResponse>();
    }

    /// <summary>
    /// Creates or updates an address.
    /// </summary>
    public async Task<AddressResponse> SaveAddressAsync(UpdateAddressRequest aRequest)
    {
        return await AuthenticatedPostAsync<AddressResponse>("/UserSvc/addresses", aRequest)
            ?? throw new AppManagerException("ADDRESS_FAILED", "Failed to save address");
    }

    /// <summary>
    /// Changes the current user's password. Both passwords are RSA-encrypted
    /// with the server's public key before transmission (v1.3+ requirement).
    /// </summary>
    public async Task ChangePasswordAsync(string aCurrentPassword, string aNewPassword)
    {
        await EnsurePublicKeyAsync();
        var vEncryptedCurrentPassword = EncryptPassword(aCurrentPassword);
        var vEncryptedNewPassword = EncryptPassword(aNewPassword);

        await AuthenticatedPostAsync<object>("/UserSvc/change-password",
            new { encryptedCurrentPassword = vEncryptedCurrentPassword, encryptedNewPassword = vEncryptedNewPassword });
    }

    /// <summary>
    /// Submits a GDPR data export request.
    /// </summary>
    public async Task<DataExportResponse> RequestDataExportAsync()
    {
        return await AuthenticatedPostAsync<DataExportResponse>("/UserSvc/data-export")
            ?? throw new AppManagerException("EXPORT_FAILED", "Failed to request data export");
    }

    /// <summary>
    /// Submits a GDPR account deletion request.
    /// </summary>
    public async Task RequestAccountDeletionAsync(string aConfirmEmail)
    {
        await AuthenticatedPostAsync<object>("/UserSvc/delete-request",
            new { confirmEmail = aConfirmEmail });
    }

    #endregion

    #region Feature Operations

    /// <summary>
    /// Gets all features with access status for the current user.
    /// </summary>
    public async Task<List<FeatureAccessResponse>> GetFeaturesAsync(int? aApplicationId = null)
    {
        var vUrl = "/FeatureSvc";
        if (aApplicationId.HasValue)
            vUrl += $"?aApplicationId={aApplicationId.Value}";

        return await AuthenticatedGetAsync<List<FeatureAccessResponse>>(vUrl)
            ?? new List<FeatureAccessResponse>();
    }

    /// <summary>
    /// Checks access to a specific feature by code.
    /// </summary>
    public async Task<FeatureAccessResponse> CheckFeatureAccessAsync(string aFeatureCode)
    {
        return await AuthenticatedGetAsync<FeatureAccessResponse>($"/FeatureSvc/{aFeatureCode}")
            ?? new FeatureAccessResponse { FeatureCode = aFeatureCode, HasAccess = false };
    }

    /// <summary>
    /// Checks the status of a feature flag.
    /// </summary>
    public async Task<FeatureAccessResponse> CheckFeatureFlagAsync(string aFlagCode)
    {
        return await AuthenticatedGetAsync<FeatureAccessResponse>($"/FeatureSvc/flags/{aFlagCode}")
            ?? new FeatureAccessResponse { FeatureCode = aFlagCode, HasAccess = false };
    }

    #endregion

    #region Payment Operations

    /// <summary>
    /// Gets the user's transaction history.
    /// </summary>
    public async Task<PagedResult<TransactionResponse>> GetTransactionsAsync(int aPage = 1, int aPageSize = 20)
    {
        return await AuthenticatedGetAsync<PagedResult<TransactionResponse>>(
            $"/PaymentSvc/transactions?aPage={aPage}&aPageSize={aPageSize}")
            ?? new PagedResult<TransactionResponse>();
    }

    /// <summary>
    /// Gets a specific transaction by ID.
    /// </summary>
    public async Task<TransactionResponse> GetTransactionAsync(int aTransactionId)
    {
        return await AuthenticatedGetAsync<TransactionResponse>($"/PaymentSvc/transactions/{aTransactionId}")
            ?? throw new AppManagerException("TRANSACTION_NOT_FOUND", "Transaction not found");
    }

    /// <summary>
    /// Gets the user's invoices.
    /// </summary>
    public async Task<PagedResult<InvoiceResponse>> GetInvoicesAsync(int aPage = 1, int aPageSize = 20)
    {
        return await AuthenticatedGetAsync<PagedResult<InvoiceResponse>>(
            $"/PaymentSvc/invoices?aPage={aPage}&aPageSize={aPageSize}")
            ?? new PagedResult<InvoiceResponse>();
    }

    /// <summary>
    /// Gets a specific invoice by ID.
    /// </summary>
    public async Task<InvoiceDetailResponse> GetInvoiceAsync(int aInvoiceId)
    {
        return await AuthenticatedGetAsync<InvoiceDetailResponse>($"/PaymentSvc/invoices/{aInvoiceId}")
            ?? throw new AppManagerException("INVOICE_NOT_FOUND", "Invoice not found");
    }

    /// <summary>
    /// Downloads an invoice as PDF.
    /// </summary>
    public async Task<byte[]> DownloadInvoiceAsync(int aInvoiceId)
    {
        await EnsureAuthenticatedAsync();
        var vResponse = await objHttpClient.GetAsync($"/PaymentSvc/invoices/{aInvoiceId}/download");

        if (!vResponse.IsSuccessStatusCode)
            throw new AppManagerException("DOWNLOAD_FAILED", "Failed to download invoice");

        return await vResponse.Content.ReadAsByteArrayAsync();
    }

    /// <summary>
    /// Gets the user's active subscriptions.
    /// </summary>
    public async Task<List<SubscriptionResponse>> GetSubscriptionsAsync()
    {
        return await AuthenticatedGetAsync<List<SubscriptionResponse>>("/PaymentSvc/subscriptions")
            ?? new List<SubscriptionResponse>();
    }

    /// <summary>
    /// Cancels a subscription.
    /// </summary>
    public async Task CancelSubscriptionAsync(int aSubscriptionId, bool aCancelImmediately = false, string? aReason = null)
    {
        await AuthenticatedPostAsync<object>($"/PaymentSvc/subscriptions/{aSubscriptionId}/cancel",
            new { cancelImmediately = aCancelImmediately, reason = aReason });
    }

    /// <summary>
    /// Validates a promo code.
    /// </summary>
    public async Task<PromoCodeResponse> ValidatePromoCodeAsync(string aCode)
    {
        return await AuthenticatedPostAsync<PromoCodeResponse>("/PaymentSvc/promo-codes/validate",
            new { code = aCode })
            ?? throw new AppManagerException("PROMO_CODE_INVALID", "Invalid promo code");
    }

    #endregion

    #region Issue Operations

    /// <summary>
    /// Gets all issues for the current user.
    /// </summary>
    public async Task<List<IssueResponse>> GetIssuesAsync(int? aApplicationId = null, string? aStatus = null)
    {
        var vUrl = "/IssueSvc";
        var vQueryParams = new List<string>();

        if (aApplicationId.HasValue)
            vQueryParams.Add($"aApplicationId={aApplicationId.Value}");
        if (!string.IsNullOrEmpty(aStatus))
            vQueryParams.Add($"aStatus={aStatus}");

        if (vQueryParams.Count > 0)
            vUrl += "?" + string.Join("&", vQueryParams);

        return await AuthenticatedGetAsync<List<IssueResponse>>(vUrl)
            ?? new List<IssueResponse>();
    }

    /// <summary>
    /// Gets a specific issue by ID with comments.
    /// </summary>
    public async Task<IssueDetailResponse> GetIssueAsync(int aIssueId)
    {
        return await AuthenticatedGetAsync<IssueDetailResponse>($"/IssueSvc/{aIssueId}")
            ?? throw new AppManagerException("ISSUE_NOT_FOUND", "Issue not found");
    }

    /// <summary>
    /// Creates a new support issue.
    /// </summary>
    public async Task<IssueResponse> CreateIssueAsync(CreateIssueRequest aRequest)
    {
        return await AuthenticatedPostAsync<IssueResponse>("/IssueSvc", aRequest)
            ?? throw new AppManagerException("CREATE_FAILED", "Failed to create issue");
    }

    /// <summary>
    /// Adds a comment to an issue.
    /// </summary>
    public async Task AddCommentAsync(int aIssueId, string aComment)
    {
        await AuthenticatedPostAsync<object>($"/IssueSvc/{aIssueId}/comments",
            new { comment = aComment });
    }

    /// <summary>
    /// Closes an issue.
    /// </summary>
    public async Task CloseIssueAsync(int aIssueId)
    {
        await AuthenticatedPostAsync<object>($"/IssueSvc/{aIssueId}/close");
    }

    #endregion

    #region Private Methods

    private void SetTokens(string? aAccess, string? aRefresh, DateTime? aExpiresAt)
    {
        objAccessToken = aAccess;
        objRefreshToken = aRefresh;
        objTokenExpiresAt = aExpiresAt;
        SetAuthHeader();
    }

    private void ClearTokens()
    {
        objAccessToken = null;
        objRefreshToken = null;
        objTokenExpiresAt = null;
        objHttpClient.DefaultRequestHeaders.Authorization = null;
    }

    private void SetAuthHeader()
    {
        if (!string.IsNullOrEmpty(objAccessToken))
        {
            objHttpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", objAccessToken);
        }
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (string.IsNullOrEmpty(objAccessToken))
            throw new AppManagerException("NOT_AUTHENTICATED", "Not authenticated");

        // Check if token is about to expire and refresh if needed
        if (objTokenExpiresAt.HasValue && objTokenExpiresAt.Value <= DateTime.UtcNow.AddMinutes(5))
        {
            await RefreshTokensAsync();
        }
    }

    private async Task RefreshTokensAsync()
    {
        if (string.IsNullOrEmpty(objRefreshToken))
            throw new AppManagerException("NO_REFRESH_TOKEN", "No refresh token available");

        var vResponse = await objHttpClient.PostAsJsonAsync("/AuthSvc/refresh",
            new { refreshToken = objRefreshToken }, objJsonOptions);

        var vResult = await vResponse.Content.ReadFromJsonAsync<ApiResponse<TokenRefreshResponse>>(objJsonOptions);

        if (vResult?.Success == true && vResult.Data != null)
        {
            SetTokens(vResult.Data.AccessToken, vResult.Data.RefreshToken, vResult.Data.ExpiresAt);
        }
        else
        {
            ClearTokens();
            throw new AppManagerException("SESSION_EXPIRED", "Session expired. Please login again.");
        }
    }

    private async Task<T?> AuthenticatedGetAsync<T>(string aPath)
    {
        await EnsureAuthenticatedAsync();
        var vResponse = await objHttpClient.GetAsync(aPath);
        return await HandleResponseAsync<T>(vResponse);
    }

    private async Task<T?> AuthenticatedPostAsync<T>(string aPath, object? aBody = null)
    {
        await EnsureAuthenticatedAsync();
        var vResponse = await objHttpClient.PostAsJsonAsync(aPath, aBody ?? new { }, objJsonOptions);
        return await HandleResponseAsync<T>(vResponse);
    }

    private async Task AuthenticatedPutAsync(string aPath, object aBody)
    {
        await EnsureAuthenticatedAsync();
        var vResponse = await objHttpClient.PutAsJsonAsync(aPath, aBody, objJsonOptions);
        await HandleResponseAsync<object>(vResponse);
    }

    private async Task AuthenticatedDeleteAsync(string aPath)
    {
        await EnsureAuthenticatedAsync();
        var vResponse = await objHttpClient.DeleteAsync(aPath);
        await HandleResponseAsync<object>(vResponse);
    }

    private async Task<T?> HandleResponseAsync<T>(HttpResponseMessage aResponse)
    {
        if (aResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(objRefreshToken))
        {
            await RefreshTokensAsync();
            // Note: In production, you would want to retry the original request here
        }

        var vResult = await aResponse.Content.ReadFromJsonAsync<ApiResponse<T>>(objJsonOptions);

        if (vResult?.Success != true && !string.IsNullOrEmpty(vResult?.Error))
        {
            throw new AppManagerException(vResult.Error, vResult.Message ?? "Request failed");
        }

        return vResult != null ? vResult.Data : default;
    }

    public void Dispose() => objHttpClient.Dispose();

    #endregion
}
```

### 4.2 Model Classes

```csharp
namespace AppManager.Client;

// API Response wrapper
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public int? StatusCode { get; set; }
}

// Paginated results
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

// Authentication & Encryption
public class PublicKeyData
{
    public string PublicKey { get; set; } = string.Empty;
    public string Algorithm { get; set; } = string.Empty;
    public string Encoding { get; set; } = string.Empty;
}

public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    // RSA-OAEP-SHA256 encrypted, base64. Fetch the key from GET /AuthSvc/public-key.
    public string EncryptedPassword { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? MobileNumber { get; set; }
    // Either supply ApplicationId here or send X-Api-Key headers; one is required.
    public int? ApplicationId { get; set; }
    public string? ApplicationRoleCode { get; set; }
}

public class ChangePasswordRequest
{
    // Both fields RSA-OAEP-SHA256 encrypted, base64.
    public string EncryptedCurrentPassword { get; set; } = string.Empty;
    public string EncryptedNewPassword { get; set; } = string.Empty;
}

public class AuthResponseData
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? ApplicationRole { get; set; }
    public string? AppManagerRole { get; set; }
    public bool IsEmailVerified { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
    public LicenseInfo? ActiveLicense { get; set; }
}

public class TokenRefreshResponse
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class ValidateTokenResponse
{
    public bool IsValid { get; set; }
    public int? UserId { get; set; }
    public string? Email { get; set; }
    public string? AppManagerRole { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

// License
public class LicenseTypeDto
{
    public int LicenseTypeId { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public string TypeCode { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? LicenseModel { get; set; }
    public int? MaxDevices { get; set; }
    public int? DurationDays { get; set; }
    public int? Quantity { get; set; }
    public List<PricingDto> Pricing { get; set; } = new();
}

public class PricingDto
{
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? FormattedPrice { get; set; }
}

public class LicenseResponse
{
    public int LicenseId { get; set; }
    public string? LicenseKey { get; set; }
    public string? LicenseName { get; set; }
    public string? LicenseModel { get; set; }
    public string? Status { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public DateTime? ActivationDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? DaysRemaining { get; set; }
    public int? RemainingQuantity { get; set; }
}

public class LicenseInfo
{
    public int LicenseId { get; set; }
    public string? LicenseName { get; set; }
    public string? Status { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? DaysRemaining { get; set; }
}

public class LicenseValidationResponse
{
    public bool IsValid { get; set; }
    public LicenseInfo? License { get; set; }
}

public class ConsumeQuantityResponse
{
    public int ConsumedQuantity { get; set; }
    public int RemainingQuantity { get; set; }
}

// User
public class UserProfileResponse
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? MobileNumber { get; set; }
    public string? ProfileImageUrl { get; set; }
    // Scoped to the calling application when X-Api-Key is provided.
    // Empty/absent when the server cannot resolve an app context.
    public string? ApplicationRole { get; set; }
    public bool IsEmailVerified { get; set; }
    public bool IsMobileVerified { get; set; }
    public DateTime CreatedDate { get; set; }
}

public class UpdateProfileRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? MobileNumber { get; set; }
    public string? ProfileImageUrl { get; set; }
}

public class AddressResponse
{
    public int AddressId { get; set; }
    public string AddressType { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? PostalCode { get; set; }
}

public class UpdateAddressRequest
{
    public int? AddressId { get; set; }
    public string AddressType { get; set; } = "Primary";
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? PostalCode { get; set; }
}

public class DataExportResponse
{
    public string? RequestId { get; set; }
    public string? Message { get; set; }
    public DateTime? EstimatedCompletionDate { get; set; }
}

// Features
public class FeatureAccessResponse
{
    public string FeatureCode { get; set; } = string.Empty;
    public string? FeatureName { get; set; }
    public string? FeatureType { get; set; }
    public bool HasAccess { get; set; }
    public string? Source { get; set; }
    public string? Reason { get; set; }
    public int? Level { get; set; }
    public string? LevelDescription { get; set; }
    public string? RequiredLicense { get; set; }
}

// Payments
public class TransactionResponse
{
    public int TransactionId { get; set; }
    public string? TransactionNumber { get; set; }
    public string? TransactionType { get; set; }
    public decimal Amount { get; set; }
    public string? CurrencyCode { get; set; }
    public string? Status { get; set; }
    public string? PaymentMethod { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? Description { get; set; }
    public string? ProviderTransactionId { get; set; }
}

public class InvoiceResponse
{
    public int InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal SubTotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? CurrencyCode { get; set; }
    public string? Status { get; set; }
    public DateTime? PaidDate { get; set; }
}

public class InvoiceDetailResponse : InvoiceResponse
{
    public string? BillingAddress { get; set; }
    public string? Notes { get; set; }
}

public class SubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public string? PlanName { get; set; }
    public string? Status { get; set; }
    public string? BillingCycle { get; set; }
    public decimal Amount { get; set; }
    public string? CurrencyCode { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public DateTime? NextBillingDate { get; set; }
}

public class PromoCodeResponse
{
    public string Code { get; set; } = string.Empty;
    public string? DiscountType { get; set; }
    public decimal DiscountValue { get; set; }
    public string? Description { get; set; }
    public DateTime? ExpiryDate { get; set; }
}

// Issues
public class IssueResponse
{
    public int IssueId { get; set; }
    public string? IssueNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IssueType { get; set; }
    public string? Priority { get; set; }
    public string? Status { get; set; }
    public int? ApplicationId { get; set; }
    public string? ApplicationName { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public DateTime? ResolvedDate { get; set; }
}

public class IssueDetailResponse : IssueResponse
{
    public List<IssueCommentResponse> Comments { get; set; } = new();
}

public class IssueCommentResponse
{
    public int CommentId { get; set; }
    public string? Comment { get; set; }
    public bool IsInternal { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedDate { get; set; }
}

public class CreateIssueRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Type { get; set; } = "Bug";
    public string? Priority { get; set; } = "Medium";
    public int? ApplicationId { get; set; }
    public string? ReproductionSteps { get; set; }
    public string? ExpectedBehavior { get; set; }
    public string? ActualBehavior { get; set; }
    public object? Environment { get; set; }
}

// Exception
public class AppManagerException : Exception
{
    public string ErrorCode { get; }

    public AppManagerException(string aErrorCode, string aMessage) : base(aMessage)
    {
        ErrorCode = aErrorCode;
    }
}
```

### 4.3 Usage Examples

```csharp
using AppManager.Client;

// Initialize the client
using var vClient = new AppManagerClient("https://api.appmanager.com");

// Example 1: User Authentication
try
{
    var vAuthResult = await vClient.LoginAsync("user@example.com", "Password123!");
    Console.WriteLine($"Welcome {vAuthResult.FirstName} {vAuthResult.LastName}!");

    if (vAuthResult.ActiveLicense != null)
    {
        Console.WriteLine($"Active License: {vAuthResult.ActiveLicense.LicenseName}");
        Console.WriteLine($"Days Remaining: {vAuthResult.ActiveLicense.DaysRemaining}");
    }
}
catch (AppManagerException ex)
{
    Console.WriteLine($"Login failed: {ex.ErrorCode} - {ex.Message}");
}

// Example 2: License Validation
var vLicenseValidation = await vClient.ValidateLicenseAsync();
if (vLicenseValidation.IsValid)
{
    Console.WriteLine($"License is valid. Expires in {vLicenseValidation.License?.DaysRemaining} days");
}
else
{
    Console.WriteLine("License is not valid. Please renew.");
}

// Example 3: Feature Access Check
var vExportFeature = await vClient.CheckFeatureAccessAsync("EXPORT_PDF");
if (vExportFeature.HasAccess)
{
    Console.WriteLine("PDF Export feature is available");
    // Enable PDF export functionality
}
else
{
    Console.WriteLine($"PDF Export not available: {vExportFeature.Reason}");
    if (!string.IsNullOrEmpty(vExportFeature.RequiredLicense))
    {
        Console.WriteLine($"Upgrade to {vExportFeature.RequiredLicense} to unlock this feature");
    }
}

// Example 4: Get User Profile
var vProfile = await vClient.GetProfileAsync();
Console.WriteLine($"Profile: {vProfile.FirstName} {vProfile.LastName}");
Console.WriteLine($"Email: {vProfile.Email} (Verified: {vProfile.IsEmailVerified})");

// Example 5: Update Profile
await vClient.UpdateProfileAsync(new UpdateProfileRequest
{
    FirstName = "John",
    LastName = "Smith",
    MobileNumber = "+919876543211"
});

// Example 6: Create Support Issue
var vIssue = await vClient.CreateIssueAsync(new CreateIssueRequest
{
    Title = "Cannot export to PDF",
    Description = "When I try to export my report, I get an error.",
    Type = "Bug",
    Priority = "High",
    ReproductionSteps = "1. Open report\n2. Click Export\n3. Select PDF\n4. Error appears",
    ExpectedBehavior = "PDF should download",
    ActualBehavior = "Error message: 'Export failed'"
});
Console.WriteLine($"Issue created: {vIssue.IssueNumber}");

// Example 7: Get Transaction History
var vTransactions = await vClient.GetTransactionsAsync(aPage: 1, aPageSize: 10);
Console.WriteLine($"Found {vTransactions.TotalCount} transactions");
foreach (var vTransaction in vTransactions.Items)
{
    Console.WriteLine($"  {vTransaction.TransactionNumber}: {vTransaction.Amount:C} ({vTransaction.Status})");
}

// Example 8: Download Invoice
var vInvoices = await vClient.GetInvoicesAsync();
if (vInvoices.Items.Any())
{
    var vInvoiceId = vInvoices.Items.First().InvoiceId;
    var vPdfBytes = await vClient.DownloadInvoiceAsync(vInvoiceId);
    await File.WriteAllBytesAsync($"Invoice_{vInvoiceId}.pdf", vPdfBytes);
    Console.WriteLine("Invoice downloaded successfully");
}

// Example 9: Quantity-based License Consumption
try
{
    var vConsumeResult = await vClient.ConsumeQuantityAsync(
        aLicenseId: 1,
        aQuantity: 1,
        aReference: "export_report_123"
    );
    Console.WriteLine($"Consumed: {vConsumeResult.ConsumedQuantity}, Remaining: {vConsumeResult.RemainingQuantity}");
}
catch (AppManagerException ex) when (ex.ErrorCode == "INSUFFICIENT_QUANTITY")
{
    Console.WriteLine("Not enough quantity remaining. Please purchase more.");
}

// Example 10: Logout
await vClient.LogoutAsync();
Console.WriteLine("Logged out successfully");
```

### 4.4 Dependency Injection Setup (ASP.NET Core)

```csharp
// Program.cs or Startup.cs
using AppManager.Client;

var vBuilder = WebApplication.CreateBuilder(args);

// Register AppManagerClient as a scoped service
vBuilder.Services.AddScoped<AppManagerClient>(sp =>
{
    var vConfiguration = sp.GetRequiredService<IConfiguration>();
    var vBaseUrl = vConfiguration["AppManager:ApiUrl"] ?? "https://api.appmanager.com";
    return new AppManagerClient(vBaseUrl);
});

// Or use HttpClientFactory for better resource management
vBuilder.Services.AddHttpClient<AppManagerClient>((sp, aClient) =>
{
    var vConfiguration = sp.GetRequiredService<IConfiguration>();
    aClient.BaseAddress = new Uri(vConfiguration["AppManager:ApiUrl"] ?? "https://api.appmanager.com");
});

var vApp = vBuilder.Build();
```

```json
// appsettings.json
{
  "AppManager": {
    "ApiUrl": "https://api.appmanager.com"
  }
}
```

### 4.5 MAUI/Blazor Integration

```csharp
// MauiProgram.cs
using AppManager.Client;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var vBuilder = MauiApp.CreateBuilder();
        vBuilder.UseMauiApp<App>();

        // Register AppManagerClient
        vBuilder.Services.AddSingleton<AppManagerClient>(sp =>
        {
#if DEBUG
            return new AppManagerClient("https://localhost:5101");
#else
            return new AppManagerClient("https://api.appmanager.com");
#endif
        });

        return vBuilder.Build();
    }
}

// Example Blazor Component
@inject AppManagerClient AppManager

@code {
    private UserProfileResponse? objProfile;
    private bool HasExportFeature;

    protected override async Task OnInitializedAsync()
    {
        if (AppManager.IsAuthenticated)
        {
            objProfile = await AppManager.GetProfileAsync();
            var vExportAccess = await AppManager.CheckFeatureAccessAsync("EXPORT_PDF");
            HasExportFeature = vExportAccess.HasAccess;
        }
    }
}
```

---

## 5. AI Agent Integration Guide

This section provides guidance for AI agents and automated systems integrating with the App Manager API using .NET.

### 5.1 Authentication for AI Agents

AI agents should:
1. Store credentials securely (use environment variables, Azure Key Vault, or other secret management)
2. Implement automatic token refresh (handled by AppManagerClient)
3. Handle authentication errors gracefully

```csharp
using AppManager.Client;
using Microsoft.Extensions.Configuration;

// Using IConfiguration (recommended for ASP.NET Core / Worker Services)
public class AppManagerAgentService
{
    private readonly AppManagerClient objClient;
    private readonly IConfiguration objConfiguration;
    private readonly ILogger<AppManagerAgentService> objLogger;

    public AppManagerAgentService(IConfiguration aConfiguration, ILogger<AppManagerAgentService> aLogger)
    {
        objConfiguration = aConfiguration;
        objLogger = aLogger;

        var vApiUrl = aConfiguration["AppManager:ApiUrl"] ?? "https://api.appmanager.com";
        objClient = new AppManagerClient(vApiUrl);
    }

    public async Task InitializeAsync()
    {
        try
        {
            var vEmail = objConfiguration["AppManager:UserEmail"]
                ?? Environment.GetEnvironmentVariable("APPMANAGER_USER_EMAIL")
                ?? throw new InvalidOperationException("AppManager user email not configured");

            var vPassword = objConfiguration["AppManager:UserPassword"]
                ?? Environment.GetEnvironmentVariable("APPMANAGER_USER_PASSWORD")
                ?? throw new InvalidOperationException("AppManager user password not configured");

            await objClient.LoginAsync(vEmail, vPassword);
            objLogger.LogInformation("Successfully authenticated with App Manager API");
        }
        catch (AppManagerException ex)
        {
            objLogger.LogError(ex, "Failed to authenticate: {ErrorCode}", ex.ErrorCode);
            throw;
        }
    }

    public async Task<bool> CheckFeatureAccessAsync(string aFeatureCode)
    {
        var vResult = await objClient.CheckFeatureAccessAsync(aFeatureCode);
        return vResult.HasAccess;
    }

    public async Task<bool> ValidateLicenseAsync()
    {
        var vResult = await objClient.ValidateLicenseAsync();
        return vResult.IsValid;
    }
}

// Using Environment Variables directly
var vApiUrl = Environment.GetEnvironmentVariable("APPMANAGER_API_URL") ?? "https://api.appmanager.com";
var vEmail = Environment.GetEnvironmentVariable("APPMANAGER_USER_EMAIL");
var vPassword = Environment.GetEnvironmentVariable("APPMANAGER_USER_PASSWORD");

using var vClient = new AppManagerClient(vApiUrl);
await vClient.LoginAsync(vEmail!, vPassword!);
```

### 5.2 Common AI Agent Operations

**Check if user has access to a feature:**
```csharp
var vFeatureAccess = await vClient.CheckFeatureAccessAsync("EXPORT_PDF");
if (vFeatureAccess.HasAccess)
{
    // Feature is available
}
else
{
    Console.WriteLine($"Feature not available: {vFeatureAccess.Reason}");
}
```

**Validate user's license:**
```csharp
var vValidation = await vClient.ValidateLicenseAsync();
if (vValidation.IsValid)
{
    Console.WriteLine($"License valid for {vValidation.License?.DaysRemaining} days");
}
```

**Get user profile:**
```csharp
var vProfile = await vClient.GetProfileAsync();
Console.WriteLine($"User: {vProfile.Email}");
```

**Submit a support ticket:**
```csharp
var vIssue = await vClient.CreateIssueAsync(new CreateIssueRequest
{
    Title = "Automated issue report",
    Description = "Issue detected by monitoring agent",
    Type = "Bug",
    Priority = "Medium"
});
Console.WriteLine($"Issue created: {vIssue.IssueNumber}");
```

### 5.3 Rate Limiting

The API implements rate limiting to ensure fair usage:
- 100 requests per minute for authenticated endpoints
- 20 requests per minute for unauthenticated endpoints

When rate limited, you'll receive a `429 Too Many Requests` response with a `Retry-After` header.

```csharp
// Implementing retry logic with Polly
using Polly;
using Polly.Retry;

public class ResilientAppManagerClient
{
    private readonly AppManagerClient objClient;
    private readonly AsyncRetryPolicy objRetryPolicy;

    public ResilientAppManagerClient(string aBaseUrl)
    {
        objClient = new AppManagerClient(aBaseUrl);

        objRetryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<AppManagerException>(ex => ex.ErrorCode == "RATE_LIMITED")
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Exponential backoff
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    Console.WriteLine($"Retry {retryCount} after {timeSpan.TotalSeconds}s due to {exception.Message}");
                });
    }

    public async Task<bool> CheckFeatureAccessWithRetryAsync(string aFeatureCode)
    {
        return await objRetryPolicy.ExecuteAsync(async () =>
        {
            var vResult = await objClient.CheckFeatureAccessAsync(aFeatureCode);
            return vResult.HasAccess;
        });
    }
}
```

### 5.4 Best Practices for AI Agents

1. **Cache feature access results** - Feature access typically doesn't change frequently

```csharp
using Microsoft.Extensions.Caching.Memory;

public class CachedFeatureService
{
    private readonly AppManagerClient objClient;
    private readonly IMemoryCache objCache;

    public CachedFeatureService(AppManagerClient aClient, IMemoryCache aCache)
    {
        objClient = aClient;
        objCache = aCache;
    }

    public async Task<bool> HasFeatureAccessAsync(string aFeatureCode)
    {
        var vCacheKey = $"feature_{aFeatureCode}";

        return await objCache.GetOrCreateAsync(vCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            var vResult = await objClient.CheckFeatureAccessAsync(aFeatureCode);
            return vResult.HasAccess;
        });
    }
}
```

2. **Implement exponential backoff** - For retrying failed requests (see Polly example above)

3. **Log all API interactions** - For debugging and audit purposes

```csharp
using Microsoft.Extensions.Logging;

public class LoggingAppManagerClient
{
    private readonly AppManagerClient objClient;
    private readonly ILogger<LoggingAppManagerClient> objLogger;

    public async Task<FeatureAccessResponse> CheckFeatureAccessAsync(string aFeatureCode)
    {
        objLogger.LogInformation("Checking feature access: {FeatureCode}", aFeatureCode);

        try
        {
            var vResult = await objClient.CheckFeatureAccessAsync(aFeatureCode);
            objLogger.LogInformation("Feature {FeatureCode} access: {HasAccess}", aFeatureCode, vResult.HasAccess);
            return vResult;
        }
        catch (AppManagerException ex)
        {
            objLogger.LogError(ex, "Feature check failed: {ErrorCode}", ex.ErrorCode);
            throw;
        }
    }
}
```

4. **Handle all error codes** - See Error Handling section below

---

## 5.5 CORS Configuration for Client Applications

The API enforces CORS (Cross-Origin Resource Sharing) to control which domains can make API calls from browsers. Each client application's origin must be registered in the API's configuration.

**Server Configuration (`appsettings.json`):**

```json
{
  "Cors": {
    "AllowedOrigins": [
      "https://app1.yourcompany.com",
      "https://app2.yourcompany.com",
      "https://mobile-backend.yourcompany.com",
      "http://localhost:3000"
    ]
  }
}
```

**For Docker deployments**, override via environment variable:

```bash
docker run -d \
  -e "Cors__AllowedOrigins__0=https://app1.yourcompany.com" \
  -e "Cors__AllowedOrigins__1=https://app2.yourcompany.com" \
  -e "Cors__AllowedOrigins__2=http://localhost:3000" \
  ...
```

**Behavior:**
- **Development** (`ASPNETCORE_ENVIRONMENT=Development`): If no origins are configured, all origins are allowed for ease of local testing
- **Production**: Only configured origins are accepted. Requests from unlisted origins will be blocked by the browser

> **Note for server-to-server calls:** CORS only applies to browser-based requests. Backend services, AI agents, and mobile apps calling the API directly are not affected by CORS restrictions.

---

## 6. Error Handling

### 6.1 Standard Error Response Format

All errors follow this format:

```json
{
  "success": false,
  "error": "ERROR_CODE",
  "message": "Human-readable error message",
  "statusCode": 400,
  "traceId": "abc123def456"
}
```

### 6.2 Common Error Codes

| Error Code | HTTP Status | Description |
|------------|-------------|-------------|
| `VALIDATION_ERROR` | 400 | Request validation failed (e.g., missing `encryptedPassword`) |
| `DECRYPTION_FAILED` | 400 | Failed to decrypt an RSA-encrypted password — wrong public key, wrong padding (must be RSA-OAEP-SHA256), or corrupted base64 |
| `UNAUTHORIZED` | 401 | Authentication required |
| `INVALID_CREDENTIALS` | 401 | Wrong email or password |
| `INVALID_TOKEN` / `INVALID_REFRESH_TOKEN` / `INVALID_RESET_TOKEN` | 401 / 400 | Token is malformed, unknown, expired, or revoked |
| `ACCOUNT_LOCKED` | 423 | Too many failed login attempts |
| `ACCOUNT_DISABLED` | 403 | Account has been deactivated |
| `NOT_FOUND` (and resource-specific `ISSUE_NOT_FOUND`, `TRANSACTION_NOT_FOUND`, `INVOICE_NOT_FOUND`, `LICENSE_NOT_FOUND`, `SUBSCRIPTION_NOT_FOUND`, `PROMO_CODE_NOT_FOUND`, `FEATURE_NOT_FOUND`, `FLAG_NOT_FOUND`, `USER_NOT_FOUND`, `DEVICE_NOT_FOUND`) | 404 | Resource not found |
| `EMAIL_EXISTS` | 409 | Email already registered |
| `INTERNAL_ERROR` | 500 | Server error |
| **Multi-tenant scoping codes (v1.3)** | | |
| `APPLICATION_ID_REQUIRED` | 400 | Endpoint requires an ApplicationId and none was resolvable (no `X-Api-Key`, no body `applicationId` / query `aApplicationId`) |
| `APP_ID_MISMATCH` | 400 / 401 / 403 | Caller's resolved ApplicationId does not match the resource's / token's ApplicationId. 400 on register / reset-password when body and API key disagree; 401 on `/AuthSvc/refresh` when the refresh token was issued for a different app; 403 on `GET /IssueSvc/{aIssueId}`, `POST /IssueSvc/{aIssueId}/comments`, `POST /IssueSvc/{aIssueId}/close` |
| `CROSS_APP_LICENSE` | 403 | Returned by `POST /LicenseSvc/{aLicenseId}/consume` and `DELETE /LicenseSvc/{aLicenseId}/devices/{aDeviceId}` when the license's ApplicationId does not match the caller's |
| `CROSS_APP_RESOURCE` | 403 | Returned by `GET /PaymentSvc/transactions/{aTransactionId}`, `GET /PaymentSvc/invoices/{aInvoiceId}`, `POST /PaymentSvc/subscriptions/{aSubscriptionId}/cancel` when the resource's ApplicationId does not match the caller's |
| `NO_APP_ACCESS` | 403 | Returned by `GET /UserSvc/profile` when the authenticated user has no `UserApplicationRole` row for the calling app |
| `PROMO_CODE_NOT_VALID_FOR_APPLICATION` | 400 | Business-logic: the promo code is scoped to a different application than the caller's (returned as 400, not 403, to keep the existing promo-validation response shape) |

### 6.3 Handling Errors in Code

```csharp
using AppManager.Client;
using Microsoft.Extensions.Logging;

public class SafeApiCaller
{
    private readonly AppManagerClient objClient;
    private readonly ILogger<SafeApiCaller> objLogger;
    private readonly string objUserEmail;
    private readonly string objUserPassword;

    public SafeApiCaller(AppManagerClient aClient, ILogger<SafeApiCaller> aLogger,
        string aUserEmail, string aUserPassword)
    {
        objClient = aClient;
        objLogger = aLogger;
        objUserEmail = aUserEmail;
        objUserPassword = aUserPassword;
    }

    public async Task<T> SafeCallAsync<T>(Func<Task<T>> aApiFunction)
    {
        try
        {
            return await aApiFunction();
        }
        catch (AppManagerException ex)
        {
            switch (ex.ErrorCode)
            {
                case "UNAUTHORIZED":
                case "INVALID_TOKEN":
                case "SESSION_EXPIRED":
                    // Handle authentication error - try to re-authenticate
                    objLogger.LogWarning("Authentication error: {ErrorCode}. Attempting re-login.", ex.ErrorCode);
                    await objClient.LoginAsync(objUserEmail, objUserPassword);
                    return await aApiFunction(); // Retry after re-authentication

                case "RATE_LIMITED":
                    // Handle rate limiting with exponential backoff
                    objLogger.LogWarning("Rate limited. Waiting before retry.");
                    await Task.Delay(TimeSpan.FromSeconds(60));
                    return await aApiFunction(); // Retry after waiting

                case "VALIDATION_ERROR":
                    // Log validation errors for debugging
                    objLogger.LogError("Validation error: {Message}", ex.Message);
                    throw;

                case "NOT_FOUND":
                case "ISSUE_NOT_FOUND":
                case "TRANSACTION_NOT_FOUND":
                case "INVOICE_NOT_FOUND":
                    // Resource not found - don't retry
                    objLogger.LogWarning("Resource not found: {Message}", ex.Message);
                    throw;

                default:
                    // Log and rethrow other errors
                    objLogger.LogError(ex, "API Error: {ErrorCode} - {Message}", ex.ErrorCode, ex.Message);
                    throw;
            }
        }
    }
}

// Usage Example
public class FeatureChecker
{
    private readonly SafeApiCaller objSafeApiCaller;
    private readonly AppManagerClient objClient;

    public async Task<bool> CheckExportFeatureAsync()
    {
        var vResult = await objSafeApiCaller.SafeCallAsync(async () =>
        {
            var vAccess = await objClient.CheckFeatureAccessAsync("EXPORT_PDF");
            return vAccess.HasAccess;
        });

        return vResult;
    }
}

// Comprehensive Error Handling with Custom Responses
public class ApiErrorHandler
{
    public static string GetUserFriendlyMessage(AppManagerException aEx)
    {
        return aEx.ErrorCode switch
        {
            "UNAUTHORIZED" => "Please log in to continue.",
            "INVALID_CREDENTIALS" => "Invalid email or password. Please try again.",
            "ACCOUNT_LOCKED" => "Your account has been locked due to too many failed attempts. Please try again later.",
            "ACCOUNT_DISABLED" => "Your account has been disabled. Please contact support.",
            "NOT_FOUND" => "The requested resource was not found.",
            "VALIDATION_ERROR" => $"Invalid input: {aEx.Message}",
            "INSUFFICIENT_QUANTITY" => "Not enough quantity remaining. Please purchase more.",
            "LICENSE_EXPIRED" => "Your license has expired. Please renew to continue.",
            "FEATURE_NOT_AVAILABLE" => "This feature is not available with your current license.",
            "RATE_LIMITED" => "Too many requests. Please wait a moment and try again.",
            "INTERNAL_ERROR" => "An unexpected error occurred. Please try again later.",
            _ => aEx.Message
        };
    }
}
```

---

## API Endpoints Summary

| Service | Method | Endpoint | Auth Required |
|---------|--------|----------|---------------|
| **AuthSvc** | GET | /AuthSvc/public-key | No |
| | POST | /AuthSvc/register | No |
| | POST | /AuthSvc/login | No |
| | POST | /AuthSvc/refresh | No |
| | POST | /AuthSvc/validate | No |
| | POST | /AuthSvc/logout | Yes |
| | POST | /AuthSvc/forgot-password | No |
| | POST | /AuthSvc/reset-password | No |
| **LicenseSvc** | GET | /LicenseSvc/types | No |
| | GET | /LicenseSvc | Yes |
| | POST | /LicenseSvc/validate | Yes |
| | POST | /LicenseSvc/{aLicenseId}/consume | Yes |
| | DELETE | /LicenseSvc/{aLicenseId}/devices/{aDeviceId} | Yes |
| **UserSvc** | GET | /UserSvc/profile | Yes |
| | PUT | /UserSvc/profile | Yes |
| | GET | /UserSvc/addresses | Yes |
| | POST | /UserSvc/addresses | Yes |
| | POST | /UserSvc/change-password | Yes |
| | POST | /UserSvc/data-export | Yes |
| | POST | /UserSvc/delete-request | Yes |
| **FeatureSvc** | GET | /FeatureSvc | Yes |
| | GET | /FeatureSvc/{aFeatureCode} | Yes |
| | GET | /FeatureSvc/flags/{aFlagCode} | Yes |
| **PaymentSvc** | GET | /PaymentSvc/transactions | Yes |
| | GET | /PaymentSvc/transactions/{aTransactionId} | Yes |
| | GET | /PaymentSvc/invoices | Yes |
| | GET | /PaymentSvc/invoices/{aInvoiceId} | Yes |
| | GET | /PaymentSvc/invoices/{aInvoiceId}/download | Yes |
| | GET | /PaymentSvc/subscriptions | Yes |
| | POST | /PaymentSvc/subscriptions/{aSubscriptionId}/cancel | Yes |
| | POST | /PaymentSvc/promo-codes/validate | Yes |
| **IssueSvc** | GET | /IssueSvc | Yes |
| | GET | /IssueSvc/{aIssueId} | Yes |
| | POST | /IssueSvc | Yes |
| | POST | /IssueSvc/{aIssueId}/comments | Yes |
| | POST | /IssueSvc/{aIssueId}/close | Yes |

---

**Need Help?** Contact your App Manager administrator or visit the support portal.
