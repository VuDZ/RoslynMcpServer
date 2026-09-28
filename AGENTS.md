# Code style for agents

C# style for this repository is in [docs/code-style.md](docs/code-style.md), relative to the repository root.

- Read that file in full once before the first creation or edit of C# code in the current task, including tests.
- Read it in full when reviewing style or discussing changes to these rules.
- Planning, research, and discussion that do not change code do not require reading the file, unless the task is about the style itself.
- If the task moves from discussion to implementation, read the file before the first code change.
- If `docs/code-style.md` changes on disk during the task, read it again in full before the next code change.
- Apply the rules to new code and to lines this task already changes. That includes the section "Applying the rules to existing code": leave the rest of a file as it is, including member order in types this task does not edit.
- If you launch a subagent that will create, edit, or review the style of C# code, its prompt must require it to read `docs/code-style.md` in full before the first edit or before style comments, and to apply those rules only to new code and to lines that subagent changes.
- Text that lives in code is English: comments, XML docs, log and diagnostic messages, exception messages, test failure text, and asserted fragments. Localized external output kept as test data stays verbatim; project documentation keeps its current language.
