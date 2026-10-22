using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace RoslynMcpServer.Services.Navigation;

/// <summary>
/// Counts first-chance exceptions raised while a declaration search runs, grouped by type, project and
/// detail. Roslyn retries a document read after an <see cref="IOException"/> six times with 200 ms pauses
/// and then indexes an empty text, so a search can lose about a second per unreadable document without
/// any error in its result; this summary makes those reads visible in the log.
/// </summary>
/// <remarks>
/// <see cref="AppDomain.FirstChanceException"/> is process-wide. An exception is attributed to a project
/// only when it is raised in the async flow that searches that project; any other exception of the process
/// in the same window is counted with no project. No stack traces or file contents are recorded.
/// </remarks>
internal sealed class SearchExceptionRecorder : IDisposable
{
    /// <summary>Project searched by the current async flow, or <see langword="null"/> between projects.</summary>
    public string? CurrentProject
    {
        get => _currentProject.Value;
        set => _currentProject.Value = value;
    }

    public SearchExceptionRecorder()
    {
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
    }

    /// <summary>
    /// One JSON object for the <c>workspace_search_exceptions</c> log line: totals and the
    /// <see cref="MaxLoggedGroups"/> largest groups.
    /// </summary>
    public string ToJson(Guid searchId)
    {
        lock (_groups)
        {
            var groups = _groups
                .Select(pair => (Key: pair.Key, Group: pair.Value))
                .OrderByDescending(entry => entry.Group.Count)
                .ThenBy(entry => entry.Group.FirstMs)
                .Take(MaxLoggedGroups)
                .Select(entry => new
                {
                    type = entry.Key.Type,
                    project = entry.Key.Project,
                    detail = entry.Key.Detail,
                    count = entry.Group.Count,
                    firstMs = entry.Group.FirstMs,
                })
                .ToList();
            return JsonSerializer.Serialize(new
            {
                searchId,
                total = _attributed + _unattributed,
                attributed = _attributed,
                unattributed = _unattributed,
                distinctGroups = _groups.Count,
                droppedGroups = _droppedGroups,
                groups,
            });
        }
    }

    public void Dispose() => AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;

    private void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs args)
    {
        // Recording allocates and takes a lock; an exception raised in here would re-enter the handler.
        if (_inHandler)
        {
            return;
        }

        _inHandler = true;
        try
        {
            Record(args.Exception, _currentProject.Value);
        }
        catch (Exception)
        {
            // Diagnostics must not change the search; a lost observation only lowers a count.
        }
        finally
        {
            _inHandler = false;
        }
    }

    private void Record(Exception exception, string? project)
    {
        var key = new GroupKey(exception.GetType().FullName ?? exception.GetType().Name, project, DetailOf(exception));
        lock (_groups)
        {
            if (project is null)
            {
                _unattributed++;
            }
            else
            {
                _attributed++;
            }

            if (_groups.TryGetValue(key, out var group))
            {
                group.Count++;
                return;
            }

            if (_groups.Count >= MaxGroups)
            {
                _droppedGroups++;
                return;
            }

            _groups.Add(key, new GroupState { Count = 1, FirstMs = _clock.Elapsed.TotalMilliseconds });
        }
    }

    /// <summary>
    /// The file name when the exception carries one; otherwise the message of an I/O or access exception,
    /// which normally names the path. Other exception types are grouped by type only.
    /// </summary>
    private static string? DetailOf(Exception exception) => exception switch
    {
        FileNotFoundException { FileName: { } fileName } => fileName,
        FileLoadException { FileName: { } fileName } => fileName,
        IOException or UnauthorizedAccessException => Truncate(exception.Message),
        _ => null,
    };

    private static string Truncate(string message) =>
        message.Length <= MaxDetailLength ? message : message[..MaxDetailLength];

    private const int MaxGroups = 200;
    private const int MaxLoggedGroups = 20;
    private const int MaxDetailLength = 300;

    private readonly record struct GroupKey(string Type, string? Project, string? Detail);

    private sealed class GroupState
    {
        public int Count { get; set; }

        public double FirstMs { get; init; }
    }

    private readonly AsyncLocal<string?> _currentProject = new();
    private readonly Dictionary<GroupKey, GroupState> _groups = new();

    // Every exception of the process runs this handler. A shared TimeProvider would let unrelated
    // exceptions advance a test clock, so offsets come from a private monotonic stopwatch.
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _attributed;
    private int _unattributed;
    private int _droppedGroups;

    [ThreadStatic]
    private static bool _inHandler;
}
