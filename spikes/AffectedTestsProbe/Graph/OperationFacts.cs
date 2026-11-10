using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace AffectedTestsProbe;

/// <summary>
/// Edges of one document. A lambda or local function stays in the enclosing member, so it has no node.
/// </summary>
internal sealed class OperationFacts : OperationWalker
{
    private readonly FactGraph _graph;
    private readonly Solution _solution;
    private readonly IReadOnlyDictionary<ProjectId, ProjectKey> _keys;
    private readonly HashSet<string> _indexedTypes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _walkedDefaults = new(StringComparer.Ordinal);
    private SemanticModel? _model;
    private MemberFact? _fact;

    public OperationFacts(FactGraph graph, Solution solution, IReadOnlyDictionary<ProjectId, ProjectKey> keys)
    {
        _graph = graph;
        _solution = solution;
        _keys = keys;
    }

    public void Walk(SyntaxNode root, SemanticModel model)
    {
        _model = model;
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case MethodDeclarationSyntax method:
                    WalkDeclared(model.GetDeclaredSymbol(method), Body(model, method, method.Body, method.ExpressionBody), method);
                    break;
                case ConstructorDeclarationSyntax constructor:
                    WalkDeclared(
                        model.GetDeclaredSymbol(constructor),
                        Body(model, constructor, constructor.Body, constructor.ExpressionBody),
                        constructor);
                    break;
                case DestructorDeclarationSyntax destructor:
                    WalkDeclared(model.GetDeclaredSymbol(destructor), Body(model, destructor, destructor.Body, destructor.ExpressionBody), destructor);
                    break;
                case OperatorDeclarationSyntax op:
                    WalkDeclared(model.GetDeclaredSymbol(op), Body(model, op, op.Body, op.ExpressionBody), op);
                    break;
                case ConversionOperatorDeclarationSyntax conversion:
                    WalkDeclared(
                        model.GetDeclaredSymbol(conversion),
                        Body(model, conversion, conversion.Body, conversion.ExpressionBody),
                        conversion);
                    break;
                case AccessorDeclarationSyntax accessor:
                    WalkDeclared(model.GetDeclaredSymbol(accessor), Body(model, accessor, accessor.Body, accessor.ExpressionBody), accessor);
                    break;
                case VariableDeclaratorSyntax variable when variable.Initializer?.Value is not null:
                    WalkDeclared(model.GetDeclaredSymbol(variable), model.GetOperation(variable.Initializer.Value), variable);
                    break;
                case PropertyDeclarationSyntax property when property.Initializer?.Value is not null:
                    WalkDeclared(model.GetDeclaredSymbol(property), model.GetOperation(property.Initializer.Value), property);
                    break;
                case PropertyDeclarationSyntax property when property.ExpressionBody is not null:
                    var getter = (model.GetDeclaredSymbol(property) as IPropertySymbol)?.GetMethod;
                    WalkDeclared(getter, model.GetOperation(property.ExpressionBody), property);
                    break;
                case IndexerDeclarationSyntax indexer when indexer.ExpressionBody is not null:
                    WalkDeclared((model.GetDeclaredSymbol(indexer) as IPropertySymbol)?.GetMethod, model.GetOperation(indexer.ExpressionBody), indexer);
                    break;
            }
        }
    }

    public override void VisitInvocation(IInvocationOperation operation)
    {
        var method = operation.TargetMethod;
        if (IsMockFactory(method.ContainingType))
        {
            foreach (var argument in method.TypeArguments)
            {
                NoteMock(argument);
            }
        }
        else
        {
            foreach (var argument in method.TypeArguments)
            {
                AddCreated(argument);
            }
        }

        // Delegate invoke is not a virtual call to a source member.
        var dispatch = operation.IsVirtual && method.ContainingType?.TypeKind != TypeKind.Delegate;
        AddCall(method, dispatch);
        base.VisitInvocation(operation);
    }

    public override void VisitObjectCreation(IObjectCreationOperation operation)
    {
        if (operation.Type is INamedTypeSymbol named && IsMockFactory(named))
        {
            foreach (var argument in named.TypeArguments)
            {
                NoteMock(argument);
            }
        }
        else if (operation.Type is INamedTypeSymbol created)
        {
            AddCreated(created);
            NoteStaticUse(created);
        }

        if (operation.Constructor is not null)
        {
            AddCall(operation.Constructor, isVirtual: false);
        }

        base.VisitObjectCreation(operation);
    }

    public override void VisitTypeOf(ITypeOfOperation operation)
    {
        AddCreated(operation.TypeOperand);
        base.VisitTypeOf(operation);
    }

    public override void VisitPropertyReference(IPropertyReferenceOperation operation)
    {
        AddCall(operation.Property.GetMethod, IsDispatch(operation.Property));
        if (operation.Property.IsStatic)
        {
            NoteStaticUse(operation.Property.ContainingType);
        }

        VisitInstance(operation.Instance);
        foreach (var argument in operation.Arguments)
        {
            Visit(argument);
        }
    }

    public override void VisitSimpleAssignment(ISimpleAssignmentOperation operation)
    {
        // The target of a property assignment is a set, not a get.
        if (operation.Target is IPropertyReferenceOperation property)
        {
            AddCall(property.Property.SetMethod, IsDispatch(property.Property));
            VisitInstance(property.Instance);
        }
        else
        {
            Visit(operation.Target);
        }

        Visit(operation.Value);
    }

    public override void VisitCompoundAssignment(ICompoundAssignmentOperation operation)
    {
        if (operation.Target is IPropertyReferenceOperation property)
        {
            AddCall(property.Property.SetMethod, IsDispatch(property.Property));
        }

        base.VisitCompoundAssignment(operation);
    }

    public override void VisitFieldReference(IFieldReferenceOperation operation)
    {
        if (operation.Field.IsConst)
        {
            AddCall(operation.Field, isVirtual: false);
        }
        else if (operation.Field.IsStatic)
        {
            NoteStaticUse(operation.Field.ContainingType);
        }

        VisitInstance(operation.Instance);
    }

    public override void VisitMethodReference(IMethodReferenceOperation operation)
    {
        var dispatch = (operation.Method.IsVirtual || operation.Method.IsAbstract || operation.Method.IsOverride)
            && operation.Method.ContainingType?.TypeKind != TypeKind.Delegate;
        AddCall(operation.Method, dispatch);
        base.VisitMethodReference(operation);
    }

    public override void VisitEventAssignment(IEventAssignmentOperation operation)
    {
        if (operation.EventReference is IEventReferenceOperation reference)
        {
            var accessor = operation.Adds ? reference.Event.AddMethod : reference.Event.RemoveMethod;
            AddCall(accessor, IsDispatch(reference.Event));
        }
        base.VisitEventAssignment(operation);
    }

    public override void VisitBinaryOperator(IBinaryOperation operation)
    {
        if (operation.OperatorMethod is not null)
        {
            AddCall(operation.OperatorMethod, isVirtual: false);
        }

        base.VisitBinaryOperator(operation);
    }

    public override void VisitUnaryOperator(IUnaryOperation operation)
    {
        if (operation.OperatorMethod is not null)
        {
            AddCall(operation.OperatorMethod, isVirtual: false);
        }

        base.VisitUnaryOperator(operation);
    }

    public override void VisitConversion(IConversionOperation operation)
    {
        if (operation.OperatorMethod is not null)
        {
            AddCall(operation.OperatorMethod, isVirtual: false);
        }

        base.VisitConversion(operation);
    }

    public override void VisitArgument(IArgumentOperation operation)
    {
        // A default value is inlined at the caller. The parameter declaration is not the predecessor.
        if (operation.IsImplicit && operation.Parameter is { HasExplicitDefaultValue: true } parameter)
        {
            var id = parameter.OriginalDefinition.GetDocumentationCommentId();
            if (id is null || _walkedDefaults.Add(id))
            {
                WalkDefault(parameter);
            }
        }

        base.VisitArgument(operation);
    }

    private void WalkDeclared(ISymbol? symbol, IOperation? operation, SyntaxNode syntax)
    {
        if (symbol is null)
        {
            return;
        }

        IndexContainingType(symbol);
        if (SymbolIds.TryNode(symbol, _solution, _keys) is not { } member)
        {
            return;
        }

        _fact = _graph.Fact(member);
        if (operation is not null)
        {
            Visit(operation);
        }

        if (syntax is ConstructorDeclarationSyntax { Initializer: { } initializer } && _model?.GetOperation(initializer) is { } initializerOperation)
        {
            Visit(initializerOperation);
        }

        RememberMember(symbol, member, syntax);
        _fact = null;
    }

    private void RememberMember(ISymbol symbol, MemberNode node, SyntaxNode syntax)
    {
        var type = symbol.ContainingType;
        var typeId = type?.OriginalDefinition.GetDocumentationCommentId();
        if (type is null || typeId is null)
        {
            return;
        }

        var shape = _graph.Shape(node.Project, typeId);
        switch (symbol)
        {
            case IMethodSymbol { MethodKind: MethodKind.Constructor }
                when syntax is ConstructorDeclarationSyntax declaration
                && declaration.Initializer?.ThisOrBaseKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ThisKeyword) == true:
                shape.ChainsToThis.Add(node);
                Add(shape.InstanceConstructors, node);
                break;
            case IMethodSymbol { MethodKind: MethodKind.Constructor }:
                Add(shape.InstanceConstructors, node);
                break;
            case IMethodSymbol { MethodKind: MethodKind.StaticConstructor }:
                Add(shape.StaticInitializers, node);
                break;
            case IFieldSymbol { IsConst: true }:
                break;
            case IFieldSymbol field when syntax is VariableDeclaratorSyntax { Initializer: not null }:
                Add(field.IsStatic ? shape.StaticInitializers : shape.InstanceInitializers, node);
                break;
            case IPropertySymbol property when syntax is PropertyDeclarationSyntax { Initializer: not null }:
                Add(property.IsStatic ? shape.StaticInitializers : shape.InstanceInitializers, node);
                break;
        }
    }

    private void IndexContainingType(ISymbol symbol)
    {
        var type = symbol.ContainingType?.OriginalDefinition;
        var key = type is null ? "" : SymbolIds.TypeKey(type, _solution);
        if (type is null || key.Length == 0 || !_indexedTypes.Add(key))
        {
            return;
        }

        IndexType(type);
    }

    private void IndexType(INamedTypeSymbol type)
    {
        var typeId = type.GetDocumentationCommentId();
        if (typeId is null || SymbolIds.TryNode(type, _solution, _keys) is not { } declaration)
        {
            return;
        }

        var shape = _graph.Shape(declaration.Project, typeId);
        shape.IsRecord = type.IsRecord;
        shape.Declaration = declaration;
        foreach (var constructor in type.InstanceConstructors)
        {
            if (SymbolIds.TryNode(constructor, _solution, _keys) is { } constructorNode)
            {
                Add(shape.InstanceConstructors, constructorNode);
            }
        }

        foreach (var member in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (member.MethodKind == MethodKind.StaticConstructor
                && SymbolIds.TryNode(member, _solution, _keys) is { } staticNode)
            {
                Add(shape.StaticInitializers, staticNode);
            }
        }

        if (type.IsRecord)
        {
            foreach (var member in type.GetMembers())
            {
                if (member.IsImplicitlyDeclared && SymbolIds.TryNode(member, _solution, _keys) is { } synthesized)
                {
                    Add(shape.Synthesized, synthesized);
                }
            }
        }

        var typeKey = SymbolIds.TypeKey(type, _solution);
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            AddOverrideChain(method, typeKey);
        }

        foreach (var iface in type.AllInterfaces)
        {
            foreach (var member in iface.GetMembers())
            {
                var implementation = type.FindImplementationForInterfaceMember(member);
                if (implementation is not null)
                {
                    AddImplementation(member, implementation, typeKey);
                }
            }
        }
    }

    private void AddOverrideChain(IMethodSymbol method, string typeKey)
    {
        if (SymbolIds.TryNode(method, _solution, _keys) is not { } implementation)
        {
            return;
        }

        for (var current = method.OverriddenMethod; current is not null; current = current.OverriddenMethod)
        {
            var id = SymbolIds.DocumentationId(current);
            if (id is not null)
            {
                _graph.Implementations.Add(id, implementation, typeKey);
            }
        }
    }

    private void AddImplementation(ISymbol declared, ISymbol implementation, string typeKey)
    {
        switch (declared, implementation)
        {
            case (IMethodSymbol declaredMethod, IMethodSymbol implemented):
                AddMethodImplementation(declaredMethod, implemented, typeKey);
                break;
            case (IPropertySymbol declaredProperty, IPropertySymbol implemented):
                Pair(declaredProperty.GetMethod, implemented.GetMethod, typeKey);
                Pair(declaredProperty.SetMethod, implemented.SetMethod, typeKey);
                break;
            case (IEventSymbol declaredEvent, IEventSymbol implemented):
                Pair(declaredEvent.AddMethod, implemented.AddMethod, typeKey);
                Pair(declaredEvent.RemoveMethod, implemented.RemoveMethod, typeKey);
                break;
        }
    }

    private void Pair(IMethodSymbol? declared, IMethodSymbol? implemented, string typeKey)
    {
        if (declared is not null && implemented is not null)
        {
            AddMethodImplementation(declared, implemented, typeKey);
        }
    }

    private void AddMethodImplementation(IMethodSymbol declared, IMethodSymbol implemented, string typeKey)
    {
        var id = SymbolIds.DocumentationId(declared);
        if (id is not null && SymbolIds.TryNode(implemented, _solution, _keys) is { } node)
        {
            _graph.Implementations.Add(id, node, typeKey);
        }
    }

    private void AddCall(ISymbol? symbol, bool isVirtual)
    {
        if (_fact is null || symbol is null || symbol is IMethodSymbol { MethodKind: MethodKind.LocalFunction })
        {
            return;
        }

        var documentationId = SymbolIds.DocumentationId(symbol);
        var node = SymbolIds.TryNode(symbol, _solution, _keys);
        var leaf = node is null ? documentationId : null;
        string? virtualId = null;
        string? declaredType = null;
        if (isVirtual)
        {
            virtualId = documentationId;
            if (symbol.ContainingType is { } containing)
            {
                declaredType = SymbolIds.TypeKey(containing, _solution);
            }
        }

        _fact.Calls.Add(new CallSite(node, isVirtual, virtualId, declaredType, leaf));
        if (symbol.IsStatic && symbol is not IMethodSymbol { MethodKind: MethodKind.StaticConstructor })
        {
            NoteStaticUse(symbol.ContainingType);
        }
    }

    private void AddCreated(ITypeSymbol? type)
    {
        if (_fact is null || type is null)
        {
            return;
        }

        if (type is IArrayTypeSymbol array)
        {
            AddCreated(array.ElementType);
            return;
        }

        if (type is not INamedTypeSymbol named || named.SpecialType != SpecialType.None)
        {
            return;
        }

        if (named.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Interface))
        {
            return;
        }

        var key = SymbolIds.TypeKey(named, _solution);
        if (key.Length > 0)
        {
            _fact.CreatedTypeKeys.Add(key);
        }

        foreach (var argument in named.TypeArguments)
        {
            AddCreated(argument);
        }
    }

    private void NoteMock(ITypeSymbol type)
    {
        if (_fact is null || type is not INamedTypeSymbol named || named.SpecialType != SpecialType.None)
        {
            return;
        }

        var key = SymbolIds.TypeKey(named, _solution);
        if (key.Length == 0)
        {
            return;
        }

        if (named.TypeKind == TypeKind.Interface)
        {
            _fact.MockedTypeKeys.Add(key);
        }
        else if (named.TypeKind is TypeKind.Class or TypeKind.Struct)
        {
            _fact.CreatedTypeKeys.Add(key);
        }
    }

    private void NoteStaticUse(INamedTypeSymbol? type)
    {
        if (_fact is null || type is null || type.TypeKind == TypeKind.Error)
        {
            return;
        }

        var original = type.OriginalDefinition;
        var id = original.GetDocumentationCommentId();
        if (id is null || SymbolIds.TryNode(original, _solution, _keys) is not { } node)
        {
            return;
        }

        _fact.StaticUses.Add((node.Project, id));
    }

    private void WalkDefault(IParameterSymbol parameter)
    {
        if (_model is null
            || parameter.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not ParameterSyntax { Default.Value: { } value }
            || !_model.Compilation.ContainsSyntaxTree(value.SyntaxTree))
        {
            return;
        }

        var operation = _model.Compilation.GetSemanticModel(value.SyntaxTree).GetOperation(value);
        if (operation is not null)
        {
            Visit(operation);
        }
    }

    private void VisitInstance(IOperation? instance)
    {
        if (instance is not null)
        {
            Visit(instance);
        }
    }

    private static IOperation? Body(SemanticModel model, SyntaxNode declaration, SyntaxNode? body, SyntaxNode? expressionBody)
    {
        if (body is not null && model.GetOperation(body) is { } bodyOperation)
        {
            return bodyOperation;
        }

        if (expressionBody is not null && model.GetOperation(expressionBody) is { } expressionOperation)
        {
            return expressionOperation;
        }

        return model.GetOperation(declaration);
    }

    private static bool IsDispatch(ISymbol symbol) => symbol switch
    {
        IMethodSymbol method => method.IsVirtual || method.IsAbstract || method.IsOverride,
        IPropertySymbol property => property.IsVirtual || property.IsAbstract || property.IsOverride,
        IEventSymbol ev => ev.IsVirtual || ev.IsAbstract || ev.IsOverride,
        _ => false,
    };

    private static bool IsMockFactory(INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        return type.Name == "Mock" && ns == "Moq"
            || type.Name == "Substitute" && ns == "NSubstitute"
            || type.Name == "A" && ns == "FakeItEasy";
    }

    private static void Add(List<MemberNode> nodes, MemberNode node)
    {
        if (!nodes.Contains(node))
        {
            nodes.Add(node);
        }
    }
}
