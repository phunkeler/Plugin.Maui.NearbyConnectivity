namespace Harness;

/// <summary>Opens Azure Boards projects through <c>az rest</c>.</summary>
/// <param name="processRunner">Runs az.</param>
/// <param name="timeProvider">Times each call against <see cref="AzWorkItems.CallTimeout"/>.</param>
sealed class AzWorkItemsFactory(IProcessRunner processRunner, TimeProvider timeProvider) : IWorkItemsFactory
{
    public IWorkItems Create(BoardsDefinition boards) => new AzWorkItems(processRunner, timeProvider, boards);
}
