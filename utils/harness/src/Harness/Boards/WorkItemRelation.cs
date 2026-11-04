namespace Harness;

/// <summary>A link from a work item.</summary>
/// <param name="Type">
/// The link type's reference name, such as <c>Microsoft.VSTS.Common.TestedBy-Forward</c> for Tested By.
/// </param>
/// <param name="Url">The target's REST URL; for a work item it ends in <c>/workItems/&lt;id&gt;</c>.</param>
sealed record WorkItemRelation(string Type, string Url);
