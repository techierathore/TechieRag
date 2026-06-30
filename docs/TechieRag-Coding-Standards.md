# TechieRag Coding Standards

**Last Updated:** 2026-06-25
**Status:** Authoritative for all code under `src/` and `tests/`. Conformance enforced via repo-root `.editorconfig` + verifier grep checks in §"Enforcement".

> **Per-project naming decision (recorded).** The TechieRag codebase (~96% complete, shipped) uses **standard Microsoft conventions — bare `camelCase`, no prefix, no underscores** for instance fields, parameters, and locals (≥95% dominance, e.g. `private readonly ILlmProvider llmProvider;`). This project therefore adopts the **no-prefix** convention and does **NOT** use the TechieFlow default `obj`/`a`/`v` prefixes. New code follows the established camelCase convention so the codebase stays internally consistent. The one hard rule shared with TechieFlow is **no underscores anywhere**.

## Database Naming Conventions

> TechieRag is a library with no application-owned relational schema, but its SQL-backed vector stores (SQLite-vec, pgvector) and any consumer schema should follow these.

### Tables and Columns
- PascalCase: `CustomerOrder` NOT `customer_order`
- Singular: `CustomerOrder` NOT `CustomerOrders`
- **NEVER use underscores** in any DB object name
- FK columns: `{TableName}Id` (e.g., `CustomerId`)
- PK: `{TableName}Id` (e.g., `UserId`)

### Stored Procedures & Functions
- PascalCase verb prefix: `GetCustomerOrders`, `InsertOrder`, `CalculateTotal`
- Action prefixes: Get / Insert / Update / Delete / Calculate

### Indexes & Constraints
- Index: `IX{Table}{Column}` · PK: `Pk{Table}` · FK: `Fk{Table}{Ref}` · Unique: `Uc{Table}{Column}`

## C# Conventions

### Classes & Interfaces
- PascalCase for classes; `I` prefix for interfaces; descriptive names.
- Async methods end with `Async`.

### Fields, Parameters, Locals

**NEVER use underscores** anywhere in any identifier.

| Kind | Convention | Example |
|------|-----------|---------|
| **Instance fields** | `camelCase`, no prefix (no underscores) | `private readonly ILogger<X> logger;`<br>`private readonly HttpClient httpClient;`<br>`private bool initialized;` |
| **Static / `const` fields** | PascalCase, no prefix | `private const string CachePrefix = "…";` |
| **Method parameters** | `camelCase`, no prefix | `LoginAsync(string email, string password)` |
| **Local variables** | `camelCase` via `var` | `var response = await …` |
| **Booleans** | `Is`/`Has`/`Can` phrasing | `IsAuthenticated`, `isValid`, `hasAccess` |
| **Properties** | PascalCase, no prefix | `public string ConnectionString { get; set; }` |
| **Constants** | PascalCase, no underscores | `MaxRetryCount` NOT `MAX_RETRY_COUNT` |
| **Test methods** | Short PascalCase, no underscores — full scenario in XML `<summary>` | `LoginRejectsBadPassword` not `Login_BadPassword_ReturnsUnauthorized` |

**Rejected forms:** `_underscore` field prefixes, snake_case anywhere, Hungarian prefixes (`strName`), `obj`/`a`/`v` prefixes (not used in this codebase), underscores in test method names.

### Controller-action parameters
Parameter names stay `camelCase` and flow through to OpenAPI. Body DTO **property** names are PascalCase.

### Environment Variables
**PascalCase, no separators.** `TechieRagBaseUrl` NOT `TECHIERAG_BASE_URL` and NOT `TechieRag__BaseUrl`. Read via `IConfiguration["Section:Key"]` (TechieRag binds its `TechieRag` config section) — never `Environment.GetEnvironmentVariable(...)`.

### File Structure
```csharp
using System;

namespace TechieRag.Services.Example;

public class DatabaseService
{
    private readonly ILogger<DatabaseService> logger;
    private readonly IConfiguration configuration;

    public DatabaseService(ILogger<DatabaseService> logger, IConfiguration configuration)
    {
        this.logger = logger;
        this.configuration = configuration;
    }

    public string ConnectionString { get; set; }

    public async Task<DataTable> GetDataAsync(string queryName)
    {
        var connString = configuration.GetConnectionString("Default");
        var result = await ExecuteQueryAsync(connString, queryName);
        return result;
    }
}
```

### Best Practices
- One class per file. File name matches class.
- File-scoped namespaces. Nullable reference types enabled.
- Methods small (<20 lines). Single responsibility.
- Max 3 nesting levels. Early returns for validation.
- `ConfigureAwait(false)` in library code.
- StringBuilder for loop concatenation. Dispose `IDisposable`. Cache expensive ops.
- LLM/embedding providers use raw `HttpClient` + `System.Text.Json` (keep the core dependency-light).

### XML Documentation (MANDATORY on public members)
`<summary>`, `<remarks>`, `<param>`, `<returns>`, `<exception>` — all required on public types and members (this is a published SDK; consumers read the IntelliSense).

### Testing
- Short PascalCase test name, no underscores. Full scenario in XML `<summary>`.
- Arrange-Act-Assert. One assertion per test where practical.

### Security
- Never hardcode credentials or API keys. Parameterized queries. Validate inputs. Log security events.

## Enforcement

### .editorconfig (machine-checkable)
- File-scoped namespaces (`warning`)
- Async-method `Async` suffix (`warning`)
- `var` for locals (`warning`)
- Nullable reference types enabled
- No `_` prefix on private fields (`warning` via custom naming rule)

### Verifier grep checks
```bash
# Forbidden underscore-prefix fields
grep -rE "private(\s+readonly)?\s+\w+\s+_[a-z]" src/ tests/ 2>/dev/null

# Forbidden test-method underscores
grep -rE "public\s+(async\s+)?Task\s+\w+_\w+\s*\(" tests/ 2>/dev/null
```

> Note: this is a **no-prefix** project — there is no "missing obj prefix" grep (that check applies only to obj-style projects). The two greps above plus the `.editorconfig` no-underscore rule are the enforcement surface.

### Severity
- **Error**: file-scoped namespace, underscore field prefix
- **Warning**: nullable, async suffix
- **Info**: consider fixing
