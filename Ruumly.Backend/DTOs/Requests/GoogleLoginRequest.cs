namespace Ruumly.Backend.DTOs.Requests;

/// <summary>
/// The 'credential' field is the Google ID token (JWT string)
/// returned by Google Identity Services on the frontend.
/// </summary>
public record GoogleLoginRequest(string Credential);
