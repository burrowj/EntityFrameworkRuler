﻿using System.Diagnostics.CodeAnalysis;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable ClassCanBeSealed.Global

namespace EntityFrameworkRuler.Design.Scaffolding.CodeGeneration;

[SuppressMessage("Usage", "EF1001:Internal EF Core API usage.")]
[SuppressMessage("ReSharper", "InconsistentNaming")]
[SuppressMessage("ReSharper", "NotAccessedField.Local")]
[SuppressMessage("ReSharper", "UnusedMember.Local")]
// ReSharper disable once ClassNeverInstantiated.Global
internal class RuledTemplatedModelGenerator {
    private const string DbContextTemplate = "DbContext.t4";
    private const string EntityTypeTemplate = "EntityType.t4";
    private const string EntityTypeConfigurationTemplate = "EntityTypeConfiguration.t4";
    private const string FunctionTemplate = "Functions.t4";
    private const string FunctionResultTypeTemplate = "FunctionResultType.t4";
    private const string FunctionsInterfaceTemplate = "FunctionsInterface.t4";
    private const string DbContextFunctionsTemplate = "DbContextFunctions.t4";

    private static FileInfo GetFile(string projectDir, string templateName) {
        if (projectDir.IsNullOrWhiteSpace()) return null;
        return new(Path.Combine(projectDir!, TemplatesDirectory, templateName));
    }

    internal static FileInfo GetDbContextFile(string projectDir) => GetFile(projectDir, DbContextTemplate);
    internal static FileInfo GetEntityTypeFile(string projectDir) => GetFile(projectDir, EntityTypeTemplate);
    internal static FileInfo GetEntityTypeConfigurationFile(string projectDir) => GetFile(projectDir, EntityTypeConfigurationTemplate);
    internal static FileInfo GetFunctionFile(string projectDir) => GetFile(projectDir, FunctionTemplate);
    internal static FileInfo GetDbContextFunctionsFile(string projectDir) => GetFile(projectDir, DbContextFunctionsTemplate);
    internal static FileInfo GetFunctionsInterfaceFile(string projectDir) => GetFile(projectDir, FunctionsInterfaceTemplate);
    internal static FileInfo GetFunctionResultTypeFile(string projectDir) => GetFile(projectDir,FunctionResultTypeTemplate);

    /// <summary> Gets the subdirectory under the project to look for templates in. </summary>
    /// <value>The subdirectory.</value>
    protected static string TemplatesDirectory { get; } = Path.Combine("CodeTemplates", "EFCore");
}