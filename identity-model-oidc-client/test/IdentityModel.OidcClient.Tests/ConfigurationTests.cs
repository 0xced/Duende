﻿// Copyright (c) Duende Software. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Net;
using Duende.IdentityModel.OidcClient.Infrastructure;
using FluentAssertions;
using Duende.IdentityModel.Client;
using Duende.IdentityModel.Jwk;

namespace Duende.IdentityModel.OidcClient
{
    public class ConfigurationTests
    {
        [Fact]
        public void Null_options_should_throw_exception()
        {
            OidcClientOptions options = null;

            Action act = () => new Duende.IdentityModel.OidcClient.OidcClient(options);

            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void No_authority_and_no_static_config_should_throw_exception()
        {
            var options = new OidcClientOptions();

            Action act = () => new Duende.IdentityModel.OidcClient.OidcClient(options);

            act.Should().Throw<ArgumentException>().Where(e => e.Message.StartsWith("No authority specified"));
        }

        [Fact]
        public async Task Providing_required_provider_information_should_not_throw()
        {
            var options = new OidcClientOptions
            {
                ProviderInformation = new ProviderInformation
                {
                    IssuerName = "issuer",
                    AuthorizeEndpoint = "authorize",
                    TokenEndpoint = "token",
                    KeySet = new JsonWebKeySet()
                }
            };

            var client = new Duende.IdentityModel.OidcClient.OidcClient(options);

            var act = async () => { await client.EnsureProviderInformationAsync(CancellationToken.None); };

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task Missing_issuer_should_throw()
        {
            var options = new OidcClientOptions
            {
                ProviderInformation = new ProviderInformation
                {
                    IssuerName = null,
                    AuthorizeEndpoint = "authorize",
                    TokenEndpoint = "token",
                    KeySet = new JsonWebKeySet()
                }
            };

            var client = new Duende.IdentityModel.OidcClient.OidcClient(options);

            var act = async () => { await client.EnsureProviderInformationAsync(CancellationToken.None); };

            await act.Should().ThrowAsync<InvalidOperationException>().Where(e => e.Message.Equals("Issuer name is missing in provider information"));
        }

        [Fact]
        public async Task Missing_authorize_endpoint_should_throw()
        {
            var options = new OidcClientOptions
            {
                ProviderInformation = new ProviderInformation
                {
                    IssuerName = "issuer",
                    AuthorizeEndpoint = null,
                    TokenEndpoint = "token",
                    KeySet = new JsonWebKeySet()
                }
            };

            var client = new Duende.IdentityModel.OidcClient.OidcClient(options);

            var act = async () => { await client.EnsureProviderInformationAsync(CancellationToken.None); };

            await act.Should().ThrowAsync<InvalidOperationException>().Where(e => e.Message.Equals("Authorize endpoint is missing in provider information"));
        }

        [Fact]
        public async Task Missing_token_endpoint_should_throw()
        {
            var options = new OidcClientOptions
            {
                ProviderInformation = new ProviderInformation
                {
                    IssuerName = "issuer",
                    AuthorizeEndpoint = "authorize",
                    TokenEndpoint = null,
                    KeySet = new JsonWebKeySet()
                }
            };

            var client = new Duende.IdentityModel.OidcClient.OidcClient(options);

            var act = async () => { await client.EnsureProviderInformationAsync(CancellationToken.None); };

            await act.Should().ThrowAsync<InvalidOperationException>().Where(e => e.Message.Equals("Token endpoint is missing in provider information"));
        }

        [Fact]
        public async Task Missing_keyset_should_throw()
        {
            var options = new OidcClientOptions
            {
                ProviderInformation = new ProviderInformation
                {
                    IssuerName = "issuer",
                    AuthorizeEndpoint = "authorize",
                    TokenEndpoint = "token",
                    KeySet = null
                }
            };

            var client = new Duende.IdentityModel.OidcClient.OidcClient(options);

            var act = async () => { await client.EnsureProviderInformationAsync(CancellationToken.None); };

            await act.Should().ThrowAsync<InvalidOperationException>().Where(e => e.Message.Equals("Key set is missing in provider information"));
        }

        [Fact]
        public async Task Exception_while_loading_discovery_document_should_throw()
        {
            var options = new OidcClientOptions
            {
                Authority = "https://authority",

                BackchannelHandler = new NetworkHandler(new Exception("error"))
            };

            var client = new Duende.IdentityModel.OidcClient.OidcClient(options);

            var act = async () => { await client.EnsureProviderInformationAsync(CancellationToken.None); };

            await act.Should().ThrowAsync<InvalidOperationException>().Where(e => e.Message.Equals("Error loading discovery document: Error connecting to https://authority/.well-known/openid-configuration. error."));
        }

        [Fact]
        public async Task Error401_while_loading_discovery_document_should_throw()
        {
            var options = new OidcClientOptions
            {
                Authority = "https://authority",

                BackchannelHandler = new NetworkHandler(HttpStatusCode.NotFound, "not found")
            };

            var client = new Duende.IdentityModel.OidcClient.OidcClient(options);

            var act = async () => { await client.EnsureProviderInformationAsync(CancellationToken.None); };

            await act.Should().ThrowAsync<InvalidOperationException>().Where(e => e.Message.Equals("Error loading discovery document: Error connecting to https://authority/.well-known/openid-configuration: not found"));
        }

        [Fact]
        public async Task GetClientAssertionAsync_should_return_statically_configured_client_assertion_by_default()
        {
            var options = new OidcClientOptions
            {
                ClientAssertion = new ClientAssertion { Type = "test", Value = "expected" }
            };

            var result = await options.GetClientAssertionAsync();
            result.Type.Should().Be("test");
            result.Value.Should().Be("expected");
        }
    }
}