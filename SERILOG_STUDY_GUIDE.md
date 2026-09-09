# Structured Logging with Serilog Study Guide

## What is Structured Logging?

Traditional logging writes plain text strings to a file or console:
"User 123 borrowed book 456 at 10:00 AM"

This is easy for humans to read but terrible for machines. If you want to search a massive log file for "all books borrowed by User 123", you have to use regular expressions to parse the string.

**Structured Logging** keeps the variables separate from the message template. It outputs a machine-readable format (usually JSON) where the data fields are preserved:

`json
{
  "Timestamp": "2026-09-09T10:00:00.000Z",
  "Level": "Information",
  "MessageTemplate": "User {UserId} borrowed book {BookId}",
  "Properties": {
    "UserId": 123,
    "BookId": 456
  }
}
`

Now, a log aggregator (like Elasticsearch, Splunk, Seq, or Datadog) can instantly filter by Properties.UserId == 123. No string parsing required.

---

## Why Serilog?

ASP.NET Core has built-in logging (ILogger<T>), and it *does* support structured parameters (e.g., _logger.LogInformation("User {UserId}", id)). 

However, by default, the built-in Console logger just flattens everything into plain text.

**Serilog** is the industry standard for .NET because:
1. It is built from the ground up for structured data.
2. It has hundreds of "Sinks" (plugins to write logs to Console, File, Elasticsearch, SQL Server, etc.).
3. It has "Enrichers" (plugins to automatically add data to every log entry, like Thread ID, Machine Name, or Request ID).

---

## How We Implemented It

### Step 1: NuGet Packages
We installed Serilog.AspNetCore. This meta-package includes the core Serilog library, the console sink, and the ASP.NET Core hosting integration.

### Step 2: Program.cs Configuration

`csharp
builder.Host.UseSerilog((context, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter()));
`

- UseSerilog(): Tells ASP.NET Core to replace its built-in logging engine with Serilog.
- ReadFrom.Configuration(): Allows you to configure Serilog via ppsettings.json (log levels, etc.).
- Enrich.FromLogContext(): Allows adding dynamic properties to logs in a specific block of code.
- WriteTo.Console(new CompactJsonFormatter()): Writes to the console, but instead of human-readable text, it outputs optimized, machine-readable JSON.

### Step 3: HTTP Request Logging

`csharp
app.UseSerilogRequestLogging();
`

Normally, ASP.NET Core logs multiple events per HTTP request (routing matched, executing endpoint, executed endpoint, etc.). This creates noise.
UseSerilogRequestLogging() replaces all that noise with a **single, consolidated log event** at the end of the request. It includes the method, path, status code, and timing in milliseconds.

---

## The Power of the Request ID (TraceIdentifier)

When you look at production logs, multiple users are making requests at the exact same time. The logs are interleaved:
- Request A starts
- Request B starts
- Request A queries DB
- Request B queries DB

How do you know which DB query belongs to Request A?

ASP.NET Core automatically generates a TraceIdentifier (Request ID) for every HTTP request. Because Serilog integrates with ASP.NET Core, it automatically includes this RequestId in the JSON properties of every log entry generated during that request.

If a request fails, the API returns a ProblemDetails JSON response to the user, which includes the 	raceId. 
The user can give you that 	raceId ("Hey, I got an error, my trace ID is 00-12345..."). 
You can paste that ID into your logging system and instantly see **only** the logs for that exact request, ignoring everything else happening on the server.

---

## Self-Check Questions

1. What is the difference between writing "User " + userId + " logged in" vs "User {UserId} logged in"?
2. Why do we output JSON to the console instead of human-readable text?
3. What is the purpose of pp.UseSerilogRequestLogging()?
4. How do you correlate multiple log entries that belong to the same HTTP request?
5. (Advanced) If you wanted to log to a file instead of the console, what Sink would you install?
