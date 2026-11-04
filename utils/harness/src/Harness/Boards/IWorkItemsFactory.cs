namespace Harness;

/// <summary>
/// Opens the work items of one Azure Boards project. The project is known only per pull request, from
/// <c>harness.json</c> on its base branch and the environment, so it is given here rather than registered.
/// </summary>
interface IWorkItemsFactory
{
    /// <summary>Opens a project's work items. Nothing is read yet.</summary>
    /// <param name="boards">The organization and project; both set, and the organization an https URL.</param>
    /// <returns>The work items, each read at most once.</returns>
    /// <exception cref="ArgumentException"><paramref name="boards"/> is not complete.</exception>
    IWorkItems Create(BoardsDefinition boards);
}
