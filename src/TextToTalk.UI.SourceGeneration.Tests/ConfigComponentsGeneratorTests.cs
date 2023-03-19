using System.Text;
using ImGuiNET;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using Microsoft.CodeAnalysis.Text;
using TextToTalk.UI.Core;
using VerifyCS =
    TextToTalk.UI.SourceGeneration.Tests.CSharpSourceGeneratorVerifier<
        TextToTalk.UI.SourceGeneration.Tests.ConfigComponentsGeneratorTests,
        TextToTalk.UI.SourceGeneration.ConfigComponentsGenerator>;

namespace TextToTalk.UI.SourceGeneration.Tests;

public class ConfigComponentsGeneratorTests
{
    private static readonly ReferenceAssemblies Net70Windows = new("net7.0-windows",
        new PackageIdentity("Microsoft.NETCore.App.Ref", "7.0.0"),
        Path.Combine("ref", "net7.0-windows"));

    private static string GetTargetConfigSourceCode(string configInterfaces)
    {
        //lang=c#
        return $@"// <auto-generated/>

using TextToTalk.UI.Core;

namespace TextToTalk.Configuration
{{
    public class Test1Config {(!string.IsNullOrEmpty(configInterfaces) ? $": {configInterfaces}" : "")}
    {{
        public bool Option1 {{ get; set; }}

        private bool Option2 {{ get; set; }}

        public bool Option3 {{ get; set; }}

        public void Save()
        {{
            // no-op
        }}
    }}
}}
";
    }

    private static string GetTargetSourceCode(string modifiers)
    {
        //lang=c#
        return $@"// <auto-generated/>
using TextToTalk.Configuration;
using TextToTalk.UI.Core;

namespace TextToTalk.UI
{{
    [UseConfigComponents(typeof(Test1Config))]
    {modifiers} class Test1Target
    {{
    }}
}}
";
    }

    private static string GetExpectedGeneratedCode(string modifiers)
    {
        //lang=c#
        return $@"// <auto-generated />
namespace TextToTalk.UI;

[global::System.CodeDom.Compiler.GeneratedCodeAttribute(""TextToTalk.UI.SourceGeneration"", ""1.0.0.0"")]
{modifiers} class Test1Target
{{
    public global::System.Action OnOptionChanged {{ get; }}

    /// <summary>
    /// Creates a checkbox which toggles the provided configuration object's
    /// <see cref=""global::TextToTalk.Configuration.Test1Config.Option1""/> property.
    /// </summary>
    /// <param name=""label"">The label for the UI element.</param>
    /// <param name=""config"">The config object being modified.</param>
    public static void ToggleOption1(string label, global::TextToTalk.Configuration.Test1Config config)
    {{
        var value = config.Option1;
        if (global::ImGuiNET.ImGui.Checkbox(label, ref value))
        {{
            config.Option1 = value;
            config.Save();
        }}
    }}

    /// <summary>
    /// Creates a checkbox which toggles the provided configuration object's
    /// <see cref=""global::TextToTalk.Configuration.Test1Config.Option3""/> property.
    /// </summary>
    /// <param name=""label"">The label for the UI element.</param>
    /// <param name=""config"">The config object being modified.</param>
    public static void ToggleOption3(string label, global::TextToTalk.Configuration.Test1Config config)
    {{
        var value = config.Option3;
        if (global::ImGuiNET.ImGui.Checkbox(label, ref value))
        {{
            config.Option3 = value;
            config.Save();
        }}
    }}
}}
";
    }

    [Fact]
    public async Task Generates_Expected_Code_When_Public()
    {
        await new VerifyCS.Test
        {
            TestState =
            {
                ReferenceAssemblies = Net70Windows,
                AdditionalReferences =
                {
                    MetadataReference.CreateFromFile(typeof(UseConfigComponentsAttribute).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(ImGui).Assembly.Location),
                },
                Sources =
                {
                    ("Test1Config.cs", SourceText.From(GetTargetConfigSourceCode("ISaveable"), Encoding.UTF8)),
                    ("Test1Target.cs", SourceText.From(GetTargetSourceCode("public partial"), Encoding.UTF8)),
                },
                GeneratedSources =
                {
                    (@"TextToTalk.UI.SourceGeneration\TextToTalk.UI.SourceGeneration.ConfigComponentsGenerator\Test1Target.ConfigComponents.g.cs",
                        SourceText.From(GetExpectedGeneratedCode("public partial"), Encoding.UTF8)),
                },
            },
        }.AddGeneratedSources().RunAsync();
    }

    [Fact]
    public async Task Generates_Expected_Code_When_Internal()
    {
        await new VerifyCS.Test
        {
            TestState =
            {
                ReferenceAssemblies = Net70Windows,
                AdditionalReferences =
                {
                    MetadataReference.CreateFromFile(typeof(UseConfigComponentsAttribute).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(ImGui).Assembly.Location),
                },
                Sources =
                {
                    ("Test1Config.cs", SourceText.From(GetTargetConfigSourceCode("ISaveable"), Encoding.UTF8)),
                    ("Test1Target.cs", SourceText.From(GetTargetSourceCode("internal partial"), Encoding.UTF8)),
                },
                GeneratedSources =
                {
                    (@"TextToTalk.UI.SourceGeneration\TextToTalk.UI.SourceGeneration.ConfigComponentsGenerator\Test1Target.ConfigComponents.g.cs",
                        SourceText.From(GetExpectedGeneratedCode("internal partial"), Encoding.UTF8)),
                },
            },
        }.AddGeneratedSources().RunAsync();
    }

    [Fact]
    public async Task Does_Not_Generate_Code_When_Not_Partial()
    {
        await Assert.ThrowsAsync<EqualWithMessageException>(() => new VerifyCS.Test
        {
            TestState =
            {
                ReferenceAssemblies = Net70Windows,
                AdditionalReferences =
                {
                    MetadataReference.CreateFromFile(typeof(UseConfigComponentsAttribute).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(ImGui).Assembly.Location),
                },
                Sources =
                {
                    ("Test1Config.cs", SourceText.From(GetTargetConfigSourceCode("ISaveable"), Encoding.UTF8)),
                    ("Test1Target.cs", SourceText.From(GetTargetSourceCode("public"), Encoding.UTF8)),
                },
                GeneratedSources =
                {
                    (@"TextToTalk.UI.SourceGeneration\TextToTalk.UI.SourceGeneration.ConfigComponentsGenerator\Test1Target.ConfigComponents.g.cs",
                        SourceText.From(GetExpectedGeneratedCode("public"), Encoding.UTF8)),
                },
            },
        }.AddGeneratedSources().RunAsync());
    }

    [Fact]
    public async Task Does_Not_Generate_Code_When_Config_Not_Saveable()
    {
        await Assert.ThrowsAsync<EqualWithMessageException>(() => new VerifyCS.Test
        {
            TestState =
            {
                ReferenceAssemblies = Net70Windows,
                AdditionalReferences =
                {
                    MetadataReference.CreateFromFile(typeof(UseConfigComponentsAttribute).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(ImGui).Assembly.Location),
                },
                Sources =
                {
                    ("Test1Config.cs", SourceText.From(GetTargetConfigSourceCode(""), Encoding.UTF8)),
                    ("Test1Target.cs", SourceText.From(GetTargetSourceCode("public"), Encoding.UTF8)),
                },
                GeneratedSources =
                {
                    (@"TextToTalk.UI.SourceGeneration\TextToTalk.UI.SourceGeneration.ConfigComponentsGenerator\Test1Target.ConfigComponents.g.cs",
                        SourceText.From(GetExpectedGeneratedCode("public"), Encoding.UTF8)),
                },
            },
        }.AddGeneratedSources().RunAsync());
    }
}