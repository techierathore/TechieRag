# DataStudio Coding Standards

## Database Naming Conventions

### Tables and Columns
- Use PascalCase for table names: `CustomerOrder` NOT `customer_order`
- Use singular table names : `CustomerOrder` NOT `CustomerOrders`
- Use PascalCase for column names: `FirstName` NOT `first_name`
- **NEVER use underscores** in database object names
- Foreign key columns: `{TableName}Id` (e.g., `CustomerId`)
- Primary key:`{TableName}Id` (e.g., `UserId`)

### Stored Procedures and Functions
- Use PascalCase with verb prefix: `GetCustomerOrders` NOT `get_customer_orders`
- Prefix with action: Get, Insert, Update, Delete, Calculate

### Indexes and Constraints
- Index naming: `IX{TableName}{ColumnName}`
- Primary key: `Pk{TableName}`
- Foreign key: `Fk{TableName}{ReferencedTable}`
- Unique constraint: `Uc{TableName}{ColumnName}`

## C# Coding Conventions

### Naming Conventions

#### Classes and Interfaces
- Use PascalCase for class names: `DatabaseConnection`
- Prefix interfaces with 'I': `IQueryExecutor`
- Use descriptive names: `SqlQueryBuilder` not `SqlQB`

#### Methods
- Use PascalCase: `ExecuteQuery()`
- Use verbs or verb phrases: `GetConnection()`, `SaveData()`
- Async methods end with 'Async': `GetDataAsync()`

#### Variables and Parameters
- Use camelCase: `connectionString`
- **NEVER use underscores** in variable names: Use `objConString` NOT `connection_string`
- Boolean variables should be questions: `objIsValid`, `objHasData`

#### Constants
- Use PascalCase: `DefaultTimeout`
- **NO underscores in constants**: Use `MaxRetryCount` NOT `MAX_RETRY_COUNT`

### Code Organization

#### File Structure
```csharp
// 1. Using directives
using System;
using System.Collections.Generic;

// 2. Namespace
namespace DataStudio.Core;

// 3. Class/Interface
public class DatabaseService
{
    // 4. Fields
    private readonly ILogger objLogger;
    
    // 5. Constructors
    public DatabaseService(ILogger logger)
    {
        objLogger = logger;
    }
    
    // 6. Properties
    public string ConnectionString { get; set; }
    
    // 7. Methods
    public async Task<DataTable> GetDataAsync()
    {
        // Implementation
    }
}
```

### Best Practices

#### General
- One class per file
- File name matches class name
- Use file-scoped namespaces
- Enable nullable reference types

#### Methods
- Keep methods small (< 20 lines)
- Single responsibility principle
- Avoid deep nesting (max 3 levels)
- Early returns for validation

#### Error Handling
```csharp
try
{
    // Operation
}
catch (SpecificException ex)
{
    objLogger.LogError(ex, "Specific error occurred");
    throw;
}
```

#### Async/Await
- Always use async/await for I/O operations
- Configure await: `ConfigureAwait(false)` in libraries
- Avoid async void except for event handlers

#### LINQ
- Use method syntax for simple queries
- Use query syntax for complex joins
- Avoid multiple enumerations

### Comments and Documentation

#### XML Documentation (MANDATORY)
**ALL public classes, methods, and properties MUST have XML documentation comments.**

```csharp
/// <summary>
/// Executes a SQL query and returns results.
/// </summary>
/// <remarks>
/// This method performs the following steps:
/// 1. Validates the input query
/// 2. Opens a database connection
/// 3. Executes the query with timeout handling
/// 4. Returns results in a DataTable format
/// </remarks>
/// <param name="query">The SQL query to execute.</param>
/// <returns>Query results as DataTable.</returns>
/// <exception cref="ArgumentNullException">Thrown when query is null or empty.</exception>
/// <exception cref="SqlException">Thrown when database operation fails.</exception>
public async Task<DataTable> ExecuteQueryAsync(string query)
```

**Required XML Documentation Elements:**
- `<summary>`: Brief description of what the member does
- `<remarks>`: Detailed explanation of code flow and logic (REQUIRED for all methods)
- `<param>`: Description for each parameter
- `<returns>`: Description of return value
- `<exception>`: Document all exceptions that can be thrown

#### Inline Comments
- Explain 'why', not 'what'
- Keep comments up to date
- Remove commented-out code
- Use inline comments sparingly - prefer XML documentation
- Complex algorithms should have step-by-step comments

### Testing

#### Unit Tests
- Name pattern: `MethodName_StateUnderTest_ExpectedBehavior`
- Arrange-Act-Assert pattern
- One assertion per test
- Use meaningful test data

```csharp
[Test]
public async Task GetConnectionValidConnectionStringReturnsOpenConnection()
{
    // Arrange
    var connectionString = "valid_connection";
    
    // Act
    var connection = await GetConnectionAsync(connectionString);
    
    // Assert
    Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
}
```

### Performance

- Use `StringBuilder` for string concatenation in loops
- Dispose IDisposable objects
- Use connection pooling
- Cache expensive operations
- Avoid premature optimization

### Security

- Never hardcode credentials
- Use parameterized queries
- Validate all inputs
- Sanitize user data
- Log security events

## Code Analysis Rules

StyleCop and .NET Analyzers are configured to enforce these standards automatically.

### Severity Levels
- **Error**: Must fix before commit
- **Warning**: Should fix
- **Info**: Consider fixing
- **Hidden**: Suggestions only