namespace LoadTimeline.Models;

/// <summary>
/// One evaluation or project build from a binlog. <see cref="Context"/> keys repeated builds: the global
/// properties and requested targets that make a build distinct.
/// </summary>
internal sealed record TimelineInterval(
    string Kind,
    int Binlog,
    string Project,
    string Context,
    bool TopLevel,
    DateTime Start,
    DateTime End)
{
    public double Ms => (End - Start).TotalMilliseconds;
}
