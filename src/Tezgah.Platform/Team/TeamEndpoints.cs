using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Persistence;
using Tezgah.Core.Results;
using Tezgah.Core.Security;
using Tezgah.Core.Web;
using Tezgah.Platform.Auth;
using Tezgah.Platform.Domain;
using Tezgah.Platform.Persistence;

namespace Tezgah.Platform.Team;

internal static class TeamPermissions
{
    public static readonly Permission Manage = new("team.manage", "Kullanıcı ekler, rol verir, hesabı kapatır", Roles.Owner);
}

/// <summary>
/// The company's people and their roles. Owners only. Every change to someone's access signs that
/// person out everywhere at once, and a company always keeps at least one active owner.
/// </summary>
internal static class TeamEndpoints
{
    public sealed record MemberResponse(
        Guid Id, string FullName, string Email, string Role, bool IsActive, bool TwoStepEnabled, bool MustChangePassword,
        DateTimeOffset? LastSignInAt);

    public sealed record AddMemberRequest(string? FullName, string? Email, string? Role);

    public sealed record UpdateMemberRequest(string? FullName, string? Role, bool? IsActive);

    /// <summary>Shown once, to be passed on to the person; it works only until they choose their own.</summary>
    public sealed record TemporaryPassword(MemberResponse Member, string Password);

    public static void Map(IEndpointRouteBuilder api)
    {
        var team = api.MapGroup("/team")
            .RequireAuthorization(Policies.TenantUser)
            .RequirePermission(TeamPermissions.Manage)
            .WithTags("Team");
        team.MapGet("", ListAsync);
        team.MapPost("", AddAsync);
        team.MapPut("/{memberId:guid}", UpdateAsync);
        team.MapPost("/{memberId:guid}/password-reset", ResetPasswordAsync);
    }

    private static async Task<IReadOnlyList<MemberResponse>> ListAsync(ClaimsPrincipal owner, PlatformDbContext db, CancellationToken ct)
    {
        var tenantId = owner.GetTenantId();
        var members = await db.Users.AsNoTracking()
            .Where(user => user.TenantId == tenantId)
            .OrderByDescending(user => user.IsActive).ThenBy(user => user.FullName)
            .ToListAsync(ct);
        return members.ConvertAll(ToResponse);
    }

    private static async Task<IResult> AddAsync(
        AddMemberRequest request, ClaimsPrincipal owner, PlatformDbContext db, IPasswordHasher<User> hasher, TimeProvider time,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) || request.Email?.Contains('@') != true)
        {
            return Error.Validation("invalid_member", "A name and a valid e-mail address are required.").ToProblem();
        }

        if (!Roles.IsCompanyRole(request.Role))
        {
            return Error.Validation("invalid_role", "The role is owner, manager or staff.").ToProblem();
        }

        var member = User.Create(owner.GetTenantId(), request.Email, request.FullName, request.Role!, time.GetUtcNow());
        var password = Secrets.NewTemporaryPassword();
        member.SetTemporaryPassword(hasher.HashPassword(member, password));
        db.Users.Add(member);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return Error.Conflict("email_in_use", "Another account already uses this e-mail address.").ToProblem();
        }

        return Results.Created($"/api/team/{member.Id}", new TemporaryPassword(ToResponse(member), password));
    }

    private static async Task<IResult> UpdateAsync(
        Guid memberId, UpdateMemberRequest request, ClaimsPrincipal owner, PlatformDbContext db, Sessions sessions,
        CancellationToken ct)
    {
        if (memberId == owner.GetUserId() && (request.Role is not null || request.IsActive is not null))
        {
            return Error.Conflict("cannot_change_self", "Ask another owner to change your own role or access.").ToProblem();
        }

        // The company row is locked for the whole change: two owners demoting each other at the same
        // moment would otherwise each see the other still in charge, and the company would end up with none.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var tenantId = owner.GetTenantId()!.Value;
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM platform.tenants WHERE id = {tenantId} FOR UPDATE", ct);

        var member = await db.Users.SingleOrDefaultAsync(user => user.Id == memberId && user.TenantId == tenantId, ct);
        if (member is null)
        {
            return MemberNotFound().ToProblem();
        }

        var versionBefore = member.SessionVersion;
        if (!string.IsNullOrWhiteSpace(request.FullName))
        {
            member.Rename(request.FullName);
        }

        if (request.Role is not null && member.ChangeRole(request.Role) is { IsSuccess: false } invalid)
        {
            return invalid.Error.ToProblem();
        }

        if (request.IsActive is { } isActive)
        {
            member.SetActive(isActive);
        }

        var ownersLeft = await db.Users.CountAsync(
            user => user.TenantId == tenantId && user.Id != member.Id && user.IsActive && user.Role == Roles.Owner, ct);
        if (ownersLeft == 0 && !(member.IsActive && member.Role == Roles.Owner))
        {
            return Error.Conflict("last_owner", "The company needs at least one active owner.").ToProblem();
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        if (member.SessionVersion != versionBefore)
        {
            await sessions.RevokeAllAsync(member.Id, ct);
        }

        return Results.Ok(ToResponse(member));
    }

    /// <summary>For a forgotten password: a new temporary one, and every session of the old one is ended.</summary>
    private static async Task<IResult> ResetPasswordAsync(
        Guid memberId, ClaimsPrincipal owner, PlatformDbContext db, IPasswordHasher<User> hasher, Sessions sessions,
        CancellationToken ct)
    {
        var tenantId = owner.GetTenantId();
        var member = await db.Users.SingleOrDefaultAsync(user => user.Id == memberId && user.TenantId == tenantId, ct);
        if (member is null)
        {
            return MemberNotFound().ToProblem();
        }

        if (member.Id == owner.GetUserId())
        {
            return Error.Conflict("cannot_change_self", "Change your own password from your account page.").ToProblem();
        }

        var password = Secrets.NewTemporaryPassword();
        member.SetTemporaryPassword(hasher.HashPassword(member, password));
        await db.SaveChangesAsync(ct);
        await sessions.RevokeAllAsync(member.Id, ct);
        return Results.Ok(new TemporaryPassword(ToResponse(member), password));
    }

    private static MemberResponse ToResponse(User user) => new(
        user.Id, user.FullName, user.Email, user.Role, user.IsActive, user.TwoStepEnabled, user.MustChangePassword, user.LastSignInAt);

    private static Error MemberNotFound() => Error.NotFound("member_not_found", "This person is not in your company.");
}
