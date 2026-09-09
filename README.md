# Book Catalog API

A RESTful API for managing a book catalog, built over 4 weeks as a learning project. The platform supports CRUD operations for books, authors, users, and a full lending system (borrow/return with history).

Built with **.NET 10**, **ASP.NET Core**, **Entity Framework Core**, and **SQL Server**. Containerised with **Docker**.

---

## Prerequisites

| Tool | Version | Install |
|---|---|---|
| Docker Desktop | Latest | [docker.com](https://www.docker.com/products/docker-desktop) |
| .NET SDK | 10.0+ | [dotnet.microsoft.com](https://dotnet.microsoft.com/download) |
| Git | Any | [git-scm.com](https://git-scm.com) |

> You only need the .NET SDK if you want to run the project locally without Docker, or to run the tests.

---

## Running with Docker (recommended)

This is the fastest way. One command starts both the API and the database.

**1. Clone the repository**
```
git clone https://github.com/IslamAli-0/BookCatalog.API.git
cd BookCatalog.API
```

**2. Create your environment file**
```
cp .env.example .env
```
Open `.env` and set a strong SQL Server password (min 8 chars, must include uppercase, lowercase, and a digit):
```env
DB_PASSWORD=Your_Secure_Password_Here!
```

**3. Start everything**
```
docker compose up
```

Docker will:
- Pull the SQL Server 2022 image
- Build the API image
- Start the database container
- Start the API container (which auto-applies EF Core migrations on startup)

Wait about 10-15 seconds for SQL Server to initialise on first run.

**4. Open Swagger UI**

Navigate to: **http://localhost:8080/swagger**

The interactive documentation lists every endpoint. You can call them directly from the browser.

**5. Stop everything**
```
docker compose down
```
Your data is persisted in a Docker named volume (`sqlserver_data`) and survives restarts.
To also wipe the data: `docker compose down -v`

---

## Running Tests

Unit tests use xUnit and Moq. They have no external dependencies - no database, no Docker.

```
dotnet test
```

Run from the repository root. All tests in `BookCatalog.Tests` will execute.

---

## Running Locally (without Docker)

**1. Set up a SQL Server instance**

Use SQL Server Developer Edition, LocalDB, or Docker for just the database:
```
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=YourPassword!" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
```

**2. Configure the connection string via User Secrets**
```
cd BookCatalog.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=BookCatalogDb;User Id=sa;Password=YourPassword!;TrustServerCertificate=True;"
```

**3. Run the API**
```
dotnet run --project BookCatalog.API
```

Swagger UI will be available at **https://localhost:7xxx/swagger** (port shown in terminal output).

---

## API Endpoints

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/books` | List books (paginated, filterable) |
| `GET` | `/api/books/{id}` | Get a single book by ID |
| `POST` | `/api/books` | Create a new book |
| `PUT` | `/api/books/{id}` | Update a book |
| `DELETE` | `/api/books/{id}` | Delete a book |
| `POST` | `/api/books/{id}/borrow` | Borrow a book |
| `POST` | `/api/books/{id}/return` | Return a borrowed book |
| `GET` | `/api/books/{id}/loans` | Get borrowing history for a book |
| `GET` | `/health/live` | Liveness check (is the process running?) |
| `GET` | `/health/ready` | Readiness check (can the service reach the database?) |

### Query Parameters for GET /api/books

| Parameter | Type | Default | Description |
|---|---|---|---|
| `pageNumber` | int | 1 | Page number |
| `pageSize` | int | 10 | Results per page (max 50) |
| `genre` | string | - | Filter by exact genre |
| `searchTerm` | string | - | Search in title and author name |

### Seeded Test Data (Development mode)

When running in Development, the app seeds:
- **Author ID**: `11111111-1111-1111-1111-111111111111` - Test Author
- **User ID**: `22222222-2222-2222-2222-222222222222` - Test User (test@user.com)

Use these IDs in the `authorId` and `userId` fields of your requests.

---

## Project Structure

```
BookCatalog.API/
├── BookCatalog.API/          # HTTP layer: controllers, middleware, Program.cs
│   ├── Controllers/
│   ├── Handlers/             # GlobalExceptionHandler
│   └── Design.md             # Architecture decisions (updated each week)
├── BookCatalog.Core/         # Domain layer: models, interfaces, services, DTOs
│   ├── Models/
│   ├── Interfaces/
│   ├── Services/
│   ├── DTOs/
│   └── Mappers/
├── BookCatalog.Infrastructure/  # Data layer: EF Core, repositories, migrations
│   ├── Data/                 # ApplicationDbContext
│   ├── Repositories/
│   └── Migrations/
├── BookCatalog.Tests/        # Unit tests (xUnit + Moq)
├── Dockerfile                # Multi-stage build
├── docker-compose.yml        # API + SQL Server
└── .env.example              # Environment variable template
```

---

## Environment Variables

| Variable | Required | Description |
|---|---|---|
| `DB_PASSWORD` | Yes | SQL Server SA password (set in `.env`) |
| `ASPNETCORE_ENVIRONMENT` | No | Set to `Development` to enable Swagger and seeding |
| `ConnectionStrings__DefaultConnection` | No | Auto-built by docker-compose |
