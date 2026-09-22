using NSubstitute;
using SimpleStockFlow.Application.Ports.Inbound;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Application.Services;
using SimpleStockFlow.Domain.Common;
using SimpleStockFlow.Domain.Identity;

namespace SimpleStockFlow.Application.UnitTests;

public sealed class AuthenticationServiceTests
{
    private const string StoredHash = "pbkdf2$stored$hash";
    private const string RightPassword = "correcta";
    private const string WrongPassword = "incorrecta";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenGenerator _tokens = Substitute.For<ITokenGenerator>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly AuthenticationService _service;

    public AuthenticationServiceTests() =>
        _service = new AuthenticationService(_users, _hasher, _tokens, _unitOfWork);

    [Fact]
    public async Task Returns_the_token_with_its_expiry_username_and_role()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(1);
        GivenRegisteredUser("ana", Roles.Admin);
        _hasher.Verify(RightPassword, StoredHash).Returns(true);
        _tokens.Generate(Arg.Any<User>()).Returns(("a.jwt.token", expiry));

        var result = await _service.LoginAsync(new Credentials("ana", RightPassword));

        result.AccessToken.Should().Be("a.jwt.token");
        result.ExpiresAt.Should().Be(expiry);
        result.Username.Should().Be("ana");
        result.Role.Should().Be(Roles.Admin);
    }

    [Fact]
    public async Task Never_puts_the_stored_hash_anywhere_in_the_result()
    {
        GivenRegisteredUser("ana", Roles.Seller);
        _hasher.Verify(RightPassword, StoredHash).Returns(true);
        _tokens.Generate(Arg.Any<User>()).Returns(("a.jwt.token", DateTimeOffset.UtcNow));

        var result = await _service.LoginAsync(new Credentials("ana", RightPassword));

        // CA-07.1: the hash must not leak through any field, not even by accident.
        new[] { result.AccessToken, result.Username, result.Role }
            .Should().NotContain(value => value.Contains(StoredHash, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rejects_an_unknown_user_and_a_wrong_password_with_the_very_same_message()
    {
        _users.FindByUsernameAsync("fantasma", Arg.Any<CancellationToken>()).Returns((User?)null);
        GivenRegisteredUser("ana", Roles.Seller);
        _hasher.Verify(WrongPassword, StoredHash).Returns(false);

        var unknownUser = await Catch(() => _service.LoginAsync(new Credentials("fantasma", RightPassword)));
        var wrongPassword = await Catch(() => _service.LoginAsync(new Credentials("ana", WrongPassword)));

        // CA-07.2: telling the two apart is how an attacker enumerates usernames.
        unknownUser.Should().NotBeNull();
        wrongPassword.Should().NotBeNull();
        wrongPassword!.Message.Should().Be(unknownUser!.Message);
    }

    [Fact]
    public async Task Hashes_against_a_decoy_when_the_user_does_not_exist()
    {
        _users.FindByUsernameAsync("fantasma", Arg.Any<CancellationToken>()).Returns((User?)null);
        _hasher.DecoyHash.Returns("decoy");

        await Catch(() => _service.LoginAsync(new Credentials("fantasma", RightPassword)));

        // CA-07.2: returning early for an absent user answers in microseconds, while a real
        // check costs 100_000 PBKDF2 iterations. Same wording, different clock, same leak.
        _hasher.Received(1).Verify(RightPassword, "decoy");
    }

    [Fact]
    public async Task Does_not_mint_a_token_when_the_password_is_wrong()
    {
        GivenRegisteredUser("ana", Roles.Seller);
        _hasher.Verify(WrongPassword, StoredHash).Returns(false);

        await Catch(() => _service.LoginAsync(new Credentials("ana", WrongPassword)));

        _tokens.DidNotReceive().Generate(Arg.Any<User>());
    }

    [Fact]
    public async Task Logging_in_commits_nothing()
    {
        GivenRegisteredUser("ana", Roles.Seller);
        _hasher.Verify(RightPassword, StoredHash).Returns(true);
        _tokens.Generate(Arg.Any<User>()).Returns(("a.jwt.token", DateTimeOffset.UtcNow));

        await _service.LoginAsync(new Credentials("ana", RightPassword));

        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stores_the_hash_the_hasher_produced_and_never_the_plain_password()
    {
        _users.FindByUsernameAsync("nueva", Arg.Any<CancellationToken>()).Returns((User?)null);
        _hasher.Hash("secreta").Returns(StoredHash);

        await _service.RegisterAsync(new Credentials("nueva", "secreta"), Roles.Seller);

        // CA-07.5: the plain password never reaches the domain or the database.
        await _users.Received(1).AddAsync(
            Arg.Is<User>(user => user.PasswordHash == StoredHash),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_a_username_that_is_already_taken()
    {
        GivenRegisteredUser("ana", Roles.Seller);

        var failure = await Catch(() => _service.RegisterAsync(new Credentials("ana", "secreta"), Roles.Seller));

        failure.Should().NotBeNull();
        await _users.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Registering_commits_once()
    {
        _users.FindByUsernameAsync("nueva", Arg.Any<CancellationToken>()).Returns((User?)null);
        _hasher.Hash("secreta").Returns(StoredHash);

        await _service.RegisterAsync(new Credentials("nueva", "secreta"), Roles.Seller);

        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Looks_the_user_up_by_the_same_name_the_domain_stores()
    {
        // The aggregate lowercases and trims the username, so a lookup that does not would
        // let "Ana " register a second account that can never log in.
        _users.FindByUsernameAsync("ana", Arg.Any<CancellationToken>()).Returns((User?)null);
        _hasher.Hash("secreta").Returns(StoredHash);

        await _service.RegisterAsync(new Credentials("  Ana  ", "secreta"), Roles.Seller);

        await _users.Received(1).FindByUsernameAsync("ana", Arg.Any<CancellationToken>());
    }

    private void GivenRegisteredUser(string username, string role) =>
        _users.FindByUsernameAsync(username, Arg.Any<CancellationToken>())
            .Returns(User.Create(username, StoredHash, role));

    private static async Task<DomainException?> Catch(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (DomainException exception)
        {
            return exception;
        }
    }
    [Fact]
    public async Task Refuses_to_register_an_administrator_through_the_registration_port()
    {
        _users.FindByUsernameAsync("nuevo", Arg.Any<CancellationToken>()).Returns((User?)null);
        // Hashing is stubbed so the only thing left that can refuse this call is the rule.
        _hasher.Hash(RightPassword).Returns(StoredHash);

        // DP-04, decided 2026-09-20: the only administrator a running system gains is the one the
        // deployment provisions. Registration is how a seller is created and nothing else, so the
        // rule lives here rather than in the controller -- an adapter is not where policy belongs.
        var refusal = async () =>
            await _service.RegisterAsync(new Credentials("nuevo", RightPassword), Roles.Admin);

        await refusal.Should().ThrowAsync<DomainException>()
            .WithMessage("*administrador*");

        await _users.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Still_provisions_the_first_administrator_for_the_deployment()
    {
        _users.FindByUsernameAsync("jefa", Arg.Any<CancellationToken>()).Returns((User?)null);
        _hasher.Hash(RightPassword).Returns(StoredHash);

        await _service.ProvisionAdministratorAsync(new Credentials("jefa", RightPassword));

        await _users.Received(1).AddAsync(
            Arg.Is<User>(user => user.Username == "jefa" && user.Role == Roles.Admin),
            Arg.Any<CancellationToken>());
    }

}
