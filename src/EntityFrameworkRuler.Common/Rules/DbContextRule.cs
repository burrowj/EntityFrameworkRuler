using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace EntityFrameworkRuler.Rules;

/// <summary> Scaffolding rules for the DB context and all containing elements. </summary>
[DataContract]
public sealed class DbContextRule : RuleBase, IRuleModelRoot {
    /// <summary> Creates a DB context rule </summary>
    public DbContextRule() {
        Schemas = Observable ? new ObservableCollection<SchemaRule>() : new List<SchemaRule>();
    }

    internal static DbContextRule DefaultNoRulesFoundBehavior => new() { IncludeUnknownSchemas = true };

    /// <summary> DB context name of the reverse engineered model that this rule set applies to. </summary>
    [DataMember(Order = 1)]
    [DisplayName("Name"), Category("Mapping"),
     Description("DB context name of the reverse engineered model that this rule set applies to.")]
    public string Name { get; set; }

    /// <summary> Preserve casing using regex. </summary>
    [DataMember(Order = 2)]
    [DisplayName("Preserve Casing Using Regex"), Category("Naming"), Description("Preserve casing using regex.")]
    public bool PreserveCasingUsingRegex { get; set; }

    /// <summary> If true, generate entity models for schemas that are not identified in this rule set.  Default is false. </summary>
    [DataMember(EmitDefaultValue = true, IsRequired = false, Order = 3)]
    [DisplayName("Include Unknown Columns"), Category("Mapping"),
     Description("If true, generate entity models for schemas that are not identified in this rule set.  Default is false.")]
    public bool IncludeUnknownSchemas { get; set; }

    /// <summary> If true, EntityTypeConfigurations will be split into separate files using EntityTypeConfiguration.t4 for EF >= 7.  Default is false. </summary>
    [DataMember(EmitDefaultValue = true, IsRequired = false, Order = 4)]
    [DisplayName("Split Entity Type Configurations"), Category("Mapping"),
     Description(
         "If true, EntityTypeConfigurations will be split into separate files using EntityTypeConfiguration.t4 for EF >= 7.  Default is false.")]
    public bool SplitEntityTypeConfigurations { get; set; }

    /// <summary> Schema rules </summary>
    [DataMember(Order = 100)]
    [DisplayName("Schemas"), Category("Schemas|Schemas"), Description("The schema rules to apply to this DB context.")]
    public IList<SchemaRule> Schemas { get; private set; }


    /// <inheritdoc />
    [IgnoreDataMember, JsonIgnore, XmlIgnore]
    public RuleModelKind Kind => RuleModelKind.DbContext;

    [IgnoreDataMember, JsonIgnore, XmlIgnore]
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    internal string FilePath { get; set; }

    /// <inheritdoc />
    protected override string GetExpectedEntityFrameworkName() => Name;

    /// <inheritdoc />
    protected override string GetNewName() => null;

    /// <inheritdoc />
    protected override void SetFinalName(string value) {
        Name = value;
        //OnPropertyChanged(nameof(Name));
    }

    /// <inheritdoc />
    [IgnoreDataMember, JsonIgnore, XmlIgnore]
    [DisplayName("Not Mapped"), Category("Mapping"), Description("Assumed true at the DB Context level.")]
    public override bool NotMapped { get => false; set { } }

    IEnumerable<ISchemaRule> IRuleModelRoot.GetSchemas() => Schemas;
    IEnumerable<IClassRule> IRuleModelRoot.GetClasses() => Schemas.SelectMany(o => o.Tables);
     string IRuleModelRoot.GetFilePath() => FilePath;
}