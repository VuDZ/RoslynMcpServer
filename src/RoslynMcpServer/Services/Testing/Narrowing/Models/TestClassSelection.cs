using RoslynMcpServer.Services.Testing.Models;

namespace RoslynMcpServer.Services.Testing.Narrowing.Models;

/// <summary>A test class of the current inventory in a stage, with every reason that put it there up to that stage.</summary>
internal sealed record TestClassSelection(VstestTestClass Class, TestClassSelectionReasons Reasons);
