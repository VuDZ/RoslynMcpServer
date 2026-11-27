namespace RoslynMcpServer.Services.Testing.Il.Models;

/// <summary>An attribute on a method, kept to recognize test methods after all assemblies are read.</summary>
internal readonly record struct IlMethodAttribute(string Method, IlTypeName Attribute);
