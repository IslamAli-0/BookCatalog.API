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