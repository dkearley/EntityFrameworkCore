﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Relational.Query.PipeLine.SqlExpressions;

namespace Microsoft.EntityFrameworkCore.Relational.Query.PipeLine
{
    public interface IMemberTranslator
    {
        SqlExpression Translate(SqlExpression instance, MemberInfo member, Type returnType);
    }
}
