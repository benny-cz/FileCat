using System.Text.Json;
using FileCat.Core.State;
using FileCat.Core.Tools;

namespace FileCat.Core.Tests;

public sealed class ToolTextRoundTripTests
{
    [Theory]
    [InlineData("")]
    [InlineData("C:\\My Editor Profile\\")]
    [InlineData("a|b")]
    [InlineData("a\"b")]
    [InlineData("\"")]
    [InlineData("Příliš žluťoučký 🐈")]
    [InlineData("before\tafter")]
    [InlineData("line one\r\nline two")]
    [InlineData("@json:[1,2]")]
    [InlineData(" space ")]
    public void Argument_boundaries_and_literal_content_survive_format_and_parse(string value)
    {
        string[] expected = ["prefix", value, "", "{files}"];
        string text = ToolTextGrammar.FormatArguments(expected);
        Assert.True(ToolTextGrammar.TryParseArguments(text, out var actual, out var error), error);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Empty_vector_and_one_empty_argument_remain_different()
    {
        Assert.True(ToolTextGrammar.TryParseArguments("", out var none, out _));
        Assert.True(ToolTextGrammar.TryParseArguments("\"\"", out var one, out _));
        Assert.Empty(none);
        Assert.Equal([""], one);
    }

    [Theory]
    [InlineData("bad \"unterminated")]
    [InlineData("@json:null")]
    [InlineData("@json:[1]")]
    [InlineData("@json:[null]")]
    [InlineData("@json:[\"incomplete\"")]
    public void Invalid_argument_fields_never_return_a_partial_vector(string text)
    {
        Assert.False(ToolTextGrammar.TryParseArguments(text, out var values, out var error));
        Assert.Empty(values);
        Assert.NotNull(error);
    }

    [Fact]
    public void Comment_marker_names_and_group_names_survive_repeated_settings_round_trips()
    {
        List<ToolDefinition> original = [
            new() { Name = "#Build", Executable = "tool.exe", Arguments = ["{files}", ""] },
            new() { Name = "#Tools", Children = [new() { Name = "#Build > quoted", Executable = "tool.exe", Arguments = ["a|b", "literal\"quote"], WorkingDirectory = "C:\\Spaced folder", ShellMode = true }] },
        ];
        string expected = JsonSerializer.Serialize(original);
        for (int i = 0; i < 3; i++)
        {
            original = UserCommandsText.Parse("# An intentional comment\n" + UserCommandsText.Format(original), out var error);
            Assert.Null(error);
            Assert.Equal(expected, JsonSerializer.Serialize(original));
        }
    }

    [Theory]
    [InlineData("Run | tool.exe | \"unterminated", "unmatched double quote")]
    [InlineData("Run | tool.exe | {file} | shell | ignored", "unexpected columns")]
    [InlineData("Run | tool.exe | x | dir=one dir=two", "Duplicate option")]
    public void Malformed_command_rows_are_reported_without_accepting_prior_rows(string invalid, string expectedMessage)
    {
        var parsed = UserCommandsText.Parse("Valid | tool.exe | x\n" + invalid, out var error);
        Assert.Empty(parsed);
        Assert.StartsWith("Line 2:", error);
        Assert.Contains(expectedMessage, error);
    }

    [Fact]
    public void Association_exclusion_mask_and_all_launch_fields_survive_an_editable_round_trip()
    {
        List<ToolDefinition> original = [new()
        {
            Name = "Custom viewer", Mask = "*.txt|secret*", Intent = "view", Executable = "C:\\My Tools\\viewer.exe",
            Arguments = ["--profile", "C:\\My Editor Profile", "", "a|b", "literal\"quote", "{file}"],
            WorkingDirectory = "C:\\Výstup", ShellMode = true, Hotkey = "",
        }];
        for (int i = 0; i < 3; i++)
        {
            var parsed = Associations.Parse(Associations.Format(original), out var error);
            Assert.Null(error);
            Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(parsed));
            Assert.NotNull(Associations.Find(parsed, "view", "ordinary.txt"));
            Assert.Null(Associations.Find(parsed, "view", "secret-notes.txt"));
            original = parsed;
        }
    }

    [Fact]
    public void Line_break_arguments_and_working_directory_remain_one_row_in_the_tool_grammar()
    {
        var tool = new ToolDefinition { Name = "Build", Executable = "tool.exe", Arguments = ["two\nlines", "a|b"], WorkingDirectory = "two\nfolders" };
        var text = UserCommandsText.Format([tool]);
        Assert.DoesNotContain('\n', text);
        var parsed = Assert.Single(UserCommandsText.Parse(text, out var error));
        Assert.Null(error);
        Assert.Equal(JsonSerializer.Serialize(tool), JsonSerializer.Serialize(parsed));
    }

    [Fact]
    public void Oversized_input_is_rejected_before_tokenization()
    {
        Assert.False(ToolTextGrammar.TryParseArguments(new string('x', ToolTextGrammar.MaximumTextLength + 1), out var values, out var error));
        Assert.Empty(values);
        Assert.Contains("editing limit", error);
    }

    [Fact]
    public void Noncanonical_submenu_structures_preserve_complete_values_order_and_empty_groups()
    {
        List<ToolDefinition> tools = [
            new() { Name = "Repeated", Hotkey = "Ctrl+1", Children = [new() { Name = "First", Executable = "first", Mask = "*.txt", Intent = "open" }] },
            new() { Name = "Between", Executable = "between" },
            new() { Name = "Repeated", Children = [new() { Name = "Last", Executable = "last" }] },
            new() { Name = "Empty", Children = [] },
        ];
        string expected = JsonSerializer.Serialize(tools);
        for (int i = 0; i < 3; i++)
        {
            string text = UserCommandsText.Format(tools);
            Assert.StartsWith("@tools:", text);
            tools = UserCommandsText.Parse(text, out var error);
            Assert.Null(error);
            Assert.Equal(expected, JsonSerializer.Serialize(tools));
        }
    }

    [Theory]
    [InlineData("@tools:null")]
    [InlineData("@tools:[null]")]
    [InlineData("@tools:[{\"Name\":\"Run\",\"Executable\":\"tool\",\"Arguments\":[null]}]")]
    [InlineData("@tools:[{\"Name\":\"Run\",\"Executable\":\"tool\",\"Unexpected\":true}]")]
    public void Structured_tool_edits_reject_unknown_or_invalid_data_atomically(string text)
    {
        Assert.Empty(UserCommandsText.Parse(text, out var error));
        Assert.NotNull(error);
    }
}
