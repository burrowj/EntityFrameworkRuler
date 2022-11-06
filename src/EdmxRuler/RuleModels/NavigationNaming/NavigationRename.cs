﻿using System.Diagnostics;
using System.Runtime.Serialization;

namespace EdmxRuler.RuleModels.NavigationNaming;
[DebuggerDisplay("Nav {Name} to {NewName}")]
[DataContract]
public sealed class NavigationRename {
    [DataMember]
    public string Name { get; set; }

    [DataMember]
    public string NewName { get; set; }

    /// <summary>
    /// Gets or sets the optional alternative name to look for if Name is not found.
    /// Used in navigation renaming since prediction of the generated name can be difficult.
    /// This way, for example, the user can use Name to suggest the "Fk+Navigation(s)" name while
    /// AlternateName supplies the basic pluralization name.
    /// </summary>
    [DataMember]
    public string AlternateName { get; set; }
}