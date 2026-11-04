namespace Harness;

/// <summary>Git object ids as text.</summary>
static class Sha
{
    const int ShortLength = 7;

    /// <summary>Abbreviates an object id as git does by default.</summary>
    /// <param name="sha">The full object id.</param>
    /// <returns>Its first seven characters.</returns>
    public static string Short(string sha) => sha[..ShortLength];
}
