﻿using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using EntityFrameworkRuler.Design.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Castle.DynamicProxy;
using EntityFrameworkRuler.Common;
using EntityFrameworkRuler.Design.Services.Models;
using EntityFrameworkRuler.Rules;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using IInterceptor = Castle.DynamicProxy.IInterceptor;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable ClassWithVirtualMembersNeverInherited.Global
// ReSharper disable ClassCanBeSealed.Global

namespace EntityFrameworkRuler.Design.Services;

/// <summary>
/// This override will apply custom property type mapping to the generated entities.
/// It is also possible to remove columns at this level.
/// </summary>
[SuppressMessage("Usage", "EF1001:Internal EF Core API usage.")]
public class RuledRelationalScaffoldingModelFactory : IScaffoldingModelFactory, IInterceptor {
    private readonly IMessageLogger reporter;
    private readonly IDesignTimeRuleLoader designTimeRuleLoader;
    private readonly IRuleModelUpdater ruleModelUpdater;
    private readonly ICandidateNamingService candidateNamingService;
    private readonly IPluralizer pluralizer;
    private readonly ICSharpUtilities cSharpUtilities;
    private DbContextRuleNode dbContextRule;
    private readonly RelationalScaffoldingModelFactory proxy;
    private readonly MethodInfo visitForeignKeyMethod;
    private readonly MethodInfo addNavigationPropertiesMethod;
    private readonly MethodInfo visitTableMethod;
    private readonly MethodInfo getEntityTypeNameMethod;

    /// <summary> This is an internal API and is subject to change or removal without notice. </summary>
    protected readonly HashSet<string> OmittedTables = new();

    /// <summary> This is an internal API and is subject to change or removal without notice. </summary>
    protected readonly HashSet<string> OmittedSchemas = new();

    private RuledCSharpUniqueNamer<DatabaseTable, EntityRule> tableNamer;
    private RuledCSharpUniqueNamer<DatabaseTable, EntityRule> dbSetNamer;
    private ModelReverseEngineerOptions options;

    /// <summary> This is an internal API and is subject to change or removal without notice. </summary>
    public RuledRelationalScaffoldingModelFactory(IServiceProvider serviceProvider,
        IMessageLogger reporter,
        IDesignTimeRuleLoader designTimeRuleLoader,
        IRuleModelUpdater ruleModelUpdater,
        ICandidateNamingService candidateNamingService,
        IPluralizer pluralizer,
        ICSharpUtilities cSharpUtilities) {
        this.reporter = reporter;
        this.designTimeRuleLoader = designTimeRuleLoader;
        this.ruleModelUpdater = ruleModelUpdater;
        this.candidateNamingService = candidateNamingService;
        this.pluralizer = pluralizer;
        this.cSharpUtilities = cSharpUtilities;

        // avoid runtime binding errors against EF6 by using reflection and a proxy to access the resources we need.
        // this allows more fluid compatibility with EF versions without retargeting this project.

        try {
            proxy = serviceProvider.CreateClassProxy<RelationalScaffoldingModelFactory>(this);
        } catch (Exception ex) {
            reporter.WriteError($"Error creating proxy of RelationalScaffoldingModelFactory: {ex.Message}");
            throw;
        }

        var t = typeof(RelationalScaffoldingModelFactory);
        // protected virtual IMutableForeignKey? VisitForeignKey(ModelBuilder modelBuilder,DatabaseForeignKey foreignKey)
        visitForeignKeyMethod = GetMethodOrLog("VisitForeignKey", o => t.GetMethod<ModelBuilder, DatabaseForeignKey>(o));

        // protected virtual void AddNavigationProperties(IMutableForeignKey foreignKey)
        addNavigationPropertiesMethod = GetMethodOrLog("AddNavigationProperties", o => t.GetMethod<IMutableForeignKey>(o));

        // protected virtual string GetEntityTypeName(DatabaseTable table)
        getEntityTypeNameMethod = GetMethodOrLog("GetEntityTypeName", o => t.GetMethod<DatabaseTable>(o));

        // protected virtual EntityTypeBuilder? VisitTable(ModelBuilder modelBuilder, DatabaseTable table)
        visitTableMethod = GetMethodOrLog("VisitTable", o => t.GetMethod<ModelBuilder, DatabaseTable>(o));

        MethodInfo GetMethodOrLog(string name, Func<string, MethodInfo> getter) {
            var m = getter(name);
            if (m == null)
                reporter.WriteWarning($"Method not found: RelationalScaffoldingModelFactory.{name}()");
            return m;
        }
    }

    /// <inheritdoc />
    public IModel Create(DatabaseModel databaseModel, ModelReverseEngineerOptions ops) {
        var model = proxy.Create(databaseModel, ops);
        return model;
    }

    /// <summary> This is an internal API and is subject to change or removal without notice. </summary>
    protected virtual IModel Create(DatabaseModel databaseModel, ModelReverseEngineerOptions ops,
        Func<DatabaseModel, ModelReverseEngineerOptions, IModel> baseCall) {
        options = ops;
        Func<DatabaseTable, NamedElementState<DatabaseTable, EntityRule>> tableNameAction;
        Func<DatabaseTable, NamedElementState<DatabaseTable, EntityRule>> dbSetNameAction;

        // Note, table naming logic has to be overriden at this level because the pluralizer step is executed AFTER
        // the CandidateNamingService returns its result.  This means that a user specified name will be subject to change
        // by the pluralizer/singularizer.  To avoid altering the user's input, we have to return more information about
        // a candidate name, hence NamedElementState, where IsFrozen can be set.

        // Preventing the pluralizer from affecting navigation names set by the user would involve replacing VisitForeignKeys
        // and AddNavigationProperties, which has significant EF wiring logic - so this is not advisable.
        // As an alternative, we may consider setting _options.NoPluralize to true during the processing of these methods only, and
        // moving the pluralize call into GetDependentEndCandidateNavigationPropertyName/GetPrincipalEndCandidateNavigationPropertyName.
        // However it is less likely there will be any need for this measure to protect nav names.

        if (ops.UseDatabaseNames) {
            tableNameAction = t => new(t.Name, t);
            dbSetNameAction = t => new(t.Name, t);
        } else {
            if (candidateNamingService is RuledCandidateNamingService ruledNamer) {
                tableNameAction = t => ruledNamer.GenerateCandidateNameState(t);
                dbSetNameAction = t => ruledNamer.GenerateCandidateNameState(t, true);
            } else
                dbSetNameAction = tableNameAction = t => new(candidateNamingService.GenerateCandidateIdentifier(t), t);
        }

        tableNameAction = tableNameAction.Cached();
        dbSetNameAction = dbSetNameAction.Cached();

        tableNamer = new(tableNameAction, cSharpUtilities, ops.NoPluralize ? null : pluralizer.Singularize);
        dbSetNamer = new(dbSetNameAction, cSharpUtilities, ops.NoPluralize ? null : pluralizer.Pluralize);

        var model = baseCall(databaseModel, ops);
        ruleModelUpdater?.OnModelCreated(model);
        return model;
    }

    /// <summary> This is an internal API and is subject to change or removal without notice. </summary>
    protected virtual TypeScaffoldingInfo GetTypeScaffoldingInfo(DatabaseColumn column, Func<TypeScaffoldingInfo> baseCall) {
        var typeScaffoldingInfo = baseCall();
        Debug.Assert(explicitEntityRuleMapping.table == column.Table);
        var entityRule = explicitEntityRuleMapping.table == column.Table ? explicitEntityRuleMapping.entityRule : null;
        if (entityRule == null) return typeScaffoldingInfo;

        var propertyRule = entityRule.TryResolveRuleFor(column.Name);
        if (propertyRule == null || propertyRule.Rule.NewType.HasNonWhiteSpace() != true) return typeScaffoldingInfo;

        var clrType = designTimeRuleLoader?.TryResolveType(propertyRule.Rule.NewType, typeScaffoldingInfo?.ClrType, reporter);
        if (clrType == null) return typeScaffoldingInfo;
        reporter.WriteVerbose($"RULED: Column {column.Table.Schema}.{column.Table.Name}.{column.Name} type set to {clrType.FullName}");
        // Regenerate the TypeScaffoldingInfo based on our new CLR type.
        return typeScaffoldingInfo.WithType(clrType);
    }

    private (DatabaseTable table, EntityRuleNode entityRule) explicitEntityRuleMapping;

    /// <summary> Get the entity rule for this table </summary>
    protected virtual EntityRuleNode TryResolveRuleFor(DatabaseTable table) {
        if (explicitEntityRuleMapping.table == table) return explicitEntityRuleMapping.entityRule;

        dbContextRule ??= designTimeRuleLoader?.GetDbContextRules() ?? new DbContextRuleNode(DbContextRule.DefaultNoRulesFoundBehavior);
        var tableNode = dbContextRule.TryResolveRuleFor(table.Schema)?.TryResolveRuleFor(table.Name);
        return tableNode;
    }

    /// <summary> This is an internal API and is subject to change or removal without notice. </summary>
    // ReSharper disable once RedundantAssignment
    protected virtual ModelBuilder VisitDatabaseModel(ModelBuilder modelBuilder, DatabaseModel databaseModel, Func<ModelBuilder> baseCall) {
        modelBuilder = baseCall();

        // Model post processing.

        return modelBuilder;
    }

    /// <summary> This is an internal API and is subject to change or removal without notice. </summary>
    protected virtual ModelBuilder VisitTables(ModelBuilder modelBuilder, ICollection<DatabaseTable> tables) {
        dbContextRule ??= designTimeRuleLoader?.GetDbContextRules() ?? new DbContextRuleNode(DbContextRule.DefaultNoRulesFoundBehavior);
        var tablesBySchema = tables.GroupBy(o => o.Schema ?? string.Empty)
            .ToDictionary(o => o.Key, o => o.ToDictionary(t => t.Name, t => new DatabaseTableNode(t)));

        foreach (var entityRule in dbContextRule.Entities) {
            var schemaName = entityRule.Parent.DbName ?? string.Empty;
            var tableName = entityRule.DbName ?? string.Empty;
            var schemaTables = tablesBySchema.TryGetValue(schemaName);
            var table = schemaTables?.TryGetValue(tableName);
            var includeSchema = entityRule.Parent.Rule.Mapped;
            var includeEntity = entityRule.Rule.Mapped && includeSchema;

            if (table != null) {
                entityRule.MapTo(table);
                table.EntityRules.Add(entityRule);

                if (!includeSchema) {
                    OmitSchema(table.Schema);
                    continue;
                }

                if (!includeEntity) {
                    OmitTable(table);
                    continue;
                }
            }

            if (table == null && entityRule.BaseEntityRuleNode == null) {
                // invalid entry
                if (entityRule.Rule.Mapped) {
                    if (tableName.HasNonWhiteSpace())
                        reporter.WriteWarning(
                            $"RULED: Entity for {schemaName}.{tableName} cannot be generated because the table cannot be found.");
                    else {
                        var name = entityRule.GetFinalName();
                        if (name.HasNonWhiteSpace())
                            reporter.WriteVerbose(
                                $"RULED: Entity for rule '{name}' cannot be generated because no table or base type is defined.");
                    }
                }

                continue;
            }

            if (table != null)
                InvokeVisitTable(table, entityRule);
            else {
                // Entity with no database table.  A base type must be available otherwise it can't be created.
                string entityTypeName = null;
                if (entityRule.Rule.NewName.HasNonWhiteSpace() || entityRule.Rule.EntityName.HasNonWhiteSpace())
                    entityTypeName = entityRule.GetFinalName().Trim();

                if (entityTypeName == null || !entityTypeName.IsValidSymbolName()) {
                    reporter.WriteWarning($"RULED: Entity '{entityTypeName}' cannot be generated because it has an invalid name.");
                    continue;
                }

                var builder = modelBuilder.Entity(entityTypeName);
                builder = ApplyEntityRules(modelBuilder, builder, null, entityRule);
                Debug.Assert(ReferenceEquals(entityRule.Builder, builder));
            }
        }

        if (!dbContextRule.Rule.IncludeUnknownSchemas &&
            !dbContextRule.Rule.Schemas.Any(s => s.IncludeUnknownTables || s.IncludeUnknownViews)) return modelBuilder;

        // we should perform another pass over the list to include any extra tables that were missing from the rule collection
        foreach (var kvp in tablesBySchema) {
            var schema = kvp.Key;
            var schemaRule = dbContextRule.Schemas.GetByDbName(schema);
            var includeSchema = schemaRule?.NotMapped == false || (schemaRule == null && dbContextRule.Rule.IncludeUnknownSchemas);
            if (!includeSchema) {
                OmitSchema(schema);
                continue;
            }

            schemaRule ??= dbContextRule.AddSchema(schema); // add on the fly

            foreach (var table in kvp.Value.Values) {
                if (table.EntityRules.Count > 0 || table.Builders.Count > 0) continue; // it's already been mapped
                var entityRule = schemaRule.TryResolveRuleFor(table.Name);
                var includeEntity = CanGenerateEntity(schemaRule, entityRule, table);
                if (!includeEntity) {
                    OmitTable(table);
                    continue;
                }

                // add on the fly
                entityRule = schemaRule.AddEntity(table.Name);

                InvokeVisitTable(table, entityRule);
            }
        }

        return modelBuilder;

        void InvokeVisitTable(DatabaseTableNode table, EntityRuleNode entityRule) {
            explicitEntityRuleMapping = (table, entityRule);
            try {
                // We have to call the base VisitTable in order to perform the basic wiring.
                // The call will be captured, and the result of the wiring will be customized based on the rules.
                var builder = VisitTable(modelBuilder, table);
                Debug.Assert(entityRule == null || builder == null || ReferenceEquals(entityRule.Builder, builder));
                table.EntityRules.Add(entityRule);
                table.Builders.Add(builder);
            } finally {
                explicitEntityRuleMapping = default;
            }
        }

        bool CanGenerateEntity(SchemaRuleNode schemaRule, EntityRuleNode entityRule, DatabaseTableNode table) {
            if (entityRule != null) return false;
            if (schemaRule == null) return true;

            var isView = table.Table is DatabaseView;
            if (isView) {
                if (!schemaRule.Rule.IncludeUnknownTables) return false;
            } else {
                // ensure M2M junctions are not auto-excluded
                if (table.Table.IsSimpleManyToManyJoinEntityType()) return true;
                if (!schemaRule.Rule.IncludeUnknownViews) return false;
            }

            return true;
        }

        void OmitSchema(string schema) {
            if (OmittedSchemas.Add(schema))
                reporter.WriteInformation($"RULED: Schema {schema} omitted.");
        }

        void OmitTable(DatabaseTableNode table) {
            if (OmittedTables.Add(table.Name))
                reporter.WriteInformation(
                    $"RULED: {(table.Table is DatabaseView ? "View" : "Table")}  {table.Schema}.{table.Name} omitted.");
        }
    }

    /// <summary> This is an internal API and is subject to change or removal without notice. </summary>
    private EntityTypeBuilder VisitTable(ModelBuilder modelBuilder, DatabaseTable table) {
        return visitTableMethod?.Invoke(proxy, new object[] { modelBuilder, table }) as EntityTypeBuilder;
    }

    /// <summary> This is an internal API and is subject to change or removal without notice. </summary>
    protected virtual EntityTypeBuilder VisitTable(ModelBuilder modelBuilder, DatabaseTable table, Func<EntityTypeBuilder> baseCall) {
        var entityRule = TryResolveRuleFor(table);
        return ApplyEntityRules(modelBuilder, baseCall(), table, entityRule);
    }


    /// <summary> This is an internal API and is subject to change or removal without notice. </summary>
    [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
    protected virtual EntityTypeBuilder ApplyEntityRules(ModelBuilder modelBuilder,
        EntityTypeBuilder entityTypeBuilder,
        DatabaseTable table, EntityRuleNode entityRuleNode) {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
        var entityRule = entityRuleNode?.Rule;
        if (entityRule == null) return entityTypeBuilder;

        if (entityTypeBuilder == null) return null;

        entityRuleNode.MapTo(entityTypeBuilder);

        if (entityRuleNode.BaseEntityRuleNode != null) {
            // get the base entity builder and reference the type directly.
            Debug.Assert(entityRuleNode.Builder != null);
            var baseName = entityRuleNode.BaseEntityRuleNode.Builder?.Metadata.Name ?? entityRuleNode.BaseEntityRuleNode.GetFinalName();
            entityTypeBuilder.HasBaseType(baseName);

            var strategy = entityRuleNode.BaseEntityRuleNode.Rule.GetMappingStrategy()?.ToUpper();
            if (strategy?.Length == 3)
                switch (strategy) {
                    case "TPH":
                        // ToTable() and DbSet should be REMOVED for TPH leafs
                        entityTypeBuilder.ToTable((string)null);
                        const string scaffoldingDbSetName = "Scaffolding:DbSetName";
                        Debug.Assert(IsValidAnnotation(scaffoldingDbSetName));
                        var removed = entityTypeBuilder.Metadata.RemoveAnnotation(scaffoldingDbSetName);
                        Debug.Assert(removed != null);
                        break;
                    case "TPT":
                        break;
                    case "TPC":
                        break;
                }
        } else {
            var strategy = entityRule.GetMappingStrategy()?.ToUpper();
            if (strategy?.Length == 3)
                // This is root of a hierarchy
                switch (strategy) {
                    case "TPH":
                        // ToTable() and DbSet should be defined for TPH root
                        entityTypeBuilder.ToTable(table.Name);
                        break;
                    case "TPT":
                        break;
                    case "TPC":
                        break;
                }
        }


        foreach (var annotation in entityRule.Annotations) {
            if (!IsValidAnnotation(annotation.Key)) {
                reporter.WriteWarning(
                    $"RULED: Entity {entityTypeBuilder.Metadata.Name} annotation '{annotation.Key}' is invalid. Skipping.");
                continue;
            }

            var v = annotation.Value;
            if (v is string s) v = s.NullIfEmpty();
            reporter.WriteVerbose(
                $"RULED: Applying entity {entityTypeBuilder.Metadata.Name} annotation '{annotation.Key}' value '{v}'.");
            entityTypeBuilder.Metadata.SetOrRemoveAnnotation(annotation.Key, v);
        }

        var discriminatorColumn = entityRule.GetDiscriminatorColumn() ??
                                  entityRule.Properties.FirstOrDefault(o => o.DiscriminatorConditions.Count > 0)?.Name;

        if (discriminatorColumn.HasNonWhiteSpace()) ApplyDiscriminator(entityTypeBuilder, discriminatorColumn, table, entityRule);

        var entity = entityTypeBuilder.Metadata;

        // process properties
        var excludedProperties = new HashSet<IMutableProperty>();
        foreach (var property in entity.GetProperties().Where(o => o.DeclaringEntityType == entity)) {
            var column = property.GetColumnNameNoDefault() ?? property.Name;
            var propertyRule = entityRuleNode.TryResolveRuleFor(column);
            if (propertyRule == null && entityRule.IncludeUnknownColumns) propertyRule = entityRuleNode.AddProperty(property, column);

            if (propertyRule?.Rule.ShouldMap() != true) excludedProperties.Add(property);

            propertyRule?.MapTo(property, column);
        }

        foreach (var property in excludedProperties) RemovePropertyAndReferences(property);

        // process navigations
        var excludedNavigations = new HashSet<IMutableNavigation>();
        foreach (var navigation in entity.GetNavigations().Where(o => o.DeclaringEntityType == entity)) {
            var foreignKey = navigation.ForeignKey;
            var thisIsPrincipal = !navigation.IsOnDependent;
            var isManyToMany = foreignKey.IsManyToMany();
            var fkName = foreignKey.GetConstraintName();
            var navEntity = thisIsPrincipal ? foreignKey.PrincipalEntityType : foreignKey.DeclaringEntityType;
            Debug.Assert(navEntity == entity);

            var navigationRule = entityRuleNode.TryResolveNavigationRuleFor(fkName, () => navigation.Name, thisIsPrincipal, isManyToMany);
            if (navigationRule == null && entityRule.IncludeUnknownColumns)
                navigationRule = entityRuleNode.AddNavigation(navigation, fkName, thisIsPrincipal, isManyToMany);

            if (navigationRule?.Rule.Mapped != true) excludedNavigations.Add(navigation);

            navigationRule?.MapTo(navigation, fkName, thisIsPrincipal, isManyToMany);
        }

        foreach (var navigation in excludedNavigations)
            if (entity is EntityType et) {
                var removed = et.RemoveNavigation(navigation.Name);
                Debug.Assert(removed != null);
            }

        if (table.ForeignKeys.Count > 0 && OmittedTables.Count > 0) {
            // check to see if any of the foreign keys map to omitted tables. if so, nuke them.
            var fksToBeRemoved = new HashSet<DatabaseForeignKey>();
            foreach (var foreignKey in table.ForeignKeys)
                if (OmittedTables.Contains(foreignKey.PrincipalTable.GetFullName()))
                    fksToBeRemoved.Add(foreignKey);

            foreach (var dbFk in fksToBeRemoved) {
                var eFk = entity.GetForeignKeys().FirstOrDefault(o => o.GetConstraintName() == dbFk.Name);
                if (eFk == null) continue;
                var removed = entity.RemoveForeignKey(eFk);
                Debug.Assert(removed != null);
            }
        }

        if (!entity.GetProperties().Any() && getEntityTypeNameMethod != null) {
            // remove the entire table
            OmittedTables.Add(table.GetFullName());
            modelBuilder.Model.RemoveEntityType(entityTypeBuilder.Metadata);
            reporter.WriteInformation($"RULED: Entity {entityTypeBuilder.Metadata.Name} omitted.");
            return null;
        }

        foreach (var excludedProperty in excludedProperties)
            reporter.WriteInformation($"RULED: Property {entityTypeBuilder.Metadata.Name}.{excludedProperty.Name} omitted.");

        return entityTypeBuilder;

        void RemovePropertyAndReferences(IMutableProperty p) {
            RemoveIndexesWith(p);
            RemoveKeysWith(p);
            RemoveFKsWith(p);
            var removed = entity.RemoveProperty(p);
            Debug.Assert(removed != null);
        }

        void RemoveIndexesWith(IMutableProperty p) {
            foreach (var item in entity.GetIndexes()
                         .Where(o => o.Properties.Any(ip => ip == p)).ToList()) {
                var removed = entity.RemoveIndex(item);
                Debug.Assert(removed != null);
            }
        }

        void RemoveKeysWith(IMutableProperty p) {
            foreach (var item in entity.GetKeys()
                         .Where(o => o.Properties.Any(ip => ip == p)).ToList()) {
                var removed = entity.RemoveKey(item);
                Debug.Assert(removed != null);
            }
        }

        void RemoveFKsWith(IMutableProperty p) {
            foreach (var item in entity.GetForeignKeys()
                         .Where(o => o.Properties.Any(ip => ip == p)).ToList()) {
                var removed = entity.RemoveForeignKey(item);
                Debug.Assert(removed != null);
            }
        }
    }

    /// <summary> This is an internal API and is subject to change or removal without notice. </summary>
    protected virtual void ApplyDiscriminator(EntityTypeBuilder entityTypeBuilder,
        string discriminatorColumn, DatabaseTable table,
        EntityRule entityRule) {
        var column = table.Columns.FirstOrDefault(o => o.Name == discriminatorColumn);
        if (column == null) {
            reporter.WriteWarning(
                $"RULED: Entity {entityTypeBuilder.Metadata.Name} discriminator column '{discriminatorColumn}' not found.");
            return;
        }

        var property = entityTypeBuilder.Metadata.GetProperties()
            .FirstOrDefault(o => o.GetColumnNameNoDefault().EqualsIgnoreCase(discriminatorColumn));
        if (property == null) {
            reporter.WriteWarning(
                $"RULED: Entity {entityTypeBuilder.Metadata.Name} discriminator property for column '{column.Name}' not found.");
            return;
        }

        var type = property.ClrType;
        var discriminatorBuilder = entityTypeBuilder.HasDiscriminator(property.Name, type);
        var propertyRule = entityRule.Properties.FirstOrDefault(o => o.Name == column.Name);
        if (propertyRule == null) return;
        foreach (var condition in propertyRule.DiscriminatorConditions) {
            object value;
            try {
                if (type == typeof(string))
                    value = condition.Value;
                else
                    value = Convert.ChangeType(condition.Value, type);
            } catch (Exception ex) {
                reporter.WriteWarning(
                    $"RULED: Entity {entityTypeBuilder.Metadata.Name} discriminator value '{condition.Value}' could not be converted to {type.Name}: {ex.Message}");
                return;
            }

            discriminatorBuilder.HasValue(condition.ToEntityName, value);
            reporter.WriteVerbose(
                $"RULED: Entity {entityTypeBuilder.Metadata.Name} discriminator value '{value}' mapped to entity {condition.ToEntityName}");
        }
    }

    /// <summary> This is an internal API and is subject to change or removal without notice. </summary>
    protected virtual bool IsValidAnnotation(string annotationKey) =>
        AnnotationHelper.GetAnnotationIndex(annotationKey)?.Contains(annotationKey) == true;

    private KeyBuilder randomKeyBuilder;

    /// <summary> This is an internal API and is subject to change or removal without notice. </summary>
    protected virtual KeyBuilder VisitPrimaryKey(EntityTypeBuilder builder, DatabaseTable table, Func<KeyBuilder> baseCall) {
        if (explicitEntityRuleMapping.table == table && explicitEntityRuleMapping.entityRule?.BaseEntityRuleNode != null)
            // EF requires that a PK is defined only on the base type. what about in TPT where there is a table?
            // Also can't return null here otherwise that will kill the entity.
            // Return a random key (it wont be used for anything!)
            return randomKeyBuilder;

        var kb = baseCall();
        if (kb != null) randomKeyBuilder = kb;
        return kb;
    }

    /// <summary> This is an internal API and is subject to change or removal without notice. </summary>
    protected virtual ModelBuilder VisitForeignKeys(ModelBuilder modelBuilder, IList<DatabaseForeignKey> foreignKeys,
        Func<IList<DatabaseForeignKey>, ModelBuilder> baseCall) {
        if (visitForeignKeyMethod == null || addNavigationPropertiesMethod == null) return baseCall(foreignKeys);

        ArgumentNullException.ThrowIfNull(foreignKeys);
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var schemaNames = foreignKeys.Select(o => o.Table.Schema).Where(o => o.HasNonWhiteSpace()).Distinct().ToArray();

        var schemas = schemaNames.Select(o => dbContextRule?.Rule?.TryResolveRuleFor(o))
            .Where(o => o?.UseManyToManyEntity == true).ToArray();

        if (OmittedTables.Count > 0) {
            // check to see if the foreign key maps to an omitted table. if so, nuke it.
            var fksToBeRemoved = new HashSet<DatabaseForeignKey>();
            foreach (var foreignKey in foreignKeys)
                if (OmittedTables.Contains(foreignKey.PrincipalTable.GetFullName()))
                    fksToBeRemoved.Add(foreignKey);
                else if (OmittedSchemas.Contains(foreignKey.PrincipalTable.Schema))
                    fksToBeRemoved.Add(foreignKey);

            if (fksToBeRemoved.Count > 0)
                foreignKeys = foreignKeys.Where(o => !fksToBeRemoved.Contains(o)).ToList();
        }

        if (schemas.IsNullOrEmpty()) return baseCall(foreignKeys);

        foreach (var grp in foreignKeys.GroupBy(o => o.Table.Schema)) {
            var schema = grp.Key;
            var schemaForeignKeys = grp.ToArray();
            var schemaReference = schemas.FirstOrDefault(o => o.SchemaName == schema);
            if (schemaReference == null) {
                modelBuilder = baseCall(schemaForeignKeys);
                continue;
            }

            // force simple ManyToMany junctions to be generated as entities
            reporter.WriteInformation($"RULED: Simple many-to-many junctions in {schema} are being forced to generate entities.");
            foreach (var fk in schemaForeignKeys)
                VisitForeignKey(modelBuilder, fk);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            foreach (var foreignKey in entityType.GetForeignKeys())
                AddNavigationProperties(foreignKey);
        }

        return modelBuilder;
    }

    /// <summary> This is an internal API and is subject to change or removal without notice. </summary>
    protected virtual void VisitForeignKey(ModelBuilder modelBuilder, DatabaseForeignKey fk) {
        visitForeignKeyMethod!.Invoke(proxy, new object[] { modelBuilder, fk });
    }

    /// <summary> This is an internal API and is subject to change or removal without notice. </summary>
    protected virtual void AddNavigationProperties(IMutableForeignKey foreignKey) {
        addNavigationPropertiesMethod!.Invoke(proxy, new object[] { foreignKey });
    }

    /// <summary> This is an internal API and is subject to change or removal without notice. </summary>
    protected virtual string GetEntityTypeName(DatabaseTable table) {
        if (explicitEntityRuleMapping.table == table && explicitEntityRuleMapping.entityRule?.Rule.NewName.HasNonWhiteSpace() == true)
            return explicitEntityRuleMapping.entityRule.Rule.NewName;
        return tableNamer.GetName(table);
    }

    /// <summary> This is an internal API and is subject to change or removal without notice. </summary>
    protected virtual string GetDbSetName(DatabaseTable table) {
        if (explicitEntityRuleMapping.table == table && explicitEntityRuleMapping.entityRule?.Rule.NewName.HasNonWhiteSpace() == true) {
            var name = explicitEntityRuleMapping.entityRule.Rule.NewName;
            name = options?.NoPluralize == true ? name : pluralizer.Pluralize(name);
            return name;
        }

        return dbSetNamer.GetName(table);
    }

    void IInterceptor.Intercept(IInvocation invocation) {
        switch (invocation.Method.Name) {
            case "GetTypeScaffoldingInfo" when invocation.Arguments.Length == 1 && invocation.Arguments[0] is DatabaseColumn dc: {
                TypeScaffoldingInfo BaseCall() {
                    invocation.Proceed();
                    return (TypeScaffoldingInfo)invocation.ReturnValue;
                }

                var response = GetTypeScaffoldingInfo(dc, BaseCall);
                invocation.ReturnValue = response;
                break;
            }
            case "VisitDatabaseModel" when invocation.Arguments.Length == 2 && invocation.Arguments[0] is ModelBuilder mb &&
                                           invocation.Arguments[1] is DatabaseModel dbm: {
                ModelBuilder BaseCall() {
                    invocation.Proceed();
                    return (ModelBuilder)invocation.ReturnValue;
                }

                var response = VisitDatabaseModel(mb, dbm, BaseCall);
                invocation.ReturnValue = response;
                break;
            }
            case "VisitTables" when invocation.Arguments.Length == 2 && invocation.Arguments[0] is ModelBuilder mb &&
                                    invocation.Arguments[1] is ICollection<DatabaseTable> dt: {
                // ModelBuilder BaseCall(ModelBuilder modelBuilder, ICollection<DatabaseTable> databaseTables) {
                //     invocation.Proceed();
                //     return (ModelBuilder)invocation.ReturnValue;
                // }

                var response = VisitTables(mb, dt);
                invocation.ReturnValue = response;
                break;
            }
            case "VisitTable" when invocation.Arguments.Length == 2 && invocation.Arguments[0] is ModelBuilder mb &&
                                   invocation.Arguments[1] is DatabaseTable dt: {
                EntityTypeBuilder BaseCall() {
                    invocation.Proceed();
                    return (EntityTypeBuilder)invocation.ReturnValue;
                }

                var response = VisitTable(mb, dt, BaseCall);
                invocation.ReturnValue = response;
                break;
            }
            case "VisitPrimaryKey" when invocation.Arguments.Length == 2 &&
                                        invocation.Arguments[0] is EntityTypeBuilder entityTypeBuilder &&
                                        invocation.Arguments[1] is DatabaseTable table: {
                KeyBuilder BaseCall() {
                    invocation.Proceed();
                    return (KeyBuilder)invocation.ReturnValue;
                }

                var response = VisitPrimaryKey(entityTypeBuilder, table, BaseCall);
                invocation.ReturnValue = response;
                break;
            }
            case "VisitForeignKeys" when invocation.Arguments.Length == 2 && invocation.Arguments[0] is ModelBuilder mb &&
                                         invocation.Arguments[1] is IList<DatabaseForeignKey> fks: {
                ModelBuilder BaseCall(IList<DatabaseForeignKey> databaseForeignKeys) {
                    invocation.SetArgumentValue(1, databaseForeignKeys);
                    invocation.Proceed();
                    return (ModelBuilder)invocation.ReturnValue;
                }

                var response = VisitForeignKeys(mb, fks, BaseCall);
                invocation.ReturnValue = response;
                break;
            }
            case "Create" when invocation.Arguments.Length == 2 && invocation.Arguments[0] is DatabaseModel dm &&
                               invocation.Arguments[1] is ModelReverseEngineerOptions op: {
                IModel BaseCall(DatabaseModel databaseModel, ModelReverseEngineerOptions options2) {
                    invocation.SetArgumentValue(0, databaseModel);
                    invocation.SetArgumentValue(1, options2);
                    invocation.Proceed();
                    return (IModel)invocation.ReturnValue;
                }

                var response = Create(dm, op, BaseCall);
                invocation.ReturnValue = response;
                break;
            }
            case "GetEntityTypeName" when invocation.Arguments.Length == 1 && invocation.Arguments[0] is DatabaseTable t: {
                var response = GetEntityTypeName(t);
                invocation.ReturnValue = response;
                break;
            }
            case "GetDbSetName" when invocation.Arguments.Length == 1 && invocation.Arguments[0] is DatabaseTable t: {
                var response = GetDbSetName(t);
                invocation.ReturnValue = response;
                break;
            }
            default:
                invocation.Proceed();
                break;
        }
    }
}