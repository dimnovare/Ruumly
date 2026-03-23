namespace Ruumly.Backend.Helpers;

/// <summary>Thrown when a request is authenticated but not permitted (HTTP 403).</summary>
public class ForbiddenException(string message) : Exception(message);

/// <summary>Thrown when a resource is not found (HTTP 404).</summary>
public class NotFoundException(string message) : Exception(message);

/// <summary>Thrown when a resource already exists (HTTP 409).</summary>
public class ConflictException(string message) : Exception(message);
