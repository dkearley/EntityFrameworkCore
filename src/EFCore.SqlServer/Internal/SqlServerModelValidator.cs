// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.SqlServer.Internal;
using Microsoft.EntityFrameworkCore.SqlServer.Metadata.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore.Internal
{
    /// <summary>
    ///     <para>
    ///         This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///         directly from your code. This API may change or be removed in future releases.
    ///     </para>
    ///     <para>
    ///         The service lifetime is <see cref="ServiceLifetime.Singleton"/>. This means a single instance
    ///         is used by many <see cref="DbContext"/> instances. The implementation must be thread-safe.
    ///         This service cannot depend on services registered as <see cref="ServiceLifetime.Scoped"/>.
    ///     </para>
    /// </summary>
    public class SqlServerModelValidator : RelationalModelValidator
    {
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public SqlServerModelValidator(
            [NotNull] ModelValidatorDependencies dependencies,
            [NotNull] RelationalModelValidatorDependencies relationalDependencies)
            : base(dependencies, relationalDependencies)
        {
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override void Validate(IModel model, DiagnosticsLoggers loggers)
        {
            base.Validate(model, loggers);

            ValidateDefaultDecimalMapping(model, loggers);
            ValidateByteIdentityMapping(model, loggers);
            ValidateNonKeyValueGeneration(model, loggers);
            ValidateIndexIncludeProperties(model, loggers);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual void ValidateDefaultDecimalMapping([NotNull] IModel model, DiagnosticsLoggers loggers)
        {
            var logger = loggers.GetLogger<DbLoggerCategory.Model.Validation>();

            foreach (var property in model.GetEntityTypes()
                .SelectMany(t => t.GetDeclaredProperties())
                .Where(
                    p => p.ClrType.UnwrapNullableType() == typeof(decimal)
                         && !p.IsForeignKey()))
            {
#pragma warning disable IDE0019 // Use pattern matching
                var type = property.FindAnnotation(RelationalAnnotationNames.ColumnType) as ConventionalAnnotation;
#pragma warning restore IDE0019 // Use pattern matching
                var typeMapping = property.FindAnnotation(CoreAnnotationNames.TypeMapping) as ConventionalAnnotation;
                if ((type == null
                     && (typeMapping == null
                         || ConfigurationSource.Convention.Overrides(typeMapping.GetConfigurationSource())))
                    || (type != null
                        && ConfigurationSource.Convention.Overrides(type.GetConfigurationSource())))
                {
                    logger.DecimalTypeDefaultWarning(property);
                }
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual void ValidateByteIdentityMapping([NotNull] IModel model, DiagnosticsLoggers loggers)
        {
            var logger = loggers.GetLogger<DbLoggerCategory.Model.Validation>();

            foreach (var property in model.GetEntityTypes()
                .SelectMany(t => t.GetDeclaredProperties())
                .Where(
                    p => p.ClrType.UnwrapNullableType() == typeof(byte)
                         && p.SqlServer().ValueGenerationStrategy == SqlServerValueGenerationStrategy.IdentityColumn))
            {
                logger.ByteIdentityColumnWarning(property);
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual void ValidateNonKeyValueGeneration([NotNull] IModel model, DiagnosticsLoggers loggers)
        {
            foreach (var property in model.GetEntityTypes()
                .SelectMany(t => t.GetDeclaredProperties())
                .Where(
                    p => ((SqlServerPropertyAnnotations)p.SqlServer()).GetSqlServerValueGenerationStrategy(fallbackToModel: false)
                         == SqlServerValueGenerationStrategy.SequenceHiLo
                         && !p.IsKey()
                         && p.ValueGenerated != ValueGenerated.Never
                         && (!(p.FindAnnotation(SqlServerAnnotationNames.ValueGenerationStrategy) is ConventionalAnnotation strategy)
                             || !ConfigurationSource.Convention.Overrides(strategy.GetConfigurationSource()))))
            {
                throw new InvalidOperationException(
                    SqlServerStrings.NonKeyValueGeneration(property.Name, property.DeclaringEntityType.DisplayName()));
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual void ValidateIndexIncludeProperties([NotNull] IModel model, DiagnosticsLoggers loggers)
        {
            foreach (var index in model.GetEntityTypes().SelectMany(t => t.GetDeclaredIndexes()))
            {
                var includeProperties = index.SqlServer().IncludeProperties;
                if (includeProperties?.Count > 0)
                {
                    var notFound = includeProperties
                        .Where(i => index.DeclaringEntityType.FindProperty(i) == null)
                        .FirstOrDefault();

                    if (notFound != null)
                    {
                        throw new InvalidOperationException(
                            SqlServerStrings.IncludePropertyNotFound(index.DeclaringEntityType.DisplayName(), notFound));
                    }

                    var duplicate = includeProperties
                        .GroupBy(i => i)
                        .Where(g => g.Count() > 1)
                        .Select(y => y.Key)
                        .FirstOrDefault();

                    if (duplicate != null)
                    {
                        throw new InvalidOperationException(
                            SqlServerStrings.IncludePropertyDuplicated(index.DeclaringEntityType.DisplayName(), duplicate));
                    }

                    var inIndex = includeProperties
                        .Where(i => index.Properties.Any(p => i == p.Name))
                        .FirstOrDefault();

                    if (inIndex != null)
                    {
                        throw new InvalidOperationException(
                            SqlServerStrings.IncludePropertyInIndex(index.DeclaringEntityType.DisplayName(), inIndex));
                    }
                }
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override void ValidateSharedTableCompatibility(
            IReadOnlyList<IEntityType> mappedTypes, string tableName, DiagnosticsLoggers loggers)
        {
            var firstMappedType = mappedTypes[0];
            var isMemoryOptimized = firstMappedType.SqlServer().IsMemoryOptimized;

            foreach (var otherMappedType in mappedTypes.Skip(1))
            {
                if (isMemoryOptimized != otherMappedType.SqlServer().IsMemoryOptimized)
                {
                    throw new InvalidOperationException(
                        SqlServerStrings.IncompatibleTableMemoryOptimizedMismatch(
                            tableName, firstMappedType.DisplayName(), otherMappedType.DisplayName(),
                            isMemoryOptimized ? firstMappedType.DisplayName() : otherMappedType.DisplayName(),
                            !isMemoryOptimized ? firstMappedType.DisplayName() : otherMappedType.DisplayName()));
                }
            }

            base.ValidateSharedTableCompatibility(mappedTypes, tableName, loggers);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override void ValidateSharedColumnsCompatibility(
            IReadOnlyList<IEntityType> mappedTypes, string tableName, DiagnosticsLoggers loggers)
        {
            base.ValidateSharedColumnsCompatibility(mappedTypes, tableName, loggers);

            var identityColumns = new List<IProperty>();
            var propertyMappings = new Dictionary<string, IProperty>();

            foreach (var property in mappedTypes.SelectMany(et => et.GetDeclaredProperties()))
            {
                var propertyAnnotations = property.Relational();
                var columnName = propertyAnnotations.ColumnName;
                if (propertyMappings.TryGetValue(columnName, out var duplicateProperty))
                {
                    var propertyStrategy = property.SqlServer().ValueGenerationStrategy;
                    var duplicatePropertyStrategy = duplicateProperty.SqlServer().ValueGenerationStrategy;
                    if (propertyStrategy != duplicatePropertyStrategy
                        && (propertyStrategy == SqlServerValueGenerationStrategy.IdentityColumn
                            || duplicatePropertyStrategy == SqlServerValueGenerationStrategy.IdentityColumn))
                    {
                        throw new InvalidOperationException(
                            SqlServerStrings.DuplicateColumnNameValueGenerationStrategyMismatch(
                                duplicateProperty.DeclaringEntityType.DisplayName(),
                                duplicateProperty.Name,
                                property.DeclaringEntityType.DisplayName(),
                                property.Name,
                                columnName,
                                tableName));
                    }
                }
                else
                {
                    propertyMappings[columnName] = property;
                    if (property.SqlServer().ValueGenerationStrategy == SqlServerValueGenerationStrategy.IdentityColumn)
                    {
                        identityColumns.Add(property);
                    }
                }
            }

            if (identityColumns.Count > 1)
            {
                var sb = new StringBuilder()
                    .AppendJoin(identityColumns.Select(p => "'" + p.DeclaringEntityType.DisplayName() + "." + p.Name + "'"));
                throw new InvalidOperationException(SqlServerStrings.MultipleIdentityColumns(sb, tableName));
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override void ValidateSharedKeysCompatibility(
            IReadOnlyList<IEntityType> mappedTypes, string tableName, DiagnosticsLoggers loggers)
        {
            base.ValidateSharedKeysCompatibility(mappedTypes, tableName, loggers);

            var keyMappings = new Dictionary<string, IKey>();

            foreach (var key in mappedTypes.SelectMany(et => et.GetDeclaredKeys()))
            {
                var keyName = key.Relational().Name;

                if (!keyMappings.TryGetValue(keyName, out var duplicateKey))
                {
                    keyMappings[keyName] = key;
                    continue;
                }

                if (key.SqlServer().IsClustered
                    != duplicateKey.SqlServer().IsClustered)
                {
                    throw new InvalidOperationException(
                        SqlServerStrings.DuplicateKeyMismatchedClustering(
                            Property.Format(key.Properties),
                            key.DeclaringEntityType.DisplayName(),
                            Property.Format(duplicateKey.Properties),
                            duplicateKey.DeclaringEntityType.DisplayName(),
                            tableName,
                            keyName));
                }
            }
        }
    }
}
