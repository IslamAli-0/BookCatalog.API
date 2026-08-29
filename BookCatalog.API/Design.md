# Book Catalog API - Design Document

## Architecture Decision: 3-Layer Architecture
I chose a standard 3-layer architecture (Controllers -> Services -> Repositories) instead of a more complex approach like Clean Architecture.

**Why:**
1. **Scope:** For a Week 1 CRUD API, Clean Architecture with multiple projects would be over-engineering and would cost time without adding immediate value.
2. **Future-proofing for Week 3:** A 3-layer setup provides enough decoupling. The API layer doesn't know about data storage. When we introduce a real database in Week 3, I will only need to swap the `IBookRepository` implementation in the Dependency Injection container. The Controllers and Services won't need to change at all.

## Project Structure
* **Controllers:** Handle HTTP requests, basic routing, and return standard status codes.
* **Services:** Contain business logic, logging, and validation beyond basic data annotations.
* **Repositories:** Manage data access. Currently implemented as a thread-safe in-memory store, ready to be swapped for a database later.
* **DTOs & Models:** Separated input payloads (DTOs) from core business entities (Models) to prevent over-posting and keep the API contract clean.

## Domain Modeling and Validation
* **Business-Driven Properties:** Expanded the `Book` model beyond basic fields to include `ISBN`, `Genre`, and `Description`. 
* **Why:** A real-world catalog requires universal identifiers (ISBN) for data integrity, and fields like Genre are necessary for future business requirements like filtering and sorting.
* **Records for DTOs:** Used C# `record` types with `init`-only properties for `CreateBookRequest` and `UpdateBookRequest` instead of standard classes.
* **Why:** DTOs are data carriers that should be immutable. Records provide built-in immutability and value-based equality which is better for testing, preventing accidental state changes as data flows from the API layer to the Service layer.
* **Validation:** Used Data Annotations, including Regex for ISBN validation. This leverages ASP.NET Core's automatic `400 Bad Request` handling to enforce business rules at the API boundary.
* **Response Isolation (BookResponse DTO):** I explicitly created a `BookResponse` DTO rather than returning the `Book` domain entity directly from the API.
* **Why:** This ensures the API contract remains stable. When the database schema evolves in the coming weeks (e.g., adding audit fields like `CreatedAt` or internal flags like `IsDeleted`), those internal details won't leak to the client.
* **Dynamic Validation (Custom Attributes):** Replaced the static `[Range]` attribute on `PublishYear` with a custom `[ValidPublishYear]` attribute.
* **Why:** Hardcoding future years (like 2100) is a bad practice. The custom attribute dynamically checks the current year (`DateTime.UtcNow.Year + 1`), allowing for realistic historical dates and near-future pre-orders without requiring code updates every year.

## Data Access Layer (Repositories)
* **Thread-Safe In-Memory Store:** Implemented `InMemoryBookRepository` using a `ConcurrentDictionary<Guid, Book>`.
* **Why:** A standard `List<T>` or `Dictionary<K,V>` is not thread-safe. In a web API where multiple requests can arrive simultaneously, standard collections cause race conditions or crash. `ConcurrentDictionary` prevents this.
* **Async by Default:** The `IBookRepository` interface uses `Task` and `Task<T>` for all methods, using `Task.FromResult` in the in-memory implementation.
* **Why:** This prepares the application for Week 3. When EF Core and a real database are introduced, the transition will be seamless. The Service and API layers are already built to handle asynchronous calls, meaning zero refactoring for upper layers.

## Service Layer (Business Logic & Logging)
* **Separation of Concerns:** Introduced a `BookService` layer to sit between the Controller and Repository. 
* **Why:** Controllers should only handle HTTP routing and status codes. The Service layer handles mapping DTOs to Domain Models, enforcing any business rules, and interacting with the data store.
* **Logging Strategy:** Injected `ILogger<BookService>` to track key operations.
* **Why:** Fulfills the requirement to log key operations and errors. I used structured logging (e.g., `{BookId}`) rather than string interpolation (`$"{id}"`) so that modern log aggregators can index the variables correctly.

## API Layer and HTTP Behavior
* **Thin Controllers:** The `BooksController` contains zero business logic. It delegates entirely to `IBookService`.
* **Strict HTTP Status Codes:** 
  * `200 OK` for successful reads and updates.
  * `201 Created` via `CreatedAtAction` for POST requests, providing the `Location` header.
  * `204 No Content` for successful DELETE operations.
  * `404 Not Found` when requesting, updating, or deleting a non-existent ID.
  * `400 Bad Request` is handled automatically by ASP.NET Core (`[ApiController]`) based on the DTO Data Annotations.
* **Routing Constraints:** Used `{id:guid}` in the route templates to ensure the API immediately rejects invalid ID formats before hitting the service layer.

## Dependency Injection
* **Singleton Repository:** Registered `InMemoryBookRepository` as a `Singleton`.
* **Why:** Since data is stored in a `ConcurrentDictionary`, a `Scoped` or `Transient` lifetime would destroy the dictionary at the end of every HTTP request. Singleton ensures the data persists while the application is running.
* **Scoped Service:** Registered `BookService` as `Scoped`.
* **Why:** This is the standard lifetime for business logic in web applications, keeping it isolated per HTTP request.






## Extras
* **Modern C# Features:** Used C# 12 Primary Constructors for Dependency Injection.
* **Why:** It removes unnecessary boilerplate (declaring and assigning `readonly` fields) and keeps the class clean and focused on business logic.
* **Mapping Strategy:** Used manual mapping via C# Extension Methods (`BookMapper.cs`) instead of an external library like AutoMapper.
* **Why:** For a small domain, external mappers add unnecessary dependencies and reflection overhead. Extension methods keep the Service layer clean (`book.ToResponse()`), maintain high performance, and keep the mapping logic explicit and easy to debug.

## Physical Layered Architecture (Multi-Project Solution)
* **Separation into 3 distinct C# projects:** Refactored from a single-project solution into three separate Class Library / Web API projects: `BookCatalog.Core`, `BookCatalog.Infrastructure`, and `BookCatalog.API`.
* **Why:** Moving from logical folder separation to physical project boundaries enforces the Dependency Inversion Principle where it matters most: `BookCatalog.Core` has zero project references to `Infrastructure` or `API`, so domain logic and contracts are guaranteed by the compiler to remain free of infrastructure concerns. `BookCatalog.API` does reference `Infrastructure` (to register the concrete implementation in the DI container), meaning a controller *could* technically import `InMemoryBookRepository` — but it would be an obvious violation of the architecture. The project split enforces the correct direction of the Core layer; the discipline of the API layer is enforced by convention and code review.
* **Project Dependency Direction (`BookCatalog.Core` ← `BookCatalog.Infrastructure` ← `BookCatalog.API`):**
  * `BookCatalog.Core` has zero dependencies on other projects. It owns the domain (`Models`), the contracts (`DTOs`, `IBookService`, `IBookRepository`), the mapping extension methods (`Mappers`), and the business logic (`BookService`).
  * `BookCatalog.Infrastructure` references only `Core`. It contains implementations that depend on external concerns — currently `InMemoryBookRepository`. Future database implementations (EF Core, Dapper) will live here.
  * `BookCatalog.API` references both `Core` (for DI interfaces) and `Infrastructure` (to register the concrete repository). It contains only HTTP-level concerns: Controllers and `Program.cs`.
* **`ValidPublishYearAttribute` placed in `BookCatalog.Core`:** The custom validation attribute was moved to `Core` alongside the DTOs that use it.
* **Why:** Keeping the attribute in the API project would force the DTOs to reference API, reversing the dependency direction. Since it is pure validation logic with no infrastructure dependencies, `Core` is the correct home.
* **`Microsoft.Extensions.Logging.Abstractions` NuGet in `BookCatalog.Core`:** Added this lightweight package to allow `BookService` to depend on `ILogger<T>`.
* **Why:** Class Library projects do not receive ASP.NET Core's implicit namespace imports. Rather than adding a full framework dependency, the abstractions-only package provides `ILogger<T>` with no runtime overhead and keeps `Core` framework-agnostic (it does not reference `Microsoft.AspNetCore.*`).
* **`IBookRepository` placed in `BookCatalog.Core/Interfaces/`:** The repository interface was moved out of a `Repositories/` folder into a dedicated `Interfaces/` folder within `Core`.
* **Why:** A `Repositories/` folder in `Core` implies implementation details, which belong in `Infrastructure`. Naming the folder `Interfaces/` makes the intent explicit: `Core` defines contracts, not implementations. The folder name is a communication tool.

## Pagination and Filtering
* **Filtering before Paginating:** Filtering logic must be executed *before* `.Skip()` and `.Take()`. Otherwise, you paginate the entire dataset and then filter that tiny chunk, resulting in empty or incomplete pages even when matching data exists.
* **The Page 5000 Edge Case:** If a client asks for page 5000 when only 2 pages exist, the repository's `.Skip()` simply bypasses all available records and returns an empty list. The API gracefully returns `200 OK` with an empty `Items` array and accurate total counts, avoiding a crash. The `TotalPages` and `TotalCount` fields in `PagedResponse<T>` give the client everything it needs to detect this case.
* **The Shift Problem:** Standard Offset Pagination (Skip/Take) is vulnerable to data shifting. If a client is on Page 2, and a new book is inserted on Page 1, all items shift down. The client will see the last item of Page 1 repeated as the first item of Page 2. Cursor-based pagination solves this, but offset pagination is sufficient for this scope.
* **Hard Page Size Cap via Backing Field:** `BookQueryParameters` uses a private backing field `_pageSize` with a `const int maxPageSize = 50`. A plain auto-property with a range annotation would still allow a client to attempt large allocations before validation fires. The backing field rejects over-limit values silently and instantly at the property setter, before the request ever reaches the service.
* **Request DTO for Query Parameters (`BookQueryParameters`):** Grouped all query string parameters (`PageNumber`, `PageSize`, `Genre`, `SearchTerm`) into a single DTO bound via `[FromQuery]`.
* **Why:** Passing four individual parameters through `IBookService` and `IBookRepository` would pollute every method signature. A single object is easier to extend (adding a `SortBy` field later requires zero interface changes) and easier to test.
* **Response Envelope (`PagedResponse<T>`):** Wrapped the list of items in a generic envelope that includes `TotalCount`, `PageNumber`, `PageSize`, and the derived `TotalPages`.
* **Why:** Without metadata, the client has no way to build pagination controls. It cannot know if there are more pages, how many buttons to render, or whether a direct page link is valid. The envelope provides all of this in a single response without a second HTTP round-trip.

## Centralized Error Handling
* **Strategy:** Implemented `IExceptionHandler` (introduced in .NET 8) to create a `GlobalExceptionHandler`, registered via `AddExceptionHandler<T>()` and activated with `app.UseExceptionHandler()` at the top of the middleware pipeline.
* **Why `IExceptionHandler` over custom middleware:** The interface is the idiomatic .NET 8+ approach. It injects cleanly via the DI container and delegates response writing to `IProblemDetailsService` (registered by `AddProblemDetails()`), which centralizes formatting, sets `Content-Type: application/problem+json`, and allows future customization from one place. The older middleware approach required manual JSON serialization and pipeline wiring.
* **Expected vs Unexpected Errors:** Expected errors — a user requesting a non-existent ID, a failed validation — are handled explicitly in the Service and Controller layers, returning `404` or `400`. Unexpected errors — null references, infrastructure faults — propagate up the call stack and are caught globally, returning a sanitized `500`.
* **Security vs Observability:** Returning a stack trace to the client is a critical security vulnerability: it exposes internal file paths, framework versions, and class names that attackers use to profile the system. The global handler returns only a generic `ProblemDetails` JSON to the client (RFC 7807 standard), while logging the full exception and stack trace server-side for the developer.
* **Correlation data in the response:** The `ProblemDetails` payload includes `Instance` (the request path) and a `traceId` extension (ASP.NET Core's `TraceIdentifier`). When a user reports an error, support can match their `traceId` to the exact server log entry without exposing internal details.
* **`OperationCanceledException` short-circuit:** Client-aborted requests (e.g., user navigates away) throw `OperationCanceledException`. Treating them as server errors produces misleading 500 counts in metrics and noisy error logs. The handler detects this case, logs it at `Information` level, and returns without writing a response body.
* **Why `ProblemDetails`:** It is an IETF standard (RFC 7807) for HTTP error responses. Using it ensures error payloads are consistent and machine-readable, which matters when clients or API gateways need to parse error bodies programmatically.
* **Why catching and doing nothing is worse:** A swallowed exception hides the system state. The application may continue in a corrupted state, producing silent data loss or wrong results with no log trail to investigate.

## Unit Testing Strategy

### Project: `BookCatalog.Tests` (xUnit + Moq)

A dedicated `BookCatalog.Tests` project was added to the solution alongside the three production projects. It references only `BookCatalog.Core` — it has no knowledge of `BookCatalog.Infrastructure` or `BookCatalog.API`. This mirrors the dependency direction of the production code and keeps the test suite free of infrastructure concerns.

**Tools chosen:**
* **xUnit** — the idiomatic testing framework for .NET. Each `[Fact]` method is instantiated in its own class instance, which enforces test isolation without extra configuration.
* **Moq** — a strongly-typed mocking library used exclusively to fake `IBookRepository`. The mocks live in constructor fields so each test receives a brand-new `Mock<T>`, preventing any state leak between runs.

**File layout:**
| File | What it tests |
|---|---|
| `BookServiceTests.cs` | All five `BookService` methods — business logic, mapping, and repository interaction |
| `BookValidationTests.cs` | `ValidPublishYearAttribute` in isolation and wired into the full `CreateBookRequest` DTO |

Every test strictly follows the **Arrange / Act / Assert** pattern with inline `// Arrange`, `// Act`, `// Assert` comments, and every method name follows the `MethodName_StateUnderTest_ExpectedBehavior` convention.

---

### Testing Decisions

* **Unit boundary at `IBookRepository`:** A unit in this suite is a single public method of `BookService`. The boundary is drawn at `IBookRepository` — everything behind that interface (`InMemoryBookRepository`, and later EF Core) is outside the scope of what these tests are proving. `ILogger<BookService>` is also faked, because logging is an infrastructure concern, not part of the business contract. The mapper methods (`ToResponse`, `ToBook`, `ApplyUpdate`) are inside the boundary intentionally: they contain no I/O, so testing them through `BookService` proves the entire transformation chain without needing a dedicated mapper test class.
* **Mocks with `Verify`, not just stubs:** `Mock<IBookRepository>` plays two roles. First, it returns canned data via `.Setup(...).ReturnsAsync(...)` so the test controls what the repository appears to see (stub behaviour). Second, it proves the service interacted with the repository correctly via `.Verify(..., Times.Once/Never)` (mock behaviour). The `Verify` calls are the critical part: `UpdateBookAsync_WhenBookDoesNotExist_ReturnsNull` uses `Verify(r => r.UpdateAsync(...), Times.Never)` to prove the service short-circuits before touching the store. A plain stub that only checked the return value could never detect a missing null-guard.
* **No dependency on `InMemoryBookRepository`:** `InMemoryBookRepository` will be replaced by an EF Core implementation in a future sprint. If the tests were wired to it directly, they would break the moment that swap happened — not because `BookService` regressed, but because the infrastructure changed underneath it. By depending only on `IBookRepository` through a mock, the tests are permanently immune to infrastructure churn and run in milliseconds with no network or disk involvement.
* **Constructor injection for zero shared state:** xUnit creates a new `BookServiceTests` instance for every `[Fact]`, and the constructor always re-creates `_mockRepo` and `_sut`. If the mocks were static or shared across tests, a previous test's `.Setup(...)` could bleed into the next one — producing a test that appears to pass because it's riding on state it didn't set up itself. In CI, tests run in any order and sometimes in parallel; shared state turns these into random, non-reproducible failures that are extremely hard to diagnose.
* **Error paths over happy paths:** The happy path for `GetBookByIdAsync` (book found → mapper runs → response returned) mostly exercises the mapper, which is trivially correct once written. The error paths — `GetBookByIdAsync_WhenBookDoesNotExist_ReturnsNull`, `UpdateBookAsync_WhenBookDoesNotExist_ReturnsNull`, `DeleteBookAsync_WhenBookDoesNotExist_ReturnsFalse` — test branching logic that directly determines the HTTP status code the controller sends back. A regression in the null-guard of `UpdateBookAsync` would cause a `NullReferenceException` that `GlobalExceptionHandler` catches and returns as a `500`, when the client deserved a `404`. Error paths protect observable, user-facing behaviour that happy paths simply cannot reach.
* **What this suite proves and what it does not:** The tests prove that `BookService` correctly orchestrates `IBookRepository` calls and correctly maps domain models to response DTOs — given that the repository behaves as the mock declares. They say nothing about whether `InMemoryBookRepository` (or the future EF Core implementation) actually stores, retrieves, updates, and deletes data correctly against a real data source. Integration tests, outside this suite's scope, are required to verify that contract. The unit tests guard the service layer; they have no opinion about the infrastructure layer beneath it.
* **`ValidPublishYear` gets its own test class:** The attribute is a pure function of its input — no mocks needed. Separating it into `BookValidationTests` keeps the two concerns (business logic and validation rules) in distinct files, making failures immediately obvious. The class also tests the attribute both in isolation (direct `GetValidationResult` call) and wired into the full `CreateBookRequest` DTO via `Validator.TryValidateObject`, which is the same code path ASP.NET Core uses internally — so the tests are realistic, not just testing the attribute in a vacuum.
* **What was deliberately not tested, and why:** Four areas were excluded by conscious decision, not by accident.
  * `BooksController` — the controller contains zero business logic. Every method is a one-liner that delegates to `IBookService` and maps the result to an HTTP status code. Unit-testing it would mean mocking `IBookService` and asserting that the controller called it — which proves the controller delegates, not that the system works. That level of confidence belongs in integration tests.
  * `InMemoryBookRepository` — the repository is the *infrastructure* layer. Unit-testing it against its own in-memory dictionary would prove nothing useful (the dictionary works, C# works). What matters is that the *interface contract* is correctly implemented against a real data source — a concern for integration tests, not unit tests.
  * `BookMapper` — the three mapper methods (`ToResponse`, `ToBook`, `ApplyUpdate`) are pure functions with no branching and no I/O. They are exercised by every `BookServiceTests` happy-path test, which asserts the mapped field values directly. A dedicated mapper test class would duplicate those assertions without adding coverage of any new code path.
  * `GlobalExceptionHandler` — the handler is middleware that intercepts unhandled exceptions and writes a `ProblemDetails` response. Unit-testing it requires faking `HttpContext`, `IProblemDetailsService`, and the exception pipeline, for very little return: the handler has one branch (is the exception an `OperationCanceledException`?), and that branch is already documented and justified. Middleware behaviour is better verified through integration or end-to-end tests where the full pipeline runs.


## What the Data Access Abstraction Hides

`IBookRepository` exposes five methods: `GetAllAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, and `DeleteAsync`. Everything behind those five signatures is deliberately invisible to `BookService` and `BooksController`. Concretely:

* **Storage engine.** The current implementation is a `ConcurrentDictionary<Guid, Book>` held in process memory. The future implementation will be EF Core talking to a relational database. `BookService` cannot tell the difference — it sees only the interface.
* **Data format and serialisation.** The in-memory store works with `Book` objects directly. A database implementation will translate between C# objects and table rows, handle column mapping, and manage change tracking. None of that leaks upward.
* **Failure modes.** A network timeout, a deadlock, or a connection pool exhaustion all manifest as exceptions thrown from behind the interface. `BookService` does not know whether a failure came from a dictionary operation or a SQL call; it only knows the awaited `Task` faulted.
* **ID generation.** `CreateAsync` receives a `Book` with `Id == Guid.Empty` and returns a `Book` with a fully assigned `Id`. The service never calls `Guid.NewGuid()` — it assumes the repository owns that responsibility. Switching to a database-generated identity column in the future requires no change to `BookService`.
* **Concurrency strategy.** `ConcurrentDictionary` handles thread-safety internally. An EF Core implementation would use transactions or optimistic concurrency tokens. The service is oblivious to either mechanism.
* **Query execution.** `GetAllAsync` receives a `BookQueryParameters` object and returns a pre-filtered, pre-paginated page of results alongside the total count. The service never touches `.Where()`, `.Skip()`, or `.Take()` — it delegates the entire query strategy to the implementation. This means filtering logic can be pushed to the database (a `WHERE` clause) or kept in memory without any change to the service signature.

## What Was Painful to Change from Week 1, and What That Tells You

Week 1 started as a single `BookCatalog.API` project with everything in one place — models, service, repository, and controllers all in one `.csproj`. The refactor to a three-project solution in Week 2 exposed several friction points:

* **Moving `ValidPublishYearAttribute` was a dependency direction problem.** The attribute lives in `Core` alongside the DTOs that use it. Initially it sat in the API project. Moving it required updating every `using` directive that referenced it and thinking carefully about which project owned it. The friction was instructive: it revealed that the original placement violated the dependency rule — a `Core` DTO was indirectly depending on something defined in `API`. The fix was straightforward once the rule was clear, but the fact that it compiled originally (because everything was in one project) masked the violation entirely.
* **`IBookRepository` had to move from `Repositories/` to `Interfaces/`.** In a single-project codebase, a folder named `Repositories/` inside the project root feels natural. When the project was split, putting the *interface* in a folder called `Repositories/` inside `Core` sent the wrong signal — it implied that `Core` knew about repository implementations. Renaming the folder to `Interfaces/` was a small change with a large communicative effect. The pain was minimal, but it highlighted that folder naming matters more in a multi-project solution because the folder is the only hint about what the project owns.
* **`BookService` depending on `ILogger<BookService>` required adding a NuGet package to `Core`.** In the original single-project setup, `ILogger<T>` was available implicitly through the ASP.NET Core SDK. Once `BookService` moved to a plain class library, the implicit imports disappeared. The fix — adding `Microsoft.Extensions.Logging.Abstractions` — was a single command, but the root cause was that the original design made `BookService` depend on a framework type without acknowledging that dependency explicitly. A class library forces you to be explicit about every dependency, which is ultimately healthier.
* **The `InMemoryBookRepository` lifetime had to change to `Singleton`.** In a single project it is easy to register everything as `Scoped` and not think about it. When the data store is in-process memory, a `Scoped` registration destroys the `ConcurrentDictionary` at the end of every request, wiping all data. The bug would have been silent and confusing. The multi-project refactor didn't cause this problem, but physically separating the layers forced a more deliberate look at DI registration — and the right lifetime became obvious once the infrastructure was treated as a separate concern.

**What the pain tells you about the original design:** the single-project layout was not wrong for Week 1, but it let structural violations hide. The compiler enforced nothing about dependency direction. The three-project split made those violations visible and required they be resolved, not just tolerated. The refactor itself was low-cost precisely because the *logical* layering was already in place — the service never called the repository directly by class name, it always went through `IBookRepository`. Physical project boundaries can only enforce what logical discipline has already established.