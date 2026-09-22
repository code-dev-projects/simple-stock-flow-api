using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SimpleStockFlow.Adapters.Rest.Contracts;
using SimpleStockFlow.Application.Ports.Inbound;

namespace SimpleStockFlow.Adapters.Rest.Controllers;

/// <summary>Signing in, and the creation of the operators who sign in.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthenticate _auth;

    /// <param name="auth">The inbound port; the controller knows no other way in.</param>
    public AuthController(IAuthenticate auth) => _auth = auth;

    /// <summary>Signs in and returns a token (E-01).</summary>
    /// <remarks>
    /// The token lives 60 minutes and carries the role every other operation reads. An unknown
    /// user and a wrong password answer the same sentence on purpose, so the reply cannot be
    /// used to find out which usernames exist.
    /// </remarks>
    /// <param name="request">Username and password.</param>
    /// <param name="ct">Cancellation of the request.</param>
    /// <response code="200">The token, its expiry, the normalised username and the role.</response>
    /// <response code="400">A field is missing or the JSON does not bind.</response>
    /// <response code="422">Wrong credentials: "Usuario o contraseña incorrectos."</response>
    [HttpPost("login")]
    // The one anonymous action of the API, and the reason the attribute sits here and not on
    // the class: at class level it also reached Register and overrode its [Authorize], because
    // AllowAnonymous always wins. That was defect A-1.
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResult), StatusCodes.Status200OK, "application/json")]
    [ValidationProblemResponse]
    [ProblemResponse(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AuthResult>> Login([FromBody] LoginRequest request, CancellationToken ct) =>
        Ok(await _auth.LoginAsync(new Credentials(request.Username, request.Password), ct));

    /// <summary>Creates an operator (E-02). Administrators only.</summary>
    /// <remarks>
    /// Answers no Location header: the only address it could name is /api/users/{id}, a route
    /// that does not exist, and a client following it would fail far from the cause (D-C6).
    /// When two requests race for the same username, the unique index leaves one row.
    /// </remarks>
    /// <param name="request">Username, password and role.</param>
    /// <param name="ct">Cancellation of the request.</param>
    /// <response code="201">The identifier of the new operator.</response>
    /// <response code="400">A field is missing or the JSON does not bind.</response>
    /// <response code="401">No token, or a token that does not validate.</response>
    /// <response code="403">A token whose role is not admin.</response>
    /// <response code="422">The username is taken, or the role is outside {admin, seller}.</response>
    [HttpPost("register")]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(typeof(CreatedResource), StatusCodes.Status201Created, "application/json")]
    [ValidationProblemResponse]
    [EmptyResponse(StatusCodes.Status401Unauthorized)]
    [EmptyResponse(StatusCodes.Status403Forbidden)]
    [ProblemResponse(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var id = await _auth.RegisterAsync(new Credentials(request.Username, request.Password), request.Role, ct);

        // D-C6: no Location. The only place it could point at is /api/users/{id}, a route that
        // does not exist, and a client following it would fail far from the cause.
        return StatusCode(StatusCodes.Status201Created, new CreatedResource(id));
    }
}
