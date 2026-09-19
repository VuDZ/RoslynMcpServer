# RoslynMcpServer — review of MCP tool surface

Repository: https://github.com/VuDZ/RoslynMcpServer

## Executive summary

RoslynMcpServer solves a real problem for AI coding agents working with C#: it exposes Roslyn semantics through MCP instead of forcing the model to reason only from text search, file reads, and shell commands.

The strongest part of the project is not generic file access or `dotnet` execution. Its real value is the semantic layer:

- symbol definition / references / implementations;
- call graph;
- diagnostics and Roslyn code fixes;
- semantic rename;
- syntax-aware source extraction;
- source/decompiled skeletons and focused member reads;
- targeted build/test workflows.

The current tool surface is broad: 63 tools split across several groups. The existing `lite` profile is already close to a good default, but the API can be simplified further.

The main recommendation is:

> Evolve the project from a collection of file- and action-oriented Roslyn commands into a symbol-oriented API for agents.

Instead of repeatedly passing `filePath + className + methodName`, the server should resolve a symbol once and return an opaque/stable symbol handle that can be used by later calls.

A strong long-term target would be roughly **20–25 expressive tools** rather than 60+ narrowly specialized tools.

---

# 1. Core tools

The current `core` group is already the best-designed part of the project.

A practical evaluation:

| Tool | Value | Notes |
|---|---:|---|
| `load_workspace` | ★★★ | Foundation for all Roslyn semantic operations |
| `reset_workspace` | ★★ | Necessary because MSBuild/source-generator state can become stale |
| `get_class_skeleton` | ★★★ | Excellent token-saving tool |
| `get_method_body` | ★★★ | One of the best tools in the project |
| `get_diagnostics_for_file` | ★★★ | Gives compiler truth instead of model guessing |
| `find_symbol_definition` | ★★★ | Core Roslyn value |
| `find_symbol_references` | ★★★ | Core Roslyn value |
| `find_usages` | ★★ | Useful, but overlaps with reference search |
| `find_implementations` | ★★★ | Hard to reproduce reliably with grep |
| `get_call_graph` | ★★★ | Very useful for debugging and impact analysis |
| `run_dotnet_build` | ★★★ | Structured build feedback |
| `run_dotnet_test` | ★★ | Necessary suite-level operation |
| `run_specific_test` | ★★★ | Very valuable for agentic TDD loops |
| `run_test_by_filter` | ★★ | Useful advanced escape hatch |
| `get_changed_files` | ★★ | Convenient, though Git can also provide this |
| `get_mcp_server_info` | ★★ | Infrastructure/discovery |
| `list_tool_groups` | ★★ | Required for dynamic profiles |
| `get_tool_help` | ★★ | Good just-in-time tool discovery mechanism |
| `enable_tool_group` | ★★ | Important for keeping the default surface small |

## Recommendation

Keep the idea of a small core profile.

The current `lite` profile is already directionally correct and should remain the default experience.

---

# 2. Merge `find_symbol_references` and `find_usages`

These two tools represent an API smell.

The distinction appears to be roughly:

- `find_symbol_references`: precise when the declaration/location is known;
- `find_usages`: starts from a name and resolves a likely symbol.

That distinction reflects Roslyn implementation details more than agent intent.

For an LLM, the intent is usually simply:

> Find references to this symbol.

A better API would be:

```text
find_references(
    symbolName,
    filePath? = null,
    line? = null,
    column? = null,
    maxResults = 30
)
```

Behavior:

1. If a source location is supplied, resolve the exact `ISymbol`.
2. If only a name is supplied, search symbols first.
3. If multiple symbols match, return candidates or require disambiguation.
4. Once resolved, run the same reference pipeline.

This reduces tool-choice ambiguity without reducing capability.

---

# 3. `get_class_skeleton` + `get_method_body` are among the best ideas in the project

These tools directly address one of the biggest problems in AI coding workflows: context waste.

Without semantic/syntax-aware extraction:

```text
read FooService.cs
→ 1800 lines
→ model scans for the relevant method
```

With RoslynMcpServer:

```text
get_class_skeleton(FooService.cs)
→ signatures / structure only

get_method_body(FooService.cs, FooService, ProcessAsync)
→ only the relevant implementation
```

This is exactly the kind of API an LLM benefits from.

## Recommendation

Generalize this concept.

Instead of only:

- class skeleton;
- method body;

introduce a generic symbol source API that can work with:

- methods;
- constructors;
- properties;
- classes;
- records;
- structs;
- interfaces;
- enums;
- events;
- fields;
- local functions.

For example:

```text
get_symbol_source(symbolId)
```

and:

```text
get_symbol_outline(symbolId)
```

This would make the API much more uniform.

---

# 4. Semantic navigation is the real reason to use this MCP

The following tools should remain central:

```text
find_symbol_definition
find_references
find_implementations
get_call_graph
```

These operations have genuine semantic value that plain text search cannot reliably reproduce.

Example:

```csharp
IHandler.Handle()
EmailHandler.Handle()
PaymentHandler.Handle()
```

A grep-based agent sees repeated text.

Roslyn understands:

- exact symbol identity;
- interface implementation relationships;
- overrides;
- inheritance;
- valid references;
- callers and callees.

That difference becomes increasingly important in larger solutions with:

- overloads;
- nested types;
- extension methods;
- interfaces;
- inheritance;
- partial classes;
- multiple projects.

---

# 5. Add `get_symbol_info`

A missing high-value primitive is a generic symbol-information tool.

Suggested shape:

```json
{
  "kind": "Method",
  "fullyQualifiedName": "Foo.Bar.OrderService.CreateAsync",
  "containingType": "Foo.Bar.OrderService",
  "returnType": "Task<Order>",
  "parameters": [],
  "accessibility": "public",
  "isAsync": true,
  "isVirtual": false,
  "interfaces": [],
  "sourceLocation": {}
}
```

Possible additional fields:

- symbol kind;
- containing namespace;
- containing assembly/project;
- generic type parameters;
- implemented/overridden members;
- attributes;
- nullable annotations;
- declaration locations;
- XML documentation summary;
- source/generated/decompiled origin.

Most importantly, return a stable opaque identifier:

```text
symbolId
```

This `symbolId` should be reusable across later tool calls.

---

# 6. Make the API symbol-oriented

This is the largest architectural improvement available.

Today many calls conceptually look like:

```text
find_symbol_definition("Process")
→ filePath

get_method_body(
    filePath,
    className="OrderService",
    methodName="Process"
)
```

A better flow would be:

```text
resolve_symbol(...)
→ symbolId

get_symbol_info(symbolId)
get_symbol_source(symbolId)
find_references(symbolId)
find_callers(symbolId)
rename_symbol(symbolId, "ProcessAsync")
```

## Why this matters

Passing around names and file paths repeatedly creates avoidable ambiguity:

- overloads;
- nested classes;
- duplicate method names;
- partial types;
- explicit interface implementations;
- generated source;
- multiple projects with similarly named types.

Roslyn already has identity mechanisms for symbols. The MCP API should take advantage of them.

The external API does not have to expose raw Roslyn internals directly. An opaque server-side symbol handle is sufficient.

---

# 7. Move `rename_symbol` into core

`rename_symbol` is one of the strongest arguments for installing this server at all.

Plain search-and-replace can accidentally modify:

- unrelated symbols with the same name;
- strings;
- comments;
- JSON/config;
- methods in unrelated types.

A Roslyn semantic rename operates on the actual symbol graph.

This is precisely the kind of transformation where compiler-aware tooling is materially safer than model-generated text edits.

## Recommendation

Move:

```text
rename_symbol
```

from optional `editing` into the semantic core.

Preferably support:

```text
rename_symbol(
    symbolId,
    newName,
    previewOnly = true
)
```

with a preview diff before write.

---

# 8. Diagnostics + CodeActions are another major strength

This workflow is excellent:

```text
get_diagnostics_for_file
        ↓
get_code_fixes
        ↓
apply_code_fix
```

The model does not need to invent every repair itself.

Instead:

1. Roslyn reports the diagnostic.
2. Roslyn provides valid `CodeAction`s.
3. The LLM selects the intended action.
4. The server previews/applies the change.

This is a very good architecture for AI coding.

The model chooses intent; compiler tooling performs the exact transformation.

## Recommendation

Keep this workflow and consider promoting it more strongly in documentation and agent instructions.

Potential future improvement:

```text
get_diagnostics(scope = file | project | solution)
```

rather than only file-level diagnostics.

---

# 9. Editing tools are too granular

The `editing` group currently contains many narrow commands, including operations such as:

```text
add_using
remove_using
organize_usings
add_method_to_class
update_method_body
add_property_to_class
add_field_to_class
remove_member
add_type_to_class_bases
implement_interface
generate_test_method_stub
get_code_fixes
apply_code_fix
run_format
rename_symbol
extract_interface
move_type_to_new_file
```

Not all of these deserve a dedicated schema exposed to the model.

Every additional tool:

- consumes context;
- creates another tool-selection decision;
- increases documentation burden;
- increases overlap between ways of performing the same task.

---

# 10. Keep `update_method_body`

`update_method_body` is a good abstraction.

The intent is clear:

> Replace only this method implementation.

This is safer than asking the model to construct a file patch with exact anchors.

Benefits:

- syntax-aware targeting;
- overload awareness;
- localized changes;
- formatting;
- easier validation.

This is a strong MCP operation and should remain.

---

# 11. Keep `implement_interface`

This is another operation where Roslyn adds genuine semantic value.

The server can know:

- which interface members exist;
- which are already implemented;
- exact signatures;
- generic constraints;
- property/event shapes;
- return and parameter types.

That is substantially better than an LLM reconstructing the interface from memory or grep results.

Keep it.

---

# 12. `add_using` and `organize_usings` are useful but secondary

These tools are convenient, but they overlap with Roslyn code fixes and IDE functionality.

A natural workflow can already be:

```text
model writes code
→ compiler reports CS0246
→ get_code_fixes
→ choose "using ..."
→ apply_code_fix
```

So separate using-management tools are not core semantic primitives.

Recommendation:

- keep them optional;
- do not expose them in the default profile;
- consider whether `organize_usings` belongs under formatting rather than semantic editing.

---

# 13. Merge `add_field_to_class`, `add_property_to_class`, `add_method_to_class`

These tools mainly insert model-generated source into the correct AST location.

The LLM already supplies most of the content:

```text
public Foo Bar { get; init; }
```

or:

```text
public async Task ProcessAsync(...)
```

Roslyn is primarily providing safe placement.

That is useful, but three separate tools are unnecessary.

Replace them with one general primitive:

```text
add_member(
    typeSymbolId,
    memberSource
)
```

Potential optional arguments:

```text
placement = auto | start | end | before | after
anchorSymbolId = ...
```

This preserves the AST-safe edit while reducing the tool surface.

---

# 14. Filesystem/search tools mostly duplicate the host

The `files` group includes operations such as:

```text
get_file_content
get_code_skeleton
update_file_content
apply_patch
list_directory_tree
search_code
read_file_range
```

For IDE-based agents such as Cursor/OpenCode/Codex-style hosts, most of these already exist natively.

Examples:

```text
read
grep/search
edit
patch
list files
```

RoslynMcpServer should avoid competing with the host unless it adds semantic information.

## Keep

```text
get_code_skeleton
```

because syntax-aware body stripping is a real value-add even without a loaded workspace.

## Consider hiding from normal IDE profiles

```text
get_file_content
update_file_content
apply_patch
list_directory_tree
search_code
read_file_range
```

These can remain available for:

- headless MCP clients;
- minimal hosts;
- automation environments without native file tools.

But they should not dominate the default surface.

---

# 15. Decompilation tools are well designed

The decompilation group is one of the strongest optional groups.

Conceptually:

```text
explore_assembly
decompile_type
get_decompiled_class_skeleton
get_decompiled_method_body
```

The important design choice is the same as for source code:

```text
assembly
  ↓
types
  ↓
type outline/skeleton
  ↓
one member
```

This avoids dumping enormous decompiled types into the model context.

## Recommendation

Keep this group.

Potential API cleanup:

```text
get_decompiled_type_outline
get_decompiled_member
```

could eventually align naming with the source-side symbol API.

The ideal long-term interface would make source and decompiled symbols feel similar to the agent.

---

# 16. Test tools should remain separate

At first glance these look redundant:

```text
run_dotnet_test
run_specific_test
run_test_by_filter
```

But they represent distinct levels of intent.

Recommended hierarchy:

```text
run_specific_test
```

Use for the common inner development loop.

```text
run_dotnet_test
```

Use for project/suite validation.

```text
run_test_by_filter
```

Use as an advanced escape hatch when framework-specific filtering is needed.

Keeping three explicit tools is better than making the model construct VSTest filter expressions for every case.

This is an example where multiple similar tools improve ergonomics rather than hurting them.

---

# 17. NuGet group is useful as an optional capability

Useful operations include:

```text
list_nuget_packages
list_outdated_packages
run_nuget_audit
search_nuget_registry
add_package_reference
remove_package_reference
```

The read/query operations are especially valuable:

```text
search_nuget_registry
list_nuget_packages
list_outdated_packages
run_nuget_audit
```

They give structured package information and reduce package/version hallucinations.

The write operations:

```text
add_package_reference
remove_package_reference
```

are convenient but less unique.

Recommendation:

- keep the group optional;
- encourage registry lookup before package modification;
- treat package mutations as explicit/high-impact operations.

---

# 18. Hide `execute_dotnet_command` by default

A generic:

```text
execute_dotnet_command
```

is effectively a shell escape hatch restricted to the `dotnet` CLI.

Once specialized tools already exist for:

- build;
- test;
- targeted test;
- run;
- formatting;
- NuGet;

the generic command becomes mostly a fallback.

That is useful, but it should not compete with specialized tools during normal model selection.

Recommendation:

- keep it available;
- require enabling an advanced/runtime group;
- document it as a fallback when no structured tool covers the operation.

---

# 19. Scratchpad does not belong in the Roslyn core

A long-lived agent scratchpad is not a Roslyn concern.

Once the same server starts owning:

```text
Roslyn semantics
filesystem
Git
shell/process execution
NuGet
agent memory
```

its responsibility becomes too broad.

Recommendation:

Move persistent scratchpad/memory functionality to:

- the agent host;
- a dedicated memory MCP;
- a separate optional server.

This keeps RoslynMcpServer conceptually focused.

---

# 20. Recommended default tool surface

A compact default API could look approximately like this:

```text
load_workspace
reset_workspace

resolve_symbol
get_symbol_info
find_references
find_implementations
get_call_graph

get_type_outline
get_symbol_source

get_diagnostics
get_code_fixes
apply_code_fix

rename_symbol
update_method_body
implement_interface

run_dotnet_build
run_specific_test
run_dotnet_test

explore_assembly
get_decompiled_type_outline
get_decompiled_member
```

Approximately **20–22 tools**.

The exact number is not the important part.

The important part is that each tool should correspond to a clear agent intent and provide meaningful semantic value.

---

# 21. Suggested RoslynMcpServer v2 API

A possible v2 design:

## Workspace

```text
load_workspace(path, configuration?, framework?)
reset_workspace()
get_workspace_info()
```

## Symbol resolution

```text
resolve_symbol(
    query,
    filePath? = null,
    line? = null,
    column? = null
)
```

Returns:

```json
{
  "symbolId": "...",
  "displayName": "...",
  "kind": "Method",
  "location": {}
}
```

If ambiguous:

```json
{
  "status": "ambiguous",
  "candidates": []
}
```

## Symbol inspection

```text
get_symbol_info(symbolId)
get_symbol_outline(symbolId)
get_symbol_source(symbolId)
```

## Navigation

```text
find_references(symbolId, maxResults?)
find_implementations(symbolId, maxResults?)
get_call_graph(symbolId, direction?, depth?)
```

Potentially split call graph only if necessary:

```text
find_callers(symbolId)
find_callees(symbolId)
```

## Diagnostics and fixes

```text
get_diagnostics(
    scope,
    path? = null,
    severity? = null
)

get_code_fixes(diagnosticId)
apply_code_fix(codeFixId, previewOnly = true)
```

## Semantic edits

```text
rename_symbol(symbolId, newName, previewOnly = true)
update_member_body(symbolId, body, previewOnly = true)
implement_interface(typeSymbolId, interfaceSymbolId?, previewOnly = true)
add_member(typeSymbolId, memberSource, previewOnly = true)
remove_symbol(symbolId, previewOnly = true)
```

## Build/test

```text
build(target?)
run_test(testId | testName)
run_tests(project?, filter?)
```

Test discovery could return stable test IDs to avoid stringly typed filters.

## Decompilation

```text
explore_assembly(assembly)
resolve_decompiled_symbol(...)
get_decompiled_symbol_info(symbolId)
get_decompiled_symbol_source(symbolId)
```

Ideally source and decompiled symbol operations should eventually share common result shapes.

---

# 22. Stable IDs should be used beyond symbols

The same principle can improve diagnostics, fixes, and tests.

Instead of repeated string-based addressing:

```text
diagnosticId
codeFixId
testId
symbolId
```

can be returned and reused.

Example:

```text
get_diagnostics(...)
→ diagnosticId

get_code_fixes(diagnosticId)
→ codeFixId

apply_code_fix(codeFixId)
```

This makes multi-step tool workflows safer and easier for agents to follow.

---

# 23. Prefer intent-oriented tools over implementation-oriented tools

A useful design test is:

> Does the tool name describe what the agent wants to accomplish, or how the server internally accomplishes it?

Good:

```text
rename_symbol
find_implementations
run_specific_test
get_diagnostics
```

Less ideal:

```text
read_file_range
add_property_to_class
add_field_to_class
```

The first group maps to user/agent intent.

The second group exposes implementation mechanics or narrowly shaped mutations.

A smaller set of expressive intent-level operations will generally be easier for an LLM to select correctly.

---

# 24. Do not minimize tool count just for its own sake

The goal is not mechanically reducing:

```text
63 → 20
```

Some superficially redundant tools are useful.

For example:

```text
run_specific_test
run_dotnet_test
run_test_by_filter
```

are worth keeping separate because they correspond to clearly different intents.

The criterion should be:

> Does this tool represent a distinct high-value intent, or is it merely another way to achieve the same operation?

Good duplication:

- targeted test vs full test suite;
- source navigation vs decompilation.

Bad duplication:

- several variants of symbol lookup that could share one resolver;
- separate add-field/add-property/add-method insertion tools;
- MCP filesystem operations when the host already has equivalent native capabilities.

---

# 25. Recommended grouping

A cleaner grouping model could be:

## `core`

Always enabled:

```text
workspace
symbol resolution
symbol inspection
references
implementations
call graph
diagnostics
build
specific tests
tool discovery
semantic rename
```

## `editing`

Optional:

```text
code fixes
member body updates
interface implementation
add/remove member
formatting
extract/move refactorings
```

## `decompile`

Optional:

```text
assembly exploration
decompiled outline/source
```

## `nuget`

Optional:

```text
package search
package inventory
audit
package modifications
```

## `project`

Optional:

```text
project references
solution/project graph changes
```

## `runtime`

Optional / advanced:

```text
run application
generic dotnet command
process management
```

## `files`

Compatibility/headless profile only:

```text
read
write
patch
grep
tree
range read
```

---

# 26. Product direction

The strongest direction for RoslynMcpServer is not adding dozens of new commands.

The project becomes more valuable if it focuses on:

1. **Semantic reliability**
   - exact symbol identity;
   - overload handling;
   - generated code behavior;
   - multi-project resolution.

2. **Token efficiency**
   - outlines;
   - focused symbol source;
   - bounded result sets;
   - pagination.

3. **Safe transformations**
   - preview-first semantic rename;
   - Roslyn CodeActions;
   - AST-aware member edits;
   - clear disk write boundaries.

4. **Agent-friendly identity**
   - reusable symbol IDs;
   - diagnostic IDs;
   - code-fix IDs;
   - test IDs.

5. **Minimal default surface**
   - expose only high-value semantic tools by default;
   - keep filesystem/shell/project-management capabilities optional.

6. **Measurable outcomes**
   - fewer source tokens read;
   - fewer incorrect edits;
   - fewer build/fix loops;
   - higher refactoring success rate;
   - fewer tool calls per task.

---

# Final assessment

The current `lite` profile is already close to the correct direction.

The next major improvement should not simply be:

> remove more tools.

It should be:

> make symbols first-class entities in the MCP protocol and collapse overlapping operations around them.

The ideal flow for an agent should look like:

```text
load workspace
→ resolve symbol
→ inspect symbol
→ navigate semantic graph
→ read only required source
→ perform semantic edit
→ inspect diagnostics
→ run targeted test
```

That workflow is where RoslynMcpServer has a strong advantage over generic coding-agent tools.

The project is most compelling when it acts as a **compiler-aware semantic backend for the agent**, not when it tries to replace the host's filesystem, shell, memory, and general editing features.
