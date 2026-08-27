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