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

## SOLID Principles in This Codebase

* **Single Responsibility — `BookService` has one reason to change.** `BookService` should only be modified if the business rules for managing books change — for example, if a new rule requires that a book's ISBN must be unique across the catalog before creation is allowed. It should never change because the database technology changes (that is `InMemoryBookRepository`'s reason to change) or because a new HTTP route is added (that is `BooksController`'s reason to change). Each class owns exactly one axis of change.

* **Open/Closed — `InMemoryBookRepository.GetAllAsync` is the current violation point.** Adding a new filter (e.g., `SortBy`) requires opening `InMemoryBookRepository.cs` and adding another conditional block to the method. This is a direct Open/Closed violation. The fix is already implicit in the design: `BookQueryParameters` is the extension point. Adding `SortBy` to that class requires no change to `IBookRepository`'s signature — only the implementation changes. When the repository is replaced with EF Core, the sorting will be expressed as an `ORDER BY` clause pushed to the database, again without touching the interface or the service.

* **Interface Segregation — `IBookRepository` is intentionally narrow.** The interface defines exactly five methods that every caller actually needs. There is no `BulkDeleteAsync`, no `GetStatisticsAsync`, no `ExistsAsync`. If those were added speculatively, any test that mocks `IBookRepository` would need to provide implementations for methods it doesn't care about, creating fragile, noisy test setup. A focused interface means a focused mock: in `BookServiceTests`, every `.Setup(...)` call maps directly to a method the test is actually exercising.

* **Dependency Inversion — DIP is the rule; DI is the tool.** Dependency Injection (passing `IBookRepository` through `BookService`'s constructor) is the *mechanism*. Dependency Inversion is the *architectural rule* that made that injection meaningful: `BookService` depends on the `IBookRepository` abstraction, not on `InMemoryBookRepository` directly. The distinction matters because you could use DI to inject `InMemoryBookRepository` concretely — that would be DI without DIP, and it would couple the service to the infrastructure layer. The project respects DIP because `BookService` lives in `Core`, has no reference to `Infrastructure`, and cannot even `using` the concrete class without a project reference that the compiler would reject.

* **Repository pattern — the counter-argument.** The standard critique of the repository pattern in modern .NET is that EF Core's `DbContext` and `IQueryable<T>` already implement the Unit of Work and Repository patterns natively. Wrapping `DbContext` in a custom `IBookRepository` adds an extra layer that can suppress EF Core's advanced features (like `IQueryable` composition, lazy loading, and change tracking). The pattern is justified here because the application has no EF Core yet — `IBookRepository` is the seam that allows the in-memory store and the future database implementation to be swapped without touching any business logic. Once EF Core is introduced, whether to keep the repository layer or expose `DbContext` directly is worth revisiting.

## Clean Code Decisions

* **Naming: methods must reveal intent without a comment.** The best-named method in the project is `GetBookByIdAsync(Guid id)` — you can read it aloud and know exactly what it does, what it takes, and that it is asynchronous without opening the body. The closest to a name-smell in the actual codebase is `ApplyUpdate(this Book book, UpdateBookRequest request)` in `BookMapper`. The name says nothing about the side-effect: the method mutates `book` in place and returns `void`, but nothing in the name signals that a caller's object is about to change. A more honest name would be `MutateFromRequest` or simply being redesigned to return a new `Book` instance. It works correctly — but the name makes a reader assume it returns something, and finding that it mutates silently is a small but real friction point.

* **Comments: explain why, never what.** The codebase uses comments in two places, both intentional. In `InMemoryBookRepository.GetAllAsync`, the inline step comments (`// 1. Apply Filters`, `// 2. Get the Total Count`, `// 3. Apply Pagination`) do describe *what* the steps are — but the method is implementing an algorithm where the sequence order carries real semantic weight (filtering before counting before paginating is a deliberate decision documented in `## Pagination and Filtering`). Those comments exist to make the sequence explicit at a glance. By contrast, the comment `// ConcurrentDictionary is thread-safe for web applications` explains *why* that specific type was chosen over a plain `Dictionary<K,V>` — that is a pure why-comment and would be lost if the code were left to speak for itself.

* **Method length: `GetAllAsync` does three things, and that is the boundary.** `InMemoryBookRepository.GetAllAsync` applies filters, computes the total count, and paginates — three sequential steps. The method is long enough that comments mark each step, but it is not extractable into smaller private methods without making the data flow harder to follow (each step depends on the result of the previous one, and they all operate on the same `IEnumerable<Book>` query variable). `BookService`'s methods are all short precisely because the query logic was pushed down to the repository. If `BookService` were also doing filtering and pagination, the "and" test would have failed and extraction would have been necessary.

## Week 3: Moving to a Relational Database with Entity Framework Core

### Why the Data Model Is Shaped This Way

The original `Book` entity stored the author's name as a raw string. That was fine for a single-entity CRUD API backed by an in-memory dictionary, but it collapses the moment you need to answer questions like "show me all books by this author" or "update the author's biography in one place." A flat string means duplicated data, inconsistent casing, and no way to attach metadata (like a biography) to the author without denormalizing it onto every book row.

The data model was normalized into four entities: `Author`, `Book`, `User`, and `Loan`.

* **`Author` as a first-class entity** — separating the author into its own table means an author's name and biography live in exactly one row. The one-to-many relationship (`Author.Books ↔ Book.AuthorId`) guarantees that every book points to a real, validated author. If the author's name changes (a pen-name update, a spelling correction), it changes in one place, and every `BookResponse` that resolves `book.Author?.Name` picks up the fix immediately.
* **`User` as a standalone entity** — users exist independently of books. An email uniqueness constraint at the database level prevents duplicate accounts without relying on application-layer checks that could race under concurrent requests. The `User` entity is deliberately thin right now (just `Email` and `FullName`) — it will grow when authentication is added, but starting with a minimal surface avoids speculative fields that never get used.
* **`Loan` as a transactional history table** — a `Loan` row is not a "current state" flag; it is a historical record of "User X borrowed Book Y at time T." Marking a book as returned means setting `ReturnedAt`, not deleting the row. This design preserves the full borrowing history for analytics, dispute resolution, and auditing. The alternative — a boolean `IsCurrentlyBorrowed` on `Book` — would lose all historical data and make questions like "who had this book last month?" unanswerable. `IsAvailable` on `Book` is a denormalized convenience flag that avoids a subquery on every book listing; it must be kept in sync with `Loan.ReturnedAt`, which is a known maintenance burden worth accepting for read performance.
* **`RowVersion` (optimistic concurrency token)** — the `[Timestamp]` attribute on `Book.RowVersion` enables EF Core's built-in optimistic concurrency. When two users try to update the same book simultaneously, the second save will throw a `DbUpdateConcurrencyException` because the `RowVersion` value will have changed. This is cheaper than pessimistic locking (no `SELECT ... FOR UPDATE`) and is the correct strategy for a web API where conflicts are rare but catastrophic when silent.

### The Decision to Use SQL Server (and the Trade-Offs)

SQL Server was chosen because it is the default relational database for the Microsoft stack. EF Core's SQL Server provider is the most mature, best-documented, and most widely deployed in production .NET applications. The trade-offs are real:

* **Cost and licensing** — SQL Server is not free at scale. For a learning project this is irrelevant (SQL Server Developer Edition and LocalDB are free), but in production, licensing costs are a genuine reason teams migrate to PostgreSQL. The EF Core provider for PostgreSQL (`Npgsql.EntityFrameworkCore.PostgreSQL`) is a near drop-in replacement — the `DbContext` and model configuration would barely change, which is one of EF Core's strongest selling points.
* **Portability** — SQL Server runs natively on Windows and Linux (via Docker or direct install), but the operational ecosystem (SSMS, SQL Agent, linked servers) is Windows-centric. If the project were cloud-native from day one, Azure SQL or AWS RDS for SQL Server would be the deployment target. For local development, LocalDB or a Docker container is sufficient.
* **Feature fit** — SQL Server's full-text search, temporal tables, and JSON support are all features this project may eventually need. PostgreSQL has equivalents (and arguably better JSON support), but SQL Server's integration with the .NET toolchain (e.g., `dotnet ef` commands, Visual Studio's SQL Server Object Explorer) reduces friction during development.

The decision was pragmatic, not dogmatic. The `ApplicationDbContext` is the only class that knows about SQL Server. Swapping to PostgreSQL requires changing one NuGet package and one line in `Program.cs` — no domain or service code changes. (Note: Any existing migrations containing SQL Server-specific types or annotations would also need to be regenerated and validated for the new provider).

### Q31. How much of your business logic changed when you swapped in-memory storage for a database? What does that number tell you?

**`BookService` — minimal business logic changed.** Initially, the storage swap required zero logic changes. However, as the data model matured to include relational integrity (specifically, an `AuthorId` foreign key), the business logic *had* to evolve. We added validation in `CreateBookAsync` and `UpdateBookAsync` to verify that the author actually exists (via a new `AuthorExistsAsync` repository method) before persisting the book. We also updated `UpdateBookAsync` to reload the `Book` from the repository after a save, ensuring its navigation properties (like `Author.Name`) are accurately reflected in the final response. The core architecture held up beautifully, but relational realities required the service to be slightly more aware of data dependencies.

**`BooksController` — zero lines changed.** It delegates to `IBookService` and maps return values to HTTP status codes. It has no knowledge of storage.

**What did change (and the honest count):**

| File | Lines Changed | Reason |
|---|---|---|
| `Book.cs` (model) | ~8 lines | Replaced `string Author` with `Guid AuthorId` + navigation property. Added `IsAvailable`, `LoanHistory`, `RowVersion`. |
| `BookResponse.cs` (DTO) | 2 lines | `Author` → `AuthorName`, added `IsAvailable`. |
| `CreateBookRequest.cs` (DTO) | 2 lines | `string Author` → `Guid AuthorId`. |
| `UpdateBookRequest.cs` (DTO) | 2 lines | Same as above. |
| `BookMapper.cs` (mapper) | 3 lines | Map `AuthorId` instead of `Author`, resolve `Author?.Name ?? "Unknown"`, map `IsAvailable`. |
| `InMemoryBookRepository.cs` | 1 line | Search filter: `b.Author.ToLower()` → `b.Author?.Name?.ToLower()`. |

Total: ~18 lines across 6 files. Of those 18, zero are in `BookService` or `BooksController`.

**What that number tells you:** the changes were *domain corrections*, not storage adaptations. The old `Book` model was wrong — it modeled "author" as a string when it is semantically an entity. That lie was invisible while the entire system was a single-entity CRUD API, but it became untenable the moment a second entity (`Loan`) needed to reference both books and users. The storage swap itself (dictionary → database) required zero changes to the orchestration layer. The `IBookRepository` abstraction paid for itself exactly as designed — the service layer was immune to the infrastructure churn. If the domain model had been correctly normalized from Week 1, the change count would have been zero everywhere above the repository layer.

### Q32. Which of your Week 2 tests broke, and were they testing the right thing?

**Every `BookServiceTests` test that constructed a `Book` or a DTO with `Author = "..."` failed to compile.** That was 11 out of 13 `BookServiceTests` methods (only the two `DeleteBookAsync` tests survived unchanged because they never construct a `Book` with an author field). All 6 `BookValidationTests` that built `CreateBookRequest` or `UpdateBookRequest` objects also failed to compile.

The breakage was *compilation errors*, not runtime assertion failures. The property `Author` on `Book` changed from `string` to `Author?` (a navigation object), so every line that wrote `Author = "Robert C. Martin"` became a type mismatch. Similarly, the DTOs changed from `Author = "..."` to `AuthorId = Guid.NewGuid()`.

**Were they testing the right thing?** Yes — and the compilation failures prove it. The tests that broke were constructing domain objects and DTOs inline. When the domain model changed shape, those tests immediately surfaced the incompatibility at compile time, not at runtime in production. A test that compiles against a stale domain model is worse than useless — it gives false confidence. The fact that the tests broke loudly and immediately is exactly the behavior you want.

**What the fix looked like:**

* Every `Book` construction replaced `Author = "Eric Evans"` with `AuthorId = authorId` and (where the test asserts the mapped author name) `Author = new Author { Id = authorId, Name = "Eric Evans" }`.
* Every `CreateBookRequest` and `UpdateBookRequest` replaced `Author = "..."` with `AuthorId = Guid.NewGuid()`.
* Every assertion that checked `result.Author` changed to `result.AuthorName`.

No test logic changed — the same scenarios, the same Arrange/Act/Assert structure, the same Moq setups. Only the data shapes were updated. This confirms the tests were testing *behavior* (does the service correctly map, delegate, and short-circuit?), not *data structure* (does the `Book` class have a string called `Author`?). The data structure changed; the behavior did not; the test logic did not.

**One test deserves special mention:** `GetBookByIdAsync_WhenBookHasNoDescription_ReturnsMappedResponseWithNullDescription` originally set `Author = "Anonymous"`. After the change, it sets `AuthorId = Guid.NewGuid()` with no `Author` navigation property — meaning `book.Author` is `null`. This accidentally tests a new code path: `book.Author?.Name ?? "Unknown"` now resolves to `"Unknown"` for this test, which is the correct defensive behavior when the navigation property is not loaded. The test still passes because it only asserts `Description` is null, but it now also exercises the null-author fallback without intending to. This is a happy accident, not a designed outcome — a dedicated test for the `"Unknown"` fallback should be added.

### Q33. Your catalog now has ten million books. Which endpoint dies first, and why?

**`GET /api/books?searchTerm=...` dies first.** Every other endpoint is either a single-row lookup by primary key (`GET /api/books/{id}`, `PUT`, `DELETE`) or a write operation (`POST`). Those scale linearly with row count but remain fast because they hit the clustered index. The search endpoint is the outlier.

**Why it dies:**

1. **No index can help `LIKE '%term%'`.** The `SearchTerm` filter produces `WHERE Title LIKE '%term%' OR Author.Name LIKE '%term%'`. The leading wildcard (`%term%`) makes every B-tree index on `Title` and `Author.Name` useless — the database must perform a full table scan of 10 million rows on every request. At 10M rows with average row sizes of ~500 bytes, that is roughly 5 GB of data the engine must read, even for a single search query.

2. **The `COUNT(*)` doubles the cost.** The current implementation counts total matching rows (`totalCount = query.Count()`) *and* fetches the page. Without careful query construction, EF Core may execute two separate full scans: one for the count, one for the paginated results. At 10M rows, two full scans per request is fatal under any meaningful concurrent load.

3. **The `JOIN` to `Authors` multiplies the scan.** The search also checks `Author.Name`, which means the query must join `Books` and `Authors`. With 10M books and potentially hundreds of thousands of authors, the join itself is cheap (foreign key index), but scanning 10M joined rows for a substring match is not.

4. **`OFFSET` pagination compounds the problem.** If a user searches and navigates to page 100 with `PageSize = 50`, the query must `OFFSET 4950 ROWS FETCH NEXT 50 ROWS ONLY`. The database scans 5,000 rows to discard 4,950 and return 50. At page 5,000, it scans 250,000 rows. Combined with the full-text scan, this produces a query that takes seconds, not milliseconds.

**What would fix it:**

* **SQL Server Full-Text Search** — replace `LIKE '%term%'` with `CONTAINS(Title, @term)` or `FREETEXT(Title, @term)`. Full-text indexes use inverted word lists, reducing text search from O(n) to O(log n) with relevance ranking. This is a schema + query change, not an architecture change.
* **Elasticsearch / Meilisearch sidecar** — for advanced search (fuzzy matching, typo tolerance, faceted filtering), offload search to a dedicated engine and use the database only for transactional writes and single-row reads. This is an architecture change.
* **Keyset pagination** — replace `OFFSET/FETCH` with `WHERE Id > @lastSeenId ORDER BY Id FETCH NEXT 50 ROWS ONLY`. This eliminates the O(n) skip cost and makes page 5,000 as fast as page 1. This changes the API contract (clients send a cursor, not a page number).
* **Materialized search columns** — add a computed column `SearchText = Title + ' ' + Author.Name` with a non-clustered index. This avoids the join for search but trades write performance (the column must be maintained on insert/update).

The search endpoint is the first to die because it is the only one that combines three scaling anti-patterns: full table scan, cross-table join, and offset pagination — all on the hottest read path.

### Q21-24. Transactions & Concurrency

**21. What do the letters in ACID mean, in your own words?**
* **Atomicity**: All or nothing. If a multi-step operation fails halfway, the database rolls back to the starting state. No partial updates.
* **Consistency**: The database goes from one valid state to another. Constraints (like foreign keys) are never violated.
* **Isolation**: Concurrent transactions don't interfere with each other. If two people update a row at the exact same time, the DB ensures the result makes sense (usually by making one wait).
* **Durability**: Once a transaction is committed, it stays committed. A power failure a millisecond later won't lose the data.

**22. Which operation in your project needs a transaction, and what breaks without one?**
Borrowing a book needs a transaction. We must set the book's `IsAvailable` flag to `false` AND insert a new `Loan` record. Without a transaction, if the database crashes right after updating the book but before inserting the loan, the book becomes permanently "borrowed" by nobody. The data is corrupted.

**23. Two users try to borrow the last copy of a book at the same time. Walk through exactly what happens in your code. Are you sure?**
Yes, I am sure. This is handled gracefully by EF Core's Optimistic Concurrency Control using the `RowVersion` timestamp column on the `Book` entity.
1. User A and User B both fetch the book. `IsAvailable` is `true`. They both get the same `RowVersion` (e.g., `0x001`).
2. User A's thread reaches `SaveChangesAsync()` first. EF Core generates `UPDATE Books SET IsAvailable = 0 WHERE Id = @id AND RowVersion = 0x001`. The database updates the row and auto-increments the `RowVersion` to `0x002`.
3. User B's thread reaches `SaveChangesAsync()`. EF Core generates `UPDATE Books SET IsAvailable = 0 WHERE Id = @id AND RowVersion = 0x001`. The database finds 0 matching rows because the `RowVersion` is now `0x002`. 
4. EF Core detects 0 rows updated and throws a `DbUpdateConcurrencyException`. The explicit `IDbContextTransaction` in our `LendingRepository` catches the exception, rolls back the transaction, and throws it up the stack.
5. The API returns a 409 Conflict. User A gets the book, User B gets denied. No double-borrowing occurs.

**24. What is a race condition? Where is yours?**
A race condition happens when the outcome of a program depends on the unpredictable timing of concurrent threads. In this system, the race condition is the "check-then-act" flaw: checking if a book is available, then acting to borrow it. Without the `RowVersion` concurrency token and transaction, two threads could check `IsAvailable` at the same time (both see `true`), and both act (both borrow it), violating the real-world constraint that one physical book can only be lent to one person.