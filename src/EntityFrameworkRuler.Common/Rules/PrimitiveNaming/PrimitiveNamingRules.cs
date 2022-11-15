using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace EntityFrameworkRuler.Rules.PrimitiveNaming;

/// <summary>
/// Renaming rules for primitive properties (database columns) as well as the classes themselves (tables).
/// Navigations are not referenced in this file.
/// </summary>
[DataContract]
public sealed class PrimitiveNamingRules : IRuleModelRoot {
    /// <summary> Preserve casing using regex </summary>
    [DataMember(Order = 1)]
    public bool PreserveCasingUsingRegex { get; set; }

    /// <summary> Schema rules </summary>
    [DataMember(Order = 2)]
    public List<SchemaRule> Schemas { get; set; } = new();


    /// <inheritdoc />
    [IgnoreDataMember, JsonIgnore, XmlIgnore]
    public RuleModelKind Kind => RuleModelKind.PrimitiveNaming;

    IEnumerable<IClassRule> IRuleModelRoot.GetClasses() => Schemas.SelectMany(o => o.Tables);
}