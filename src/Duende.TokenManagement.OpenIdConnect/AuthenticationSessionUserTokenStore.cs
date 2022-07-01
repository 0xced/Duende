﻿// Copyright (c) Brock Allen & Dominick Baier. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Duende.TokenManagement.OpenIdConnect;

/// <summary>
/// Token store using the ASP.NET Core authentication session
/// </summary>
public class AuthenticationSessionUserAccessTokenStore : IUserTokenStore
{
    private const string TokenPrefix = ".Token.";

    private readonly IHttpContextAccessor _contextAccessor;
    private readonly ILogger<AuthenticationSessionUserAccessTokenStore> _logger;

    /// <summary>
    /// ctor
    /// </summary>
    /// <param name="contextAccessor"></param>
    /// <param name="logger"></param>
    public AuthenticationSessionUserAccessTokenStore(
        IHttpContextAccessor contextAccessor,
        ILogger<AuthenticationSessionUserAccessTokenStore> logger)
    {
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<UserAccessToken> GetTokenAsync(
        ClaimsPrincipal user, 
        UserAccessTokenRequestParameters? parameters = null)
    {
        parameters ??= new UserAccessTokenRequestParameters();
        var result = await _contextAccessor!.HttpContext!.AuthenticateAsync(parameters.SignInScheme);

        if (!result.Succeeded)
        {
            _logger.LogInformation("Cannot authenticate scheme: {scheme}", parameters.SignInScheme ?? "default signin scheme");

            return new UserAccessToken();
        }

        if (result.Properties == null)
        {
            _logger.LogInformation("Authentication result properties are null for scheme: {scheme}",
                parameters.SignInScheme ?? "default signin scheme");

            return new UserAccessToken();
        }

        var tokens = result.Properties.Items.Where(i => i.Key.StartsWith(TokenPrefix)).ToList();
        if (!tokens.Any())
        {
            _logger.LogInformation("No tokens found in cookie properties. SaveTokens must be enabled for automatic token refresh.");

            return new UserAccessToken();
        }

        var tokenName = $"{TokenPrefix}{OpenIdConnectParameterNames.AccessToken}";
        if (!string.IsNullOrEmpty(parameters.Resource))
        {
            tokenName += $"::{parameters.Resource}";
        }

        var expiresName = $"{TokenPrefix}expires_at";
        if (!string.IsNullOrEmpty(parameters.Resource))
        {
            expiresName += $"::{parameters.Resource}";
        }

        const string refreshTokenName = $"{TokenPrefix}{OpenIdConnectParameterNames.RefreshToken}";

        string? refreshToken = null;
        string? accessToken = null;
        string? expiresAt = null;

        if (!string.IsNullOrEmpty(parameters.ChallengeScheme))
        {
            refreshToken = tokens.SingleOrDefault(t => t.Key == $"{refreshTokenName}||{parameters.ChallengeScheme}").Value;
            accessToken = tokens.SingleOrDefault(t => t.Key == $"{tokenName}||{parameters.ChallengeScheme}").Value;
            expiresAt = tokens.SingleOrDefault(t => t.Key == $"{expiresName}||{parameters.ChallengeScheme}").Value;
        }

        refreshToken ??= tokens.SingleOrDefault(t => t.Key == $"{refreshTokenName}").Value;
        accessToken ??= tokens.SingleOrDefault(t => t.Key == $"{tokenName}").Value;
        expiresAt ??= tokens.SingleOrDefault(t => t.Key == $"{expiresName}").Value;

        DateTimeOffset dtExpires;
        if (expiresAt != null)
        {
            dtExpires = DateTimeOffset.Parse(expiresAt, CultureInfo.InvariantCulture);
        }
        else
        {
            dtExpires = DateTimeOffset.MaxValue;
        }

        return new UserAccessToken
        {
            Value = accessToken,
            RefreshToken = refreshToken,
            Expiration = dtExpires
        };
    }

    /// <inheritdoc/>
    public async Task StoreTokenAsync(
        ClaimsPrincipal user,
        UserAccessToken token,
        UserAccessTokenRequestParameters? parameters = null)
    {
        parameters ??= new UserAccessTokenRequestParameters();
        var result = await _contextAccessor!.HttpContext!.AuthenticateAsync(parameters.SignInScheme)!;

        if (result is not { Succeeded: true })
        {
            throw new Exception("Can't store tokens. User is anonymous");
        }

        // in case you want to filter certain claims before re-issuing the authentication session
        var transformedPrincipal = await FilterPrincipalAsync(result.Principal!);
            
        var expiresName = "expires_at";
        if (!string.IsNullOrEmpty(parameters.Resource))
        {
            expiresName += $"::{parameters.Resource}";
        }

        var tokenName = OpenIdConnectParameterNames.AccessToken;
        if (!string.IsNullOrEmpty(parameters.Resource))
        {
            tokenName += $"::{parameters.Resource}";
        }

        var refreshTokenName = $"{OpenIdConnectParameterNames.RefreshToken}";
        if (!string.IsNullOrEmpty(parameters.ChallengeScheme))
        {
            refreshTokenName += $"||{parameters.ChallengeScheme}";
            tokenName += $"||{parameters.ChallengeScheme}";
            expiresName += $"||{parameters.ChallengeScheme}";
        }
        
        // todo: deal with missing expires_in
        result.Properties!.Items[$"{TokenPrefix}{tokenName}"] = token.Value;
        result.Properties!.Items[$"{TokenPrefix}{expiresName}"] = token.Expiration.ToString("o", CultureInfo.InvariantCulture);

        if (token.RefreshToken != null)
        {
            if (!result.Properties.UpdateTokenValue(refreshTokenName, token.RefreshToken))
            {
                result.Properties.Items[$"{TokenPrefix}{refreshTokenName}"] = token.RefreshToken;
            }
        }

        var options = _contextAccessor!.HttpContext!.RequestServices.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        var schemeProvider = _contextAccessor.HttpContext.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = parameters.SignInScheme ?? (await schemeProvider.GetDefaultSignInSchemeAsync())?.Name;
        var cookieOptions = options.Get(scheme);

        if (result.Properties.AllowRefresh == true ||
            (result.Properties.AllowRefresh == null && cookieOptions.SlidingExpiration))
        {
            // this will allow the cookie to be issued with a new issued (and thus a new expiration)
            result.Properties.IssuedUtc = null;
            result.Properties.ExpiresUtc = null;
        }

        await _contextAccessor.HttpContext.SignInAsync(parameters.SignInScheme, transformedPrincipal, result.Properties);
    }

    /// <inheritdoc/>
    public Task ClearTokenAsync(
        ClaimsPrincipal user, 
        UserAccessTokenRequestParameters? parameters = null)
    {
        // todo
        return Task.CompletedTask;
    }

    /// <summary>
    /// Allows transforming the principal before re-issuing the authentication session
    /// </summary>
    /// <param name="principal"></param>
    /// <returns></returns>
    protected virtual Task<ClaimsPrincipal> FilterPrincipalAsync(ClaimsPrincipal principal)
    {
        return Task.FromResult(principal);
    }
}