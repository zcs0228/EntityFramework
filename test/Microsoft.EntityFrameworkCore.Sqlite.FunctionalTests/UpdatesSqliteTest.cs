﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.FunctionalTests;

namespace Microsoft.EntityFrameworkCore.Sqlite.FunctionalTests
{
    public class UpdatesSqliteTest : UpdatesRelationalTest<UpdatesSqliteFixture, SqliteTestStore>
    {
        public UpdatesSqliteTest(UpdatesSqliteFixture fixture)
            : base(fixture)
        {
        }
    }
}
