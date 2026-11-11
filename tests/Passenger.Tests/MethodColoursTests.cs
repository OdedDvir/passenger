using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Unresolved member calls keep the parser's method colour when semantic tokens arrive, without overriding
/// the compiler's more specific colours for real locals, parameters, properties and fields invoked as delegates.</summary>
public sealed class MethodColoursTests : IClassFixture<MethodColoursTests.Project>
{
    private const string Source = """
        using System;

        StaticLogger.EnsureInitialized();
        Log.Information("warming up...");
        var builder = Host.CreateApplicationBuilder(args);
        int value = 1;
        Console.WriteLine(value);

        static class StaticLogger
        {
            public static void EnsureInitialized() { }
        }
        """;

    private readonly Project project;

    public MethodColoursTests(Project project)
    {
        this.project = project;
        project.Reset();
    }

    [AvaloniaTheory]
    [InlineData("Program.cs")]
    [InlineData("Unresolved.cs")]
    [InlineData("Variants.cs")]
    public void Method_calls_keep_their_colour_when_the_language_server_takes_over(string file)
    {
        string[] calls = file switch
        {
            "Unresolved.cs" => ["Information"],
            "Variants.cs" => ["Generic", "Conditional"],
            _ => ["EnsureInitialized", "Information", "CreateApplicationBuilder", "WriteLine"],
        };
        MainWindow window = project.Open(file, languageServer: false);
        try
        {
            foreach (string call in calls)
            {
                Assert.Equal(TokenKind.Method, Kind(window, call));
            }
        }
        finally
        {
            window.Close();
        }

        window = project.Open(file);
        try
        {
            Ui.WaitFor(() => Kind(window, file == "Program.cs" ? "value);" : "value++") == TokenKind.Local, "the language server's colour on the local's use");
            foreach (string call in calls)
            {
                Assert.True(Kind(window, call) == TokenKind.Method,
                    $"{call} was a method before the server loaded, but is {Kind(window, call)} after it loaded");
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Delegate_calls_keep_the_servers_local_parameter_property_and_field_colours()
    {
        MainWindow window = project.Open("Delegates.cs");
        try
        {
            Ui.WaitFor(() => Kind(window, "local();") == TokenKind.Local, "the server's colour on a local delegate call");
            Assert.Equal(TokenKind.Parameter, Kind(window, "parameter();"));
            Assert.Equal(TokenKind.Property, Kind(window, "Property();"));
            Assert.Equal(TokenKind.Field, Kind(window, "Field();"));
        }
        finally
        {
            window.Close();
        }
    }

    private static TokenKind? Kind(MainWindow window, string word)
    {
        OpenDocument document = window.EditorArea.Active!;
        TextSnapshot text = document.Text.Snapshot;
        int at = text.ToString().IndexOf(word, StringComparison.Ordinal);
        return document.Highlighter!.GetSpans(text, at, 1).Where(span => span.Start <= at && span.End > at)
            .Select(span => (TokenKind?)span.Kind).FirstOrDefault();
    }

    public sealed class Project : SharedProject
    {
        public Project()
            : base("method-colours-")
        {
            File.WriteAllText(PathOf("Sample.csproj"), TestProjects.Console());
            File.WriteAllText(PathOf("Program.cs"), Source);
            File.WriteAllText(PathOf("Unresolved.cs"), "static class Minimal\n{\n    static void Run()\n    {\n        Log.Information();\n        int value = 1;\n        value++;\n    }\n}\n");
            File.WriteAllText(PathOf("Variants.cs"), "static class Variants\n{\n    static void Run(object receiver)\n    {\n"
                + "        Log. /* a comment between the dot and name */ Generic<int>();\n        receiver?.Conditional();\n"
                + "        int value = 1;\n        value++;\n    }\n}\n");
            File.WriteAllText(PathOf("Delegates.cs"), "using System;\nstatic class Delegates\n{\n"
                + "    public static Action Property { get; } = () => { };\n    public static Action Field = () => { };\n"
                + "    public static void Run(Action parameter)\n    {\n        Action local = () => { };\n"
                + "        local();\n        parameter();\n        Delegates.Property();\n        Delegates.Field();\n    }\n}\n");
        }
    }
}
