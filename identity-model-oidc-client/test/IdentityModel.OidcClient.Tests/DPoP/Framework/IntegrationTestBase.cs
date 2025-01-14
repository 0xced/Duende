﻿// Copyright (c) Duende Software. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace Duende.IdentityModel.OidcClient.DPoP.Framework;

public class IntegrationTestBase
{
    protected readonly IdentityServerHost IdentityServerHost;
    protected ApiHost ApiHost;

    public IntegrationTestBase()
    {
        IdentityServerHost = new IdentityServerHost();
        IdentityServerHost.InitializeAsync().Wait();

        ApiHost = new ApiHost(IdentityServerHost);
        ApiHost.InitializeAsync().Wait();
    }
}