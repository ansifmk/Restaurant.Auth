# Restaurant Auth API

A secure authentication and authorization API for a restaurant system, built with
ASP.NET Core 8, Clean Architecture, and the CQRS pattern (MediatR).

Tokens are transported in encrypted `HttpOnly` cookies — never in request or
response bodies.

## Technology Stack

| Component        | Technology                                |
| ---------------- | ----------------------------------------- |
| Backend          | ASP.NET Core 8 Web API (`net8.0`)         |
| Database         | SQL Server + Entity Framework Core 8      |
| Authentication   | JWT (HMAC-SHA256) + opaque refresh tokens |
| Architecture     | Clean Architecture + CQRS (MediatR)       |
| Validation       | FluentValidation + DataAnnotations        |
| Password hashing | BCrypt.Net-Next                           |
| API docs         | Swagger / Swashbuckle (Development only)  |

## Project Structure

```
Restaurant.Auth/
├── src/
│   ├── Restaurant.Auth.Domain/           # Entities, Enums, Exceptions
│   ├── Restaurant.Auth.Application/      # CQRS Commands, Validators, Interfaces
│   ├── Restaurant.Auth.Infrastructure/   # EF Core, JWT, Password Hashing, Repositories
│   └── Restaurant.Auth.Api/              # Controllers, Middleware, Configuration
└── Restaurant.Auth.sln
```

---

## 1. Setup Instructions

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- SQL Server (local instance; the default connection string uses Windows
  authentication against `localhost`)
- Visual Studio 2022, VS Code, or the .NET CLI
- EF Core CLI tools (for manual migrations):
  ```bash
  dotnet tool install --global dotnet-ef
  ```

### Step 1 — Clone and restore

```bash
git clone <repository-url>
cd Restaurant.Auth
dotnet restore
```

### Step 2 — Configure secrets (required)

The API **will not start** without a JWT secret of at least 32 characters
(it throws `InvalidOperationException` on startup). Never commit the real
secret to `appsettings.json` — use User Secrets locally:

```bash
cd src/Restaurant.Auth.Api

dotnet user-secrets init

dotnet user-secrets set "Jwt:Secret" "your-secret-key-minimum-32-characters-long"

dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=RestaurantAuthDb;Trusted_Connection=True;TrustServerCertificate=True"
```

For production, provide the same keys via environment variables instead:

| User Secrets key                      | Environment variable                   |
| ------------------------------------- | -------------------------------------- |
| `Jwt:Secret`                          | `Jwt__Secret`                          |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` |

### Step 3 — Set up the database

In **Development** the API applies pending EF Core migrations automatically
on startup (`dbContext.Database.Migrate()` in `Program.cs`), so you can skip
to Step 4. For other environments, or to create the database explicitly:

```bash
dotnet ef database update \
  --project src/Restaurant.Auth.Infrastructure \
  --startup-project src/Restaurant.Auth.Api
```

To create a new migration after changing entities:

```bash
dotnet ef migrations add <MigrationName> \
  --project src/Restaurant.Auth.Infrastructure \
  --startup-project src/Restaurant.Auth.Api
```

### Step 4 — Run the API

```bash
dotnet run --project src/Restaurant.Auth.Api
```

| URL                              | Purpose                        |
| -------------------------------- | ------------------------------ |
| `https://localhost:7243`         | API (HTTPS)                    |
| `http://localhost:5073`          | API (HTTP, redirects to HTTPS) |
| `https://localhost:7243/swagger` | Swagger UI (Development only)  |

### Step 5 — Smoke test

```bash
curl -k -X POST https://localhost:7243/api/auth/register \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"user@restaurant.com\",\"password\":\"SecurePass123!\",\"confirmPassword\":\"SecurePass123!\",\"firstName\":\"John\",\"lastName\":\"Doe\"}" \
  -c cookies.txt -v
```

You should get `201 Created` with an `ApiResponse` body, and `cookies.txt`
should contain the `accessToken` and `refreshToken` cookies.

---

## 2. Configuration Requirements

All settings live under `appsettings.json` / `appsettings.Development.json`
and can be overridden by User Secrets or environment variables.

### Connection strings

| Key                                   | Default (Development)                                                                            | Notes                                 |
| ------------------------------------- | ------------------------------------------------------------------------------------------------ | ------------------------------------- |
| `ConnectionStrings:DefaultConnection` | `Server=localhost;Database=RestaurantAuthDb;Trusted_Connection=True;TrustServerCertificate=True` | Windows auth against local SQL Server |

### JWT (`Jwt`)

| Key                                | Default                  | Required              | Notes                                                               |
| ---------------------------------- | ------------------------ | --------------------- | ------------------------------------------------------------------- |
| `Jwt:Secret`                       | _(empty)_                | **Yes, min 32 chars** | HMAC-SHA256 signing key. App fails fast at startup if missing/short |
| `Jwt:Issuer`                       | `Restaurant.Auth.Api`    | Yes                   | Must match token validation parameters                              |
| `Jwt:Audience`                     | `Restaurant.Auth.Client` | Yes                   | Must match token validation parameters                              |
| `Jwt:AccessTokenExpirationMinutes` | `15`                     | No                    | Access token lifetime; validated with zero clock skew               |
| `Jwt:RefreshTokenExpirationDays`   | `7`                      | No                    | Refresh token lifetime                                              |

### Cookies (`Cookie`)

| Key                       | Default        | Notes                                                      |
| ------------------------- | -------------- | ---------------------------------------------------------- |
| `Cookie:AccessTokenName`  | `accessToken`  | Cookie name, path `/`                                      |
| `Cookie:RefreshTokenName` | `refreshToken` | Cookie name                                                |
| `Cookie:RefreshTokenPath` | `/`            | **Must be `"/"` in every environment** (see warning below) |
| `Cookie:MaxAgeDays`       | `7`            | Refresh cookie lifetime                                    |

Cookies are encrypted with ASP.NET Data Protection (keys persisted to the
`keys/` folder next to the running binary, 90-day key lifetime) and flagged
`HttpOnly`, `Secure`, `SameSite=Strict`.

### Full example

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=RestaurantAuthDb;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "Jwt": {
    "Secret": "YOUR_SECRET_KEY_MINIMUM_32_CHARACTERS_LONG",
    "Issuer": "Restaurant.Auth.Api",
    "Audience": "Restaurant.Auth.Client",
    "AccessTokenExpirationMinutes": 15,
    "RefreshTokenExpirationDays": 7
  },
  "Cookie": {
    "AccessTokenName": "accessToken",
    "RefreshTokenName": "refreshToken",
    "RefreshTokenPath": "/",
    "MaxAgeDays": 7
  }
}
```

### Rate limits (fixed window, per endpoint)

| Endpoint                  | Limit                |
| ------------------------- | -------------------- |
| `POST /api/auth/register` | 5 requests / minute  |
| `POST /api/auth/login`    | 5 requests / minute  |
| `POST /api/auth/refresh`  | 10 requests / minute |
| `POST /api/auth/logout`   | 10 requests / minute |

Exceeding a limit returns `429 Too Many Requests`.

---

## 3. Database Setup

- **Provider:** SQL Server (`Microsoft.EntityFrameworkCore.SqlServer` 8.0.11)
- **Context:** `Restaurant.Auth.Infrastructure.Data.AppDbContext`
- **Migrations:** auto-applied on startup in Development; apply manually via
  `dotnet ef database update` in other environments (see Setup Step 3).

### Schema

**Users table**

| Column       | Type             | Constraints                 |
| ------------ | ---------------- | --------------------------- |
| Id           | UNIQUEIDENTIFIER | PRIMARY KEY                 |
| Email        | NVARCHAR(256)    | UNIQUE, NOT NULL            |
| PasswordHash | NVARCHAR(MAX)    | NOT NULL (BCrypt)           |
| FirstName    | NVARCHAR(50)     | NOT NULL                    |
| LastName     | NVARCHAR(50)     | NOT NULL                    |
| Role         | NVARCHAR(50)     | NOT NULL (stored as string) |
| CreatedAt    | DATETIME2        | NOT NULL                    |
| UpdatedAt    | DATETIME2        | NULL                        |

**RefreshTokens table**

| Column      | Type             | Constraints                                 |
| ----------- | ---------------- | ------------------------------------------- |
| Id          | UNIQUEIDENTIFIER | PRIMARY KEY                                 |
| TokenHash   | NVARCHAR(500)    | UNIQUE, NOT NULL (SHA-256 hash)             |
| UserId      | UNIQUEIDENTIFIER | FOREIGN KEY → Users, cascade delete         |
| Expires     | DATETIME2        | NOT NULL                                    |
| Created     | DATETIME2        | NOT NULL                                    |
| Revoked     | DATETIME2        | NULL                                        |
| CreatedByIp | NVARCHAR(50)     | NOT NULL                                    |
| FamilyId    | UNIQUEIDENTIFIER | Indexed; token family for rotation tracking |

> The raw refresh token is **never stored** — only its SHA-256 hash
> (`TokenHash`). A token is active when `Revoked IS NULL` and not expired.

---

## 4. API Usage Examples

All endpoints are under `/api/auth`. All responses use the `ApiResponse<T>`
envelope:

```json
{
  "success": true,
  "message": "Login successful.",
  "data": { "...": "..." },
  "errors": null,
  "timestamp": "2026-09-06T18:00:00Z"
}
```

Errors carry machine-readable codes:

```json
{
  "success": false,
  "message": "Refresh token cookie not found.",
  "data": null,
  "errors": [
    {
      "code": "REFRESH_TOKEN_MISSING",
      "field": null,
      "message": "Refresh token cookie not found."
    }
  ],
  "timestamp": "2026-09-06T18:09:31Z"
}
```

Common codes: `AUTHENTICATION_FAILED`, `REGISTRATION_FAILED`,
`REFRESH_TOKEN_MISSING`, `REFRESH_FAILED`, `LOGOUT_FAILED`.

> **Cookies first:** register/login/refresh set `HttpOnly` cookies that the
> browser (or curl's cookie jar) sends automatically. JavaScript frontends must
> opt in with `credentials: "include"` (fetch) or `withCredentials: true`
> (axios). Authenticated requests work via the `accessToken` cookie; the
> `Authorization: Bearer` header is also accepted (mainly for Swagger/testing).

### Register — `POST /api/auth/register`

Creates the account and sets both auth cookies. Returns `201 Created`.

```bash
curl -k -X POST https://localhost:7243/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"email":"user@restaurant.com","password":"SecurePass123!","confirmPassword":"SecurePass123!","firstName":"John","lastName":"Doe"}' \
  -c cookies.txt
```

```js
const res = await fetch("https://localhost:7243/api/auth/register", {
  method: "POST",
  credentials: "include",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({
    email: "user@restaurant.com",
    password: "SecurePass123!",
    confirmPassword: "SecurePass123!",
    firstName: "John",
    lastName: "Doe",
  }),
});
const json = await res.json();
```

Validation rules: email (required, valid format, ≤256 chars), first/last name
(required, ≤50 chars), password (required, 8–128 chars, must contain uppercase,
lowercase, digit, and special character), confirmPassword (must match).

### Login — `POST /api/auth/login`

```bash
curl -k -X POST https://localhost:7243/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"user@restaurant.com","password":"SecurePass123!"}' \
  -c cookies.txt
```

```js
const res = await fetch("https://localhost:7243/api/auth/login", {
  method: "POST",
  credentials: "include",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({
    email: "user@restaurant.com",
    password: "SecurePass123!",
  }),
});
```

Response `200 OK`:

```json
{
  "success": true,
  "message": "Login successful.",
  "data": {
    "accessTokenExpiresAt": "2026-09-06T18:15:00Z",
    "refreshTokenExpiresAt": "2026-09-13T18:00:00Z"
  },
  "errors": null,
  "timestamp": "2026-09-06T18:00:00Z"
}
```

### Authenticated request

With cookies saved, just reuse the jar — no manual token handling:

```bash
curl -k https://localhost:7243/api/<protected-route> -b cookies.txt
```

```js
const res = await fetch("https://localhost:7243/api/<protected-route>", {
  credentials: "include",
});
```

### Refresh — `POST /api/auth/refresh`

No body needed — the `refreshToken` cookie is sent automatically. Rotates both
tokens (issues new cookies, invalidates the old refresh token).

```bash
curl -k -X POST https://localhost:7243/api/auth/refresh -b cookies.txt -c cookies.txt
```

```js
const res = await fetch("https://localhost:7243/api/auth/refresh", {
  method: "POST",
  credentials: "include",
});
```

### Logout — `POST /api/auth/logout`

No body needed — uses the `refreshToken` cookie, revokes it in the database,
and clears both cookies.

```bash
curl -k -X POST https://localhost:7243/api/auth/logout -b cookies.txt -c cookies.txt
```

```js
const res = await fetch("https://localhost:7243/api/auth/logout", {
  method: "POST",
  credentials: "include",
});
```

Response `200 OK`:

```json
{
  "success": true,
  "message": "Logged out successfully.",
  "data": { "message": "Logged out successfully." },
  "errors": null,
  "timestamp": "2026-09-06T18:20:00Z"
}
```

### Trying it in Swagger

Open `https://localhost:7243/swagger`. Cookie-based flows work directly from
the Swagger page (same origin, so the browser attaches cookies). For the
Authorize button, paste a raw JWT access token as a Bearer token.

---

## Security Features

- **Password hashing**: BCrypt (work factor 12)
- **JWT**: HMAC-SHA256, 15-minute access tokens, zero clock skew
- **Refresh tokens**: opaque random tokens, 7-day lifetime, rotated on every
  refresh, revocable; only SHA-256 hashes persisted
- **Cookie transport**: Data Protection–encrypted `HttpOnly` + `Secure` +
  `SameSite=Strict` cookies — tokens never exposed to JavaScript
- **Rate limiting**: fixed-window policies per auth endpoint (see table above)
- **Pipeline**: security-headers middleware, global exception handling, HSTS
  (non-Development), cookie policy enforcement
- **Secrets**: User Secrets locally, environment variables in production
- **Error handling**: `ApiResponse` envelope with error codes, no sensitive
  data leaked

## Troubleshooting

### API fails at startup: "JWT Secret is not configured"

Set a ≥32-character secret via User Secrets (`Jwt:Secret`) or the
`Jwt__Secret` environment variable (see Setup Step 2).

### Database connection fails

Verify SQL Server is reachable with the configured connection string and, in
non-Development environments, that migrations were applied with
`dotnet ef database update`.
