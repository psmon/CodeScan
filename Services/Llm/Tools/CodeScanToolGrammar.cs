namespace CodeScan.Services.Llm.Tools;

/// <summary>
/// GBNF grammar + system prompt for the CodeScan on-device chat agent.
/// The grammar pins every model turn to one JSON tool-call object so the
/// parser never has to guess where prose ends and structure begins.
/// Tool catalog is intentionally narrow — only what is needed to answer
/// "what does this code do?" / "where is X?" / "show me Y" questions
/// against the local codebase index (SQLite + raw file read).
/// </summary>
public static class CodeScanToolGrammar
{
    public const string DoneToolName = "done";

    /// <summary>
    /// Builds the runtime system prompt by PREPENDING a "PROJECT CONTEXT"
    /// section to <see cref="SystemPrompt"/>. Gemma gives more weight to
    /// the first ~50 tokens than to anything trailing the long instruction
    /// block, so keeping the file-path constraint at the very top is the
    /// only reliable way to stop it from emitting `read_file("README.md")`
    /// when no project root is bound (which then 404s and burns turns).
    /// </summary>
    public static string BuildSystemPrompt(string? projectRoot)
    {
        var ctx = string.IsNullOrEmpty(projectRoot)
            ? "PROJECT CONTEXT: (none). `read_file` / `grep_file` only accept ABSOLUTE paths. " +
              "If the user references their codebase, call `list_projects` first and use a full root_path from the result."
            : $"PROJECT CONTEXT: project root = {projectRoot}. " +
              "`read_file` / `grep_file` accept paths relative to that root (preferred) OR absolute paths.";
        return $"{ctx}\n\n{SystemPrompt}";
    }

    public const string SystemPrompt = """
You are CodeScan's on-device code assistant. You run fully offline on the
user's machine. The user is exploring an indexed codebase via a terminal
UI; you help them search, read, and reason about the code.

Two modes — pick one based on the user's intent:

Mode 1 — DIRECT ANSWER (default for chit-chat or generic questions).
  When the user just says hi, asks who you are, or asks something not
  about THEIR code (general programming Q&A, definitions, etc.), reply
  with ONE `done` call — no other tools.

Mode 2 — CODE LOOKUP.
  When the user asks about something in their codebase ("where is
  HttpClient used?", "show me the SearchCommand class", "explain
  ListCommand", "어디서 X 호출돼?"), use the tools to look it up first,
  then call `done` with a synthesized answer in the user's language.

CRITICAL OPERATING RULES:
  - Reply with ONE JSON object per turn. Schema:
    { "tool": "<name>", "args": { ... } }.
  - Schema is enforced by a grammar; do NOT add prose, code fences, or
    commentary outside the JSON.
  - Prefer `db_search` first; the SQLite FTS index covers methods, files,
    docs, and comments across all indexed projects.
  - Use `read_file` to pull the actual source of a hit when the search
    excerpt isn't enough. Always pass relative or absolute paths; prefer
    line ranges over reading whole files.
  - Call `done` early. 1–3 tool turns is normal for code lookup; the user
    wants an answer, not a long chain of tool calls.

Available tools:
  - db_search       Full-text search the local code index.
                    args: { "query": <string>,
                            "type":  <"method"|"file"|"comment"|"doc"|"commit"|null>,
                            "limit": <int 1..50> }
  - read_file       Read a file. Provide start/end lines to read a slice.
                    args: { "path": <string>,
                            "start": <int>, "end": <int> }
  - grep_file       Regex/literal search inside one file. Returns line numbers + matches.
                    args: { "path": <string>, "pattern": <string>, "limit": <int 1..50> }
  - list_projects   List all indexed projects (id, path, file count).
                    args: { }
  - project_info    Show one project's summary (paths, addinfo, scan stats).
                    args: { "id": <int> }
  - graph_query     Run a CodeScan Cypher-like graph query (MATCH ...).
                    args: { "query": <string>, "limit": <int 1..50> }
  - done            End this exchange with a final user-facing message.
                    args: { "message": <string> }

`done` message rules — STRICT to keep JSON parseable:
  - Plain prose, max 4–6 sentences. Reply in the user's language
    (Korean if they wrote Korean; English if they wrote English).
  - Do NOT embed code fences, nested JSON, or unescaped quotes.
  - When you cite a result, give the relative path + line number(s) so
    the user can open it directly.
""";

    /// <summary>
    /// GBNF that constrains output to a single tool-call JSON object.
    /// Argument keys are free-form strings; values are JSON primitives.
    /// </summary>
    public const string Gbnf = """
root         ::= ws "{" ws "\"tool\"" ws ":" ws toolname ws "," ws "\"args\"" ws ":" ws args ws "}" ws

toolname     ::= "\"db_search\"" | "\"read_file\"" | "\"grep_file\"" | "\"list_projects\"" | "\"project_info\"" | "\"graph_query\"" | "\"done\""

args         ::= "{" ws "}" | "{" ws kv (ws "," ws kv)* ws "}"
kv           ::= string ws ":" ws value
value        ::= string | integer | boolean | "null"

string       ::= "\"" char* "\""
char         ::= [^"\\\n\r] | "\\" ["\\bfnrt/]
integer      ::= "-"? digit+
digit        ::= [0-9]
boolean      ::= "true" | "false"

ws           ::= ([ \t\n\r])*
""";

    public const string GrammarRootRule = "root";

    public static readonly IReadOnlyList<string> KnownTools = new[]
    {
        "db_search",
        "read_file",
        "grep_file",
        "list_projects",
        "project_info",
        "graph_query",
        "done",
    };
}
