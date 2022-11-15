﻿using System.Diagnostics;
using System.Runtime.Serialization;

namespace EntityFrameworkRuler.Rules.NavigationNaming;

/// <inheritdoc />
[DebuggerDisplay("Class {Name}")]
[DataContract]
public sealed class ClassReference : IClassRule {
    /// <summary>
    /// The database schema name that the entity table is derived from.  Used to aid in resolution of this rule instance during the scaffolding phase.
    /// Optional.
    /// </summary>
    [DataMember(EmitDefaultValue = false, IsRequired = false, Order = 1)]
    public string DbSchema { get; set; }

    /// <summary>
    /// The raw database name of the table.  Used to aid in resolution of this rule instance during the scaffolding phase.
    /// Usually only populated when different from Name.
    /// </summary>
    [DataMember(EmitDefaultValue = false, IsRequired = false, Order = 2)]
    public string DbName { get; set; }

    /// <summary> The expected EF generated name for the entity. Required. </summary>
    [DataMember(EmitDefaultValue = true, IsRequired = true, Order = 3)]
    public string Name { get; set; }

    /// <summary> The property rules to apply to this entity. </summary>
    [DataMember(EmitDefaultValue = false, IsRequired = false, Order = 4)]
    public List<NavigationRename> Properties { get; set; } = new();

    string IClassRule.GetOldName() => Name.CoalesceWhiteSpace(DbName);
    string IClassRule.GetNewName() => Name;
    IEnumerable<IPropertyRule> IClassRule.GetProperties() => Properties;
}